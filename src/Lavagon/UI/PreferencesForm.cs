using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Lavagon.Core;
using Lavagon.Core.Services;

namespace Lavagon.UI
{
    /// <summary>Menu > Preferences: General + Services &amp; Ports.</summary>
    public class PreferencesForm : Form
    {
        private readonly float _s;
        private readonly CheckBox _runAtStartup, _runMinimized, _startAll, _autoVhosts, _apacheSsl;
        private readonly TextBox _hostname;
        private readonly TextLink _docRoot, _dataDir;
        private readonly Dictionary<ServiceBase, CheckBox> _use = new Dictionary<ServiceBase, CheckBox>();
        private readonly Dictionary<string, NumericUpDown> _ports = new Dictionary<string, NumericUpDown>();
        private string _newDocRoot, _newDataDir;

        public bool WebConfigChanged { get; private set; }

        private int S(float v) => (int)Math.Round(v * _s);

        public PreferencesForm()
        {
            _s = DeviceDpi / 96f;
            Text = "Preferences";
            Font = Theme.UiFont(9f);
            BackColor = Theme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(520), S(430));
            Icon = Logo.ToIcon(S(32));

            var tabs = new TabControl { Location = new Point(S(10), S(10)), Size = new Size(S(500), S(370)) };
            var general = new TabPage("General") { BackColor = Theme.Background };
            var services = new TabPage("Services && Ports") { BackColor = Theme.Background };
            tabs.TabPages.Add(general);
            tabs.TabPages.Add(services);
            Controls.Add(tabs);

            // ---- General ----
            var st = App.Settings;
            int y = S(14);
            _runAtStartup = Check(general, "Run Lavagon when Windows starts", st.RunAtStartup, ref y);
            _runMinimized = Check(general, "Run minimized (left click the tray icon to show Lavagon)", st.RunMinimized, ref y);
            _startAll = Check(general, "Start All automatically when Lavagon runs", st.StartAllAutomatically, ref y);
            y += S(10);

            AddLabel(general, "Document Root", S(16), y);
            _docRoot = new TextLink { Text = st.DocumentRoot, ForeColor = Theme.Link, Location = new Point(S(140), y) };
            _docRoot.Click += (o, e) =>
            {
                var dir = Ui.PickFolder("Select the Document Root", _newDocRoot ?? st.DocumentRoot, this);
                if (dir != null) { _newDocRoot = dir; _docRoot.Text = dir; }
            };
            general.Controls.Add(_docRoot);
            y += S(28);

            AddLabel(general, "Data Directory", S(16), y);
            _dataDir = new TextLink { Text = App.MySql.Installed ? App.MySql.DataDir : "(MySQL not installed)", ForeColor = Theme.Link, Location = new Point(S(140), y) };
            _dataDir.Click += (o, e) =>
            {
                if (!App.MySql.Installed) return;
                var dir = Ui.PickFolder("Select MySQL's data directory", _newDataDir ?? App.MySql.DataDir, this);
                if (dir != null) { _newDataDir = dir; _dataDir.Text = dir; }
            };
            general.Controls.Add(_dataDir);
            y += S(36);

            _autoVhosts = Check(general, "Auto-create Virtual Hosts", st.AutoVirtualHosts, ref y);
            var hint = new Label
            {
                Text = "Just put a folder in the Document Root && reload, Lavagon creates the matching\nhostname in the hosts file and the web server's virtual hosts.\nTip: use {name}.localhost to avoid editing the hosts file (no admin prompt).",
                ForeColor = Theme.MutedText, AutoSize = true, Location = new Point(S(34), y - S(4)), UseMnemonic = false,
            };
            hint.Text = hint.Text.Replace("&&", "&");
            general.Controls.Add(hint);
            y += S(56);
            AddLabel(general, "Hostname", S(34), y + S(3));
            _hostname = new TextBox { Text = st.HostnameFormat, Location = new Point(S(140), y), Width = S(180) };
            general.Controls.Add(_hostname);
            AddLabel(general, "ex: {name}.test", S(330), y + S(3)).ForeColor = Theme.MutedText;

