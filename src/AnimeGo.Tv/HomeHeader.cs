using System;
using System.Collections.Generic;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Шапка главной (макет Main.dc.html): логотип, вкладки, справа имя пользователя или «Войти».
    // Лежит поверх рядов: при прокрутке они уходят под её фон.
    sealed class HomeHeader
    {
        public const int Top = 40, H = 56, Bottom = Top + H + 40;   // ряды начинаются с Bottom
        public static readonly string[] Tabs = { "Главная", "Поиск", "Мой список", "Каталог" };
        public const int UserItem = 4;   // индекс имени / «Войти» после вкладок
        const int TabH = 52, TabY = Top + (H - TabH) / 2, CharW = 11;
        const string TabTextHex = "#d5d8de";
        static readonly Color Avatar = Color.FromHex("#2a2d36");

        struct Slot { public int X, Y, W, H; }

        readonly Widgets ui;
        readonly List<EvasObject> parts = new List<EvasObject>();
        readonly List<Slot> slots = new List<Slot>();
        EvasObject frame;
        bool active;

        public int Focus { get; private set; }

        public HomeHeader(Widgets ui, string userName)
        {
            this.ui = ui;
            Build(userName);
        }

        // userName null — гость («Войти»).
        public void Build(string userName)
        {
            foreach (var o in parts) ui.Remove(o);
            parts.Clear();
            slots.Clear();

            parts.Add(ui.Rect(0, 0, Theme.W, Bottom, Theme.Bg));
            frame = ui.RoundFocus(0, 0, TabH, TabH, TabH / 2);
            parts.Add(frame);
            parts.Add(ui.Text(Theme.Margin, Top + 6, 240, 48,
                "<align=left><font_size=30 color=" + Theme.TextHex + " font_weight=Bold>Anime</font_size>" +
                "<font_size=30 color=" + Theme.AccentHex + " font_weight=Bold>Go</font_size></align>"));

            var x = Theme.Margin + 130 + 32 + 16;   // ширина логотипа ≈130, отступ 32, зазор 16
            for (var i = 0; i < Tabs.Length; i++)
            {
                var w = 52 + CharW * Tabs[i].Length;
                var current = i == 0;   // мы на «Главной»
                parts.Add(ui.RoundRect(x, TabY, w, TabH, TabH / 2, current ? Theme.Text : Theme.Card));
                parts.Add(ui.Text(x, TabY + 12, w, 32, Theme.T(Tabs[i], 20, current ? Theme.BgHex : TabTextHex, current, "center")));
                slots.Add(new Slot { X = x, Y = TabY, W = w, H = TabH });
                x += w + 16;
            }

            if (userName != null)
            {
                var nameW = Math.Max(20, CharW * userName.Length);
                var nx = Theme.W - Theme.Margin - nameW;
                var ax = nx - 12 - 44;
                const int ay = Top + (H - 44) / 2;
                parts.Add(ui.Circle(ax + 22, ay + 22, 22, Avatar));
                var letter = userName.Length > 0 ? userName.Substring(0, 1).ToUpperInvariant() : "?";
                parts.Add(ui.Text(ax, ay + 8, 44, 32, Theme.T(letter, 20, bold: true, align: "center")));
                parts.Add(ui.Text(nx, ay + 8, nameW + 20, 32, Theme.T(userName, 20, Theme.MutedHex)));
                slots.Add(new Slot { X = ax, Y = ay, W = 44 + 12 + nameW, H = 44 });
            }
            else
            {
                var w = 52 + CharW * RuText.Login.Length;
                var lx = Theme.W - Theme.Margin - w;
                parts.Add(ui.RoundRect(lx, TabY, w, TabH, TabH / 2, Theme.Card));
                parts.Add(ui.Text(lx, TabY + 12, w, 32, Theme.T(RuText.Login, 20, Theme.TextHex, true, "center")));
                slots.Add(new Slot { X = lx, Y = TabY, W = w, H = TabH });
            }
            Focus = Math.Min(Focus, slots.Count - 1);
            UpdateFrame();
        }

        public void SetActive(bool on)
        {
            active = on;
            UpdateFrame();
        }

        public void Move(int dx)
        {
            var next = Math.Max(0, Math.Min(Focus + dx, slots.Count - 1));
            if (next == Focus) return;
            Focus = next;
            UpdateFrame();
        }

        // Поверх рядов, которые создаются позже шапки.
        public void Raise()
        {
            foreach (var o in parts) ui.Raise(o);
        }

        void UpdateFrame()
        {
            var s = slots[Focus];
            ui.MoveFocus(frame, s.X, s.Y, s.W, s.H, s.H / 2);   // «таблетка»: радиус — половина высоты
            ui.ShowFocus(frame, active);
        }
    }
}
