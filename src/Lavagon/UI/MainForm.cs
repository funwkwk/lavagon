using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lavagon.Core;
using Lavagon.Core.Services;

namespace Lavagon.UI
{
    /// <summary>
    /// Main window: logo + Menu at the top, one row per service (switch, name/version, port,
    /// action) and the Start All / Web / Database / Terminal / Root buttons at the bottom.
    /// </summary>
    public class MainForm : Form
    {
        private class Row
        {
            public ServiceBase Service;
            public ToggleSwitch Toggle;
            public Label Name;
            public GlyphButton Extra;
            public Label Port;
            public TextLink Action;
        }

        private readonly EventWaitHandle _showSignal;
        private readonly NotifyIcon _tray;
        private readonly ContextMenu _menu;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Icon _iconOn, _iconOff, _trayOn, _trayOff;

        private readonly PictureBox _logo;
        private readonly TextLink _menuLink;
        private readonly Label _profile;
        private readonly GlyphButton _hosts;
        private readonly TextLink _help;
        private readonly GlyphButton _gear;
        private readonly Label _empty;
        private readonly List<Row> _rows = new List<Row>();
        private readonly FlatButton _startAll, _web, _database, _terminal, _root;
        private readonly ToolTip _tips = new ToolTip();

        private bool _allowShow;
        private bool _exiting;
        private bool _balloonShown;
        private int _busy;
        private string _busyText;

        public MainForm(EventWaitHandle showSignal)
        {
            _showSignal = showSignal;
            _allowShow = !App.Settings.RunMinimized;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.UiFont();
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            float s = DeviceDpi / 96f;
            ClientSize = new Size((int)(760 * s), (int)(470 * s));
            MinimumSize = new Size((int)(560 * s), (int)(360 * s));

            _iconOn = Logo.ToIcon((int)(32 * s));
            _iconOff = Logo.ToIcon((int)(32 * s), active: false);
            _trayOn = Logo.ToIcon(SystemInformation.SmallIconSize.Width);
            _trayOff = Logo.ToIcon(SystemInformation.SmallIconSize.Width, active: false);
            Icon = _iconOn;

            _logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Image = Logo.Render((int)(96 * s)), Cursor = Cursors.Hand };
            _logo.Click += (o, e) => Proc.OpenUrl(Program.HomePage);
            _tips.SetToolTip(_logo, $"{Program.Name} {Program.Version} - Online help");

            _menuLink = new TextLink { Text = "Menu", ForeColor = Theme.Text };
            _menuLink.MouseUp += (o, e) => _menu.Show(_menuLink, new Point(0, _menuLink.Height));

            _profile = new Label { AutoSize = true, Font = Theme.UiFont(8.25f), Text = "[" + App.Settings.Profile + "]" };
            _tips.SetToolTip(_profile, "Current profile");

            _hosts = new GlyphButton { Glyph = Glyph.Hosts, GlyphColor = Color.FromArgb(232, 86, 86) };
            _hosts.Click += (o, e) => Safe(HostsFile.EditAsAdmin);
            _tips.SetToolTip(_hosts, "Click to edit hosts file as Administrator manually.");

            _help = new TextLink { Text = "?", Font = Theme.UiFont(12f), ForeColor = Theme.Text };
            _help.Click += (o, e) => Safe(() => Proc.OpenUrl(Program.HomePage));
            _tips.SetToolTip(_help, "Help");

            _gear = new GlyphButton { Glyph = Glyph.Gear };
            _gear.Click += (o, e) => ShowPreferences();
            _tips.SetToolTip(_gear, "Preferences");

            _empty = new Label
            {
                AutoSize = false, ForeColor = Theme.MutedText, Visible = false, TextAlign = ContentAlignment.TopLeft, UseMnemonic = false,
                Text = "No services found.\n\nPut the binaries into " + AppPaths.Bin + "\\<tool>\\<version>, for example:\n" +
                       "   bin\\apache\\httpd-2.4.x\n   bin\\php\\php-8.3.x-Win32-vs16-x64\n   bin\\mysql\\mysql-8.4.x-winx64\n\n" +
                       "Apache, MySQL and Mailpit show up automatically; other services can be\n" +
                       "enabled in Menu > Preferences > Services & Ports.",
            };

