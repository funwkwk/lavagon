using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Lavagon.Core;

namespace Lavagon.UI
{
    /// <summary>
    /// Shown instead of the service list while Lavagon downloads and sets up Apache, PHP, MySQL,
    /// Mailpit and the Visual C++ runtime (first start, or when bin\ has nothing usable).
    /// </summary>
    public class SetupPanel : Panel
    {
        private class Row
        {
            public Components.Item Item;
            public Label Name, Status;
            public ProgressLine Bar;
        }

        private readonly Label _title, _intro, _footer;
        private readonly TextLink _retry;
        private readonly List<Row> _rows = new List<Row>();
        private readonly float _s;
        private CancellationTokenSource _cts;

        private const string IntroText =
            "Lavagon is downloading Apache, PHP, MySQL and Mailpit from their official websites.\n" +
            "This happens only once and takes a few minutes, depending on your connection.";

        private bool _idle;

        public bool Running { get; private set; }

        /// <summary>Raised on the UI thread when a run ends; true when everything needed is installed.</summary>
        public event Action<bool> Finished;

        private int S(float v) => (int)Math.Round(v * _s);

        public SetupPanel(float scale)
        {
            _s = scale;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            _title = new Label { Text = "Setting up Lavagon", Font = Theme.UiFont(12f, FontStyle.Bold), ForeColor = Theme.AccentDark, AutoSize = true };
            _intro = new Label
            {
                AutoSize = true, ForeColor = Theme.Text, UseMnemonic = false,
                Text = IntroText,
            };
            _footer = new Label { AutoSize = true, ForeColor = Theme.MutedText, UseMnemonic = false };
            _retry = new TextLink { Text = "Retry", ForeColor = Theme.Link, Visible = false };
            _retry.Click += (o, e) => Start();
            Controls.AddRange(new Control[] { _title, _intro, _footer, _retry });
        }

        /// <summary>Downloads whatever is missing. Does nothing while a run is in progress.</summary>
        public void Start()
        {
            if (Running) return;
            var items = Components.Plan();
            if (items.Count == 0)
            {
                Components.MarkDone();
                Finished?.Invoke(true);
                return;
            }

            Running = true;
            _idle = false;
            BuildRows(items);
            _intro.Text = IntroText;
            _retry.Text = "Retry";
            _retry.Visible = false;
            _footer.ForeColor = Theme.MutedText;
            _footer.Text = "Your services appear here as soon as everything is ready.";
            UpdateView();

            _cts = new CancellationTokenSource();
            var task = Components.RunAsync(items, () => Ui(UpdateView), _cts.Token);
            task.ContinueWith(t => Ui(() =>
            {
                Running = false;
                if (t.Exception != null) Log.Error(t.Exception);
                bool ok = items.All(i => i.Optional || i.State == Components.State.Done || i.State == Components.State.Skipped);
                if (ok) Components.MarkDone();
                else
                {
                    _footer.ForeColor = Color.FromArgb(190, 40, 40);
                    _footer.Text = "Some components could not be set up. Check your internet connection, then click Retry.\n" +
                                   "Details are in logs\\lavagon.log.";
                    _retry.Visible = true;
                }
                UpdateView();
                Finished?.Invoke(ok);
            }));
        }

        public void Cancel() => _cts?.Cancel();

        /// <summary>Nothing to show in the main window and no setup running (e.g. every service switched off).</summary>
        public void ShowNoServices()
        {
            if (Running) return;
            BuildRows(new List<Components.Item>());
            bool missing = Components.Plan().Any();
            _idle = true;
            _intro.Text = "Turn services on in Menu > Preferences > Services & Ports" +
                          (missing ? ",\nor let Lavagon download the missing ones (Apache, PHP, MySQL, Mailpit)." : ".");
            _footer.Text = "";
            _retry.Text = "Download missing components";
            _retry.Visible = missing;
            UpdateView();
        }

        private void Ui(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(a);
            }
            catch (InvalidOperationException)
            {
                // window is closing
            }
        }

