using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lavagon.Core;

namespace Lavagon.UI
{
    /// <summary>
    /// Shown instead of the service list while bin\ has no usable services yet (fresh install):
    /// explains what to download, where to put it, and shows what is already there.
    /// </summary>
    public class WelcomePanel : Panel
    {
        private class Component
        {
            public string Title, Tool, Hint, Url;
            public bool Optional;
            public Label Name, Status;
            public LinkLabel Download;
        }

        private readonly Component[] _components =
        {
            new Component { Title = "Apache 2.4", Tool = "apache", Hint = "Win64 zip", Url = "https://www.apachelounge.com/download/" },
            new Component { Title = "PHP", Tool = "php", Hint = "Thread Safe, x64 zip", Url = "https://windows.php.net/download/" },
            new Component { Title = "MySQL", Tool = "mysql", Hint = "Windows ZIP archive (or MariaDB)", Url = "https://dev.mysql.com/downloads/mysql/" },
            new Component { Title = "Mailpit", Tool = "mailpit", Hint = "optional - catches e-mails", Url = "https://github.com/axllent/mailpit/releases", Optional = true },
        };

        private readonly Label _title, _intro, _footer;
        private readonly LinkLabel _openBin, _guide;
        private readonly float _s;
        private readonly bool _ready;

        private int S(float v) => (int)Math.Round(v * _s);

        public WelcomePanel(float scale)
        {
            _s = scale;
            SuspendLayout();
            BackColor = Theme.Background;
            DoubleBuffered = true;

            _title = new Label { Text = "Welcome to Lavagon!", Font = Theme.UiFont(12f, FontStyle.Bold), ForeColor = Theme.AccentDark, AutoSize = true };
            _intro = new Label
            {
                AutoSize = true, ForeColor = Theme.Text, UseMnemonic = false,
                Text = "Lavagon doesn't include Apache, PHP or MySQL. Download the official Windows zips (free)\n" +
                       "and extract each one into its own folder inside the bin folder:",
            };
            Controls.Add(_title);
            Controls.Add(_intro);

            foreach (var c in _components)
            {
                var comp = c;
                c.Name = new Label { Text = c.Title, AutoSize = true, Font = Theme.UiFont(9.75f, FontStyle.Bold) };
                c.Status = new Label { AutoSize = true, UseMnemonic = false };
                c.Download = new LinkLabel { Text = "Download", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark };
                c.Download.LinkClicked += (o, e) => Open(comp.Url);
                Controls.AddRange(new Control[] { c.Name, c.Status, c.Download });
            }

            _footer = new Label
            {
                AutoSize = true, ForeColor = Theme.MutedText, UseMnemonic = false,
                Text = "Example: bin\\php\\php-8.4.x-Win32-vs17-x64\\php.exe. Lavagon detects new folders automatically -\n" +
                       "no restart needed. Apache, MySQL and Mailpit are enabled by default; enable other services in\n" +
                       "Menu > Preferences > Services & Ports.",
            };
            _openBin = new LinkLabel { Text = "Open bin folder", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark };
            _openBin.LinkClicked += (o, e) => { try { Proc.Explore(AppPaths.Bin); } catch (Exception ex) { Ui.ShowError(ex); } };
            _guide = new LinkLabel { Text = "Step-by-step guide", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark };
            _guide.LinkClicked += (o, e) => Open(Program.HomePage + "#getting-the-components");
            Controls.AddRange(new Control[] { _footer, _openBin, _guide });

            _ready = true;
            ResumeLayout(false);
            UpdateStatus();
        }

        private static void Open(string url)
        {
            try { Proc.OpenUrl(url); } catch (Exception ex) { Ui.ShowError(ex); }
        }

        /// <summary>Shows which components are already in bin\ (and their versions).</summary>
        public void UpdateStatus()
        {
            foreach (var c in _components)
            {
                var versions = Versions.List(c.Tool);
                bool found = versions.Count > 0;
                c.Status.Text = found
                    ? "✓ found: " + string.Join(", ", versions.Select(Versions.Parse))
                    : "bin\\" + c.Tool + "\\   (" + c.Hint + ")";
                c.Status.ForeColor = found ? Color.FromArgb(22, 130, 60) : Theme.MutedText;
                c.Download.Visible = !found;
            }
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!_ready) return; // controls are still being created
            int y = 0;
            _title.Location = new Point(0, y); y += _title.Height + S(6);
            _intro.Location = new Point(0, y); y += _intro.Height + S(12);
            foreach (var c in _components)
            {
                c.Name.Location = new Point(S(12), y);
                c.Status.Location = new Point(S(110), y);
                c.Download.Location = new Point(Math.Max(c.Status.Right + S(12), S(380)), y);
                y += c.Name.Height + S(8);
            }
            y += S(6);
            _footer.Location = new Point(0, y); y += _footer.Height + S(10);
            _openBin.Location = new Point(0, y);
            _guide.Location = new Point(_openBin.Right + S(20), y);
        }
    }
}
