using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Lavagon.Setup
{
    /// <summary>
    /// Lavagon-Setup.exe
    ///   (no arguments)                       setup wizard
    ///   --silent [--dir D] [--no-desktop] [--no-startmenu] [--no-register] [--launch]
    ///   --uninstall [--silent] [--dir D]      remove Lavagon (user data is kept)
    /// Exit codes: 0 ok, 1 error, 2 cancelled.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            string Value(string flag)
            {
                int i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool silent = Has("--silent");

            try
            {
                if (Has("--uninstall")) return Uninstall(Value("--dir"), silent, Has("--from-temp"));

                if (silent)
                {
                    var o = new InstallOptions
                    {
                        Directory = Value("--dir") ?? Installer.DefaultDirectory,
                        DesktopShortcut = !Has("--no-desktop"),
                        StartMenuShortcut = !Has("--no-startmenu"),
                        Register = !Has("--no-register"),
                    };
                    Installer.Install(o, null);
                    if (Has("--launch")) Process.Start(Path.Combine(o.Directory, "Lavagon.exe"));
                    return 0;
                }

                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, "Lavagon Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static int Uninstall(string dir, bool silent, bool fromTemp)
        {
            var self = Assembly.GetExecutingAssembly().Location;
            dir = dir ?? Installer.InstalledLocation ?? Path.GetDirectoryName(self);

            // An exe cannot delete itself: run the uninstaller from a temporary copy.
            if (!fromTemp && string.Equals(Path.GetDirectoryName(Path.GetFullPath(self)), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                var temp = Path.Combine(Path.GetTempPath(), "Lavagon-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(self, temp, true);
                var args = $"--uninstall --from-temp --dir \"{dir.TrimEnd('\\')}\"" + (silent ? " --silent" : "");
                // Don't wait: this process must exit so the copy can delete uninstall.exe.
                Process.Start(new ProcessStartInfo(temp, args) { UseShellExecute = false })?.Dispose();
                return 0;
            }

            if (!silent)
            {
                var answer = MessageBox.Show(
                    $"Remove Lavagon from\n{dir}\n\nYour projects and data stay where they are (www, data, bin, etc, usr).\n" +
                    "Tip: click Stop in Lavagon first so no server keeps running.",
                    "Uninstall Lavagon", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) return 2;
            }

            Installer.Uninstall(dir);

            if (!silent)
            {
                var left = Directory.Exists(dir)
                    ? $"\n\nYour files are still in:\n{dir}\nDelete that folder yourself if you no longer need them."
                    : "";
                MessageBox.Show("Lavagon was removed." + left, "Uninstall Lavagon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (fromTemp) ScheduleDelete(self);
            return 0;
        }

        private static void ScheduleDelete(string file)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                $"/c ping -n 3 127.0.0.1 > nul & del /f /q \"{file}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
    }
}
