using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Lavagon.UI
{
    /// <summary>
    /// The Lavagon rhino. The 512 px master (Resources\lavagon.png) is embedded in the exe and
    /// scaled down in halving steps, which keeps it crisp even at 16 px in the tray.
    /// Also produces Resources\lavagon.ico (Lavagon.exe --export-icon).
    /// </summary>
    public static class Logo
    {
        private static Bitmap _master;

        private static Bitmap Master
        {
            get
            {
                if (_master != null) return _master;
                using (var s = typeof(Logo).Assembly.GetManifestResourceStream("Lavagon.logo.png"))
                    _master = s != null ? new Bitmap(s) : new Bitmap(1, 1);
                return _master;
            }
        }

        public static Bitmap Render(int size, bool active = true)
        {
            // Progressive halving: one big bicubic downscale (512 -> 16) aliases badly.
            Bitmap current = new Bitmap(Master);
            while (current.Width / 2 >= size)
            {
                var half = Scale(current, current.Width / 2);
                current.Dispose();
                current = half;
            }
            var result = Scale(current, size);
            current.Dispose();
            if (!active) Grayscale(result);
            return result;
        }

        private static Bitmap Scale(Image src, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the borders
                    g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
        }

        /// <summary>Stopped state for the tray icon: grey and slightly faded.</summary>
        private static void Grayscale(Bitmap bmp)
        {
            var matrix = new ColorMatrix(new[]
            {
                new[] { 0.30f, 0.30f, 0.30f, 0, 0 },
                new[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                new[] { 0f, 0, 0, 0.85f, 0 },
                new[] { 0.05f, 0.05f, 0.05f, 0, 1 },
            });
            using (var copy = new Bitmap(bmp))
            using (var g = Graphics.FromImage(bmp))
            using (var attrs = new ImageAttributes())
            {
                attrs.SetColorMatrix(matrix);
                g.Clear(Color.Transparent);
                g.DrawImage(copy, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
        }

        public static Icon ToIcon(int size, bool active = true)
        {
            using (var bmp = Render(size, active))
            {
                IntPtr h = bmp.GetHicon();
                using (var tmp = Icon.FromHandle(h))
                {
                    var icon = (Icon)tmp.Clone();
                    DestroyIcon(h);
                    return icon;
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public static void ExportPng(string path, int size)
        {
            using (var bmp = Render(size)) bmp.Save(path, ImageFormat.Png);
        }

        public static void ExportIco(string path)
        {
            using (var fs = File.Create(path)) WriteIco(fs, new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, true);
        }

        /// <summary>Writes a multi-resolution .ico with PNG-compressed images.</summary>
        private static void WriteIco(Stream stream, int[] sizes, bool active)
        {
            var images = new List<byte[]>();
            foreach (var size in sizes)
                using (var bmp = Render(size, active))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            var w = new BinaryWriter(stream);
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
            w.Flush();
        }
    }
}
