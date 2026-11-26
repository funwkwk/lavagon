using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Lavagon.Core;
using Lavagon.UI;

namespace Lavagon
{
    internal static class Program
    {
        public const string Name = "Lavagon";
        public const string Version = "1.0.1";
        public const string HomePage = "https://github.com/funwkwk/lavagon";

        [STAThread]
        private static int Main(string[] args)
        {
            string root = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--root" && i + 1 < args.Length) root = args[++i];
                else if (a == "--apply-hosts" && i + 1 < args.Length) return HostsFile.ApplyFromFile(args[++i]);
                else if (a == "--export-icon" && i + 1 < args.Length) { Logo.ExportIco(args[++i]); return 0; }
                else if (a == "--export-png" && i + 2 < args.Length) { Logo.ExportPng(args[i + 1], int.Parse(args[i + 2])); return 0; }
            }

            root = root
                   ?? Environment.GetEnvironmentVariable("LAVAGON_ROOT")
                   ?? Path.GetDirectoryName(Application.ExecutablePath);
            root = Path.GetFullPath(root);

            AppPaths.Init(root);
            App.Init();

            string id = "Lavagon_" + StableHash(root.ToLowerInvariant());
            using var mutex = new Mutex(true, id, out bool firstInstance);
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, id + "_show");
            if (!firstInstance)
            {
                // Lavagon is already running from this folder: bring the existing window to front.
                showSignal.Set();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Ui.ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error(e.ExceptionObject as Exception);

            Application.Run(new MainForm(showSignal));
            return 0;
        }

        private static string StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
