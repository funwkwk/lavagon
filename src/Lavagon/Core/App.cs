using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lavagon.Core.Services;

namespace Lavagon.Core
{
    /// <summary>Global application state: settings and the list of services.</summary>
    public static class App
    {
        public static Settings Settings { get; private set; }
        public static List<ServiceBase> Services { get; private set; }

        public static ApacheService Apache { get; private set; }
        public static NginxService Nginx { get; private set; }
        public static MySqlService MySql { get; private set; }
        public static PostgreSqlService PostgreSql { get; private set; }
        public static MongoDbService MongoDb { get; private set; }
        public static RedisService Redis { get; private set; }
        public static MemcachedService Memcached { get; private set; }
        public static MailpitService Mailpit { get; private set; }

        public static void Init()
        {
            bool firstRun = !File.Exists(AppPaths.ConfigFile);
            Settings = new Settings(AppPaths.ConfigFile);

            Apache = new ApacheService();
            Nginx = new NginxService();
            MySql = new MySqlService();
            PostgreSql = new PostgreSqlService();
            MongoDb = new MongoDbService();
            Redis = new RedisService();
            Memcached = new MemcachedService();
            Mailpit = new MailpitService();

            // Display order in the main window.
            Services = new List<ServiceBase> { Apache, Nginx, MySql, PostgreSql, MongoDb, Redis, Memcached, Mailpit };

            if (firstRun) WriteDefaults();
            QuickApp.EnsureSitesFile();
        }

        /// <summary>Services that are installed and ticked in Preferences > Services &amp; Ports.</summary>
        public static IEnumerable<ServiceBase> ActiveServices => Services.Where(s => s.Enabled && s.Installed);

        /// <summary>"My_Shop" -> "my-shop.test" (underscores/spaces are not valid in hostnames).</summary>
        public static string Hostname(string project)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(project.ToLowerInvariant(), @"[^a-z0-9\-\.]+", "-").Trim('-', '.');
            return Settings.HostnameFormat.Replace("{name}", name);
        }

        private static void WriteDefaults()
        {
            Settings.Set("preferences", "HostnameFormat", "{name}.test");
            Settings.SetBool("preferences", "AutoVirtualHosts", true);
            // Only record services that exist now; missing ones fall back to their default
            // (Apache, MySQL and Mailpit on) as soon as their binaries are dropped into bin\.
            foreach (var s in Services)
                if (s.Installed) s.Enabled = s.DefaultUse;
        }
    }
}
