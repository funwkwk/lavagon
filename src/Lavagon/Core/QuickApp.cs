using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lavagon.Core
{
    /// <summary>Menu > Quick app: create a project from usr\sites.conf (composer command, archive URL or blank).</summary>
    public static class QuickApp
    {
        public class Recipe
        {
            public string Name;
            public string Source;
        }

        public static void EnsureSitesFile()
        {
            if (!File.Exists(AppPaths.SitesFile)) File.WriteAllText(AppPaths.SitesFile, Templates.SitesConf);
        }

        public static List<Recipe> Recipes()
        {
            EnsureSitesFile();
            var list = new List<Recipe>();
            foreach (var raw in File.ReadAllLines(AppPaths.SitesFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("---")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var name = line.Substring(0, eq).Trim();
                if (name.Equals("AutoCreateDatabase", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new Recipe { Name = name, Source = line.Substring(eq + 1).Trim() });
            }
            return list;
        }

        public static bool AutoCreateDatabase
        {
            get
            {
                EnsureSitesFile();
                var line = File.ReadAllLines(AppPaths.SitesFile)
                    .FirstOrDefault(l => l.Trim().StartsWith("AutoCreateDatabase", StringComparison.OrdinalIgnoreCase));
                return line == null || line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static bool IsValidName(string name) => !string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9_\-\.]*$");

        /// <summary>Creates the project (blocking; runs visible console windows for downloads/composer).</summary>
        public static string Create(Recipe recipe, string name)
        {
            if (!IsValidName(name)) throw new LavagonException("The name is not valid!\nUse letters, digits, '-', '_' or '.'.");
            var docRoot = App.Settings.DocumentRoot;
            var target = Path.Combine(docRoot, name);
            if (Directory.Exists(target))
                throw new LavagonException($"Project exists!\n\n{target}\n\nIf you really want to create the project, delete the project's folder and try again.");

            var src = recipe.Source;
            if (string.IsNullOrWhiteSpace(src))
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "index.php"), $"<?php\n// {name} - created by Lavagon\nphpinfo();\n");
            }
            else if (Regex.IsMatch(src, @"^https?://", RegexOptions.IgnoreCase))
            {
                CreateFromArchive(src, name, target);
            }
            else
            {
                var cmd = src.Contains("%s") ? src.Replace("%s", name) : src + " " + name;
                Env.RunVisible(cmd, docRoot, $"Lavagon - creating {name}");
                if (!Directory.Exists(target))
                    throw new LavagonException($"Could not create {name}.\n\nThe command did not create the folder:\n{target}");
            }

            if (AutoCreateDatabase && App.MySql.IsRunning)
            {
                var db = Regex.Replace(name, @"[^A-Za-z0-9_]", "_");
                var r = App.MySql.Sql($"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                if (!r.Ok) Log.Error("auto-create database: " + r.Output);
            }
            return target;
        }

        private static void CreateFromArchive(string url, string name, string target)
        {
            var work = Path.Combine(AppPaths.Tmp, "quickapp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(fileName) || !fileName.Contains(".")) fileName = "download.zip";
            var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            var cmd = $"\"{curl}\" -L --fail -o \"{fileName}\" \"{url}\" && mkdir out && \"{tar}\" -xf \"{fileName}\" -C out";
            Env.RunVisible(cmd, work, $"Lavagon - downloading {fileName}");

            var outDir = Path.Combine(work, "out");
            if (!Directory.Exists(outDir) || !Directory.EnumerateFileSystemEntries(outDir).Any())
                throw new LavagonException($"Error occurred when downloading or extracting:\n{url}\n\nPlease check the link & try again.");

            // Archives usually contain a single top folder (e.g. "wordpress/").
            var entries = Directory.GetFileSystemEntries(outDir);
            var source = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : outDir;
            Directory.Move(source, target);
            try { Directory.Delete(work, true); } catch { /* ignored */ }
        }
    }
}
