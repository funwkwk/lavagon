using System.Drawing;

namespace Lavagon.UI
{
    public static class Theme
    {
        // Brand: rhino blue (taken from the logo). Change these to re-theme the whole app.
        public static readonly Color Accent = Color.FromArgb(30, 136, 255);      // #1E88FF body
        public static readonly Color AccentLight = Color.FromArgb(51, 173, 255); // #33ADFF highlight
        public static readonly Color AccentDark = Color.FromArgb(3, 99, 226);    // #0363E2 shadow
        public static readonly Color AccentPale = Color.FromArgb(198, 226, 253); // #C6E2FD horn

        public static readonly Color Background = Color.White;
        public static readonly Color Text = Color.FromArgb(20, 20, 20);
        public static readonly Color MutedText = Color.FromArgb(110, 110, 110);
        public static readonly Color Link = AccentDark;
        public static readonly Color ToggleOff = Color.FromArgb(175, 175, 175);

        public static readonly Color ButtonFace = Color.FromArgb(253, 253, 253);
        public static readonly Color ButtonBorder = Color.FromArgb(208, 208, 208);
        public static readonly Color ButtonHover = Color.FromArgb(234, 244, 255);
        public static readonly Color ButtonHoverBorder = Accent;
        public static readonly Color ButtonPressed = AccentPale;

        public static readonly Color IconBlue = Accent;
        public static readonly Color IconDark = Color.FromArgb(60, 60, 60);

        public static Font UiFont(float size = 9.75f, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);
    }
}
