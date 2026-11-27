using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Lavagon.Core
{
    /// <summary>
    /// Downloads the official Windows builds of Apache, PHP, MySQL and Mailpit into bin\{tool}\{version}
    /// (and the Visual C++ runtime they need), so a fresh Lavagon works without manual downloads.
    /// The newest suitable version is looked up online on every run; nothing is pinned to a mirror.
    /// </summary>
    public static class Components
    {
        public enum State { Waiting, Working, Done, Skipped, Failed }

        /// <summary>One thing to set up; its status is shown live in the setup panel.</summary>
        public class Item
        {
            public string Key, Title;
            public State State = State.Waiting;
            public string Status = "Waiting...";
            /// <summary>0..1 while downloading, -1 when there is no meaningful progress.</summary>
            public double Progress = -1;
            public bool Optional;

            internal Func<Item, CancellationToken, Task> Run;
        }

        private class Download
        {
            public string Url, Folder, Sha256;
        }

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = Timeout.InfiniteTimeSpan, // big files; stalls are caught per read below
            };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"Lavagon/{Program.Version} (+{Program.HomePage})");
            return c;
        }

        // ---- What is needed --------------------------------------------------------------

        /// <summary>Executable that proves a version folder is complete.</summary>
        private static readonly Dictionary<string, string> Marker = new Dictionary<string, string>
        {
            ["apache"] = @"bin\httpd.exe",
            ["php"] = "php.exe",
            ["mysql"] = @"bin\mysqld.exe|bin\mariadbd.exe",
            ["mailpit"] = "mailpit.exe",
        };

        private static readonly string[] Core = { "apache", "php", "mysql" };

        public static bool IsInstalled(string tool) =>
            Versions.List(tool).Any(v => HasMarker(Path.Combine(AppPaths.Bin, tool, v), tool));

        private static bool HasMarker(string dir, string tool) =>
            Marker[tool].Split('|').Any(m => File.Exists(Path.Combine(dir, m)));

        private static bool SetupDone
        {
            get => App.Settings.GetBool("preferences", "SetupDone", false);
            set => App.Settings.SetBool("preferences", "SetupDone", value);
        }

        /// <summary>
        /// Set up automatically on the first start, and whenever bin\ has nothing usable at all.
        /// After a successful setup, removing a single tool (e.g. Apache for an Nginx-only setup) is respected.
        /// </summary>
        public static bool NeedsSetup =>
            Core.Any(t => !IsInstalled(t)) && (!SetupDone || !App.ActiveServices.Any());

        public static void MarkDone() => SetupDone = true;

        /// <summary>The steps for whatever is missing right now.</summary>
        public static List<Item> Plan()
        {
            var items = new List<Item>();
            if (!IsInstalled("apache")) items.Add(new Item { Key = "apache", Title = "Apache", Run = (i, ct) => Install(i, "apache", ResolveApache, ct) });
            if (!IsInstalled("php")) items.Add(new Item { Key = "php", Title = "PHP", Run = (i, ct) => Install(i, "php", ResolvePhp, ct) });
            if (!IsInstalled("mysql")) items.Add(new Item { Key = "mysql", Title = "MySQL", Run = (i, ct) => Install(i, "mysql", ResolveMySql, ct) });
            if (!IsInstalled("mailpit")) items.Add(new Item { Key = "mailpit", Title = "Mailpit", Optional = true, Run = (i, ct) => Install(i, "mailpit", ResolveMailpit, ct) });
            // Apache, PHP and MySQL are built with Visual C++; make sure a recent runtime is installed.
            if (items.Any(i => !i.Optional))
                items.Add(new Item { Key = "vcredist", Title = "Visual C++ runtime", Run = InstallVcRuntime });
            return items;
        }

        /// <summary>Runs every step in parallel; <paramref name="changed"/> fires (on a worker thread) on every status change.</summary>
        public static async Task RunAsync(IEnumerable<Item> items, Action changed, CancellationToken ct)
        {
            CleanLeftovers();
            await Task.WhenAll(items.Where(i => i.State != State.Done && i.State != State.Skipped).Select(async item =>
            {
                item.State = State.Working;
                item.Progress = -1;
                item.Status = "Looking for the latest version...";
                changed();
                var reporter = new Timer(_ => changed(), null, 250, 250);
                try
                {
                    await item.Run(item, ct).ConfigureAwait(false);
                    if (item.State == State.Working) item.State = State.Done;
                }
                catch (Exception ex)
                {
                    Log.Error($"Setup of {item.Title} failed: {ex}");
                    item.State = State.Failed;
                    item.Status = "Failed: " + Describe(ex);
                }
                finally
                {
                    reporter.Dispose();
                    item.Progress = -1;
                    changed();
                }
            })).ConfigureAwait(false);
        }

        private static string Describe(Exception ex)
        {
            ex = ex is AggregateException ae ? ae.GetBaseException() : ex;
            if (ex is HttpRequestException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is WebException we && we.InnerException != null) ex = we.InnerException;
            if (ex is OperationCanceledException) return "the connection timed out.";
            return ex.Message.TrimEnd('.') + ".";
        }

        // ---- Resolving the latest versions ------------------------------------------------

        /// <summary>Apache Lounge: the 64-bit Apache 2.4 build listed on the download page.</summary>
        private static async Task<Download> ResolveApache(CancellationToken ct)
        {
            const string page = "https://www.apachelounge.com/download/";
            var html = await GetString(page, ct);
            var best = Regex.Matches(html, @"href=""(?<p>[^""]*/(?<f>httpd-2\.4\.\d+-\d+-win64-VS\d+)\.zip)""", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .OrderByDescending(m => SortKey(m.Groups["f"].Value))
                .FirstOrDefault();
            if (best == null) throw new LavagonException("no Apache 2.4 Win64 build found on " + page);
            return new Download { Url = new Uri(new Uri(page), best.Groups["p"].Value).ToString(), Folder = best.Groups["f"].Value };
        }

        /// <summary>
        /// windows.php.net releases.json: the Thread Safe x64 build (needed for Apache's mod_php) of the newest
        /// PHP branch that has had a couple of bug-fix releases (a brand-new x.y.0 often breaks frameworks).
        /// </summary>
        private static async Task<Download> ResolvePhp(CancellationToken ct)
        {
            const string baseUrl = "https://windows.php.net/downloads/releases/";
            var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await GetString(baseUrl + "releases.json", ct));
            var builds = new List<(System.Version Version, string Path, string Sha256)>();
            foreach (var branch in json.Values.OfType<Dictionary<string, object>>())
            {
                if (!(branch.TryGetValue("version", out var v) && System.Version.TryParse(v as string, out var version))) continue;
                var ts = branch.Where(kv => kv.Key.StartsWith("ts-") && kv.Key.EndsWith("-x64")).Select(kv => kv.Value).OfType<Dictionary<string, object>>().FirstOrDefault();
                if (ts == null || !(ts.TryGetValue("zip", out var z) && z is Dictionary<string, object> zip)) continue;
                builds.Add((version, zip["path"] as string, zip.TryGetValue("sha256", out var sha) ? sha as string : null));
            }
            if (builds.Count == 0) throw new LavagonException("no PHP Thread Safe x64 build found on windows.php.net");
            var pick = builds.Where(b => b.Version.Build >= 2).OrderByDescending(b => b.Version).Concat(builds.OrderByDescending(b => b.Version)).First();
            return new Download { Url = baseUrl + pick.Path, Folder = Path.GetFileNameWithoutExtension(pick.Path), Sha256 = pick.Sha256 };
        }

        /// <summary>
        /// MySQL 8.4 LTS (the long-term branch, and the last one that still offers mysql_native_password for
        /// old PHP versions). dev.mysql.com has no API, so probe for the newest 8.4.x zip on Oracle's CDN.
        /// </summary>
        private static async Task<Download> ResolveMySql(CancellationToken ct)
        {
            const int knownGood = 13; // 8.4.13 exists; newer patch releases are found by probing
            string Current(int n) => $"https://cdn.mysql.com/Downloads/MySQL-8.4/mysql-8.4.{n}-winx64.zip";
            string Archive(int n) => $"https://downloads.mysql.com/archives/get/p/23/file/mysql-8.4.{n}-winx64.zip";

            var probes = Enumerable.Range(knownGood, 12).Select(async n =>
            {
                if (await Exists(Current(n), ct)) return (n, Current(n));
                if (await Exists(Archive(n), ct)) return (n, Archive(n));
                return (n, (string)null);
            }).ToList();
            var found = (await Task.WhenAll(probes)).Where(p => p.Item2 != null).OrderByDescending(p => p.n).FirstOrDefault();
            if (found.Item2 == null) throw new LavagonException("could not reach the MySQL download server (cdn.mysql.com)");
            return new Download { Url = found.Item2, Folder = $"mysql-8.4.{found.n}-winx64" };
        }

        /// <summary>Mailpit: the latest GitHub release.</summary>
        private static async Task<Download> ResolveMailpit(CancellationToken ct)
        {
            var version = "latest";
            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                {
                    c.DefaultRequestHeaders.UserAgent.ParseAdd($"Lavagon/{Program.Version}");
                    using (var r = await c.GetAsync("https://github.com/axllent/mailpit/releases/latest", ct))
                    {
                        var m = Regex.Match(r.Headers.Location?.ToString() ?? "", @"/tag/v?(\d+(\.\d+)+)");
                        if (m.Success) version = m.Groups[1].Value;
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log.Error(ex); // the download below works without knowing the version number
            }
            return new Download
            {
                Url = "https://github.com/axllent/mailpit/releases/latest/download/mailpit-windows-amd64.zip",
                Folder = "mailpit-" + version,
            };
        }

        // ---- Download + extract -------------------------------------------------------------

        private static async Task Install(Item item, string tool, Func<CancellationToken, Task<Download>> resolve, CancellationToken ct)
        {
            var d = await resolve(ct);
            var label = Versions.Parse(d.Folder);
            var target = Path.Combine(AppPaths.Bin, tool, d.Folder);
            if (Directory.Exists(target) && HasMarker(target, tool))
            {
                item.Status = "✓ " + label;
                return;
            }

            var zip = Path.Combine(DownloadDir, Path.GetFileName(new Uri(d.Url).AbsolutePath));
            await DownloadFile(d.Url, zip, item, label, ct);
            try
            {
                if (!string.IsNullOrEmpty(d.Sha256))
                {
                    item.Status = $"Verifying {label}...";
                    item.Progress = -1;
                    if (!string.Equals(Sha256(zip), d.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new LavagonException("the download is damaged (checksum mismatch). Please retry");
                }

                item.Status = $"Extracting {label}...";
                item.Progress = -1;
                Extract(zip, tool, target, item, label);
            }
            finally
            {
                TryDelete(zip);
            }
            item.Status = "✓ " + label;
            Log.Info($"Installed {tool} into {target}");
        }

        private static string DownloadDir
        {
            get
            {
                var dir = Path.Combine(AppPaths.Tmp, "downloads");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static async Task DownloadFile(string url, string file, Item item, string label, CancellationToken ct)
        {
            var what = ("Downloading " + label).TrimEnd();
            item.Status = what + "...";
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new LavagonException($"{url} returned {(int)response.StatusCode} {response.ReasonPhrase}");
                long total = response.Content.Headers.ContentLength ?? -1;
                var partial = file + ".part";
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                {
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    while (true)
                    {
                        // A read that stalls for a minute means the connection is gone.
                        using (var stall = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            stall.CancelAfter(TimeSpan.FromSeconds(60));
                            int n;
                            // .NET Framework ignores the token while the socket waits: closing the response unblocks it.
                            using (stall.Token.Register(response.Dispose))
                            {
                                try { n = await input.ReadAsync(buffer, 0, buffer.Length, stall.Token); }
                                catch (Exception) when (stall.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    throw new LavagonException("the download stalled. Please retry");
                                }
                            }
                            if (n == 0) break;
                            await output.WriteAsync(buffer, 0, n, ct);
                            done += n;
                        }
                        item.Progress = total > 0 ? (double)done / total : -1;
                        item.Status = total > 0
                            ? $"{what}...  {Mb(done)} of {Mb(total)}"
                            : $"{what}...  {Mb(done)}";
                    }
                    if (total > 0 && done != total) throw new LavagonException("the download was interrupted. Please retry");
                }
                TryDelete(file);
                File.Move(partial, file);
            }
        }

        private static string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.0" : "0") + " MB";

        /// <summary>
        /// Extracts into a hidden temp folder next to the target (Versions ignores names starting with "."),
        /// finds the folder holding the tool (zips often wrap it, e.g. Apache24\ or mysql-8.4.x-winx64\)
        /// and moves that into place, so a half-extracted version never shows up in Lavagon.
        /// </summary>
        private static void Extract(string zipFile, string tool, string target, Item item, string label)
        {
            var temp = Path.Combine(AppPaths.Bin, tool, ".setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(temp);
                var root = Path.GetFullPath(temp).TrimEnd('\\') + "\\";
                using (var zip = ZipFile.OpenRead(zipFile))
                {
                    var entries = zip.Entries;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        var dest = Path.GetFullPath(Path.Combine(temp, e.FullName.Replace('/', '\\')));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                        if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                        item.Progress = (i + 1.0) / entries.Count;
                        item.Status = $"Extracting {label}...  {(int)(100.0 * (i + 1) / entries.Count)}%";
                    }
                }

                var content = new[] { temp }
                    .Concat(Directory.GetDirectories(temp))
                    .Concat(Directory.GetDirectories(temp).SelectMany(Directory.GetDirectories))
                    .FirstOrDefault(d => HasMarker(d, tool));
                if (content == null) throw new LavagonException($"the {tool} zip does not contain {Marker[tool].Split('|')[0]}");

                if (Directory.Exists(target)) Directory.Delete(target, true); // an incomplete earlier attempt
                Directory.Move(content, target);
            }
            finally
            {
                if (Directory.Exists(temp)) TryDeleteDir(temp);
            }
        }

        /// <summary>Remove temp folders and partial downloads of an interrupted earlier setup.</summary>
        private static void CleanLeftovers()
        {
            foreach (var tool in Marker.Keys)
            {
                var dir = Path.Combine(AppPaths.Bin, tool);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir, ".setup-*")) TryDeleteDir(d);
            }
            var dl = Path.Combine(AppPaths.Tmp, "downloads");
            if (Directory.Exists(dl))
                foreach (var f in Directory.GetFiles(dl)) TryDelete(f);
        }

        // ---- Visual C++ runtime --------------------------------------------------------------

        private const string VcRuntimeUrl = "https://aka.ms/vc14/vc_redist.x64.exe";

        /// <summary>
        /// Installs Microsoft's Visual C++ 2015-2026 runtime (x64) when it is missing or older than the
        /// current one. Apache Lounge, PHP and MySQL builds refuse to start without it. Needs one UAC prompt.
        /// </summary>
        private static async Task InstallVcRuntime(Item item, CancellationToken ct)
        {
            var installed = InstalledVcRuntime();
            var exe = Path.Combine(DownloadDir, "vc_redist.x64.exe");
            await DownloadFile(VcRuntimeUrl, exe, item, "", ct);
            try
            {
                System.Version latest = null;
                try { System.Version.TryParse(FileVersionInfo.GetVersionInfo(exe).ProductVersion, out latest); } catch { /* unknown */ }
                if (installed != null && latest != null && installed >= new System.Version(latest.Major, latest.Minor, latest.Build))
                {
                    item.Status = "✓ " + installed.ToString(3) + " (up to date)";
                    return;
                }

                item.Status = "Installing (please allow the Windows prompt)...";
                item.Progress = -1;
                int code;
                try
                {
                    var psi = new ProcessStartInfo(exe, "/install /quiet /norestart") { UseShellExecute = true, Verb = "runas" };
                    using (var p = Process.Start(psi))
                    {
                        await Task.Run(() => p.WaitForExit(), ct);
                        code = p.ExitCode;
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // UAC prompt declined
                {
                    if (installed != null)
                    {
                        item.State = State.Skipped;
                        item.Status = $"Skipped - keeping {installed.ToString(3)} (Apache may need a newer one)";
                        return;
                    }
                    throw new LavagonException("Windows permission was not given. Click Retry and choose Yes");
                }

                // 3010 = installed, restart recommended; 1638 = a newer version is already installed.
                if (code != 0 && code != 3010 && code != 1638 && code != 1641)
                    throw new LavagonException($"the installer exited with code {code}");
                var now = InstalledVcRuntime() ?? latest;
                item.Status = "✓ " + (now?.ToString(3) ?? "installed") + (code == 3010 ? " (restart Windows if Apache does not start)" : "");
            }
            finally
            {
                TryDelete(exe);
            }
        }

        private static System.Version InstalledVcRuntime()
        {
            foreach (var path in new[] { @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var key = hklm.OpenSubKey(path))
                    {
                        if (key == null || Convert.ToInt32(key.GetValue("Installed", 0)) != 1) continue;
                        return new System.Version(Convert.ToInt32(key.GetValue("Major")), Convert.ToInt32(key.GetValue("Minor")), Convert.ToInt32(key.GetValue("Bld")));
                    }
                }
                catch
                {
                    // unreadable: treat as not installed
                }
            }
            return null;
        }

        // ---- Helpers ------------------------------------------------------------------------

        private static async Task<string> GetString(string url, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                using (var r = await Http.GetAsync(url, cts.Token))
                {
                    if (!r.IsSuccessStatusCode) throw new LavagonException($"{url} returned {(int)r.StatusCode} {r.ReasonPhrase}");
                    return await r.Content.ReadAsStringAsync();
                }
            }
        }

        private static async Task<bool> Exists(string url, CancellationToken ct)
        {
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    using (var req = new HttpRequestMessage(HttpMethod.Head, url))
                    using (var r = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                        return r.IsSuccessStatusCode;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        private static string SortKey(string s) => Regex.Replace(s.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* in use */ }
        }

        private static void TryDeleteDir(string dir)
        {
            try { Directory.Delete(dir, true); } catch { /* in use */ }
        }
    }
}
