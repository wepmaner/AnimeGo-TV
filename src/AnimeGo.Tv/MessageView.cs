using System;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Полноэкранное сообщение крупным текстом; если есть retry — одна кнопка «Повторить» в фокусе.
    sealed class MessageView : IScreen
    {
        readonly Action retry, back;
        readonly Widgets ui;

        public MessageView(App app, string message, Action retry = null, Action back = null)
        {
            this.retry = retry;
            this.back = back;
            ui = new Widgets(app.Win);

            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg, false);
            var text = ui.Text(160, retry == null ? 470 : 380, 1600, 160, Theme.T(message, 56, bold: true, align: "center"), false);
            text.LineWrapType = WrapType.Word;
            if (retry == null) return;
            const int x = 760, y = 600, w = 400, h = 110;
            ui.Focus(x, y, w, h, false);
            ui.Rect(x, y, w, h, Theme.Accent, false);
            ui.Text(x, y + 25, w, 70, Theme.T(RuText.Retry, 44, bold: true, align: "center"), false);
        }

        public void Show() => ui.ShowAll();

        public void Hide() => ui.Dispose();

        public bool OnKey(string key)
        {
            if (key == Keys.Ok && retry != null) { retry(); return true; }
            if (key == Keys.Back && back != null) { back(); return true; }
            return false;
        }
    }
}
