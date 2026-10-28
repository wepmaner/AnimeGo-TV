using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Меню под именем пользователя: «Выйти» / «Отмена». ↑↓ — выбор, OK — действие, «Назад» — закрыть.
    // Своё, а не ChoicePanel: маленькое меню у шапки, а ChoicePanel — боковая панель плеера.
    sealed class LogoutPopup
    {
        const int W = 360, ItemH = 72, Pad = 8;
        static readonly string[] Items = { "Выйти", "Отмена" };

        readonly Widgets ui;
        readonly Action logout, close;
        readonly List<EvasObject> parts = new List<EvasObject>();
        readonly Polygon[] backs = new Polygon[Items.Length];
        readonly Label[] labels = new Label[Items.Length];
        int focus;

        public LogoutPopup(Widgets ui, Action logout, Action close)
        {
            this.ui = ui;
            this.logout = logout;
            this.close = close;
            var x = Theme.W - Theme.Margin - W;
            const int y = HomeHeader.Top + HomeHeader.H + 16;
            parts.Add(ui.RoundRect(x, y, W, 2 * Pad + Items.Length * ItemH, 14, Theme.Card2));
            for (var i = 0; i < Items.Length; i++)
            {
                backs[i] = ui.RoundRect(x + Pad, y + Pad + i * ItemH, W - 2 * Pad, ItemH, 10, Theme.Card2);
                labels[i] = ui.Text(x + Pad + 24, y + Pad + i * ItemH + 17, W - 2 * Pad - 48, 40, "");
                parts.Add(backs[i]);
                parts.Add(labels[i]);
            }
            Paint();
        }

        // Все клавиши — меню, пока оно открыто.
        public bool OnKey(string key)
        {
            switch (key)
            {
                case Keys.Up: focus = 0; Paint(); break;
                case Keys.Down: focus = Items.Length - 1; Paint(); break;
                case Keys.Ok: if (focus == 0) logout(); else close(); break;
                case Keys.Back: close(); break;
            }
            return key != Keys.Exit;
        }

        public void Close()
        {
            foreach (var o in parts) ui.Remove(o);
            parts.Clear();
        }

        public void Raise()
        {
            foreach (var o in parts) ui.Raise(o);
        }

        // Пункт в фокусе — светлый с тёмным текстом, как текущая вкладка.
        void Paint()
        {
            for (var i = 0; i < Items.Length; i++)
            {
                var on = i == focus;
                backs[i].Color = on ? Theme.Focus : Theme.Card2;
                labels[i].Text = Theme.T(Items[i], 28, on ? Theme.BgHex : Theme.TextHex, on);
            }
        }
    }
}
