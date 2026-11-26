using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lavagon.Core
{
    /// <summary>
    /// Auto virtual hosts: every folder in the document root becomes http://{name}.test (hostname
    /// format is configurable). Files are written as sites-enabled\auto.{hostname}.conf and are
    /// regenerated on every (re)start; rename a file to drop the "auto." prefix to customize it.
    /// </summary>
    public static class VirtualHosts
    {
        public class Project
        {
            public string Name;
            public string Dir;
            public string WebRoot;
            public string Hostname;
        }

        public static List<Project> Projects()
        {
            var root = App.Settings.DocumentRoot;
            if (!Directory.Exists(root)) return new List<Project>();
            return Directory.GetDirectories(root)
                .Select(d => new DirectoryInfo(d))
                .Where(d => !d.Name.StartsWith(".") && (d.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new Project
                {
                    Name = d.Name,
                    Dir = d.FullName,
                    WebRoot = WebRootOf(d.FullName),
                    Hostname = App.Hostname(d.Name),
                })
                .Where(p => IsValidHostname(p.Hostname))
                .ToList();
        }

        /// <summary>Laravel/Symfony style projects are served from their public folder.</summary>
        private static string WebRootOf(string dir)
        {
            foreach (var sub in new[] { "public", "web", "webroot" })
            {
                var p = Path.Combine(dir, sub);
                if (File.Exists(Path.Combine(p, "index.php")) || File.Exists(Path.Combine(p, "index.html"))) return p;
            }
            return dir;
        }

        public static bool IsValidHostname(string host) =>
            !string.IsNullOrEmpty(host) && host.Length < 250 &&
            host.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '.') && !host.StartsWith(".") && !host.StartsWith("-");

        public static string Url(Project p)
        {
            if (!App.Settings.AutoVirtualHosts)
            {
                int port = App.Apache.Enabled ? App.Apache.HttpPort : App.Nginx.HttpPort;
                return "http://localhost" + (port == 80 ? "" : ":" + port) + "/" + Uri.EscapeDataString(p.Name) + "/";
            }
            if (App.Apache.Enabled && App.Apache.SslEnabled)
                return "https://" + p.Hostname + (App.Apache.SslPort == 443 ? "" : ":" + App.Apache.SslPort) + "/";
            int http = App.Apache.Enabled ? App.Apache.HttpPort : App.Nginx.HttpPort;
            return "http://" + p.Hostname + (http == 80 ? "" : ":" + http) + "/";
        }

        public static void SyncApache()
        {
            var apache = App.Apache;
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (App.Settings.AutoVirtualHosts)
            {
                foreach (var p in Projects())
                {
                    var conf = Templates.Fill(Templates.ApacheVhost,
                        ("PORT", apache.HttpPort.ToString()), ("PROJECT_DIR", AppPaths.Fwd(p.WebRoot)), ("HOSTNAME", p.Hostname));
                    if (apache.SslEnabled)
                    {
                        Certificates.EnsureFor(p.Hostname);
                        conf += Templates.Fill(Templates.ApacheVhostSsl,
                            ("SSL_PORT", apache.SslPort.ToString()), ("PROJECT_DIR", AppPaths.Fwd(p.WebRoot)),
                            ("HOSTNAME", p.Hostname), ("SSL_DIR", AppPaths.Fwd(Certificates.Dir)));
                    }
                    wanted["auto." + p.Hostname + ".conf"] = conf;
                }
            }
            WriteAutoFiles(apache.SitesDir, wanted);
            UpdateHosts();
        }

        public static void SyncNginx()
        {
            var nginx = App.Nginx;
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (App.Settings.AutoVirtualHosts)
            {
                foreach (var p in Projects())
                    wanted["auto." + p.Hostname + ".conf"] = Templates.Fill(Templates.NginxServer,
                        ("PORT", nginx.HttpPort.ToString()), ("PROJECT_DIR", AppPaths.Fwd(p.WebRoot)),
                        ("HOSTNAME", p.Hostname), ("NGINX_DIR", AppPaths.Fwd(nginx.Dir)));
            }
            WriteAutoFiles(nginx.SitesDir, wanted);
            UpdateHosts();
        }

        private static void WriteAutoFiles(string dir, Dictionary<string, string> wanted)
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(dir, "auto.*.conf"))
                if (!wanted.ContainsKey(Path.GetFileName(file))) File.Delete(file);
            foreach (var kv in wanted)
            {
                var path = Path.Combine(dir, kv.Key);
                // A user-customized copy (same name without "auto.") wins over the generated one.
                if (File.Exists(Path.Combine(dir, kv.Key.Substring("auto.".Length)))) { if (File.Exists(path)) File.Delete(path); continue; }
                if (!File.Exists(path) || File.ReadAllText(path) != kv.Value) File.WriteAllText(path, kv.Value);
            }
        }

        private static void UpdateHosts()
        {
            if (!App.Settings.AutoVirtualHosts) return;
            // *.localhost always resolves to 127.0.0.1, so it never needs the hosts file.
            var hosts = Projects().Select(p => p.Hostname)
                .Where(h => !h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)).ToList();
            HostsFile.Sync(hosts);
        }
    }
}
