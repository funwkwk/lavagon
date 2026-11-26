using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Lavagon.Core.Services
{
    /// <summary>MySQL or MariaDB (both live in bin\mysql\{version}).</summary>
    public class MySqlService : ServiceBase
    {
        public override string Key => "mysql";
        public override string Title => IsMariaDb ? "MariaDB" : "MySQL";
        public override bool DefaultUse => true;
        protected override string[] ExeCandidates => new[] { @"bin\mysqld.exe", @"bin\mariadbd.exe" };
        protected override int StartupCheckMs => 2500;

        public int TcpPort => Port("Port", 3306);
        public override int[] Ports => new[] { TcpPort };

        public bool IsMariaDb => (Folder ?? "").IndexOf("mariadb", StringComparison.OrdinalIgnoreCase) >= 0;

        public string BinDir => Path.Combine(Dir, "bin");
        public string ClientExe => new[] { "mysql.exe", "mariadb.exe" }.Select(e => Path.Combine(BinDir, e)).FirstOrDefault(File.Exists);
        public string AdminExe => new[] { "mysqladmin.exe", "mariadb-admin.exe" }.Select(e => Path.Combine(BinDir, e)).FirstOrDefault(File.Exists);
        public string DumpExe => new[] { "mysqldump.exe", "mariadb-dump.exe" }.Select(e => Path.Combine(BinDir, e)).FirstOrDefault(File.Exists);

        public string IniFile => Path.Combine(AppPaths.Etc, "mysql", Folder ?? "none", "my.ini");
        public string ErrorLog => Path.Combine(AppPaths.Logs, "mysqld.log");

        /// <summary>Data directory: data\mysql-8.4 by default (one per major.minor, data is not portable across them).</summary>
        public string DataDir
        {
            get
            {
                var custom = App.Settings.Get(Key, "DataDir");
                if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(Path.Combine(AppPaths.Root, custom));
                return Path.Combine(AppPaths.Data, (IsMariaDb ? "mariadb-" : "mysql-") + Versions.MajorMinor(Version));
            }
            set => App.Settings.Set(Key, "DataDir", value);
        }

        public string RootPassword
        {
            get => App.Settings.GetSecret(Key, "RootPassword");
            set => App.Settings.SetSecret(Key, "RootPassword", value);
        }

        protected override void Prepare()
        {
            if (!File.Exists(IniFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IniFile));
                File.WriteAllText(IniFile, Templates.MyIni);
            }

            if (!Directory.Exists(DataDir) || !Directory.EnumerateFileSystemEntries(DataDir).Any())
                InitializeDataDir();
        }

        public System.Version VersionNumber =>
            System.Version.TryParse(Versions.MajorMinor(Version), out var v) ? v : new System.Version(8, 0);

        /// <summary>
        /// MySQL 8.x defaults to caching_sha2_password, which PHP &lt; 7.4 (mysqlnd) cannot use.
        /// "Legacy authentication" switches root to mysql_native_password (gone in MySQL 9).
        /// </summary>
        public bool LegacyAuthApplies => !IsMariaDb && VersionNumber >= new System.Version(8, 0);
        public bool LegacyAuthAvailable => LegacyAuthApplies && VersionNumber < new System.Version(9, 0);

        public bool LegacyAuth
        {
            get => App.Settings.GetBool(Key, "LegacyAuth", false);
            private set => App.Settings.SetBool(Key, "LegacyAuth", value);
        }

        private string ServerArgs()
        {
            var args = $"--defaults-file=\"{IniFile}\" --basedir=\"{AppPaths.Fwd(Dir)}\" --datadir=\"{AppPaths.Fwd(DataDir)}\" --port={TcpPort}";
            // MySQL 8.4 ships mysql_native_password disabled; keep it loadable for old PHP clients.
            if (!IsMariaDb && VersionNumber >= new System.Version(8, 4) && VersionNumber < new System.Version(9, 0))
                args += " --mysql-native-password=ON";
            // Old mysqlnd (PHP 5.6/7.0) fails if the server greets with caching_sha2_password,
            // even when the account itself uses mysql_native_password.
            if (LegacyAuth && LegacyAuthAvailable)
                args += VersionNumber >= new System.Version(8, 4)
                    ? " --authentication-policy=mysql_native_password"
                    : " --default-authentication-plugin=mysql_native_password";
            return args;
        }

        public void SetLegacyAuth(bool enable)
        {
            if (!LegacyAuthAvailable)
                throw new LavagonException($"{Title} {Version} does not support mysql_native_password.\nUse MySQL 8.x or MariaDB for PHP < 7.4.");
            var plugin = enable ? "mysql_native_password" : "caching_sha2_password";
            var pwd = RootPassword.Replace("\\", "\\\\").Replace("'", "\\'");
            var r = Sql($"ALTER USER 'root'@'localhost' IDENTIFIED WITH {plugin} BY '{pwd}'; FLUSH PRIVILEGES;");
            if (!r.Ok) throw new LavagonException("Could not change the authentication method:\n" + r.Output);
            LegacyAuth = enable;
            Restart(); // the server's default plugin is a startup option
        }

        /// <summary>SQL that sets the root password, keeping the chosen authentication plugin.</summary>
        public string SetPasswordSql(string newPassword)
        {
            var escaped = (newPassword ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
            var with = LegacyAuth && LegacyAuthAvailable ? "WITH mysql_native_password " : "";
            if (!IsMariaDb && VersionNumber < new System.Version(5, 7))
                return $"SET PASSWORD FOR 'root'@'localhost' = PASSWORD('{escaped}'); FLUSH PRIVILEGES;";
            return $"ALTER USER 'root'@'localhost' IDENTIFIED {with}BY '{escaped}'; FLUSH PRIVILEGES;";
        }

        private void InitializeDataDir()
        {
            Directory.CreateDirectory(DataDir);
            RunResult r;
            var installDb = new[] { "mariadb-install-db.exe", "mysql_install_db.exe" }
                .Select(e => Path.Combine(BinDir, e)).FirstOrDefault(File.Exists);
            var shippedData = Path.Combine(Dir, "data");

            if (!IsMariaDb && VersionNumber < new System.Version(5, 7) && Directory.Exists(shippedData))
            {
                // MySQL 5.5/5.6 zips ship a ready data directory instead of --initialize.
                CopyDirectory(shippedData, DataDir);
                r = new RunResult { ExitCode = 0, Output = "" };
            }
            else if (IsMariaDb && installDb != null)
                r = Proc.Run(installDb, $"--datadir=\"{DataDir}\" --port={TcpPort}", BinDir, 180000);
            else
                r = Proc.Run(ExePath, ServerArgs() + " --initialize-insecure --console", BinDir, 180000);

            if (!r.Ok)
            {
                try { Directory.Delete(DataDir, true); } catch { /* ignored */ }
                throw new LavagonException($"Could not initialize the {Title} data directory:\n{DataDir}\n\n{r.Output}");
            }
            RootPassword = "";
            LegacyAuth = false;
            Log.Info($"{Title} data directory initialized: {DataDir}");
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }

        protected override Process Launch() =>
            Proc.StartHidden(ExePath, ServerArgs() + $" --log-error=\"{AppPaths.Fwd(ErrorLog)}\" --standalone", BinDir);

        protected override string StartFailureDetails() => Tail(ErrorLog);

        public override void Stop()
        {
            int pid = ReadPid();
            // Graceful shutdown first so InnoDB doesn't need crash recovery - but only when the
            // server listening on our port is the one Lavagon started (or its child process),
            // so another MySQL on this PC (another stack, a Windows service) is never touched.
            if (pid > 0 && Proc.IsAlive(pid, ProcessName) && AdminExe != null && OwnsPort(pid))
            {
                using (var cred = new ClientCredentials(RootPassword))
                    Proc.Run(AdminExe, $"{cred.Arg} -h 127.0.0.1 -P {TcpPort} -u root shutdown", BinDir, 15000);
                Proc.WaitForExit(pid, 10000);
            }
            base.Stop();
        }

        private bool OwnsPort(int pid)
        {
            int owner = Net.PortOwner(TcpPort);
            return owner > 0 && (owner == pid || Proc.ParentOf(owner) == pid);
        }

        /// <summary>Run SQL as root through the mysql client.</summary>
        public RunResult Sql(string sql, string password = null)
        {
            if (!IsRunning) throw new LavagonException($"Please start {Title} first!");
            if (ClientExe == null) throw new LavagonException($"{Title} client (mysql.exe) was not found in {BinDir}");
            var tmp = Path.Combine(AppPaths.Tmp, "lavagon-" + Guid.NewGuid().ToString("N") + ".sql");
            File.WriteAllText(tmp, sql);
            try
            {
                using (var cred = new ClientCredentials(password ?? RootPassword))
                    return Proc.Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        $"/c \"\"{ClientExe}\" {cred.Arg} -h 127.0.0.1 -P {TcpPort} -u root --batch < \"{tmp}\"\"", BinDir, 60000);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* ignored */ }
            }
        }

        /// <summary>
        /// Writes the password to a temporary option file so it never shows up on a command line.
        /// Passed with --defaults-file, so the client reads ONLY this file and never picks up a
        /// my.ini of another stack (e.g. one with [client] port=3306) from its base directory.
        /// </summary>
        private sealed class ClientCredentials : IDisposable
        {
            private readonly string _file;
            public string Arg { get; }

            public ClientCredentials(string password)
            {
                _file = Path.Combine(AppPaths.Tmp, "lavagon-" + Guid.NewGuid().ToString("N") + ".cnf");
                File.WriteAllText(_file, "[client]\r\nuser=root\r\npassword=\"" + (password ?? "").Replace("\"", "\\\"") + "\"\r\n");
                Arg = $"--defaults-file=\"{_file}\"";
            }

            public void Dispose()
            {
                try { File.Delete(_file); } catch { /* ignored */ }
            }
        }
    }
}
