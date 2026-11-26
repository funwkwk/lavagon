using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Lavagon.Core
{
    /// <summary>Minimal INI reader/writer that keeps section and key order.</summary>
    public class IniFile
    {
        private readonly string _path;
        private readonly List<KeyValuePair<string, List<KeyValuePair<string, string>>>> _sections =
            new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();

        private readonly object _sync = new object();

        public IniFile(string path)
        {
            _path = path;
            Load();
        }

        public void Load()
        {
            lock (_sync)
            {
                _sections.Clear();
                if (!File.Exists(_path)) return;
                List<KeyValuePair<string, string>> current = Section("", create: true);
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        current = Section(line.Substring(1, line.Length - 2).Trim(), create: true);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    current.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                }
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                var sb = new StringBuilder();
                foreach (var s in _sections)
                {
                    if (s.Value.Count == 0) continue;
                    if (s.Key.Length > 0) sb.Append('[').Append(s.Key).Append("]\r\n");
                    foreach (var kv in s.Value) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                    sb.Append("\r\n");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, sb.ToString());
            }
        }

        public string Get(string section, string key, string def = "")
        {
            lock (_sync)
            {
                var s = Section(section, create: false);
                if (s == null) return def;
                foreach (var kv in s)
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return def;
            }
        }

        public void Set(string section, string key, string value)
        {
            lock (_sync)
            {
                var s = Section(section, create: true);
                for (int i = 0; i < s.Count; i++)
                {
                    if (string.Equals(s[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        s[i] = new KeyValuePair<string, string>(s[i].Key, value ?? "");
                        return;
                    }
                }
                s.Add(new KeyValuePair<string, string>(key, value ?? ""));
            }
        }

        public IEnumerable<string> Keys(string section)
        {
            lock (_sync) return (Section(section, create: false)?.Select(k => k.Key) ?? Enumerable.Empty<string>()).ToList();
        }

        private List<KeyValuePair<string, string>> Section(string name, bool create)
        {
            foreach (var s in _sections)
                if (string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase)) return s.Value;
            if (!create) return null;
            var list = new List<KeyValuePair<string, string>>();
            _sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(name, list));
            return list;
        }
    }
}
