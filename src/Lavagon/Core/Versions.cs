using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lavagon.Core
{
    /// <summary>
    /// Every tool lives in bin\{tool}\{version-folder}. Adding another version is just a matter of
    /// extracting it next to the existing ones; it then shows up in the Menu.
    /// </summary>
    public static class Versions
    {
        public static List<string> List(string tool)
        {
            var dir = Path.Combine(AppPaths.Bin, tool);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetDirectories(dir)
                .Select(Path.GetFileName)
                .Where(n => !n.StartsWith(".") && !n.StartsWith("_"))
                .OrderBy(n => SortKey(n))
                .ToList();
        }

        /// <summary>The preferred version if installed, otherwise the newest one.</summary>
        public static string Resolve(string tool, string preferred)
        {
            var all = List(tool);
            if (all.Count == 0) return null;
            if (!string.IsNullOrEmpty(preferred))
            {
                var match = all.FirstOrDefault(v => v.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return all[all.Count - 1];
        }

        /// <summary>"httpd-2.4.69-261002-Win64-VS18" -> "2.4.69", "redis-x64-5.0.14.1" -> "5.0.14.1", "node-v22" -> "22".</summary>
        public static string Parse(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            var m = Regex.Match(folder, @"\d+(\.\d+)+");
            if (m.Success) return m.Value;
            m = Regex.Match(folder, @"v?(\d+)");
            return m.Success ? m.Groups[1].Value : folder;
        }

        /// <summary>"8.4.3" -> "8.4"</summary>
        public static string MajorMinor(string version)
        {
            var parts = (version ?? "").Split('.');
            return parts.Length >= 2 ? parts[0] + "." + parts[1] : version;
        }

        private static string SortKey(string folder)
        {
            // Zero-pad numbers so "php-8.10" sorts after "php-8.9".
            return Regex.Replace(folder.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));
        }
    }
}
