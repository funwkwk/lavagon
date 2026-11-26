using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Lavagon.Core
{
    public class RunResult
    {
        public int ExitCode;
        public string Output;
        public bool TimedOut;
        public bool Ok => ExitCode == 0 && !TimedOut;
    }

    /// <summary>Process helpers: start hidden services, run tools and capture output, kill trees.</summary>
    public static class Proc
    {
        public static Process StartHidden(string exe, string args, string workDir, IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe, args ?? "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workDir ?? Path.GetDirectoryName(exe),
            };
            ApplyEnv(psi, env);
            Log.Info($"start: \"{exe}\" {args}");
            return Process.Start(psi);
        }

        public static RunResult Run(string exe, string args, string workDir = null, int timeoutMs = 60000,
            IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe, args ?? "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir ?? (Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : AppPaths.Root),
            };
            ApplyEnv(psi, env);

            var output = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { KillTree(p.Id); } catch { /* ignored */ }
                    return new RunResult { ExitCode = -1, TimedOut = true, Output = output.ToString() };
                }
                p.WaitForExit(); // flush async readers
                return new RunResult { ExitCode = p.ExitCode, Output = output.ToString().Trim() };
            }
        }

        private static void ApplyEnv(ProcessStartInfo psi, IDictionary<string, string> env)
        {
            if (env == null) return;
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
        }

        public static bool IsAlive(int pid, string expectedName = null)
        {
            if (pid <= 0) return false;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) return false;
                    return expectedName == null || string.Equals(p.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void KillTree(int pid)
        {
            if (pid <= 0) return;
            var taskkill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
            Run(taskkill, $"/PID {pid} /T /F", Environment.SystemDirectory, 15000);
        }

        public static bool WaitForExit(int pid, int timeoutMs)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                if (!IsAlive(pid)) return true;
                Thread.Sleep(150);
            }
            return !IsAlive(pid);
        }

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInfo info, int size, out int returned);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ProcessBasicInfo
        {
            public IntPtr Reserved1, PebBaseAddress, Reserved2a, Reserved2b, UniqueProcessId, InheritedFromUniqueProcessId;
        }

        /// <summary>Parent process id (0 if unknown).</summary>
        public static int ParentOf(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    var info = new ProcessBasicInfo();
                    int status = NtQueryInformationProcess(p.Handle, 0, ref info,
                        System.Runtime.InteropServices.Marshal.SizeOf(info), out _);
                    return status == 0 ? info.InheritedFromUniqueProcessId.ToInt32() : 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        public static string NameOf(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    string path = null;
                    try { path = p.MainModule?.FileName; } catch { /* access denied for elevated processes */ }
                    return path ?? p.ProcessName;
                }
            }
            catch
            {
                return "PID " + pid;
            }
        }

        // ---- Shell helpers --------------------------------------------------------------

        public static void Shell(string target, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                throw new LavagonException($"Could not open {target}\n{ex.Message}");
            }
        }

        public static void OpenUrl(string url) => Shell(url);

        public static void Explore(string dir)
        {
            Directory.CreateDirectory(dir);
            Shell("explorer.exe", "\"" + dir + "\"");
        }

        /// <summary>Open a text/config file in the configured editor (Notepad++ if bundled, else Notepad).</summary>
        public static void Edit(string file)
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "");
            }
            Shell(Editor(), "\"" + file + "\"");
        }

        public static string Editor()
        {
            var configured = App.Settings.Editor;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            var npp = Path.Combine(AppPaths.Bin, "notepad++", "notepad++.exe");
            if (File.Exists(npp)) return npp;
            return "notepad.exe";
        }
    }
}
