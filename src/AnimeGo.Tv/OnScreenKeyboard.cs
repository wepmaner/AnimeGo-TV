using System;
using System.Collections.Generic;
using System.Text;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Своя экранная клавиатура QWERTY: панель внизу экрана; ←→↑↓ — выбор (по горизонтали по кругу), OK — нажать,
    // «Назад» — «Отмена». Системная клавиатура ТВ для нас непригодна (CLAUDE.md). Набранный текст нигде не логируется.
    sealed class OnScreenKeyboard
    {
        const int Slots = 12, KeyW = 112, KeyH = 72, Gap = 16, MaxLen = 64;
        const int PanelY = 560, KeysY = 608, CharRows = 4;
        const int RowW = Slots * KeyW + (Slots - 1) * Gap;
        const int X0 = (Theme.W - RowW) / 2;
        const string KeyHex = "#363a45", DarkHex = "#0e0f13", ShiftOnHex = "#7a2a2d";
        static readonly string[] LayoutNames = { "abc", "АБВ", "123" };
        // Ряд 0 — цифры во всех раскладках; в латинице под рукой @ . _ - для e-mail.
        static readonly string[][] Layouts =
        {
            new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm@._-" },
            new[] { "1234567890", "йцукенгшщзхъ", "фывапролджэ", "ячсмитьбюё" },
            new[] { "1234567890", "@._-!?#$%&", "*+=/\\:;'\",", "()[]{}<>~^|`" },
        };
        enum Special { None, Shift, Lang, Space, Erase, Cancel, Done }

        sealed class Key
        {
            public int Row, X, Y, W;
            public char Ch;
            public Special Kind;
            public Rectangle Back;
            public Label Label;
        }

        readonly Widgets ui;
        readonly List<EvasObject> own = new List<EvasObject>();
        readonly List<Key> keys = new List<Key>();   // сначала символьные ряды 0..3, потом служебный ряд 4
        readonly List<Key> specials = new List<Key>();
        readonly Rectangle frame, gap;
        readonly StringBuilder text;
        int layout, focus;
        bool shift, closed;

        public event Action<string> Done;
        public event Action Cancelled;
        public event Action<string> Changed;   // текст изменился — показать в поле

        public string Text => text.ToString();

        // Виджеты кладутся в Widgets экрана: закрылся экран — исчезла и клавиатура. win оставлен для единообразия с Keyboard.
        public OnScreenKeyboard(Widgets ui, Window win, bool password, string initial)
        {
            this.ui = ui;
            text = new StringBuilder(initial ?? "");
            Add(ui.Rect(0, PanelY, Theme.W, Theme.H - PanelY, Theme.Card2));
            Add(ui.Text(X0, PanelY + 10, RowW, 34, Theme.T("Стрелки — выбор клавиши, OK — ввести, «Назад» — отмена", 18, Theme.MutedHex)));
            // Рамка фокуса — до клавиш, чтобы лежать под ними.
            frame = Add(ui.Rect(0, 0, 1, 1, Theme.Focus));
            gap = Add(ui.Rect(0, 0, 1, 1, Theme.Card2));

            // Служебный ряд: по ширине как 12 клавиш.
            var kinds = new[] { Special.Shift, Special.Lang, Special.Space, Special.Erase, Special.Cancel, Special.Done };
            var widths = new[] { 192, 192, 480, 192, 192, 192 };
            var x = X0;
            for (var i = 0; i < kinds.Length; i++)
            {
                specials.Add(MakeKey(CharRows, x, widths[i], kinds[i], '\0'));
                x += widths[i] + Gap;
            }
            BuildChars();
            focus = Layouts[layout][0].Length;   // первая буква (q)
            Redraw();
            DevLog.Write("kbd: open " + (password ? "password" : "login"));
        }

        // Символьные ряды зависят от раскладки (разной длины) — пересоздаются при смене языка. Ряды по центру.
        void BuildChars()
        {
            foreach (var k in keys)
                if (k.Kind == Special.None) { ui.Remove(k.Back); ui.Remove(k.Label); own.Remove(k.Back); own.Remove(k.Label); }
            keys.Clear();
            var rows = Layouts[layout];
            for (var r = 0; r < rows.Length; r++)
            {
                var x0 = X0 + (Slots - rows[r].Length) * (KeyW + Gap) / 2;
                for (var c = 0; c < rows[r].Length; c++)
                    keys.Add(MakeKey(r, x0 + c * (KeyW + Gap), KeyW, Special.None, rows[r][c]));
            }
            keys.AddRange(specials);
        }

        Key MakeKey(int row, int x, int w, Special kind, char ch)
        {
            var k = new Key { Row = row, X = x, Y = KeysY + row * (KeyH + Gap), W = w, Kind = kind, Ch = ch };
            k.Back = Add(ui.Rect(k.X, k.Y, w, KeyH, Color.FromHex(KeyHex)));
            k.Label = Add(ui.Text(k.X, k.Y + (kind == Special.None ? 12 : 20), w, KeyH - 12, ""));
            return k;
        }

        T Add<T>(T o) where T : EvasObject { own.Add(o); return o; }

        // true — клавиша обработана. Пока клавиатура открыта, экран под ней пульт не получает.
        public bool OnKey(string key)
        {
            if (closed) return false;
            switch (key)
            {
                case Keys.Left: MoveH(-1); return true;
                case Keys.Right: MoveH(1); return true;
                case Keys.Up: MoveV(-1); return true;
                case Keys.Down: MoveV(1); return true;
                case Keys.Ok: Press(keys[focus]); return true;
                case Keys.Back: Cancel(); return true;
            }
            return key != Keys.Exit && !key.StartsWith("XF86Audio", StringComparison.Ordinal);   // громкость — системе
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            foreach (var o in own) ui.Remove(o);
            own.Clear();
            keys.Clear();
        }

        // По горизонтали — по кругу внутри ряда.
        void MoveH(int d)
        {
            var row = keys[focus].Row;
            int first = keys.FindIndex(k => k.Row == row), last = keys.FindLastIndex(k => k.Row == row);
            focus += d;
            if (focus < first) focus = last;
            if (focus > last) focus = first;
            Redraw();
        }

        // В соседний ряд — на клавишу, чей центр ближе всего по горизонтали.
        void MoveV(int d)
        {
            var row = keys[focus].Row + d;
            if (row < 0 || row > CharRows) return;
            focus = Nearest(row, keys[focus].X + keys[focus].W / 2);
            Redraw();
        }

        int Nearest(int row, int center)
        {
            int best = focus, bestDist = int.MaxValue;
            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].Row != row) continue;
                var dist = Math.Abs(keys[i].X + keys[i].W / 2 - center);
                if (dist < bestDist) { best = i; bestDist = dist; }
            }
            return best;
        }

        char CharOf(Key k) => shift ? char.ToUpperInvariant(k.Ch) : k.Ch;

        void Press(Key k)
        {
            switch (k.Kind)
            {
                case Special.None: Type(CharOf(k)); break;
                case Special.Space: Type(' '); break;
                case Special.Erase:
                    if (text.Length > 0) { text.Length--; Changed?.Invoke(Text); }
                    break;
                case Special.Shift: shift = !shift; Redraw(); break;
                case Special.Lang:
                    layout = (layout + 1) % Layouts.Length;
                    BuildChars();
                    focus = keys.IndexOf(k);   // остаёмся на «Язык»
                    Redraw();
                    break;
                case Special.Cancel: Cancel(); break;
                case Special.Done:
                    DevLog.Write("kbd: done, " + text.Length + " chars");
                    var s = Text;
                    Close();
                    Done?.Invoke(s);
                    break;
            }
        }

        void Type(char ch)
        {
            if (text.Length >= MaxLen) return;
            text.Append(ch);
            Changed?.Invoke(Text);
        }

        void Cancel()
        {
            DevLog.Write("kbd: cancel");
            Close();
            Cancelled?.Invoke();
        }

        void Redraw()
        {
            for (var i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                var on = i == focus;
                string label;
                switch (k.Kind)
                {
                    case Special.None: label = CharOf(k).ToString(); break;
                    case Special.Shift: label = "Регистр"; break;
                    case Special.Lang: label = LayoutNames[(layout + 1) % LayoutNames.Length]; break;   // на что переключит
                    case Special.Space: label = "Пробел"; break;
                    case Special.Erase: label = "⌫ Стереть"; break;
                    case Special.Cancel: label = "Отмена"; break;
                    default: label = "Готово"; break;
                }
                var back = k.Kind == Special.Done ? Theme.AccentHex : k.Kind == Special.Shift && shift ? ShiftOnHex : KeyHex;
                k.Back.Color = on ? Color.White : Color.FromHex(back);
                k.Label.Text = Theme.T(label, k.Kind == Special.None ? 32 : 24, on ? DarkHex : Theme.TextHex, bold: true, align: "center");
            }
            var f = keys[focus];
            var b = Theme.FocusBorder + Theme.FocusGap;
            frame.Move(f.X - b, f.Y - b);
            frame.Resize(f.W + 2 * b, KeyH + 2 * b);
            gap.Move(f.X - Theme.FocusGap, f.Y - Theme.FocusGap);
            gap.Resize(f.W + 2 * Theme.FocusGap, KeyH + 2 * Theme.FocusGap);
        }
    }
}
