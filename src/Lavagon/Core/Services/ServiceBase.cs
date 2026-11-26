using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Lavagon.Core.Services
{
    /// <summary>
    /// A background server (Apache, MySQL, Redis...). Binaries come from bin\{Tool}\{version}\,
    /// the started process id is kept in tmp\pids\{Key}.pid so Lavagon can find its services again
    /// after a restart of the UI.
    /// </summary>
    public abstract class ServiceBase
    {
        public abstract string Key { get; }
        public abstract string Title { get; }
        public virtual string Tool => Key;
        public virtual bool DefaultUse => false;

        /// <summary>Candidate executables relative to the version folder; the first one found is used.</summary>
        protected abstract string[] ExeCandidates { get; }

        public abstract int[] Ports { get; }
        public virtual string PortLabel => string.Join("/", Ports);

        /// <summary>Text shown in the third column (e.g. "Reload") or null.</summary>
        public virtual string ActionLabel => null;
        public virtual void Action() { }

        /// <summary>How long to watch a freshly started process before declaring success.</summary>
        protected virtual int StartupCheckMs => 1500;

        /// <summary>Upper bound for waiting until the service listens on its ports.</summary>
        protected virtual int StartupTimeoutSeconds => 20;

        public List<string> AllVersions => Versions.List(Tool);
        public string Folder => Versions.Resolve(Tool, App.Settings.Get(Key, "Version"));
        public string Dir => Folder == null ? null : Path.Combine(AppPaths.Bin, Tool, Folder);
        public virtual string Version => Versions.Parse(Folder);

        public string ExePath
        {
            get
            {
                if (Dir == null) return null;
                return ExeCandidates.Select(c => Path.Combine(Dir, c)).FirstOrDefault(File.Exists);
            }
        }

        public bool Installed => ExePath != null;

        public bool Enabled
        {
            get => App.Settings.GetBool(Key, "Use", DefaultUse);
            set => App.Settings.SetBool(Key, "Use", value);
        }

        public void SelectVersion(string folder) => App.Settings.Set(Key, "Version", folder);

        protected int Port(string name, int def) => App.Settings.GetInt(Key, name, def);

        // ---- Process tracking -----------------------------------------------------------

        private string PidFile => Path.Combine(AppPaths.PidDir, Key + ".pid");

        protected int ReadPid()
        {
            try { return File.Exists(PidFile) ? int.Parse(File.ReadAllText(PidFile).Trim()) : 0; }
            catch { return 0; }
        }

        protected void WritePid(int pid) => File.WriteAllText(PidFile, pid.ToString());

        protected void ClearPid()
        {
            try { File.Delete(PidFile); } catch { /* ignored */ }
        }

        protected string ProcessName => Path.GetFileNameWithoutExtension(ExePath ?? ExeCandidates[0]);

        public virtual bool IsRunning => Proc.IsAlive(ReadPid(), ProcessName);

        // ---- Lifecycle -----------------------------------------------------------------

        public void Start()
        {
            if (!Installed)
                throw new LavagonException($"{Title} is not installed.\n\nExtract {Title} into:\n{Path.Combine(AppPaths.Bin, Tool)}\\<version>");
            if (IsRunning) return;

            CheckPortsFree();
            Prepare();

            var p = Launch();
            if (p != null)
            {
                WritePid(p.Id);
                // Wait until the process listens on its ports (or dies). Watch at least StartupCheckMs
                // because some servers (e.g. Apache with a broken module) die a moment after binding.
                var start = DateTime.UtcNow;
                var minUntil = start.AddMilliseconds(StartupCheckMs);
                var maxUntil = start.AddSeconds(StartupTimeoutSeconds);
                while (DateTime.UtcNow < maxUntil)
                {
                    if (p.HasExited) break;
                    if (DateTime.UtcNow >= minUntil && Ports.Where(x => x > 0).All(Net.IsListening)) break;
                    Thread.Sleep(150);
                }
                if (p.HasExited)
                {
                    ClearPid();
                    throw new LavagonException($"{Title} could not start (exit code {p.ExitCode}).\n\n{StartFailureDetails()}".TrimEnd());
                }
            }
            Log.Info($"{Title} started");
        }

        public virtual void Stop()
        {
            int pid = ReadPid();
            if (pid > 0 && Proc.IsAlive(pid, ProcessName)) Proc.KillTree(pid);
            ClearPid();
            Log.Info($"{Title} stopped");
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        /// <summary>Write configuration, initialize data directories, etc.</summary>
        protected virtual void Prepare() { }

        protected abstract Process Launch();

        protected virtual string StartFailureDetails() => "";

        protected void CheckPortsFree()
        {
            foreach (var port in Ports.Where(p => p > 0))
            {
                if (!Net.IsListening(port)) continue;
                int owner = Net.PortOwner(port);
                var who = owner > 0 ? Proc.NameOf(owner) : "another program";
                throw new LavagonException(
                    $"Port {port} needed by {Title} is already in use by:\n{who}" + (owner > 0 ? $" (PID {owner})" : "") +
                    "\n\nStop that program (for example another WAMP stack) or change the port in Menu > Preferences > Services & Ports.");
            }
        }

        protected static string Tail(string file, int lines = 15)
        {
            try
            {
                if (!File.Exists(file)) return "";
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    var all = sr.ReadToEnd().Split('\n');
                    return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines))).Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        protected static void WriteIfChanged(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            File.WriteAllText(path, content);
        }

        protected Dictionary<string, string> ServiceEnv() => Env.Variables();

        public override string ToString() => $"{Title} {Version}";
    }
}
