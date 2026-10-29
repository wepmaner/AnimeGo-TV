using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // «Пропустить опенинг» поверх видео справа снизу, когда опенинг начался: фокус сразу на ней (OK — пропустить),
    // полоса внутри убывает за Seconds — так видно, что кнопка скоро исчезнет. Панель плеера при этом скрыта.
    sealed class SkipOverlay
    {
        public const double Seconds = 10;
        const int W = 380, H = 76, R = H / 2, X = Theme.W - Theme.Margin - W, Y = Theme.H - 96 - H;
        static readonly Color Back = Color.FromHex("#16181e"), Fill = Color.FromHex("#7a2a30");   // непрозрачные: под кольцом фокуса белая подложка

        readonly Widgets ui;
        readonly Polygon ring, back, fill;
        readonly Label text;
        readonly UiTimer timer = new UiTimer();
        long shownAt;

        public bool Visible { get; private set; }

        public SkipOverlay(Widgets ui)
        {
            this.ui = ui;
            ring = ui.RoundFocus(X, Y, W, H, R, Back, false);   // белое кольцо фокуса — кнопка всегда «в фокусе»
            back = ui.RoundRect(X, Y, W, H, R, Back, false);
            fill = ui.RoundRect(X, Y, W, H, R, Fill, false);
            text = ui.Text(X, Y + (H - 38) / 2, W, 40, Theme.T(RuText.SkipOpening, 26, bold: true, align: "center"), false);
        }

        public void Show()
        {
            Visible = true;
            shownAt = Clock.Ms;
            Widgets.Shape(fill, X, Y, W, H, R);
            ui.ShowFocus(ring, true);
            back.Show(); fill.Show(); text.Show();
            timer.Start(0.1, Step, true);
        }

        // Полоса убывает справа налево; дошла до нуля — кнопка уходит.
        void Step()
        {
            var left = 1 - (Clock.Ms - shownAt) / (Seconds * 1000);
            if (left <= 0) { Hide(); return; }
            var w = (int)(W * left);
            if (w < H) fill.Hide();   // уже скругления — не рисуем, иначе форма ломается
            else Widgets.Shape(fill, X, Y, w, H, R);
        }

        public void Hide()
        {
            timer.Stop();
            Visible = false;
            ui.ShowFocus(ring, false);
            back.Hide(); fill.Hide(); text.Hide();
        }
    }
}