            // ---- Services & Ports ----
            y = S(16);
            AddService(services, App.Apache, ref y, ("Port", "Port", App.Apache.HttpPort));
            _apacheSsl = new CheckBox { Text = "SSL", Checked = App.Apache.SslEnabled, AutoSize = true, Location = new Point(S(250), y - S(30)) };
            services.Controls.Add(_apacheSsl);
            AddPort(services, "apache.SSLPort", App.Apache.SslPort, S(310), y - S(31));
            AddService(services, App.Nginx, ref y, ("Port", "Port", App.Nginx.HttpPort));
            AddService(services, App.MySql, ref y, ("Port", "Port", App.MySql.TcpPort));
            AddService(services, App.PostgreSql, ref y, ("Port", "Port", App.PostgreSql.TcpPort));
            AddService(services, App.MongoDb, ref y, ("Port", "Port", App.MongoDb.TcpPort));
            AddService(services, App.Redis, ref y, ("Port", "Port", App.Redis.TcpPort));
            AddService(services, App.Memcached, ref y, ("Port", "Port", App.Memcached.TcpPort));
            AddService(services, App.Mailpit, ref y, ("SmtpPort", "SMTP", App.Mailpit.SmtpPort), ("HttpPort", "Web", App.Mailpit.HttpPort));
            var note = new Label
            {
                Text = "Services that are not installed in bin\\ are greyed out.",
                ForeColor = Theme.MutedText, AutoSize = true, Location = new Point(S(16), y + S(4)),
            };
            services.Controls.Add(note);

            // ---- Buttons ----
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(S(84), S(28)), Location = new Point(S(330), S(392)) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(S(84), S(28)), Location = new Point(S(424), S(392)) };
            ok.Click += (o, e) => Save();
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private CheckBox Check(Control parent, string text, bool value, ref int y)
        {
            var cb = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(S(16), y) };
            parent.Controls.Add(cb);
            y += S(28);
            return cb;
        }

        private Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(l);
            return l;
        }

        private void AddService(Control parent, ServiceBase svc, ref int y, params (string key, string label, int value)[] ports)
        {
            var cb = new CheckBox
            {
                Text = svc.Installed ? $"{svc.Title} {svc.Version}" : svc.Title,
                Checked = svc.Enabled && svc.Installed,
                Enabled = svc.Installed,
                AutoSize = true,
                Location = new Point(S(16), y + S(2)),
            };
            parent.Controls.Add(cb);
            _use[svc] = cb;
            int x = S(170);
            foreach (var (key, label, value) in ports)
            {
                if (ports.Length > 1)
                {
                    AddLabel(parent, label, x, y + S(4));
                    x += S(40);
                }
                AddPort(parent, svc.Key + "." + key, value, x, y);
                x += S(90);
            }
            y += S(34);
        }

        private void AddPort(Control parent, string id, int value, int x, int y)
        {
            var n = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Max(1, Math.Min(65535, value)), Location = new Point(x, y), Width = S(76) };
            parent.Controls.Add(n);
            _ports[id] = n;
        }

        private void Save()
        {
            var st = App.Settings;
            if (!VirtualHosts.IsValidHostname(_hostname.Text.Replace("{name}", "x")) || !_hostname.Text.Contains("{name}"))
            {
                Ui.Info("Wrong format! Must have project name pattern {name}.xxx!");
                DialogResult = DialogResult.None;
                return;
            }

            if (_runAtStartup.Checked != st.RunAtStartup)
            {
                Env.SetRunAtStartup(_runAtStartup.Checked, Application.ExecutablePath);
                st.RunAtStartup = _runAtStartup.Checked;
            }
            st.RunMinimized = _runMinimized.Checked;
            st.StartAllAutomatically = _startAll.Checked;

            if (_autoVhosts.Checked != st.AutoVirtualHosts || _hostname.Text != st.HostnameFormat) WebConfigChanged = true;
            st.AutoVirtualHosts = _autoVhosts.Checked;
            st.HostnameFormat = _hostname.Text.Trim();

            if (_newDocRoot != null && !string.Equals(_newDocRoot, st.DocumentRoot, StringComparison.OrdinalIgnoreCase))
            {
                AppMenu.SetDocumentRoot(_newDocRoot);
            }
            if (_newDataDir != null) App.MySql.DataDir = _newDataDir;

            foreach (var kv in _use)
                if (kv.Key.Installed) kv.Key.Enabled = kv.Value.Checked;

            foreach (var kv in _ports)
            {
                var parts = kv.Key.Split('.');
                int value = (int)kv.Value.Value;
                if (st.GetInt(parts[0], parts[1], -1) != value)
                {
                    bool isDefault = parts[0] == "apache" || parts[0] == "nginx";
                    if (isDefault) WebConfigChanged = true;
                    st.SetInt(parts[0], parts[1], value);
                }
            }

            if (_apacheSsl.Checked != App.Apache.SslEnabled)
            {
                App.Apache.SslEnabled = _apacheSsl.Checked;
                WebConfigChanged = true;
            }
        }
    }
}
