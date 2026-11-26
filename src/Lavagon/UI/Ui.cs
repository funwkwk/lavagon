using System;
using System.Drawing;
using System.Windows.Forms;
using Lavagon.Core;

namespace Lavagon.UI
{
    public static class Ui
    {
        public static void ShowError(Exception ex)
        {
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1) ex = agg.InnerException;
            if (ex is LavagonException)
            {
                MessageBox.Show(ex.Message, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Log.Error(ex);
            MessageBox.Show("Oops! Something went sideways... Lavagon detected an error:\n\n" + ex.Message +
                            "\n\nDetails were written to logs\\lavagon.log", Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Info(string message) =>
            MessageBox.Show(message, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);

        public static bool Confirm(string message) =>
            MessageBox.Show(message, Program.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        /// <summary>Simple one-line input dialog. Returns null when cancelled.</summary>
        public static string Prompt(string title, string label, string value = "", bool password = false, IWin32Window owner = null)
        {
            using (var form = new Form())
            {
                float s = form.DeviceDpi / 96f;
                int S(int v) => (int)Math.Round(v * s);
                form.Text = title;
                form.Font = Theme.UiFont(9f);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = form.MaximizeBox = false;
                form.ShowInTaskbar = owner == null;
                form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                form.BackColor = Theme.Background;
                form.ClientSize = new Size(S(380), S(118));

                var lbl = new Label { Text = label, AutoSize = true, Location = new Point(S(16), S(14)) };
                var box = new TextBox { Text = value, Location = new Point(S(16), S(38)), Width = S(348), UseSystemPasswordChar = password };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(S(196), S(76)), Size = new Size(S(80), S(28)) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(S(284), S(76)), Size = new Size(S(80), S(28)) };
                form.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += (o, e) => { box.Focus(); box.SelectAll(); };
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        public static string PickFolder(string description, string initial, IWin32Window owner)
        {
            using (var dlg = new FolderBrowserDialog { Description = description, SelectedPath = initial, ShowNewFolderButton = true })
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
        }
    }
}
