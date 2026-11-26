using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Lavagon.Core
{
    /// <summary>
    /// PHP version management (PHP 5.6 up to 8.6). Each PHP version gets its own php.ini in
    /// etc\php\{folder}\php.ini (created from php.ini-development on first use), so bin\ stays
    /// untouched and adding a PHP version is just dropping a new folder in bin\php.
    /// </summary>
    public static class Php
    {
        /// <summary>
        /// Extensions enabled in a fresh php.ini when their DLL exists. Order matters for old PHP:
        /// exif must come after mbstring (PHP &lt; 7.3). "gd2" is the PHP 5/7 name of "gd".
        /// </summary>
        private static readonly string[] DefaultExtensions =
        {
            "bz2", "curl", "fileinfo", "gd", "gd2", "gettext", "intl", "mbstring", "exif", "mysqli", "openssl",
            "pdo_mysql", "pdo_pgsql", "pdo_sqlite", "pgsql", "sqlite3", "sodium", "zip", "xsl",
        };

        public static List<string> All => Versions.List("php");
        public static string Folder => Versions.Resolve("php", App.Settings.Get("php", "Version"));
        public static bool Installed => Folder != null && File.Exists(Path.Combine(Dir, "php.exe"));
        public static string Dir => Folder == null ? null : Path.Combine(AppPaths.Bin, "php", Folder);
        public static string PhpExe => Path.Combine(Dir, "php.exe");
        public static string PhpCgiExe => Path.Combine(Dir, "php-cgi.exe");
        public static string IniDir => Path.Combine(AppPaths.Etc, "php", Folder ?? "none");
        public static string IniPath => Path.Combine(IniDir, "php.ini");

        public static void Select(string folder) => App.Settings.Set("php", "Version", folder);

        /// <summary>Real version from php.exe (folder names can be anything), e.g. "8.3.36".</summary>
        public static string Version
        {
            get
            {
                if (!Installed) return Versions.Parse(Folder);
                try
                {
                    var v = FileVersionInfo.GetVersionInfo(PhpExe).ProductVersion;
                    var m = Regex.Match(v ?? "", @"^\d+\.\d+\.\d+");
                    if (m.Success) return m.Value;
                }
                catch { /* fall back to the folder name */ }
                return Versions.Parse(Folder);
            }
        }

        public static System.Version VersionNumber =>
            System.Version.TryParse(Regex.Match(Version ?? "", @"^\d+\.\d+(\.\d+)?").Value, out var v) ? v : new System.Version(8, 0);

        /// <summary>"extension=curl" works from PHP 7.2; older versions need "extension=php_curl.dll".</summary>
        public static bool ShortExtensionNames => VersionNumber >= new System.Version(7, 2);

        /// <summary>Thread safe builds ship the Apache module DLL; NTS builds don't.</summary>
        public static string ApacheModuleDll
        {
            get
            {
                if (Dir == null) return null;
                foreach (var name in new[] { "php8apache2_4.dll", "php7apache2_4.dll", "php5apache2_4.dll" })
                {
                    var p = Path.Combine(Dir, name);
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        public static bool ThreadSafe =>
            ApacheModuleDll != null || (Dir != null && Directory.GetFiles(Dir, "php*ts.dll").Length > 0);

        public static string ApacheModuleName
        {
            get
            {
                var dll = Path.GetFileName(ApacheModuleDll ?? "");
                if (dll.StartsWith("php7")) return "php7_module";
                if (dll.StartsWith("php5")) return "php5_module";
                return "php_module";
            }
        }

        public static ushort Machine => Installed ? PeFile.Machine(PhpExe) : (ushort)0;

        // ---- Health checks ------------------------------------------------------------------

        private static readonly ConcurrentDictionary<string, string> RuntimeChecked = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Makes sure php.exe can actually run. A missing or outdated Microsoft Visual C++
        /// runtime is the #1 reason a PHP build silently fails on a fresh Windows.
        /// </summary>
        public static void CheckRuntime()
        {
            if (!Installed) throw new LavagonException("PHP is not installed.\n\nExtract PHP into:\n" + Path.Combine(AppPaths.Bin, "php") + "\\<version>");
            var key = PhpExe + "|" + File.GetLastWriteTimeUtc(PhpExe).Ticks;
            if (RuntimeChecked.ContainsKey(key)) return;

            var r = Proc.Run(PhpExe, "-n -v", Dir, 20000);
            if (!r.Ok || r.Output.IndexOf("PHP ", StringComparison.Ordinal) < 0)
                throw new LavagonException($"PHP {Version} ({Folder}) cannot run on this computer.\n\n" + ExplainExitCode(r.ExitCode, Folder) +
                                           (r.Output.Length > 0 ? "\n\n" + r.Output : ""));
            RuntimeChecked[key] = r.Output;
        }

        public static string ExplainExitCode(int code, string folder)
        {
            string vc = Regex.Match(folder ?? "", @"(?i)\b(vc|vs)(\d+)\b").Value.ToUpperInvariant();
            string redist = vc == "VC11"
                ? "Microsoft Visual C++ 2012 Redistributable (x64): https://www.microsoft.com/download/details.aspx?id=30679"
                : vc == "VC9" || vc == "VC6"
                    ? "This build is too old for 64-bit Apache; use PHP 5.5 or newer."
                    : "the latest Microsoft Visual C++ 2015-2022 Redistributable (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe";
            switch (unchecked((uint)code))
            {
                case 0xC0000135: return $"A required DLL is missing. This {vc} build needs {redist}";
                case 0xC0000139: return $"A DLL entry point is missing - your Visual C++ runtime is probably too old. Install {redist}";
                case 0xC000007B: return "The build does not match your Windows (32-bit vs 64-bit). Use the x64 build.";
                default: return $"php.exe exited with code {code}. If it is a missing DLL, install {redist}";
            }
        }

        /// <summary>Extensions that fail to load with the current php.ini (checked with the CLI).</summary>
        public static List<string> FailingExtensions()
        {
            var failed = new List<string>();
            if (!Installed || !File.Exists(IniPath)) return failed;
            var r = Proc.Run(PhpExe, $"-c \"{IniPath}\" -d display_startup_errors=1 -d error_reporting=-1 -m", Dir, 30000,
                new Dictionary<string, string> { ["PATH"] = Dir + ";" + Environment.GetEnvironmentVariable("PATH") });
            foreach (Match m in Regex.Matches(r.Output, @"Unable to load dynamic library '([^']+)'", RegexOptions.IgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('/', '\\'));
                if (name.StartsWith("php_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
                if (!failed.Contains(name, StringComparer.OrdinalIgnoreCase)) failed.Add(name);
            }
            return failed;
        }

        // ---- php.ini ------------------------------------------------------------------------

        public static void EnsureIni()
        {
            if (!Installed || File.Exists(IniPath)) return;
            Directory.CreateDirectory(IniDir);

            // php.ini-development first: a php.ini inside the PHP folder usually belongs to
            // another stack and contains its absolute paths.
            string source = new[] { "php.ini-development", "php.ini-production", "php.ini" }
                .Select(n => Path.Combine(Dir, n)).FirstOrDefault(File.Exists);
            var lines = source != null ? File.ReadAllLines(source).ToList() : new List<string>();

            SetDirective(lines, "extension_dir", Quote(AppPaths.Fwd(Path.Combine(Dir, "ext"))));
            SetDirective(lines, "memory_limit", "512M");
            SetDirective(lines, "post_max_size", "2G");
            SetDirective(lines, "upload_max_filesize", "2G");
            SetDirective(lines, "max_execution_time", "600");
            SetDirective(lines, "max_input_time", "600");
            SetDirective(lines, "max_input_vars", "5000");
            SetDirective(lines, "date.timezone", Quote(LocalTimeZone()));
            SetDirective(lines, "upload_tmp_dir", Quote(AppPaths.Fwd(Path.Combine(AppPaths.Tmp, "upload"))));
            SetDirective(lines, "session.save_path", Quote(AppPaths.Fwd(Path.Combine(AppPaths.Tmp, "sessions"))));
            SetDirective(lines, "SMTP", "127.0.0.1");
            SetDirective(lines, "smtp_port", App.Mailpit.SmtpPort.ToString());
            Directory.CreateDirectory(Path.Combine(AppPaths.Tmp, "upload"));
            Directory.CreateDirectory(Path.Combine(AppPaths.Tmp, "sessions"));

            var ca = Path.Combine(AppPaths.Etc, "ssl", "cacert.pem");
            if (File.Exists(ca))
            {
                SetDirective(lines, "curl.cainfo", Quote(AppPaths.Fwd(ca)));
                SetDirective(lines, "openssl.cafile", Quote(AppPaths.Fwd(ca)));
            }

            var available = AvailableExtensions();
            foreach (var ext in DefaultExtensions)
                if (available.Contains(ext)) SetExtensionInLines(lines, ext, true);

            File.WriteAllLines(IniPath, lines);

            // A fresh php.ini must start cleanly: switch off defaults that can't load here
            // (e.g. pdo_pgsql without libpq on an old build).
            try
            {
                var failing = FailingExtensions();
                if (failing.Count > 0)
                {
                    lines = File.ReadAllLines(IniPath).ToList();
                    foreach (var f in failing) SetExtensionInLines(lines, f, false);
                    File.WriteAllLines(IniPath, lines);
                    Log.Info($"PHP {Version}: disabled extensions that cannot load: {string.Join(", ", failing)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        public static HashSet<string> AvailableExtensions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extDir = Dir == null ? null : Path.Combine(Dir, "ext");
            if (extDir == null || !Directory.Exists(extDir)) return set;
            foreach (var f in Directory.GetFiles(extDir, "php_*.dll"))
                set.Add(Path.GetFileNameWithoutExtension(f).Substring(4));
            return set;
        }

        private static string ExtensionPattern(string name) =>
            @"^\s*(;?)\s*(zend_)?extension\s*=\s*""?(?:[^""]*[\\/])?(?:php_)?" + Regex.Escape(name) + @"(?:\.dll)?""?\s*(;.*)?$";

        /// <summary>All extensions found in ext\ with their enabled state in php.ini.</summary>
        public static List<KeyValuePair<string, bool>> Extensions()
        {
            EnsureIni();
            var lines = File.Exists(IniPath) ? File.ReadAllLines(IniPath) : new string[0];
            return AvailableExtensions().OrderBy(e => e)
                .Select(e =>
                {
                    var rx = new Regex(ExtensionPattern(e), RegexOptions.IgnoreCase);
                    bool on = lines.Any(l => { var m = rx.Match(l); return m.Success && m.Groups[1].Value.Length == 0; });
                    return new KeyValuePair<string, bool>(e, on);
                }).ToList();
        }

        /// <summary>Enable/disable an extension. Returns an error text when it was enabled but cannot load.</summary>
        public static string SetExtension(string name, bool enable)
        {
            EnsureIni();
            var lines = File.ReadAllLines(IniPath).ToList();
            SetExtensionInLines(lines, name, enable);
            File.WriteAllLines(IniPath, lines);
            if (!enable) return null;
            var failing = FailingExtensions();
            return failing.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? $"PHP {Version} could not load the extension '{name}' (a DLL it needs may be missing)."
                : null;
        }

        private static bool IsZendExtension(string name) =>
            name.Equals("opcache", StringComparison.OrdinalIgnoreCase) || name.StartsWith("xdebug", StringComparison.OrdinalIgnoreCase);

        private static string ExtensionLine(string name)
        {
            bool zend = IsZendExtension(name);
            if (ShortExtensionNames) return (zend ? "zend_extension=" : "extension=") + name;
            // PHP < 7.2: file names; zend_extension wants the full path on old builds.
            var file = File.Exists(Path.Combine(Dir, "ext", "php_" + name + ".dll")) ? "php_" + name + ".dll" : name + ".dll";
            return zend
                ? "zend_extension=\"" + AppPaths.Fwd(Path.Combine(Dir, "ext", file)) + "\""
                : "extension=" + file;
        }

        private static void SetExtensionInLines(List<string> lines, string name, bool enable)
        {
            var rx = new Regex(ExtensionPattern(name), RegexOptions.IgnoreCase);
            bool done = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var m = rx.Match(lines[i]);
                if (!m.Success) continue;
                bool active = m.Groups[1].Value.Length == 0;
                if (enable && !done) { lines[i] = ExtensionLine(name); done = true; }
                else if (active) lines[i] = ";" + lines[i].TrimStart(); // disable, or remove duplicates
            }
            if (enable && !done) lines.Add(ExtensionLine(name));
        }

        /// <summary>Set "key = value": replace the active line, else the last commented one, else append.</summary>
        public static void SetDirective(List<string> lines, string key, string value)
        {
            var active = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
            var commented = new Regex(@"^\s*;\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
            int idx = lines.FindIndex(l => active.IsMatch(l));
            if (idx < 0) idx = lines.FindLastIndex(l => commented.IsMatch(l));
            var line = $"{key} = {value}";
            if (idx >= 0) lines[idx] = line; else lines.Add(line);
        }

        public static string GetDirective(string key)
        {
            if (!File.Exists(IniPath)) return "";
            var active = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=\s*(.*)$", RegexOptions.IgnoreCase);
            foreach (var l in File.ReadAllLines(IniPath))
            {
                var m = active.Match(l);
                if (m.Success) return m.Groups[1].Value.Trim().Trim('"');
            }
            return "";
        }

        public static void UpdateDirective(string key, string value)
        {
            EnsureIni();
            var lines = File.ReadAllLines(IniPath).ToList();
            SetDirective(lines, key, value);
            File.WriteAllLines(IniPath, lines);
        }

        /// <summary>
        /// DLLs shipped in the PHP folder (php8ts.dll, libcrypto, libssh2, icu, libpq...) in
        /// dependency order. Apache preloads them so PHP never picks up same-named DLLs of another
        /// version from Apache's bin folder, System32 or PATH (e.g. a php8ts.dll of PHP 8.0 copied
        /// next to httpd.exe by another stack would otherwise be used for PHP 8.1).
        /// </summary>
        public static List<string> SupportDlls()
        {
            if (!Installed) return new List<string>();
            var dlls = Directory.GetFiles(Dir, "*.dll")
                .Where(p => !Regex.IsMatch(Path.GetFileName(p), @"^php\d*(apache|embed|phpdbg).*\.dll$", RegexOptions.IgnoreCase))
                .ToList();
            return PeFile.DependencyOrder(dlls);
        }

        private static string Quote(string s) => "\"" + s + "\"";

        // ---- Time zone ----------------------------------------------------------------------

        [DllImport("icu.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucal_getTimeZoneIDForWindowsID(string winid, int len,
            [MarshalAs(UnmanagedType.LPStr)] string region, StringBuilder id, int idCapacity, ref int status);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultGeoName(StringBuilder geoName, int geoNameCount);

        /// <summary>IANA name of the Windows time zone (e.g. "Asia/Jakarta"), "UTC" when unknown.</summary>
        public static string LocalTimeZone()
        {
            try
            {
                var win = TimeZoneInfo.Local.Id;
                // Windows "home location" (Settings > Region > Country) beats the format culture:
                // an en-US format on a PC located in Indonesia should give Asia/Jakarta.
                string region = null;
                try
                {
                    var geo = new StringBuilder(16);
                    if (GetUserDefaultGeoName(geo, geo.Capacity) > 0 && geo.Length == 2) region = geo.ToString();
                }
                catch { /* Windows < 10 1709 */ }
                if (region == null)
                    try { region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch { /* no region */ }
                var sb = new StringBuilder(128);
                int status = 0;
                int len = ucal_getTimeZoneIDForWindowsID(win, win.Length, region, sb, sb.Capacity, ref status);
                if (status > 0 || len <= 0)
                {
                    // Region-specific lookup failed: fall back to the zone's main city.
                    status = 0;
                    len = ucal_getTimeZoneIDForWindowsID(win, win.Length, null, sb, sb.Capacity, ref status);
                }
                if (status <= 0 && len > 0)
                {
                    var id = sb.ToString(0, Math.Min(len, sb.Length));
                    if (Regex.IsMatch(id, @"^[A-Za-z_]+(/[A-Za-z0-9_\-+]+)*$")) return id;
                }
            }
            catch
            {
                // icu.dll is only on Windows 10 1903+.
            }
            return "UTC";
        }
    }
}
