using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Lavagon.Core.Services
{
    public class PostgreSqlService : ServiceBase
    {
        public override string Key => "postgresql";
        public override string Title => "PostgreSQL";
        protected override string[] ExeCandidates => new[] { @"bin\postgres.exe", @"pgsql\bin\postgres.exe" };
        protected override int StartupCheckMs => 2500;

        public int TcpPort => Port("Port", 5432);
        public override int[] Ports => new[] { TcpPort };
        private string BinDir => Path.GetDirectoryName(ExePath);
        public string DataDir => Path.Combine(AppPaths.Data, "postgresql-" + Version.Split('.')[0]);
        private string LogFile => Path.Combine(AppPaths.Logs, "postgresql.log");

        protected override void Prepare()
        {
            if (File.Exists(Path.Combine(DataDir, "PG_VERSION"))) return;
            Directory.CreateDirectory(DataDir);
            var r = Proc.Run(Path.Combine(BinDir, "initdb.exe"), $"-D \"{DataDir}\" -U postgres -A trust -E UTF8 --no-locale", BinDir, 180000);
            if (!r.Ok)
            {
                try { Directory.Delete(DataDir, true); } catch { /* ignored */ }
                throw new LavagonException("Could not initialize PostgreSQL data directory:\n\n" + r.Output);
            }
        }

        protected override Process Launch() =>
            Proc.StartHidden(ExePath, $"-D \"{DataDir}\" -p {TcpPort} -c logging_collector=off", BinDir);

        public override void Stop()
        {
            var pgCtl = Path.Combine(BinDir ?? "", "pg_ctl.exe");
            int pid = ReadPid();
            if (pid > 0 && Proc.IsAlive(pid, ProcessName) && File.Exists(pgCtl))
            {
                Proc.Run(pgCtl, $"stop -D \"{DataDir}\" -m fast -w", BinDir, 20000);
                Proc.WaitForExit(pid, 5000);
            }
            base.Stop();
        }

        protected override string StartFailureDetails() => Tail(LogFile);
    }

    public class MongoDbService : ServiceBase
    {
        public override string Key => "mongodb";
        public override string Title => "MongoDB";
        protected override string[] ExeCandidates => new[] { @"bin\mongod.exe", "mongod.exe" };
        protected override int StartupCheckMs => 2500;

        public int TcpPort => Port("Port", 27017);
        public override int[] Ports => new[] { TcpPort };
        private string DataDir => Path.Combine(AppPaths.Data, "mongodb");
        private string LogFile => Path.Combine(AppPaths.Logs, "mongod.log");

        protected override void Prepare() => Directory.CreateDirectory(DataDir);

        protected override Process Launch() =>
            Proc.StartHidden(ExePath, $"--dbpath \"{DataDir}\" --port {TcpPort} --bind_ip 127.0.0.1 --logpath \"{LogFile}\" --logappend", Dir);

        protected override string StartFailureDetails() => Tail(LogFile);
    }

    public class RedisService : ServiceBase
    {
        public override string Key => "redis";
        public override string Title => "Redis";
        protected override string[] ExeCandidates => new[] { "redis-server.exe", @"bin\redis-server.exe" };

        public int TcpPort => Port("Port", 6379);
        public override int[] Ports => new[] { TcpPort };
        private string DataDir => Path.Combine(AppPaths.Data, "redis");

        protected override void Prepare() => Directory.CreateDirectory(DataDir);

        protected override Process Launch() =>
            Proc.StartHidden(ExePath, $"--port {TcpPort} --bind 127.0.0.1 --dir \"{AppPaths.Fwd(DataDir)}\"", Dir);
    }

    public class MemcachedService : ServiceBase
    {
        public override string Key => "memcached";
        public override string Title => "Memcached";
        protected override string[] ExeCandidates => new[] { "memcached.exe", @"bin\memcached.exe" };

        public int TcpPort => Port("Port", 11211);
        public override int[] Ports => new[] { TcpPort };

        protected override Process Launch() => Proc.StartHidden(ExePath, $"-p {TcpPort} -m 64", Dir);
    }

    /// <summary>Mailpit catches every e-mail sent by your apps (SMTP) and shows them in a web UI.</summary>
    public class MailpitService : ServiceBase
    {
        public override string Key => "mailpit";
        public override string Title => "Mailpit";
        public override bool DefaultUse => true;
        protected override string[] ExeCandidates => new[] { "mailpit.exe" };

        public int SmtpPort => Port("SmtpPort", 1025);
        public int HttpPort => Port("HttpPort", 8025);
        public override int[] Ports => new[] { SmtpPort, HttpPort };
        public string WebUrl => $"http://localhost:{HttpPort}";
        private string DataDir => Path.Combine(AppPaths.Data, "mailpit");

        protected override void Prepare() => Directory.CreateDirectory(DataDir);

        protected override Process Launch() =>
            Proc.StartHidden(ExePath,
                $"--smtp 127.0.0.1:{SmtpPort} --listen 127.0.0.1:{HttpPort} --database \"{Path.Combine(DataDir, "mailpit.db")}\"", Dir);
    }
}
