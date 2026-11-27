using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Lavagon.Core;

namespace Lavagon.UI
{
    /// <summary>
    /// Click on [profile] in the main window: the version folder in use for every installed tool,
    /// in lavagon.ini style. The text can be selected and copied (handy for bug reports).
    /// </summary>
    public class InfoDialog : Form
    {
        public InfoDialog(string title, string text)
        {
            float s = DeviceDpi / 96f;
            int S(float v) => (int)Math.Round(v * s);

            Text = title;
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = Logo.ToIcon(S(32));

            var footer = new Panel { Dock = DockStyle.Bottom, Height = S(46), BackColor = Color.FromArgb(240, 240, 240) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(S(84), S(28)), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            footer.Controls.Add(ok);

            var box = new TextBox
            {
                Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Background,
                ForeColor = Theme.Text, TabStop = false, WordWrap = false,
            };

            // Size the window to the text, within reason; scroll beyond that.
            var lines = box.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var measure = TextRenderer.MeasureText(string.Join("\n", lines.OrderByDescending(l => l.Length).Take(3)), Font);
            int lineH = TextRenderer.MeasureText("Xg", Font).Height;
            int w = Math.Max(S(360), Math.Min(S(720), measure.Width + S(40)));
            int h = Math.Min(S(560), lines.Length * lineH + S(24));
            if (lines.Length * lineH + S(24) > h) box.ScrollBars = ScrollBars.Vertical;
            ClientSize = new Size(w + S(24), h + S(16) + footer.Height);
            box.Bounds = new Rectangle(S(12), S(14), w, h);
            footer.Width = ClientSize.Width; // docking only applies once the form lays out
            ok.Location = new Point(footer.Width - ok.Width - S(12), (footer.Height - ok.Height) / 2);

            Controls.Add(box);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = ok;
            Shown += (o, e) => { box.SelectionLength = 0; ok.Focus(); };
        }

        /// <summary>"Current profile: default" followed by [tool] / Version=folder for each installed tool.</summary>
        public static string InstalledVersions()
        {
            var sb = new StringBuilder();
            sb.Append("Current profile: ").Append(App.Settings.Profile).Append("\n");
            void Add(string section, string folder)
            {
                if (string.IsNullOrEmpty(folder)) return;
                sb.Append('[').Append(section).Append("]\n").Append("Version=").Append(folder).Append("\n\n");
            }

            Add(App.Apache.Tool, App.Apache.Folder);
            Add(App.Nginx.Tool, App.Nginx.Folder);
            Add(App.MySql.Tool, App.MySql.Folder);
            Add("php", Php.Folder);
            foreach (var svc in new Core.Services.ServiceBase[] { App.PostgreSql, App.MongoDb, App.Redis, App.Memcached, App.Mailpit })
                Add(svc.Tool, svc.Folder);
            foreach (var tool in new[] { "nodejs", "python" })
                Add(tool, Versions.Resolve(tool, App.Settings.Get(tool, "Version")));

            var text = sb.ToString().TrimEnd('\n');
            return text.Contains("[") ? text : text + "\n\nNothing installed in bin\\ yet.";
        }
    }
}
