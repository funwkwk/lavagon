using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lavagon.Core
{
    /// <summary>Typed access to usr\lavagon.ini.</summary>
    public class Settings
    {
        private readonly IniFile _ini;

        public Settings(string path)
        {
            _ini = new IniFile(path);
        }

        public string Get(string section, string key, string def = "") => _ini.Get(section, key, def);

        public void Set(string section, string key, string value)
        {
            _ini.Set(section, key, value);
            _ini.Save();
        }

        public bool GetBool(string section, string key, bool def)
        {
            var v = _ini.Get(section, key, null);
            if (string.IsNullOrEmpty(v)) return def;
            // Accept 1/-1/true/yes as "on" (Laragon-style ini files use -1 for true).
            return v == "1" || v == "-1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

        public int GetInt(string section, string key, int def)
        {
            return int.TryParse(_ini.Get(section, key, null), out int v) ? v : def;
        }

        public void SetInt(string section, string key, int value) => Set(section, key, value.ToString());

        /// <summary>Secret stored encrypted with Windows DPAPI (only this Windows user can read it).</summary>
        public string GetSecret(string section, string key)
        {
            var v = _ini.Get(section, key, "");
            if (v.Length == 0) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(v), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }

        public void SetSecret(string section, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) { Set(section, key, ""); return; }
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            Set(section, key, Convert.ToBase64String(bytes));
        }

        // ---- Preferences -------------------------------------------------------------

        public bool RunAtStartup { get => GetBool("preferences", "RunAtStartup", false); set => SetBool("preferences", "RunAtStartup", value); }
        public bool RunMinimized { get => GetBool("preferences", "RunMinimized", false); set => SetBool("preferences", "RunMinimized", value); }
        public bool StartAllAutomatically { get => GetBool("preferences", "StartAllAutomatically", false); set => SetBool("preferences", "StartAllAutomatically", value); }
        public bool AutoVirtualHosts { get => GetBool("preferences", "AutoVirtualHosts", true); set => SetBool("preferences", "AutoVirtualHosts", value); }
        public string HostnameFormat { get => Get("preferences", "HostnameFormat", "{name}.test"); set => Set("preferences", "HostnameFormat", value); }
        public string Profile { get => Get("preferences", "Profile", "default"); set => Set("preferences", "Profile", value); }

        public string DocumentRoot
        {
            get
            {
                var v = Get("preferences", "DocumentRoot", "");
                return string.IsNullOrWhiteSpace(v) ? AppPaths.Www : Path.GetFullPath(Path.Combine(AppPaths.Root, v));
            }
            set => Set("preferences", "DocumentRoot", value);
        }

        public string Editor { get => Get("preferences", "Editor", ""); set => Set("preferences", "Editor", value); }
    }
}
