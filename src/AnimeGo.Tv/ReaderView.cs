using System;
using System.Collections.Generic;
using System.Linq;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Читалка поверх экрана (макет title.html, кадр 4): полный текст рецензии или описания крупно, по страницам.
    // ↑/← — предыдущая страница, ↓/→ — следующая, «Назад» и OK — закрыть. Внизу «2 / 5».
    sealed class ReaderView
    {
        const int PanelW = 1240, PanelX = (Theme.W - PanelW) / 2, PadX = 90, TextW = PanelW - 2 * PadX;
        const int Font = 28, LineH = 44, CharsPerLine = 66, BodyH = 14 * LineH;

        readonly Widgets ui;
        readonly Label body, counter;
        readonly List<List<string>> pages;
        readonly Action close;
        int page;

        public ReaderView(Window win, string meta, string title, string text, Action close)
        {
            this.close = close;
            ui = new Widgets(win);
            ui.Rect(0, 0, Theme.W, Theme.H, Color.FromRgba(0, 0, 0, 200));
            ui.Rect(PanelX, 0, PanelW, Theme.H, Theme.Card);
            var y = 70;
            if (!string.IsNullOrEmpty(meta))
            {
                var m = ui.Text(PanelX + PadX, y, TextW, 32, Theme.T(meta, 20, Theme.MutedHex));
                m.IsEllipsis = true;
                y += 44;
            }
            var t = ui.Text(PanelX + PadX, y, TextW, 108, Theme.T(title, 40, bold: true));
            t.LineWrapType = WrapType.Word;   // заголовок до двух строк
            y += Lines(title, 50) > 1 ? 122 : 70;
            pages = TextPages.Split(text, CharsPerLine, Math.Min(14, (Theme.H - 120 - y) / LineH));
            body = ui.Text(PanelX + PadX, y + 10, TextW, BodyH, "");
            counter = ui.Text(PanelX + PadX, Theme.H - 80, TextW, 32, "");
            Show();
        }

        static int Lines(string s, int chars) => Math.Max(1, ((s ?? "").Length + chars - 1) / chars);

        void Show()
        {
            body.Text = "<align=left><font_size=" + Font + " color=#d5d8de>" +
                string.Join("<br>", pages[page].Select(Theme.Escape)) + "</font_size></align>";
            counter.Text = pages.Count > 1 ? Theme.T(RuText.Of(page + 1, pages.Count), 20, Theme.MutedHex) : "";
        }

        public bool OnKey(string key)
        {
            switch (key)
            {
                case Keys.Up: case Keys.Left: if (page > 0) { page--; Show(); } return true;
                case Keys.Down: case Keys.Right: if (page < pages.Count - 1) { page++; Show(); } return true;
                case Keys.Back: case Keys.Ok: close(); return true;
            }
            return key != Keys.Exit && !key.StartsWith("XF86Audio", StringComparison.Ordinal);   // громкость — телевизору
        }

        public void Hide() => ui.Dispose();
    }

    // Кадры на весь экран: снизу кадр 640×360 (обычно уже в кеше), поверх — оригинал, когда загрузится.
    // ←→ — соседний кадр, «Назад» и OK — закрыть.
    sealed class GalleryView
    {
        readonly App app;
        readonly Widgets ui;
        readonly IReadOnlyList<string> shots;
        readonly Action close;
        readonly Label counter;
        readonly Polygon pill;
        Image small, full;
        int index;

        public GalleryView(App app, IReadOnlyList<string> shots, int index, Action close)
        {
            this.app = app;
            this.shots = shots;
            this.index = index;
            this.close = close;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Color.Black);
            small = ui.Picture(0, 0, Theme.W, Theme.H);
            full = ui.Picture(0, 0, Theme.W, Theme.H);
            pill = ui.RoundRect(Theme.W / 2 - 70, Theme.H - 90, 140, 50, 25, Color.FromRgba(0, 0, 0, 184));
            counter = ui.Text(Theme.W / 2 - 70, Theme.H - 82, 140, 36, "");
            Show();
        }

        // https://img.cdngos.com/v/640x360/screenshot/… → https://img.cdngos.com/screenshot/… (оригинал).
        static string Original(string url)
        {
            var i = url.IndexOf("/v/", StringComparison.Ordinal);
            if (i < 0) return url;
            var j = url.IndexOf('/', i + 3);
            return j < 0 ? url : url.Substring(0, i) + url.Substring(j);
        }

        void Show()
        {
            // Новые Image: у старых могла остаться картинка соседнего кадра, а загрузка оригинала идёт секунды.
            ui.Remove(small);
            ui.Remove(full);
            small = ui.Picture(0, 0, Theme.W, Theme.H);
            full = ui.Picture(0, 0, Theme.W, Theme.H);
            ui.Raise(pill);
            ui.Raise(counter);
            app.Images.Set(small, shots[index]);
            app.Images.Set(full, Original(shots[index]));
            counter.Text = Theme.T(RuText.Of(index + 1, shots.Count), 24, bold: true, align: "center");
        }

        public bool OnKey(string key)
        {
            switch (key)
            {
                case Keys.Left: if (index > 0) { index--; Show(); } return true;
                case Keys.Right: if (index < shots.Count - 1) { index++; Show(); } return true;
                case Keys.Back: case Keys.Ok: close(); return true;
            }
            return key != Keys.Exit && !key.StartsWith("XF86Audio", StringComparison.Ordinal);   // громкость — телевизору
        }

        public void Hide() => ui.Dispose();
    }
}
