using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Lavagon.Core
{
    /// <summary>Environment (PATH etc.) that Lavagon gives to terminals, services and tools.</summary>
    public static class Env
    {
        /// <summary>Tools that can have several versions: bin\{tool}\{version} (selected in the Menu).</summary>
        public static readonly string[] VersionedTools = { "nodejs", "python", "git" };

        public static string ToolDir(string tool)
        {
            var folder = Versions.Resolve(tool, App.Settings.Get(tool, "Version"));
            return folder == null ? null : Path.Combine(AppPaths.Bin, tool, folder);
        }

        /// <summary>Directories that Lavagon prepends to PATH, in priority order.</summary>
        public static List<string> BinPaths()
        {
            var list = new List<string> { Path.Combine(AppPaths.Usr, "bin") };
            void Add(string dir) { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) list.Add(dir); }

            if (Php.Installed) Add(Php.Dir);
            Add(Path.Combine(AppPaths.Bin, "composer"));
            if (App.Apache.Dir != null) Add(Path.Combine(App.Apache.Dir, "bin"));
            if (App.MySql.Dir != null) Add(Path.Combine(App.MySql.Dir, "bin"));
            if (App.PostgreSql.Dir != null) Add(Path.Combine(App.PostgreSql.Dir, "bin"));
            if (App.MongoDb.Dir != null) Add(Path.Combine(App.MongoDb.Dir, "bin"));
            Add(App.Nginx.Dir);
            Add(App.Redis.Dir);
            Add(App.Memcached.Dir);
            Add(App.Mailpit.Dir);

            var node = ToolDir("nodejs");
            Add(node);
            var py = ToolDir("python");
            Add(py);
            if (py != null) Add(Path.Combine(py, "Scripts"));

            var git = Path.Combine(AppPaths.Bin, "git");
            if (!File.Exists(Path.Combine(git, "cmd", "git.exe"))) git = ToolDir("git") ?? git;
            Add(Path.Combine(git, "cmd"));
            Add(Path.Combine(git, "bin"));
            Add(Path.Combine(git, "usr", "bin"));

            Add(Path.Combine(AppPaths.Bin, "ngrok"));
            Add(Path.Combine(AppPaths.Bin, "notepad++"));
            Add(Path.Combine(AppPaths.Bin, "heidisql"));
            Add(AppPaths.Bin);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Add(Path.Combine(appData, "npm"));
            Add(Path.Combine(appData, "Composer", "vendor", "bin"));
            return list;
        }

        /// <summary>The official Composer download is only composer.phar: add a composer.bat next to it in usr\bin.</summary>
        private static void EnsureComposerShim()
        {
            var phar = Path.Combine(AppPaths.Bin, "composer", "composer.phar");
            if (!File.Exists(phar) || File.Exists(Path.Combine(AppPaths.Bin, "composer", "composer.bat"))) return;
            var shim = Path.Combine(AppPaths.Usr, "bin", "composer.bat");
            if (File.Exists(shim)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(shim));
            File.WriteAllText(shim, "@echo off\r\nphp \"%LAVAGON_ROOT%\\bin\\composer\\composer.phar\" %*\r\n");
        }

        public static Dictionary<string, string> Variables()
        {
            try { EnsureComposerShim(); } catch (Exception ex) { Log.Error(ex); }
            var current = Environment.GetEnvironmentVariable("PATH") ?? "";
            var env = new Dictionary<string, string>
            {
                ["PATH"] = string.Join(";", BinPaths()) + ";" + current,
                ["LAVAGON_ROOT"] = AppPaths.Root,
                ["DOCUMENT_ROOT"] = App.Settings.DocumentRoot,
            };
            if (Php.Installed)
            {
                Php.EnsureIni();
                env["PHPRC"] = Php.IniDir;
            }
            var openssl = App.Apache.Dir == null ? null : Path.Combine(App.Apache.Dir, "conf", "openssl.cnf");
            if (openssl != null && File.Exists(openssl)) env["OPENSSL_CONF"] = openssl;
            return env;
        }

        /// <summary>Open a terminal in the document root with Lavagon's tools on PATH.</summary>
        public static void OpenTerminal(string workDir = null)
        {
            workDir = workDir ?? App.Settings.DocumentRoot;
            var env = Variables();

            var cmder = Path.Combine(AppPaths.Bin, "cmder", "Cmder.exe");
            ProcessStartInfo psi;
            if (File.Exists(cmder))
                psi = new ProcessStartInfo(cmder, $"/START \"{workDir}\"");
            else
                psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/k \"title Lavagon && echo Lavagon terminal - tools from " + AppPaths.Root + " are on PATH\"");

            psi.UseShellExecute = false;
            psi.WorkingDirectory = workDir;
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            Process.Start(psi);
        }

        /// <summary>Run a command line in a visible console window and wait until it closes.</summary>
        public static int RunVisible(string commandLine, string workDir, string title)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                $"/c \"title {title} & {commandLine} & echo. & echo Done. Press any key to close... & pause > nul\"")
            {
                UseShellExecute = false,
                WorkingDirectory = workDir,
            };
            foreach (var kv in Variables()) psi.EnvironmentVariables[kv.Key] = kv.Value;
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ---- User PATH (Tools > Path) ---------------------------------------------------

        private const string EnvKey = "Environment";

        public static void AddToUserPath()
        {
            var paths = BinPaths().Where(p => p.StartsWith(AppPaths.Root, StringComparison.OrdinalIgnoreCase)).ToList();
            var current = GetUserPath();
            var parts = current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith(AppPaths.Root, StringComparison.OrdinalIgnoreCase)).ToList();
            SetUserPath(string.Join(";", paths.Concat(parts)));
        }

        public static void RemoveFromUserPath()
        {
            var parts = GetUserPath().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith(AppPaths.Root, StringComparison.OrdinalIgnoreCase));
            SetUserPath(string.Join(";", parts));
        }

        private static string GetUserPath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(EnvKey))
                return key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        }

        private static void SetUserPath(string value)
        {
            // Environment.SetEnvironmentVariable(User) writes the registry and broadcasts WM_SETTINGCHANGE.
            Environment.SetEnvironmentVariable("Path", value, EnvironmentVariableTarget.User);
        }

        // ---- Run at Windows startup ---------------------------------------------------

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetRunAtStartup(bool enable, string exePath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) return;
                if (enable) key.SetValue("Lavagon", $"\"{exePath}\" --root \"{AppPaths.Root}\"");
                else key.DeleteValue("Lavagon", false);
            }
        }
    }
}
