using System.IO;

namespace Lavagon.Core
{
    /// <summary>
    /// Folder layout of a Lavagon installation (everything is relative to the root, so the
    /// whole folder is portable):
    ///   bin\   third-party binaries, one sub folder per tool and per version (bin\php\php-8.3.x)
    ///   etc\   configuration generated and owned by Lavagon (apache2, nginx, php, mysql, ssl)
    ///   data\  database data directories
    ///   www\   default document root
    ///   usr\   user settings (lavagon.ini, sites.conf) and user scripts (usr\bin)
    ///   tmp\   pid files, sessions, uploads
    ///   logs\  service logs
    /// </summary>
    public static class AppPaths
    {
        public static string Root { get; private set; }
        public static string Bin { get; private set; }
        public static string Etc { get; private set; }
        public static string Data { get; private set; }
        public static string Www { get; private set; }
        public static string Usr { get; private set; }
        public static string Tmp { get; private set; }
        public static string Logs { get; private set; }

        public static string ConfigFile => Path.Combine(Usr, "lavagon.ini");
        public static string SitesFile => Path.Combine(Usr, "sites.conf");
        public static string PidDir => Path.Combine(Tmp, "pids");

        public static void Init(string root)
        {
            Root = root.TrimEnd('\\', '/');
            Bin = Path.Combine(Root, "bin");
            Etc = Path.Combine(Root, "etc");
            Data = Path.Combine(Root, "data");
            Www = Path.Combine(Root, "www");
            Usr = Path.Combine(Root, "usr");
            Tmp = Path.Combine(Root, "tmp");
            Logs = Path.Combine(Root, "logs");

            foreach (var d in new[] { Bin, Etc, Data, Www, Usr, Tmp, Logs, PidDir, Path.Combine(Usr, "bin") })
                Directory.CreateDirectory(d);
        }

        /// <summary>Path with forward slashes, which Apache / Nginx / MySQL all accept on Windows.</summary>
        public static string Fwd(string path) => path?.Replace('\\', '/');

        public static string Combine(params string[] parts) => Path.Combine(parts);
    }
}