            _startAll = Button("Start All", Glyph.Start, (o, e) => ToggleAll());
            _web = Button("Web", Glyph.Globe, (o, e) => Safe(OpenWeb));
            _database = Button("Database", Glyph.Database, (o, e) => Safe(OpenDatabase));
            _terminal = Button("Terminal", Glyph.Terminal, (o, e) => Safe(() => Env.OpenTerminal()));
            _root = Button("Root", Glyph.Folder, (o, e) => Safe(() => Proc.Explore(App.Settings.DocumentRoot)));
            _tips.SetToolTip(_web, "Start page");
            _tips.SetToolTip(_database, "Database Management");
            _tips.SetToolTip(_terminal, "Open Terminal");
            _tips.SetToolTip(_root, "Document Root");

            Controls.AddRange(new Control[] { _logo, _menuLink, _profile, _hosts, _help, _gear, _empty, _startAll, _web, _database, _terminal, _root });

            _menu = new ContextMenu();
            _menu.Popup += (o, e) => AppMenu.Build(_menu.MenuItems, this);
            AppMenu.Build(_menu.MenuItems, this);
            ContextMenu = _menu;

            _tray = new NotifyIcon
            {
                Icon = _trayOff,
                Text = Program.Name + "\nLeft click: Open - Right click: Menu",
                ContextMenu = _menu,
                Visible = true,
            };
            _tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

            _timer = new System.Windows.Forms.Timer { Interval = 2000 };
            _timer.Tick += (o, e) => RefreshState();
            _timer.Start();

            ThreadPool.RegisterWaitForSingleObject(_showSignal, (st, to) =>
            {
                try { BeginInvoke((Action)ShowMain); } catch { /* closing */ }
            }, null, -1, false);

            RebuildRows();
            ResumeLayout();
        }

        private FlatButton Button(string text, Glyph glyph, EventHandler click)
        {
            var b = new FlatButton { Text = text, Glyph = glyph, Font = Theme.UiFont(9.75f) };
            b.Click += click;
            return b;
        }

