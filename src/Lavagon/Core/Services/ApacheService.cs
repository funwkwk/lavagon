using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Lavagon.Core.Services
{
    public class ApacheService : ServiceBase
    {
        public override string Key => "apache";
        public override string Title => "Apache";
        public override bool DefaultUse => true;
        protected override string[] ExeCandidates => new[] { @"bin\httpd.exe" };

        public int HttpPort => Port("Port", 80);
        public int SslPort => Port("SSLPort", 443);
        public bool SslEnabled
        {
            get => App.Settings.GetBool(Key, "SSL", false);
            set => App.Settings.SetBool(Key, "SSL", value);
        }

        public override int[] Ports => SslEnabled ? new[] { HttpPort, SslPort } : new[] { HttpPort };
        public override string PortLabel => SslEnabled ? $"{HttpPort}/{SslPort}" : HttpPort.ToString();
        public override string ActionLabel => "Reload";
        public override void Action() => Restart();

        public string EtcDir => Path.Combine(AppPaths.Etc, "apache2");
        public string ConfFile => Path.Combine(EtcDir, "httpd.conf");
        public string SitesDir => Path.Combine(EtcDir, "sites-enabled");
        public string AliasDir => Path.Combine(EtcDir, "alias");
        public string CustomConf => Path.Combine(EtcDir, "custom.conf");
        public string ErrorLog => Path.Combine(AppPaths.Logs, "apache_error.log");

        private static readonly string[] Modules =
        {
            "access_compat", "actions", "alias", "allowmethods", "asis", "auth_basic", "authn_core", "authn_file",
            "authz_core", "authz_groupfile", "authz_host", "authz_user", "autoindex", "cgi", "deflate", "dir", "env",
            "expires", "filter", "headers", "include", "isapi", "log_config", "mime", "negotiation", "proxy",
            "proxy_http", "proxy_fcgi", "proxy_wstunnel", "rewrite", "setenvif", "socache_shmcb", "vhost_alias",
        };

        protected override void Prepare()
        {
            if (!Php.Installed)
                throw new LavagonException("PHP is not installed.\n\nExtract PHP (Thread Safe build) into:\n" + Path.Combine(AppPaths.Bin, "php") + "\\<version>");
            if (Php.ApacheModuleDll == null)
                throw new LavagonException($"PHP {Php.Version} is a Non Thread Safe (NTS) build, Apache needs the Thread Safe (TS) build.\n\nDownload the \"Thread Safe\" zip from windows.php.net, or use Nginx for NTS builds.");

            ushort apacheArch = PeFile.Machine(ExePath), phpArch = PeFile.Machine(Php.ApacheModuleDll);
            if (apacheArch != 0 && phpArch != 0 && apacheArch != phpArch)
                throw new LavagonException($"PHP {Php.Version} is {PeFile.MachineName(phpArch)} but Apache {Version} is {PeFile.MachineName(apacheArch)}.\n\nUse the PHP build with the same architecture as Apache (x64).");

            Php.CheckRuntime();
            Php.EnsureIni();
            Directory.CreateDirectory(SitesDir);
            Directory.CreateDirectory(AliasDir);
            if (!File.Exists(CustomConf))
                File.WriteAllText(CustomConf, "# Your own Apache directives. Lavagon includes this file and never overwrites it.\r\n");

            if (SslEnabled) Certificates.EnsureFor("localhost");
            WriteIfChanged(Path.Combine(EtcDir, "php.conf"), PhpConf());
            WriteDefaultSite();
            VirtualHosts.SyncApache();
            WriteIfChanged(ConfFile, HttpdConf());

            var test = Proc.Run(ExePath, $"-t -f \"{ConfFile}\"", Path.Combine(Dir, "bin"), 30000, ServiceEnv());
            if (!test.Ok)
            {
                if (test.ExitCode < 0 && test.Output.Length == 0)
                    throw new LavagonException($"Apache {Version} cannot run on this computer.\n\n" + Php.ExplainExitCode(test.ExitCode, Folder));
                throw new LavagonException("Apache configuration error:\n\n" + test.Output);
            }
        }

        protected override int StartupCheckMs => 3000;

        protected override Process Launch() =>
            Proc.StartHidden(ExePath, $"-f \"{ConfFile}\"", Path.Combine(Dir, "bin"), ServiceEnv());

        protected override string StartFailureDetails()
        {
            var tail = Tail(ErrorLog);
            if (tail.Contains("AH01882"))
                tail += "\n\nHint: mod_ssl and libcrypto/libssl in " + Path.Combine(Dir, "bin") +
                        " are different OpenSSL versions (often caused by copying PHP's DLLs into Apache's bin folder). " +
                        "Use a clean Apache build, or disable SSL.";
            return tail;
        }

        private string PhpConf()
        {
            var f = AppPaths.Fwd;

            // Windows looks for DLLs in Apache's bin folder before PHP's folder. Same-named DLLs
            // (libcrypto, libssh2, nghttp2, libcurl...) of another version there break PHP
            // extensions, so load PHP's own copies first, dependencies before dependants.
            // With SSL on, mod_ssl has already loaded Apache's OpenSSL: keep that one.
            var preload = new StringBuilder();
            foreach (var dll in Php.SupportDlls())
            {
                var name = Path.GetFileName(dll).ToLowerInvariant();
                bool openssl = name.StartsWith("libcrypto") || name.StartsWith("libssl");
                if (openssl && SslEnabled && File.Exists(Path.Combine(Dir, "bin", name))) continue;
                preload.Append($"LoadFile \"{f(dll)}\"\r\n");
            }

            return
$@"# Generated by Lavagon - PHP {Php.Version}
{preload}LoadModule {Php.ApacheModuleName} ""{f(Php.ApacheModuleDll)}""
<IfModule {Php.ApacheModuleName}>
    PHPIniDir ""{f(Php.IniDir)}""
    <FilesMatch ""\.php$"">
        SetHandler application/x-httpd-php
    </FilesMatch>
    <FilesMatch ""\.phps$"">
        SetHandler application/x-httpd-php-source
    </FilesMatch>
</IfModule>
";
        }

        private void WriteDefaultSite()
        {
            var f = AppPaths.Fwd;
            var docRoot = f(App.Settings.DocumentRoot);
            var sb = new StringBuilder();
            sb.Append("# Generated by Lavagon - default site (http://localhost)\r\n");
            sb.Append($"<VirtualHost _default_:{HttpPort}>\r\n    DocumentRoot \"{docRoot}\"\r\n    ServerName localhost\r\n</VirtualHost>\r\n");
            if (SslEnabled)
            {
                var ssl = f(Certificates.Dir);
                sb.Append($"\r\n<VirtualHost _default_:{SslPort}>\r\n    DocumentRoot \"{docRoot}\"\r\n    ServerName localhost\r\n");
                sb.Append($"    SSLEngine on\r\n    SSLCertificateFile \"{ssl}/localhost.crt\"\r\n    SSLCertificateKeyFile \"{ssl}/localhost.key\"\r\n</VirtualHost>\r\n");
            }
            WriteIfChanged(Path.Combine(SitesDir, "00-default.conf"), sb.ToString());
        }

        private string HttpdConf()
        {
            var f = AppPaths.Fwd;
            var modDir = Path.Combine(Dir, "modules");
            var mods = Modules.ToList();
            if (SslEnabled) mods.Add("ssl");
            var load = new StringBuilder();
            foreach (var m in mods)
                if (File.Exists(Path.Combine(modDir, $"mod_{m}.so")))
                    load.Append($"LoadModule {m}_module modules/mod_{m}.so\r\n");

            var docRoot = f(App.Settings.DocumentRoot);
            var ssl = SslEnabled
                ? $@"
# ---- SSL -------------------------------------------------------------------
Listen {SslPort}
SSLSessionCache ""shmcb:{f(AppPaths.Tmp)}/ssl_scache(512000)""
SSLSessionCacheTimeout 300
SSLProtocol all -SSLv3 -TLSv1 -TLSv1.1
SSLCipherSuite HIGH:!aNULL:!MD5
"
                : "";

            return
$@"# =============================================================================
# Generated by Lavagon on every start - DO NOT EDIT, your changes will be lost.
# Put your own directives in: {f(CustomConf)}
# Per-site configuration lives in: {f(SitesDir)}
# =============================================================================
Define LAVAGON_ROOT ""{f(AppPaths.Root)}""
ServerRoot ""{f(Dir)}""
Listen {HttpPort}

{load}
ServerAdmin admin@localhost
ServerName localhost:{HttpPort}

<Directory />
    AllowOverride none
    Require all denied
</Directory>

DocumentRoot ""{docRoot}""
<Directory ""{docRoot}"">
    Options Indexes FollowSymLinks
    AllowOverride All
    Require all granted
</Directory>

<IfModule dir_module>
    DirectoryIndex index.php index.html index.htm
</IfModule>

<Files "".ht*"">
    Require all denied
</Files>

PidFile ""{f(AppPaths.Tmp)}/httpd.pid""
ErrorLog ""{f(ErrorLog)}""
LogLevel warn
LogFormat ""%h %l %u %t \""%r\"" %>s %b \""%{{Referer}}i\"" \""%{{User-Agent}}i\"""" combined
CustomLog ""{f(AppPaths.Logs)}/apache_access.log"" combined

TypesConfig conf/mime.types
AddType application/x-compress .Z
AddType application/x-gzip .gz .tgz

AcceptFilter http none
AcceptFilter https none
EnableMMAP off
EnableSendfile off
Timeout 600
KeepAlive On
ThreadsPerChild 150
{ssl}
Include ""{f(EtcDir)}/php.conf""
IncludeOptional ""{f(AliasDir)}/*.conf""
IncludeOptional ""{f(CustomConf)}""
IncludeOptional ""{f(SitesDir)}/*.conf""
";
        }
    }
}
