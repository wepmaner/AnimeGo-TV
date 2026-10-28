using System;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Конец серии (спека §5.6): плашка справа внизу «Следующая серия через N» с кнопками «Смотреть» / «Отмена».
    // Отсчёт кончился или «Смотреть» — Watch; «Отмена» или «Назад» — Cancel.
    sealed class EndOverlay
    {
        const int W = 600, H = 220, X = Theme.W - Theme.Margin - W, Y = Theme.H - Theme.Margin - H;
        static readonly Color BtnBg = Color.FromHex("#2f3034");

        readonly Polygon box;
        readonly Label text;
        readonly Polygon[] btn = new Polygon[2];
        readonly Label[] label = new Label[2];
        readonly UiTimer timer = new UiTimer();   // в поле: GC
        int left, focus;

        public Action Watch, Cancel;

        public EndOverlay(Widgets ui)
        {
            box = ui.RoundRect(X, Y, W, H, 20, Theme.Card, false);
            text = ui.Text(X + 40, Y + 36, W - 80, 48, "", false);
            for (var i = 0; i < 2; i++)
            {
                btn[i] = ui.RoundRect(X + 40 + i * 270, Y + 116, 250, 64, 32, BtnBg, false);
                label[i] = ui.Text(X + 40 + i * 270, Y + 130, 250, 36, "", false);
            }
        }

        public bool Visible => timer.Running;

        public void Show(int seconds)
        {
            left = seconds;
            focus = 0;
            box.Show();
            text.Show();
            for (var i = 0; i < 2; i++) { btn[i].Show(); label[i].Show(); }
            Update();
            Toast.Raise();
            timer.Start(1, () => { if (--left <= 0) Fire(Watch); else Update(); }, true);
        }

        public void Hide()
        {
            timer.Stop();
            box.Hide();
            text.Hide();
            for (var i = 0; i < 2; i++) { btn[i].Hide(); label[i].Hide(); }
        }

        public bool OnKey(string key)
        {
            switch (key)
            {
                case Keys.Left: case Keys.Right: focus = key == Keys.Left ? 0 : 1; Update(); break;
                case Keys.Ok: Fire(focus == 0 ? Watch : Cancel); break;
                case Keys.Back: Fire(Cancel); break;
            }
            return true;
        }

        void Fire(Action a)
        {
            Hide();
            a?.Invoke();
        }

        void Update()
        {
            text.Text = Theme.T(RuText.NextIn(left), 30, bold: true);
            var names = new[] { RuText.Watch, RuText.Cancel };
            for (var i = 0; i < 2; i++)
            {
                btn[i].Color = focus == i ? Theme.Focus : BtnBg;
                label[i].Text = Theme.T(names[i], 24, focus == i ? Theme.BgHex : Theme.TextHex, true, "center");
            }
        }
    }
}