        private void BuildRows(List<Components.Item> items)
        {
            SuspendLayout();
            foreach (var r in _rows)
            {
                Controls.Remove(r.Name); r.Name.Dispose();
                Controls.Remove(r.Status); r.Status.Dispose();
                Controls.Remove(r.Bar); r.Bar.Dispose();
            }
            _rows.Clear();
            foreach (var item in items)
            {
                var row = new Row
                {
                    Item = item,
                    Name = new Label { Text = item.Title, AutoSize = true, Font = Theme.UiFont(9.75f, FontStyle.Bold), UseMnemonic = false },
                    Status = new Label { AutoSize = true, UseMnemonic = false, ForeColor = Theme.MutedText },
                    Bar = new ProgressLine(),
                };
                Controls.AddRange(new Control[] { row.Name, row.Status, row.Bar });
                _rows.Add(row);
            }
            ResumeLayout();
        }

        /// <summary>Copies the item states into the labels.</summary>
        public void UpdateView()
        {
            foreach (var r in _rows)
            {
                var i = r.Item;
                var text = i.Status ?? "";
                if (r.Status.Text != text) r.Status.Text = text;
                r.Status.ForeColor =
                    i.State == Components.State.Done ? Color.FromArgb(22, 130, 60) :
                    i.State == Components.State.Failed ? Color.FromArgb(190, 40, 40) :
                    i.State == Components.State.Skipped ? Color.FromArgb(170, 110, 0) :
                    i.State == Components.State.Working ? Theme.Text : Theme.MutedText;
                r.Bar.Visible = i.State == Components.State.Working;
                r.Bar.Value = i.Progress;
            }
            bool done = !Running && _rows.All(r => r.Item.Optional || r.Item.State != Components.State.Failed);
            _title.Text = _idle ? "No services enabled" : Running ? "Setting up Lavagon" : done ? "Lavagon is ready" : "Setup is not complete";
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_title == null) return; // still constructing
            int y = 0;
            _title.Location = new Point(0, y); y += _title.Height + S(6);
            _intro.Location = new Point(0, y); y += _intro.Height + S(14);
            foreach (var r in _rows)
            {
                r.Name.Location = new Point(S(12), y);
                r.Status.Location = new Point(S(160), y);
                r.Bar.Bounds = new Rectangle(S(160), y + r.Status.Height + S(1), Math.Max(S(40), Math.Min(S(420), Width - S(170))), S(4));
                y += r.Name.Height + S(14);
            }
            y += S(4);
            _footer.Location = new Point(0, y); y += _footer.Height + S(8);
            _retry.Location = new Point(0, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _cts?.Cancel(); _cts?.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>Thin progress bar; a moving segment when the progress is unknown.</summary>
        private class ProgressLine : Control
        {
            private double _value = -1;
            private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer { Interval = 40 };
            private int _phase;

            public ProgressLine()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                _anim.Tick += (o, e) => { _phase = (_phase + 1) % 100; Invalidate(); };
            }

            public double Value
            {
                get => _value;
                set
                {
                    if (Math.Abs(_value - value) < 0.001) return;
                    _value = value;
                    UpdateTimer();
                    Invalidate();
                }
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                UpdateTimer();
            }

            private void UpdateTimer() => _anim.Enabled = Visible && _value < 0;

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Theme.Background);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = Height / 2f;
                using (var track = new SolidBrush(Color.FromArgb(230, 236, 244)))
                using (var path = Glyphs.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), r))
                    g.FillPath(track, path);

                RectangleF fill;
                if (_value >= 0) fill = new RectangleF(0, 0, (float)(Math.Min(1, _value) * (Width - 1)), Height - 1);
                else
                {
                    float seg = Width * 0.25f;
                    float x = (Width + seg) * _phase / 100f - seg;
                    fill = RectangleF.Intersect(new RectangleF(x, 0, seg, Height - 1), new RectangleF(0, 0, Width - 1, Height - 1));
                }
                if (fill.Width < 1) return;
                using (var bar = new SolidBrush(Theme.Accent))
                using (var path = Glyphs.RoundRect(fill, Math.Min(r, fill.Width / 2)))
                    g.FillPath(bar, path);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _anim.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
