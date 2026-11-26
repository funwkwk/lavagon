using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lavagon.Setup
{
    /// <summary>One-page setup wizard: folder + options, then progress, then finish.</summary>
    public class SetupForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(30, 136, 255);
        private static readonly Color AccentDark = Color.FromArgb(3, 99, 226);
        private static readonly Color Muted = Color.FromArgb(105, 105, 105);

        private readonly float _s;
        private readonly TextBox _dir;
        private readonly Button _browse, _install, _cancel;
        private readonly CheckBox _desktop, _startMenu, _launch;
        private readonly ProgressBar _progress;
        private readonly Label _status, _intro;
        private bool _done;

        private int S(float v) => (int)Math.Round(v * _s);

        public SetupForm()
        {
            _s = DeviceDpi / 96f;
            Text = $"Lavagon {Installer.Version} Setup";
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(540), S(370));
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);

            // Header band
            var header = new Panel { Bounds = new Rectangle(0, 0, ClientSize.Width, S(96)), BackColor = Color.FromArgb(240, 247, 255) };
            header.Paint += (o, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(214, 232, 255))) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            var logo = new PictureBox { Bounds = new Rectangle(S(20), S(12), S(80), S(72)), SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo(), BackColor = Color.Transparent };
            var title = new Label { Text = "Lavagon", Font = new Font("Segoe UI Semibold", 17f), ForeColor = AccentDark, AutoSize = true, Location = new Point(S(112), S(18)), BackColor = Color.Transparent };
            var subtitle = new Label
            {
                Text = $"Version {Installer.Version} · Portable local development environment for Windows",
                ForeColor = Muted, AutoSize = true, Location = new Point(S(115), S(56)), BackColor = Color.Transparent,
            };
            header.Controls.AddRange(new Control[] { logo, title, subtitle });
            Controls.Add(header);

            bool upgrade = File.Exists(Path.Combine(Installer.DefaultDirectory, "Lavagon.exe"));
            _intro = new Label
            {
                Text = upgrade
                    ? "Lavagon is already installed in this folder. Setup will update the program and keep your\nprojects, databases and settings."
                    : "Choose where to install Lavagon. Your projects (www), databases (data) and tools (bin)\nwill live in this folder too, so pick a drive with enough space.",
                Location = new Point(S(24), S(112)), AutoSize = true,
            };
            Controls.Add(_intro);

            Controls.Add(new Label { Text = "Install folder", Location = new Point(S(24), S(160)), AutoSize = true });
            _dir = new TextBox { Text = Installer.DefaultDirectory, Location = new Point(S(24), S(180)), Width = S(400) };
            _browse = new Button { Text = "Browse...", Location = new Point(S(432), S(178)), Size = new Size(S(84), S(26)) };
            _browse.Click += (o, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Select the Lavagon folder", SelectedPath = _dir.Text, ShowNewFolderButton = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) _dir.Text = dlg.SelectedPath.EndsWith("lavagon", StringComparison.OrdinalIgnoreCase) ? dlg.SelectedPath : Path.Combine(dlg.SelectedPath, "lavagon");
            };
            Controls.AddRange(new Control[] { _dir, _browse });

            _startMenu = new CheckBox { Text = "Create a Start menu shortcut", Checked = true, Location = new Point(S(24), S(218)), AutoSize = true };
            _desktop = new CheckBox { Text = "Create a desktop shortcut", Checked = true, Location = new Point(S(24), S(242)), AutoSize = true };
            _launch = new CheckBox { Text = "Run Lavagon when setup finishes", Checked = true, Location = new Point(S(24), S(266)), AutoSize = true };
            Controls.AddRange(new Control[] { _startMenu, _desktop, _launch });

            _progress = new ProgressBar { Location = new Point(S(24), S(298)), Size = new Size(S(492), S(10)), Visible = false };
            _status = new Label { Location = new Point(S(24), S(312)), AutoSize = true, ForeColor = Muted, Text = "" };
            Controls.AddRange(new Control[] { _progress, _status });

            _install = new Button { Text = upgrade ? "Update" : "Install", Location = new Point(S(340), S(330)), Size = new Size(S(84), S(28)) };
            _cancel = new Button { Text = "Cancel", Location = new Point(S(432), S(330)), Size = new Size(S(84), S(28)), DialogResult = DialogResult.Cancel };
            _install.Click += async (o, e) => await InstallAsync();
            _cancel.Click += (o, e) => Close();
            Controls.AddRange(new Control[] { _install, _cancel });
            AcceptButton = _install;
            CancelButton = _cancel;

            if (!Installer.HasPayload)
            {
                _install.Enabled = false;
                _status.Text = "This setup was built without the Lavagon files.";
            }
        }

        private static Image LoadLogo()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lavagon.Setup.logo.png"))
                return s == null ? null : new Bitmap(s);
        }

        private async Task InstallAsync()
        {
            if (_done)
            {
                if (_launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(_dir.Text.Trim(), "Lavagon.exe")) { WorkingDirectory = _dir.Text.Trim() });
                Close();
                return;
            }

            var dir = _dir.Text.Trim();
            if (dir.Length < 3 || !Path.IsPathRooted(dir))
            {
                MessageBox.Show(this, "Please choose a full folder path, for example C:\\lavagon.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (dir.Contains(" ") &&
                MessageBox.Show(this, "The folder contains a space. Lavagon works with spaces, but some PHP tools don't.\n\nContinue anyway?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (Control c in new Control[] { _dir, _browse, _desktop, _startMenu, _install, _cancel }) c.Enabled = false;
            _progress.Visible = true;
            var options = new InstallOptions { Directory = dir, DesktopShortcut = _desktop.Checked, StartMenuShortcut = _startMenu.Checked };
            var progress = new Progress<Tuple<int, string>>(p => { _progress.Value = p.Item1; _status.Text = p.Item2; });
            IProgress<Tuple<int, string>> report = progress;

            try
            {
                await Task.Run(() => Installer.Install(options, (pct, text) => report.Report(Tuple.Create(pct, text))));
                _done = true;
                _intro.Text = $"Lavagon {Installer.Version} is installed in {dir}.\n\nNext: put Apache, PHP and MySQL into the bin folder (see bin\\README.txt), then click Start All.";
                _status.Text = "Installation complete.";
                _status.ForeColor = Color.FromArgb(22, 130, 60);
                _install.Text = "Finish";
                _install.Enabled = true;
                _install.Focus();
            }
            catch (Exception ex)
            {
                _status.Text = "Installation failed.";
                _status.ForeColor = Color.FromArgb(200, 40, 40);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                foreach (Control c in new Control[] { _dir, _browse, _desktop, _startMenu, _install, _cancel }) c.Enabled = true;
                _progress.Visible = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Thin accent line under the header, matching Lavagon's blue.
            using (var b = new LinearGradientBrush(new Rectangle(0, S(96), ClientSize.Width, S(3)), Accent, Color.FromArgb(51, 173, 255), LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(b, 0, S(96), ClientSize.Width, S(3));
        }
    }
}
