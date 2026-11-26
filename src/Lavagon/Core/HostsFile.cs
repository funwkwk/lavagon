using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Lavagon.Core
{
    /// <summary>
    /// Keeps a Lavagon block in the Windows hosts file. Only the lines between the markers are
    /// touched. Writing needs admin rights; if Lavagon isn't elevated it re-launches itself with
    /// "--apply-hosts" through UAC for just that write.
    /// </summary>
    public static class HostsFile
    {
        private const string Begin = "# >>> Lavagon (auto virtual hosts) - do not edit this block";
        private const string End = "# <<< Lavagon";

        public static string Path => System.IO.Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

        public static void Sync(IEnumerable<string> hostnames)
        {
            var block = BuildBlock(hostnames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h).ToList());
            string current;
            try { current = File.ReadAllText(Path); }
            catch (Exception ex) { Log.Error(ex); return; }

            var updated = ReplaceBlock(current, block);
            if (updated == current) return;

            try
            {
                File.WriteAllText(Path, updated, new UTF8Encoding(false));
                Log.Info("hosts file updated");
                return;
            }
            catch (UnauthorizedAccessException) { /* needs elevation */ }
            catch (IOException ex) { Log.Error(ex); }

            var tmp = System.IO.Path.Combine(AppPaths.Tmp, "hosts.lavagon");
            File.WriteAllText(tmp, block);
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, $"--apply-hosts \"{tmp}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(60000);
                    if (p.ExitCode != 0) Log.Error("--apply-hosts exited with " + p.ExitCode);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Info("hosts update cancelled at the UAC prompt");
            }
        }

        /// <summary>Entry point of the elevated helper: lavagon.exe --apply-hosts file</summary>
        public static int ApplyFromFile(string blockFile)
        {
            try
            {
                var block = File.ReadAllText(blockFile);
                var current = File.ReadAllText(Path);
                var updated = ReplaceBlock(current, block);
                if (updated != current) File.WriteAllText(Path, updated, new UTF8Encoding(false));
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        private static string BuildBlock(List<string> hosts)
        {
            if (hosts.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append(Begin).Append("\r\n");
            foreach (var h in hosts)
            {
                if (!VirtualHosts.IsValidHostname(h)) continue;
                sb.Append("127.0.0.1      ").Append(h).Append("\r\n");
                sb.Append("::1            ").Append(h).Append("\r\n");
            }
            sb.Append(End).Append("\r\n");
            return sb.ToString();
        }

        private static string ReplaceBlock(string content, string block)
        {
            int b = content.IndexOf(Begin, StringComparison.Ordinal);
            int e = content.IndexOf(End, StringComparison.Ordinal);
            string without;
            if (b >= 0 && e > b)
            {
                int after = e + End.Length;
                while (after < content.Length && (content[after] == '\r' || content[after] == '\n')) after++;
                without = content.Substring(0, b) + content.Substring(after);
            }
            else
            {
                without = content;
            }
            if (block.Length == 0) return without;
            if (without.Length > 0 && !without.EndsWith("\n")) without += "\r\n";
            return without + block;
        }

        public static void EditAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Path + "\"") { UseShellExecute = true, Verb = "runas" });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // cancelled
            }
        }
    }
}
