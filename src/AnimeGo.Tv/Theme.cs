using ElmSharp;

namespace AnimeGo.Tv
{
    // Цвета из спеки §5. Экран — 1920×1080, поля 80. Шрифт системный (Manrope на ТВ нет).
    static class Theme
    {
        public static readonly Color Bg = Color.FromHex("#0e0f13");
        public static readonly Color Card = Color.FromHex("#1b1d24");
        public static readonly Color Card2 = Color.FromHex("#22252d");
        public static readonly Color Text = Color.FromHex("#f2f3f5");
        public static readonly Color Muted = Color.FromHex("#b4b9c3");
        public static readonly Color Accent = Color.FromHex("#e5484d");
        public static readonly Color Focus = Color.FromHex("#ffffff");

        public const string TextHex = "#f2f3f5";
        public const string MutedHex = "#b4b9c3";
        public const string AccentHex = "#e5484d";
        public const string BgHex = "#0e0f13";

        public const int W = 1920, H = 1080, Margin = 80;
        public const int FocusBorder = 4, FocusGap = 4;   // белая рамка 4 px с тёмным зазором 4 px

        // Разметка EFL-текста. align — left/center/right.
        public static string T(string text, int px, string color = TextHex, bool bold = false, string align = "left") =>
            "<align=" + align + "><font_size=" + px + " color=" + color + (bold ? " font_weight=Bold" : "") + ">" +
            Escape(text) + "</font_size></align>";

        public static string Escape(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
