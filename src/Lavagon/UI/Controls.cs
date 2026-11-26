using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lavagon.UI
{
    /// <summary>Flat push button with a line icon (Start All / Web / Database / Terminal / Root).</summary>
    public class FlatButton : Control
    {
        private bool _hover, _pressed;
        private Glyph _glyph;

        public Glyph Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Default;
            BackColor = Theme.Background;
            TabStop = true;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var face = !Enabled ? Color.FromArgb(245, 245, 245) : _pressed ? Theme.ButtonPressed : _hover ? Theme.ButtonHover : Theme.ButtonFace;
            var border = _hover && Enabled ? Theme.ButtonHoverBorder : Theme.ButtonBorder;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Glyphs.RoundRect(rect, 3f * DeviceDpi / 96f))
            using (var b = new SolidBrush(face))
            using (var p = new Pen(border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            var textColor = Enabled ? Theme.Text : Theme.MutedText;
            var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            float icon = _glyph == Glyph.None ? 0 : (float)Math.Round(Font.Height * 0.85f);
            float gap = icon > 0 ? 4f * DeviceDpi / 96f : 0;
            float total = icon + gap + textSize.Width;
            float x = (Width - total) / 2f;
            if (icon > 0)
                Glyphs.Draw(g, _glyph, new RectangleF(x, (Height - icon) / 2f, icon, icon), Enabled ? Theme.IconDark : Theme.MutedText);
            var textRect = new Rectangle((int)(x + icon + gap), 0, textSize.Width + 2, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 6, Height - 6));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Small pill switch shown in front of each service. On = service running.</summary>
    public class ToggleSwitch : Control
    {
        private bool _on;
        private bool _busy;

        public bool On { get => _on; set { if (_on != value) { _on = value; Invalidate(); } } }
        public bool Busy { get => _busy; set { _busy = value; Invalidate(); } }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float scale = DeviceDpi / 96f;
            float h = Math.Min(Height - 2, 13 * scale), w = Math.Min(Width - 2, 22 * scale);
            var r = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
            var color = _busy ? Theme.MutedText : _on ? Theme.Accent : Theme.ToggleOff;

            using (var path = Glyphs.RoundRect(r, h / 2f))
            using (var pen = new Pen(color, 1.3f * scale))
            {
                if (_on && !_busy)
                    using (var fill = new SolidBrush(Color.FromArgb(28, Theme.Accent)))
                        g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            float knob = h * 0.38f;
            float cx = _on ? r.Right - h / 2f : r.Left + h / 2f;
            float cy = r.Top + h / 2f;
            using (var pen = new Pen(color, 1.3f * scale))
                g.DrawEllipse(pen, cx - knob / 2f, cy - knob / 2f, knob, knob);
        }
    }

    /// <summary>A borderless, clickable glyph (gear, lock, mail, hosts).</summary>
    public class GlyphButton : Control
    {
        private bool _hover;
        public Glyph Glyph { get; set; }
        public Color GlyphColor { get; set; } = Theme.IconDark;
        public float GlyphScale { get; set; } = 1f;

        public GlyphButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
            float s = Math.Min(Width, Height) * GlyphScale;
            var color = _hover ? Theme.Accent : GlyphColor;
            Glyphs.Draw(e.Graphics, Glyph, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s), color);
        }
    }

    /// <summary>Plain text that behaves like a link (hover underline, hand cursor).</summary>
    public class TextLink : Label
    {
        public Color HoverColor { get; set; } = Theme.Accent;
        private Color _normal;

        public TextLink()
        {
            AutoSize = true;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _normal = ForeColor;
            ForeColor = HoverColor;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            ForeColor = _normal;
            base.OnMouseLeave(e);
        }
    }
}