        // ---- Visibility / tray ----------------------------------------------------------

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        public void ShowMain()
        {
            _allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshState();
            if (App.Settings.StartAllAutomatically)
                BeginInvoke((Action)(() => StartAll()));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                // Like the close button of most tray apps: keep running in the notification area.
                e.Cancel = true;
                Hide();
                if (!_balloonShown)
                {
                    _balloonShown = true;
                    _tray.ShowBalloonTip(3000, Program.Name, "Lavagon is minimized here.\nLeft click: Open - Right click: Menu", ToolTipIcon.Info);
                }
                return;
            }
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosing(e);
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            RunAsync("Stopping...", () =>
            {
                foreach (var svc in App.Services.Where(x => x.Installed && x.IsRunning).Reverse())
                    try { svc.Stop(); } catch (Exception ex) { Log.Error(ex); }
            }, () => Application.Exit());
        }

        public void Notify(string text, ToolTipIcon icon = ToolTipIcon.Info) =>
            _tray.ShowBalloonTip(3000, Program.Name, text, icon);

        // ---- Layout ---------------------------------------------------------------------

        public void RebuildRows()
        {
            SuspendLayout();
            foreach (var r in _rows)
            {
                Controls.Remove(r.Toggle); r.Toggle.Dispose();
                Controls.Remove(r.Name); r.Name.Dispose();
                if (r.Extra != null) { Controls.Remove(r.Extra); r.Extra.Dispose(); }
                Controls.Remove(r.Port); r.Port.Dispose();
                Controls.Remove(r.Action); r.Action.Dispose();
            }
            _rows.Clear();

            foreach (var svc in App.ActiveServices)
            {
                var row = new Row
                {
                    Service = svc,
                    Toggle = new ToggleSwitch(),
                    Name = new Label { AutoSize = true, BackColor = Color.Transparent },
                    Port = new Label { AutoSize = true, BackColor = Color.Transparent },
                    Action = new TextLink { ForeColor = Theme.Link, Visible = false },
                };
                var captured = row;
                row.Toggle.Click += (o, e) => ToggleService(captured.Service);
                _tips.SetToolTip(row.Toggle, "Start / Stop " + svc.Title);
                row.Action.Click += (o, e) => RunAsync(svc.ActionLabel + "...", svc.Action);

                if (svc is ApacheService)
                {
                    row.Extra = new GlyphButton { GlyphScale = 0.95f, GlyphColor = Theme.MutedText };
                    row.Extra.Click += (o, e) => ToggleSsl();
                }
                else if (svc is MailpitService mp)
                {
                    row.Extra = new GlyphButton { Glyph = Glyph.Mail, GlyphScale = 0.95f, GlyphColor = Theme.AccentDark };
                    row.Extra.Click += (o, e) => Safe(() => Proc.OpenUrl(mp.WebUrl));
                    _tips.SetToolTip(row.Extra, "Open Mailpit (" + mp.WebUrl + ")");
                }

                Controls.Add(row.Toggle);
                Controls.Add(row.Name);
                if (row.Extra != null) Controls.Add(row.Extra);
                Controls.Add(row.Port);
                Controls.Add(row.Action);
                _rows.Add(row);
            }
            _empty.Visible = _rows.Count == 0;
            ResumeLayout();
            RefreshState();
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_logo == null) return;
            float s = DeviceDpi / 96f;
            int S(float v) => (int)Math.Round(v * s);
            int W = ClientSize.Width, H = ClientSize.Height;

            _logo.Bounds = new Rectangle(S(8), S(2), S(60), S(54)); // the rhino is wider than tall
            _menuLink.Location = new Point(S(75), S(9));

            int gearSize = S(22);
            _gear.Bounds = new Rectangle(W - S(12) - gearSize, S(6), gearSize, gearSize);
            _help.Location = new Point(_gear.Left - S(14) - _help.Width, S(4));
            _hosts.Bounds = new Rectangle(_help.Left - S(10) - S(16), S(7), S(16), S(18));
            _profile.Location = new Point(_hosts.Left - S(14) - _profile.Width, S(11));

            int top = S(72), rowH = S(40);
            int portX = (int)(W * 0.5), actionX = (int)(W * 0.65);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                int y = top + i * rowH;
                int textH = r.Name.PreferredHeight;
                r.Toggle.Bounds = new Rectangle(S(116), y + (textH - S(20)) / 2, S(30), S(20));
                r.Name.Location = new Point(S(150), y);
                if (r.Extra != null)
                    r.Extra.Bounds = new Rectangle(r.Name.Right + S(4), y + (textH - S(18)) / 2, S(18), S(18));
                r.Port.Location = new Point(portX, y);
                r.Action.Location = new Point(actionX, y);
            }
            _empty.Bounds = new Rectangle(S(116), top, Math.Max(10, W - S(140)), Math.Max(10, H - top - S(80)));

            int margin = S(56), gap = S(8), bh = S(24);
            var buttons = new[] { _startAll, _web, _database, _terminal, _root };
            int bw = (W - 2 * margin - gap * (buttons.Length - 1)) / buttons.Length;
            int by = H - S(56);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].Bounds = new Rectangle(margin + i * (bw + gap), by, bw, bh);
        }

        // ---- State ---------------------------------------------------------------------

        public void RefreshState()
        {
            if (IsDisposed) return;
            bool anyRunning = false;
            foreach (var r in _rows)
            {
                var svc = r.Service;
                bool running = svc.IsRunning;
                anyRunning |= running;
                r.Toggle.On = running;
                r.Toggle.Busy = _busy > 0;
                SetText(r.Name, $"{svc.Title} {svc.Version}");
                SetText(r.Port, svc.PortLabel);
                r.Action.Visible = running && svc.ActionLabel != null;
                if (r.Action.Visible) SetText(r.Action, svc.ActionLabel);
                if (r.Extra != null && svc is ApacheService apache)
                {
                    var glyph = apache.SslEnabled ? Glyph.LockClosed : Glyph.LockOpen;
                    if (r.Extra.Glyph != glyph) { r.Extra.Glyph = glyph; r.Extra.Invalidate(); }
                    r.Extra.GlyphColor = apache.SslEnabled ? Color.FromArgb(22, 150, 60) : Theme.MutedText;
                    _tips.SetToolTip(r.Extra, apache.SslEnabled ? "SSL is enabled. Click to disable" : "SSL is disabled. Click to enable");
                }
            }

            if (_busy > 0)
            {
                SetText(_startAll, _busyText ?? "Working...");
            }
            else
            {
                SetText(_startAll, anyRunning ? "Stop" : "Start All");
                _startAll.Glyph = anyRunning ? Glyph.Stop : Glyph.Start;
            }
            _startAll.Enabled = _busy == 0 && _rows.Count > 0;

            var icon = anyRunning ? _trayOn : _trayOff;
            if (_tray.Icon != icon) _tray.Icon = icon;
            var title = BuildTitle();
            if (Text != title) Text = title;
            PerformLayout();
        }

        private static void SetText(Control c, string text)
        {
            if (c.Text != text) c.Text = text;
        }

        private string BuildTitle()
        {
            var php = Php.Installed ? $" php-{Php.Version} [{(Php.ThreadSafe ? "TS" : "NTS")}]" : "";
            if (_ip == null || (DateTime.UtcNow - _ipTime).TotalSeconds > 30)
            {
                _ip = Net.LocalIPv4();
                _ipTime = DateTime.UtcNow;
            }
            return $"{Program.Name} v{Program.Version} [{App.Settings.Profile}]{php}  {_ip} {AppPaths.Root}";
        }

        private string _ip;
        private DateTime _ipTime;

        // ---- Actions -------------------------------------------------------------------

        /// <summary>Run work off the UI thread, show errors, then refresh.</summary>
        public void RunAsync(string busyText, Action work, Action then = null)
        {
            Interlocked.Increment(ref _busy);
            _busyText = busyText;
            RefreshState();
            Task.Run(work).ContinueWith(t =>
            {
                BeginInvoke((Action)(() =>
                {
                    Interlocked.Decrement(ref _busy);
                    _busyText = null;
                    if (t.Exception != null) Ui.ShowError(t.Exception.GetBaseException());
                    RefreshState();
                    then?.Invoke();
                }));
            });
        }

        public void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex) { Ui.ShowError(ex); }
        }

        private void ToggleAll()
        {
            if (App.ActiveServices.Any(s => s.IsRunning)) StopAll();
            else StartAll();
        }

        public void StartAll()
        {
            var services = App.ActiveServices.ToList();
            if (services.Count == 0) return;
            RunAsync("Starting...", () =>
            {
                var errors = new List<string>();
                foreach (var svc in services)
                {
                    try { svc.Start(); }
                    catch (LavagonException ex) { errors.Add(ex.Message); }
                    catch (Exception ex) { Log.Error(ex); errors.Add($"{svc.Title}: {ex.Message}"); }
                }
                if (errors.Count > 0) throw new LavagonException(string.Join("\n\n----------\n\n", errors));
            });
        }

        public void StopAll()
        {
            RunAsync("Stopping...", () =>
            {
                foreach (var svc in App.Services.Where(s => s.Installed && s.IsRunning).Reverse())
                    svc.Stop();
            });
        }

        private void ToggleService(ServiceBase svc)
        {
            if (_busy > 0) return;
            if (svc.IsRunning) RunAsync("Stopping...", svc.Stop);
            else RunAsync("Starting...", svc.Start);
        }

        private void ToggleSsl()
        {
            var apache = App.Apache;
            apache.SslEnabled = !apache.SslEnabled;
            if (apache.IsRunning)
                RunAsync("Reloading...", apache.Restart, () => Notify(apache.SslEnabled ? "SSL enabled" : "SSL disabled"));
            else
                RefreshState();
        }

        public void RestartWebServers()
        {
            RunAsync("Reloading...", () =>
            {
                if (App.Apache.Installed && App.Apache.IsRunning) App.Apache.Restart();
                if (App.Nginx.Installed && App.Nginx.IsRunning) App.Nginx.Restart();
            });
        }

        private int WebPort => App.Apache.Enabled && App.Apache.Installed ? App.Apache.HttpPort : App.Nginx.HttpPort;

        private void OpenWeb()
        {
            int port = WebPort;
            Proc.OpenUrl("http://localhost" + (port == 80 ? "" : ":" + port) + "/");
        }

        public void OpenDatabase()
        {
            var heidi = Path.Combine(AppPaths.Bin, "heidisql", "heidisql.exe");
            if (File.Exists(heidi))
            {
                var pwd = App.MySql.RootPassword;
                var args = $"-h=127.0.0.1 -u=root -P={App.MySql.TcpPort}" + (pwd.Length > 0 ? $" -p=\"{pwd}\"" : "");
                Proc.Shell(heidi, args);
                return;
            }
            var adminer = Path.Combine(AppPaths.Etc, "apps", "adminer");
            if (Directory.Exists(adminer))
            {
                int port = WebPort;
                Proc.OpenUrl("http://localhost" + (port == 80 ? "" : ":" + port) + "/adminer/");
                return;
            }
            throw new LavagonException("No database tool found.\n\nPut HeidiSQL into bin\\heidisql\\ or Adminer (index.php) into etc\\apps\\adminer\\.");
        }

        public void ShowPreferences()
        {
            using (var f = new PreferencesForm())
            {
                ShowMain();
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    RebuildRows();
                    if (f.WebConfigChanged) RestartWebServers();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _tips?.Dispose();
                _menu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
