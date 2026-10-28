using System;
using System.Collections.Generic;
using System.Linq;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    sealed class ChoiceItem
    {
        public string Key, Title;
        public string Note;          // «CVH · Kodik», «недоступно в приложении»; справа в строке
        public string Mark;          // «сейчас · основная» и т.п.; показывается перед Note
        public string Group = "";    // «ДУБЛЯЖ» / «МНОГОГОЛОСАЯ» / «СУБТИТРЫ» / «» (без заголовка)
        public bool Enabled = true;  // false — видна, но фокус её пропускает
        public bool Current;         // играет сейчас: красная рамка и ✓
        public bool IsMain;          // уже основная: кнопки «Сделать основной» нет
    }

    // Панель выбора справа (макет PlayerDub.dc.html): 600 px на всю высоту, заголовок, подсказка, группы строк 68 px,
    // внизу «↑↓ выбрать · OK применить · Назад закрыть». Не влезает по высоте — список прокручивается за фокусом.
    // Если задан OnMakeMain, у строки в фокусе справа кнопка «Сделать основной» (←→ — между строкой и кнопкой).
    // Строки строятся заново при каждом движении (их десяток-другой); Close убирает всё из Widgets.
    sealed class ChoicePanel
    {
        const int W = 600, X = Theme.W - W, Pad = 48, Top = 56, CX = X + Pad, CW = W - 2 * Pad;
        const int ItemH = 68, ItemGap = 10, GroupH = 24, GroupGap = 28, HintLine = 27, FooterY = Theme.H - Top - 26;
        const int ListBottom = FooterY - 28, Ring = 8, PillW = 196, PillH = 44, MaxRightW = 300;
        static readonly Color PanelBg = Color.FromHex("#121318"), PillBg = Color.FromHex("#dfe1e6"), PillDark = Color.FromHex("#3a3d46");
        const string MutedHex = "#a3a9b4", DarkHex = "#0e0f13", DarkNoteHex = "#4a4f5a", OffHex = "#6b707c";

        readonly Widgets ui;
        readonly List<EvasObject> fixedParts = new List<EvasObject>(), listParts = new List<EvasObject>();
        readonly int listTop;
        IReadOnlyList<ChoiceItem> items;
        int[] itemY = new int[0];                       // y строки от начала списка
        readonly List<(int y, string text)> headers = new List<(int, string)>();
        int contentH, scroll, focus = -1;
        bool onPill;

        public Action<ChoiceItem> OnPick, OnMakeMain;
        public Action OnClose;
        public bool Visible { get; private set; } = true;

        public ChoicePanel(Widgets ui, string title, string hint, IReadOnlyList<ChoiceItem> items)
        {
            this.ui = ui;
            fixedParts.Add(ui.Rect(X, 0, W, Theme.H, PanelBg));
            fixedParts.Add(ui.Text(CX, Top, CW, 46, Theme.T(title, 34, bold: true)));
            // Подсказка переносится по словам; строк — по грубой оценке ширины символа 19 px шрифта.
            var lines = Math.Max(1, Math.Min(4, (int)Math.Ceiling((hint ?? "").Length * 10.0 / CW)));
            var h = ui.Text(CX, Top + 54, CW, lines * HintLine + 4, Theme.T(hint, 19, MutedHex));
            h.LineWrapType = WrapType.Word;
            fixedParts.Add(h);
            fixedParts.Add(ui.Text(CX, FooterY, CW, 30, Theme.T(RuText.ChoiceKeys, 18, MutedHex)));
            listTop = Top + 54 + lines * HintLine + 28;
            SetItems(items);
        }

        // Новые данные (пометки поменялись) с сохранением фокуса на той же строке.
        public void SetItems(IReadOnlyList<ChoiceItem> list)
        {
            var keep = focus >= 0 && focus < (items?.Count ?? 0) ? items[focus].Key : null;
            items = list;
            Measure();
            focus = Index(i => i.Key == keep && i.Enabled);
            if (focus < 0) focus = Index(i => i.Current && i.Enabled);
            if (focus < 0) focus = Index(i => i.Enabled);
            if (!PillAllowed) onPill = false;
            Build();
        }

        int Index(Func<ChoiceItem, bool> f) => items.ToList().FindIndex(i => f(i));

        public bool OnKey(string key)
        {
            if (!Visible) return false;
            switch (key)
            {
                case Keys.Up: Step(-1); break;
                case Keys.Down: Step(1); break;
                case Keys.Left: if (onPill) { onPill = false; Build(); } break;
                case Keys.Right: if (!onPill && PillAllowed) { onPill = true; Build(); } break;
                case Keys.Ok: case Keys.Select:
                    if (focus < 0) break;
                    var it = items[focus];
                    if (onPill) OnMakeMain?.Invoke(it); else OnPick?.Invoke(it);
                    break;
                case Keys.Back: case Keys.Cancel: OnClose?.Invoke(); break;
            }
            return true;
        }

        public void Close()
        {
            if (!Visible) return;
            Visible = false;
            foreach (var o in listParts) ui.Remove(o);
            foreach (var o in fixedParts) ui.Remove(o);
            listParts.Clear();
            fixedParts.Clear();
        }

        bool PillAllowed => OnMakeMain != null && focus >= 0 && items[focus].Enabled && !items[focus].IsMain;

        // ↑↓ — к соседней доступной строке; дальше некуда — фокус стоит.
        void Step(int d)
        {
            for (var i = focus + d; i >= 0 && i < items.Count; i += d)
                if (items[i].Enabled) { focus = i; break; }
            onPill = false;
            Build();
        }

        // Раскладка всего списка: заголовок группы перед первой строкой группы, между группами 28 px.
        void Measure()
        {
            headers.Clear();
            itemY = new int[items.Count];
            var y = 0;
            string group = null;
            for (var i = 0; i < items.Count; i++)
            {
                var g = items[i].Group ?? "";
                if (g != group)
                {
                    if (i > 0) y += GroupGap - ItemGap;
                    if (g != "") { headers.Add((y, g)); y += GroupH + ItemGap; }
                    group = g;
                }
                itemY[i] = y;
                y += ItemH + ItemGap;
            }
            contentH = Math.Max(0, y - ItemGap);
        }

        // Прокрутка: строка в фокусе (с кольцом) целиком видна; у первой — вместе с заголовком группы;
        // первая/последняя доступные — список прижат к краю, чтобы недоступные строки по краям тоже было видно.
        void Scroll()
        {
            var view = ListBottom - listTop;
            if (contentH <= view || focus < 0) { scroll = 0; return; }
            var top = itemY[focus] - Ring;
            var hdr = headers.Where(h => h.y < itemY[focus] && h.y >= itemY[focus] - GroupH - ItemGap).Select(h => h.y).DefaultIfEmpty(top).Min();
            top = Math.Min(top, hdr);
            var bottom = itemY[focus] + ItemH + Ring;
            if (top < scroll) scroll = top;
            if (bottom > scroll + view) scroll = bottom - view;
            if (Index(i => i.Enabled) == focus) scroll = 0;
            if (items.ToList().FindLastIndex(i => i.Enabled) == focus) scroll = contentH - view;
            scroll = Math.Max(0, Math.Min(scroll, contentH - view));
        }

        void Build()
        {
            foreach (var o in listParts) ui.Remove(o);
            listParts.Clear();
            Scroll();
            int min = listTop - Ring, max = ListBottom + Ring;
            foreach (var (hy, text) in headers)
            {
                var y = listTop + hy - scroll;
                if (y >= min && y + GroupH <= max) listParts.Add(ui.Text(CX, y, CW, GroupH + 6, Theme.T(text, 17, MutedHex, true)));
            }
            for (var i = 0; i < items.Count; i++)
            {
                var y = listTop + itemY[i] - scroll;
                if (y >= min && y + ItemH <= max) BuildItem(items[i], y, i == focus);
            }
            Toast.Raise();
        }

        void BuildItem(ChoiceItem it, int y, bool focused)
        {
            var rowFocus = focused && !onPill;
            var pill = focused && PillAllowed;
            if (rowFocus)
            {
                Add(ui.RoundRect(CX - Ring, y - Ring, CW + 2 * Ring, ItemH + 2 * Ring, 14 + Ring, Theme.Accent));
                Add(ui.RoundRect(CX - 4, y - 4, CW + 8, ItemH + 8, 18, PanelBg));
                Add(ui.RoundRect(CX, y, CW, ItemH, 14, Theme.Focus));
            }
            else if (it.Current)
            {
                Add(ui.RoundRect(CX, y, CW, ItemH, 14, Theme.Accent));
                Add(ui.RoundRect(CX + 2, y + 2, CW - 4, ItemH - 4, 12, Theme.Card2));
            }
            else Add(ui.RoundRect(CX, y, CW, ItemH, 14, Theme.Card2));

            var x = CX + 24;
            if (it.Current)
            {
                Add(ui.Text(x, y + 18, 30, 34, Theme.T("✓", 22, Theme.AccentHex, true)));
                x += 34;
            }
            // Справа — пометки и провайдеры: «сейчас · основная · CVH · Kodik». С кнопкой «Сделать основной» строка
            // в две строчки: название, под ним пометки, кнопка справа — так пометки не прячутся.
            var right = it.Enabled && !string.IsNullOrEmpty(it.Mark) ? it.Mark + (string.IsNullOrEmpty(it.Note) ? "" : " · " + it.Note) : it.Note;
            int rightW = pill ? PillW + 12 : string.IsNullOrEmpty(right) ? 0 : Math.Min(MaxRightW, right.Length * 9 + 16);
            var color = !it.Enabled ? OffHex : rowFocus ? DarkHex : Theme.TextHex;
            var t = ui.Text(x, y + (pill && !string.IsNullOrEmpty(right) ? 6 : 18), CX + CW - 24 - rightW - x, 34, Theme.T(it.Title, 22, color, rowFocus || it.Current));
            t.IsEllipsis = true;
            Add(t);
            if (pill && !string.IsNullOrEmpty(right))
            {
                var sub = ui.Text(x, y + 38, CX + CW - 24 - rightW - x, 26, Theme.T(right, 15, rowFocus ? DarkNoteHex : MutedHex));
                sub.IsEllipsis = true;
                Add(sub);
            }

            if (pill)
            {
                int px = CX + CW - 12 - PillW, py = y + (ItemH - PillH) / 2;
                if (onPill)
                {
                    Add(ui.RoundRect(px - 6, py - 6, PillW + 12, PillH + 12, PillH / 2 + 6, Theme.Accent));
                    Add(ui.RoundRect(px - 3, py - 3, PillW + 6, PillH + 6, PillH / 2 + 3, PanelBg));
                }
                Add(ui.RoundRect(px, py, PillW, PillH, PillH / 2, onPill ? Theme.Focus : rowFocus ? PillBg : PillDark));
                Add(ui.Text(px, py + 10, PillW, 28, Theme.T(RuText.MakeMain, 17, onPill || rowFocus ? DarkHex : Theme.TextHex, true, "center")));
            }
            else if (!string.IsNullOrEmpty(right))
            {
                var r = ui.Text(CX + CW - 24 - rightW, y + 22, rightW, 28, Theme.T(right, 17, rowFocus ? DarkNoteHex : MutedHex, rowFocus, "right"));
                r.IsEllipsis = true;
                Add(r);
            }
        }

        void Add(EvasObject o) => listParts.Add(o);
    }
}
