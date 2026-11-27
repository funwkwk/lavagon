using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Lavagon.Core
{
    /// <summary>
    /// Updates Lavagon itself from the GitHub releases: finds the newest release, downloads its
    /// Lavagon-Setup-vX.Y.Z.exe, checks it against the published .sha256 file and runs it in update
    /// mode. The setup waits for Lavagon to close, replaces Lavagon.exe only (bin, data, etc, usr and
    /// www are never touched) and starts Lavagon again. Running services keep running meanwhile.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "funwkwk/lavagon";

        public class Release
        {
            public Version Version;
            public string Tag, PageUrl, InstallerUrl, ChecksumUrl, Notes;
        }

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"Lavagon/{Program.Version} (+{Program.HomePage})");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version Current => Version.Parse(Program.Version);

        // ---- Settings (Menu > Preferences > Updates) ----------------------------------------

        public static bool CheckAutomatically
        {
            get => App.Settings.GetBool("updates", "CheckAutomatically", true);
            set => App.Settings.SetBool("updates", "CheckAutomatically", value);
        }

        public static bool InstallAutomatically
        {
            get => App.Settings.GetBool("updates", "InstallAutomatically", false);
            set => App.Settings.SetBool("updates", "InstallAutomatically", value);
        }

        /// <summary>A version the user said "Skip this version" to; not offered again automatically.</summary>
        public static string SkippedVersion
        {
            get => App.Settings.Get("updates", "SkippedVersion", "");
            set => App.Settings.Set("updates", "SkippedVersion", value);
        }

        public static DateTime LastCheck
        {
            get => DateTime.TryParse(App.Settings.Get("updates", "LastCheck", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
            set => App.Settings.Set("updates", "LastCheck", value.ToString("o"));
        }

        // ---- Check ---------------------------------------------------------------------------

        /// <summary>The latest published release, or null when Lavagon is up to date.</summary>
        public static async Task<Release> CheckAsync(CancellationToken ct = default)
        {
            string json;
            using (var r = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct))
            {
                if (r.StatusCode == (HttpStatusCode)403 || r.StatusCode == (HttpStatusCode)429)
                    throw new LavagonException("GitHub is limiting requests from this network right now. Please try again later.");
                if (!r.IsSuccessStatusCode) throw new LavagonException($"GitHub answered {(int)r.StatusCode} {r.ReasonPhrase}.");
                json = await r.Content.ReadAsStringAsync();
            }
            LastCheck = DateTime.UtcNow;

            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            var tag = d.TryGetValue("tag_name", out var t) ? t as string : null;
            if (!Version.TryParse((tag ?? "").TrimStart('v', 'V'), out var version) || version <= Current) return null;
            if (d.TryGetValue("draft", out var draft) && draft is bool isDraft && isDraft) return null;

            var assets = (d.TryGetValue("assets", out var a) ? a as System.Collections.IEnumerable : null)?
                .Cast<object>()
                .OfType<Dictionary<string, object>>()
                .Select(x => (Name: x["name"] as string, Url: x["browser_download_url"] as string))
                .ToList() ?? new List<(string, string)>();
            var installer = assets.FirstOrDefault(x => Regex.IsMatch(x.Name ?? "", @"^Lavagon-Setup.*\.exe$", RegexOptions.IgnoreCase));
            if (installer.Url == null) return null; // a release without an installer can't be applied in place
            var checksum = assets.FirstOrDefault(x => string.Equals(x.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

            return new Release
            {
                Version = version,
                Tag = tag,
                PageUrl = d.TryGetValue("html_url", out var u) ? u as string : $"https://github.com/{Repo}/releases/latest",
                InstallerUrl = installer.Url,
                ChecksumUrl = checksum.Url,
                Notes = Summarize(d.TryGetValue("body", out var b) ? b as string : ""),
            };
        }

        /// <summary>The "What's new" part of the release notes as plain text.</summary>
        private static string Summarize(string markdown)
        {
            var lines = new List<string>();
            foreach (var raw in (markdown ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("## ") && lines.Count > 0) break; // stop at "Install / update"
                if (line.StartsWith("#") || line.Length == 0) continue;
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");
                line = line.Replace("**", "").Replace("`", "");
                line = Regex.Replace(line, @"(?<![\w*])\*([^*\s][^*]*)\*(?![\w*])", "$1"); // *italic*
                line = Regex.Replace(line, @"^[-*] ", "• ");
                lines.Add(line);
            }
            var text = string.Join("\n", lines);
            return text.Length > 700 ? text.Substring(0, 700).TrimEnd() + "..." : text;
        }

        // ---- Download + install -----------------------------------------------------------------

        /// <summary>Downloads and verifies the setup; returns its path.</summary>
        public static async Task<string> DownloadAsync(Release release, CancellationToken ct = default)
        {
            var dir = Path.Combine(AppPaths.Tmp, "update");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir)) { try { File.Delete(old); } catch { /* in use */ } }
            var file = Path.Combine(dir, Path.GetFileName(new Uri(release.InstallerUrl).AbsolutePath));

            var bytes = await Http.GetByteArrayAsync(release.InstallerUrl);
            if (release.ChecksumUrl != null)
            {
                var expected = Regex.Match(await Http.GetStringAsync(release.ChecksumUrl), @"[0-9a-fA-F]{64}").Value;
                string actual;
                using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new LavagonException("The downloaded update is damaged (SHA-256 mismatch). Please try again later.");
            }
            File.WriteAllBytes(file, bytes);
            Log.Info($"Downloaded update {release.Tag} to {file}");
            return file;
        }

        /// <summary>
        /// Starts the setup in update mode. The caller must exit right after: the setup waits for this
        /// process to end, installs into this Lavagon folder and starts Lavagon again.
        /// </summary>
        public static void StartInstaller(string setup)
        {
            var args = $"--update {Process.GetCurrentProcess().Id} --dir \"{AppPaths.Root.TrimEnd('\\')}\"";
            Process.Start(new ProcessStartInfo(setup, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(setup) })?.Dispose();
            Log.Info("Started the update: " + setup + " " + args);
        }
    }
}
