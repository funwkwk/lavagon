using System;
using System.Drawing;
using System.Windows.Forms;
using Lavagon.Core;

namespace Lavagon.UI
{
    /// <summary>"Lavagon X.Y.Z is available": Update now / Later / Skip this version.</summary>
    public class UpdateDialog : Form
    {
        public enum Choice { Later, Update, Skip }

        public Choice Result { get; private set; } = Choice.Later;

        public UpdateDialog(Updater.Release release)
        {
            float s = DeviceDpi / 96f;
            int S(float v) => (int)Math.Round(v * s);

            Text = "Lavagon Update";
            Font = Theme.UiFont(9f);
            BackColor = Theme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Icon = Logo.ToIcon(S(32));
            ClientSize = new Size(S(460), S(320));

            var logo = new PictureBox { Image = Logo.Render(S(48)), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(S(16), S(16), S(48), S(48)) };
            var title = new Label
            {
                Text = $"Lavagon {release.Version.ToString(3)} is available",
                Font = Theme.UiFont(12f, FontStyle.Bold), ForeColor = Theme.AccentDark, AutoSize = true, Location = new Point(S(76), S(16)),
            };
            var sub = new Label
            {
                Text = $"You have {Program.Version}. Updating takes a few seconds: Lavagon restarts,\nyour running services, projects and databases are not touched.",
                ForeColor = Theme.MutedText, AutoSize = true, Location = new Point(S(76), S(44)), UseMnemonic = false,
            };
            var notes = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(release.Notes) ? "(no release notes)" : release.Notes.Replace("\n", "\r\n"),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(248, 250, 253),
                Bounds = new Rectangle(S(16), S(84), S(428), S(170)), TabStop = false,
            };
            var page = new LinkLabel { Text = "Release notes on GitHub", AutoSize = true, Location = new Point(S(16), S(262)), LinkColor = Theme.Accent };
            page.LinkClicked += (o, e) => { try { Proc.OpenUrl(release.PageUrl); } catch (Exception ex) { Ui.ShowError(ex); } };

            var update = new Button { Text = "Update now", Bounds = new Rectangle(S(160), S(282), S(100), S(28)) };
            var later = new Button { Text = "Later", Bounds = new Rectangle(S(266), S(282), S(80), S(28)) };
            var skip = new Button { Text = "Skip this version", Bounds = new Rectangle(S(352), S(282), S(92), S(28)) };
            skip.Font = Theme.UiFont(8.25f);
            update.Click += (o, e) => { Result = Choice.Update; Close(); };
            later.Click += (o, e) => { Result = Choice.Later; Close(); };
            skip.Click += (o, e) => { Result = Choice.Skip; Close(); };

            Controls.AddRange(new Control[] { logo, title, sub, notes, page, update, later, skip });
            AcceptButton = update;
            CancelButton = later;
        }
    }
}
