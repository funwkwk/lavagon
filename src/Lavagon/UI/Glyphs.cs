using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Lavagon.UI
{
    public enum Glyph
    {
        None, Start, Stop, Globe, Database, Terminal, Folder, Gear, LockOpen, LockClosed, Mail, Hosts,
    }

    /// <summary>Small line icons drawn with GDI+ (scale-independent, no image assets).</summary>
    public static class Glyphs
    {
        public static void Draw(Graphics g, Glyph glyph, RectangleF r, Color color)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float w = Math.Max(1f, s / 12f);
            float x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
            using (var pen = new Pen(color, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case Glyph.Stop:
                        using (var fill = new SolidBrush(Theme.AccentPale))
                        using (var path = RoundRect(new RectangleF(x + s * 0.1f, y + s * 0.1f, s * 0.8f, s * 0.8f), s * 0.18f))
                        using (var p2 = new Pen(Theme.IconBlue, w))
                        {
                            g.FillPath(fill, path);
                            g.DrawPath(p2, path);
                        }
                        break;

                    case Glyph.Start:
                        using (var path = new GraphicsPath())
                        using (var fill = new SolidBrush(Color.FromArgb(206, 240, 214)))
                        using (var p2 = new Pen(Color.FromArgb(22, 150, 60), w) { LineJoin = LineJoin.Round })
                        {
                            path.AddPolygon(new[] { new PointF(x + s * 0.22f, y + s * 0.1f), new PointF(x + s * 0.88f, y + s * 0.5f), new PointF(x + s * 0.22f, y + s * 0.9f) });
                            g.FillPath(fill, path);
                            g.DrawPath(p2, path);
                        }
                        break;

                    case Glyph.Globe:
                        g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                        g.DrawEllipse(pen, x + s * 0.32f, y + s * 0.08f, s * 0.36f, s * 0.84f);
                        g.DrawLine(pen, x + s * 0.1f, y + s * 0.5f, x + s * 0.9f, y + s * 0.5f);
                        break;

                    case Glyph.Database:
                        {
                            float l = x + s * 0.15f, rr = s * 0.7f, eh = s * 0.24f;
                            g.DrawEllipse(pen, l, y + s * 0.06f, rr, eh);
                            g.DrawLine(pen, l, y + s * 0.18f, l, y + s * 0.82f);
                            g.DrawLine(pen, l + rr, y + s * 0.18f, l + rr, y + s * 0.82f);
                            g.DrawArc(pen, l, y + s * 0.38f, rr, eh, 0, 180);
                            g.DrawArc(pen, l, y + s * 0.70f, rr, eh, 0, 180);
                            break;
                        }

                    case Glyph.Terminal:
                        using (var path = RoundRect(new RectangleF(x + s * 0.06f, y + s * 0.12f, s * 0.88f, s * 0.76f), s * 0.12f))
                            g.DrawPath(pen, path);
                        g.DrawLines(pen, new[] { new PointF(x + s * 0.24f, y + s * 0.36f), new PointF(x + s * 0.40f, y + s * 0.5f), new PointF(x + s * 0.24f, y + s * 0.64f) });
                        g.DrawLine(pen, x + s * 0.48f, y + s * 0.66f, x + s * 0.72f, y + s * 0.66f);
                        break;

                    case Glyph.Folder:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[]
                            {
                                new PointF(x + s * 0.06f, y + s * 0.84f), new PointF(x + s * 0.06f, y + s * 0.18f),
                                new PointF(x + s * 0.38f, y + s * 0.18f), new PointF(x + s * 0.48f, y + s * 0.30f),
                                new PointF(x + s * 0.94f, y + s * 0.30f), new PointF(x + s * 0.94f, y + s * 0.84f),
                            });
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                            g.DrawLine(pen, x + s * 0.06f, y + s * 0.42f, x + s * 0.94f, y + s * 0.42f);
                        }
                        break;

                    case Glyph.Gear:
                        DrawGear(g, color, x, y, s);
                        break;

                    case Glyph.LockOpen:
                    case Glyph.LockClosed:
                        using (var path = RoundRect(new RectangleF(x + s * 0.18f, y + s * 0.45f, s * 0.64f, s * 0.48f), s * 0.08f))
                            g.DrawPath(pen, path);
                        if (glyph == Glyph.LockClosed)
                            g.DrawArc(pen, x + s * 0.30f, y + s * 0.10f, s * 0.40f, s * 0.70f, 180, 180);
                        else
                            g.DrawArc(pen, x + s * 0.42f, y + s * 0.04f, s * 0.40f, s * 0.70f, 180, 160);
                        break;

                    case Glyph.Mail:
                        g.DrawRectangle(pen, x + s * 0.08f, y + s * 0.2f, s * 0.84f, s * 0.6f);
                        g.DrawLines(pen, new[] { new PointF(x + s * 0.08f, y + s * 0.22f), new PointF(x + s * 0.5f, y + s * 0.55f), new PointF(x + s * 0.92f, y + s * 0.22f) });
                        break;

                    case Glyph.Hosts:
                        using (var f = new Font("Segoe UI", s * 0.8f, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("h", f, brush, new RectangleF(x, y - s * 0.05f, s, s), sf);
                        g.DrawLine(pen, x + s * 0.25f, y + s * 0.95f, x + s * 0.75f, y + s * 0.95f);
                        break;
                }
            }
            g.SmoothingMode = oldMode;
        }

        private static void DrawGear(Graphics g, Color color, float x, float y, float s)
        {
            float cx = x + s / 2, cy = y + s / 2;
            float outer = s * 0.48f, inner = s * 0.36f;
            const int teeth = 8;
            var pts = new PointF[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                double a = i * Math.PI * 2 / teeth;
                double d = Math.PI / teeth * 0.45;
                pts[i * 4 + 0] = Polar(cx, cy, inner, a - d * 1.6);
                pts[i * 4 + 1] = Polar(cx, cy, outer, a - d);
                pts[i * 4 + 2] = Polar(cx, cy, outer, a + d);
                pts[i * 4 + 3] = Polar(cx, cy, inner, a + d * 1.6);
            }
            using (var pen = new Pen(color, Math.Max(1f, s / 14f)) { LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(pen, pts);
                g.DrawEllipse(pen, cx - s * 0.14f, cy - s * 0.14f, s * 0.28f, s * 0.28f);
            }
        }

        private static PointF Polar(float cx, float cy, float r, double a) =>
            new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
