using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Lavagon.Setup
{
    public class InstallOptions
    {
        public string Directory;
        public bool StartMenuShortcut = true;
        public bool DesktopShortcut = true;
        public bool Register = true;
    }

    /// <summary>
    /// Per-user installer: extracts the embedded portable zip, adds shortcuts and an entry in
    /// Settings &gt; Apps (HKCU, no admin). Upgrades and the uninstaller never touch user data
    /// (bin, data, etc, usr, www, logs, tmp, backup).
    /// </summary>
    public static class Installer
    {
        public const string AppName = "Lavagon";
        public const string Publisher = "Lavagon contributors";
        public const string HomePage = "https://github.com/funwkwk/lavagon";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lavagon";

        /// <summary>Files Lavagon owns: always replaced on install, removed on uninstall.</summary>
        private static readonly string[] ProgramFiles = { "Lavagon.exe", "LICENSE", "uninstall.exe" };

        public static string Version
        {
            get
            {
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
        }

        public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("Lavagon.Setup.payload.zip");

        public static string DefaultDirectory
        {
            get
            {
                var previous = InstalledLocation;
                if (!string.IsNullOrEmpty(previous)) return previous;
                return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "lavagon");
            }
        }

        public static string InstalledLocation
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return key?.GetValue("InstallLocation") as string;
            }
        }

        public static void Install(InstallOptions o, Action<int, string> progress)
        {
            if (!HasPayload) throw new InvalidOperationException("This setup does not contain Lavagon (it was built without a payload).");
            var dir = Path.GetFullPath(o.Directory.Trim());
            EnsureNotRunning(dir);
            Directory.CreateDirectory(dir);

            // 1. Extract
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lavagon.Setup.payload.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entries = zip.Entries.ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var rel = StripRoot(e.FullName);
                    if (string.IsNullOrEmpty(rel)) continue;
                    var target = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!target.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard

                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        bool owned = ProgramFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)
                                     || rel.Equals(@"bin\README.txt", StringComparison.OrdinalIgnoreCase);
                        // Never overwrite user content (e.g. www\index.php) on upgrade.
                        if (owned || !File.Exists(target)) e.ExtractToFile(target, true);
                    }
                    progress?.Invoke(10 + 70 * (i + 1) / entries.Count, "Copying " + rel);
                }
            }

            // 2. Uninstaller = a copy of this setup
            progress?.Invoke(82, "Creating uninstaller");
            var self = Assembly.GetExecutingAssembly().Location;
            var uninstaller = Path.Combine(dir, "uninstall.exe");
            if (!string.Equals(Path.GetFullPath(self), uninstaller, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);

            // 3. Shortcuts
            var exe = Path.Combine(dir, "Lavagon.exe");
            progress?.Invoke(88, "Creating shortcuts");
            if (o.StartMenuShortcut) CreateShortcut(StartMenuShortcut, exe, dir);
            if (o.DesktopShortcut) CreateShortcut(DesktopShortcut, exe, dir);

            // 4. Settings > Apps entry
            if (o.Register)
            {
                progress?.Invoke(95, "Registering");
                using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key.SetValue("DisplayName", AppName);
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("Publisher", Publisher);
                    key.SetValue("DisplayIcon", exe);
                    key.SetValue("InstallLocation", dir);
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
                    key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall --silent");
                    key.SetValue("URLInfoAbout", HomePage);
                    key.SetValue("HelpLink", HomePage + "/issues");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", (int)(ProgramFiles.Select(f => Path.Combine(dir, f)).Where(File.Exists).Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
                }
            }
            progress?.Invoke(100, "Done");
        }

        /// <summary>Removes Lavagon's program files, shortcuts and registration. User data stays.</summary>
        public static void Uninstall(string dir)
        {
            dir = Path.GetFullPath(dir);
            EnsureNotRunning(dir);
            foreach (var f in ProgramFiles)
            {
                var p = Path.Combine(dir, f);
                if (File.Exists(p) && !IsSelf(p)) DeleteWithRetry(p);
            }
            var binReadme = Path.Combine(dir, "bin", "README.txt");
            if (File.Exists(binReadme)) File.Delete(binReadme);
            foreach (var s in new[] { StartMenuShortcut, DesktopShortcut })
                if (File.Exists(s) && ShortcutPointsInto(s, dir)) File.Delete(s);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            DeleteEmptyDirectories(dir);
        }

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lavagon.lnk");

        public static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lavagon.lnk");

        private static string StripRoot(string entry)
        {
            var parts = entry.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].Equals("Lavagon", StringComparison.OrdinalIgnoreCase)) parts = parts.Skip(1).ToArray();
            return string.Join("\\", parts);
        }

        private static void EnsureNotRunning(string dir)
        {
            var exe = Path.Combine(dir, "Lavagon.exe");
            foreach (var p in Process.GetProcessesByName("Lavagon"))
            {
                string path = null;
                try { path = p.MainModule?.FileName; } catch { /* other user / elevated */ }
                if (path != null && string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Lavagon is running from this folder.\n\nExit Lavagon first (Menu > Exit), then try again.");
            }
        }

        private static bool IsSelf(string path) =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(Assembly.GetExecutingAssembly().Location), StringComparison.OrdinalIgnoreCase);

        /// <summary>The launching uninstall.exe may still be exiting for a moment.</summary>
        private static void DeleteWithRetry(string path)
        {
            for (int i = 0; ; i++)
            {
                try { File.Delete(path); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && i < 40)
                {
                    System.Threading.Thread.Sleep(250);
                }
            }
        }

        private static void DeleteEmptyDirectories(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var d in Directory.GetDirectories(dir)) DeleteEmptyDirectories(d);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                try { Directory.Delete(dir); } catch { /* in use */ }
        }

        // ---- Shortcuts through WScript.Shell (no extra dependencies) --------------------

        private static void CreateShortcut(string lnk, string target, string workDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Lavagon - local development environment" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        public static bool ShortcutPointsInto(string lnk, string dir)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var target = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
                return target != null && target.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
