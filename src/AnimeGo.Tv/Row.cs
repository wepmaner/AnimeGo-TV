using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    sealed class RowItem
    {
        public string Title;
        public string Subtitle;
        public string ImageUrl;
        public double Progress = -1;   // −1 — без полосы
        public string Badge;           // метка в правом верхнем углу кадра («Новая серия»)
        public string TimeLabel;       // «05:08 / 23:59» в левом нижнем углу кадра
        public object Tag;
    }

    // Горизонтальный ряд под заголовком (макет Main.dc.html). Вид карточки по размеру:
    // широкая (кадр 480×270: полоса прогресса, время, метка, название 24 px и подпись 20 px),
    // узкая (постер 200×280: название 20 px и подпись 17 px), cardW = 0 — кнопки-«таблетки» по ширине текста (жанры).
    // Y — место ряда в общем содержимом главной; вертикальный сдвиг передаёт экран в Place(scrollY).
    // Горизонтально ряд сдвигается сам, чтобы карточка в фокусе не уходила за поле 80 px.
    sealed class Row
    {
        public const int HeadH = 41 + 18;   // заголовок 30 px (строка ≈41) и зазор до карточек 18
        const int PillPad = 32, PillFont = 21, PillCharW = 11;
        static readonly Color Track = Color.FromHex("#55575c");          // белый 25 % поверх тёмного, непрозрачный
        static readonly Color Shade = Color.FromRgba(0, 0, 0, 184);       // подложка времени, чёрный 72 %
        const string PosterTitleHex = "#d5d8de", DimHex = "#9aa0ab";

        struct Part { public EvasObject O; public int X, Y; }
        struct Slot { public int X, W; }

        readonly Widgets ui;
        readonly ImageCache images;
        readonly int cardW, cardH, gap;
        readonly Label head;
        readonly List<Part> parts = new List<Part>();
        readonly List<Slot> slots = new List<Slot>();
        IReadOnlyList<RowItem> items = new RowItem[0];
        EvasObject frame;
        int offsetX, screenY;
        bool active;

        public int Y;
        public int Focus { get; private set; }
        public Action<RowItem> OnOk;
        public IReadOnlyList<RowItem> Items => items;
        public bool IsEmpty => items.Count == 0;
        public int Height => HeadH + cardH + (cardW == 0 ? 0 : Wide ? 14 + 32 + 4 + 28 : 10 + 27 + 4 + 24);
        bool Wide => cardW >= 300;
        int Radius => cardW == 0 || Wide ? 14 : 10;   // как в макете: кнопки и кадры 14, постеры 10

        public Row(Widgets ui, ImageCache images, int y, int cardW, int cardH, int gap, string title, string hint = null)
        {
            this.ui = ui;
            this.images = images;
            this.cardW = cardW;
            this.cardH = cardH;
            this.gap = gap;
            Y = y;
            head = ui.Text(Theme.Margin, y, Theme.W - 2 * Theme.Margin, 48, "", false);
            SetTitle(title, hint);
        }

        public void SetTitle(string title, string hint = null) =>
            head.Text = "<align=left><font_size=30 color=" + Theme.TextHex + " font_weight=Bold>" + Theme.Escape(title) + "</font_size>" +
                (hint == null ? "" : "<font_size=22 color=" + DimHex + "> " + Theme.Escape(hint) + "</font_size>") + "</align>";

        // Фокус и сдвиг сохраняются (в пределах нового числа карточек). После вызова экран делает Place.
        public void SetItems(IReadOnlyList<RowItem> list)
        {
            items = list ?? new RowItem[0];
            Focus = Math.Max(0, Math.Min(Focus, items.Count - 1));
            Build();
        }

        // Дорисовать метку карточке index (item.Badge уже задан): только её, без пересоздания карточек и картинок.
        // Звать один раз на карточку. После вызова экран делает Place.
        public void AddBadge(int index)
        {
            if (index < 0 || index >= items.Count || cardW == 0) return;
            BuildBadge(items[index], slots[index].X);
        }

        public void SetActive(bool on)
        {
            active = on;
            UpdateFrame();
        }

        // ←/→. false — дальше некуда.
        public bool Move(int dx)
        {
            var next = Math.Max(0, Math.Min(Focus + dx, items.Count - 1));
            if (next == Focus) return false;
            Focus = next;
            if (KeepVisible()) Place(Y - screenY); else UpdateFrame();
            return true;
        }

        public void Ok()
        {
            if (items.Count > 0) OnOk?.Invoke(items[Focus]);
        }

        public void Place(int scrollY)
        {
            screenY = Y - scrollY;
            if (items.Count == 0) { head.Hide(); return; }
            head.Move(Theme.Margin, screenY);
            head.Show();
            foreach (var p in parts)
            {
                p.O.Move(Theme.Margin + offsetX + p.X, screenY + HeadH + p.Y);
                p.O.Show();
            }
            UpdateFrame();
        }

        void Build()
        {
            foreach (var p in parts) ui.Remove(p.O);
            parts.Clear();
            slots.Clear();
            ui.Remove(frame);
            frame = null;
            if (items.Count == 0) { head.Hide(); return; }
            frame = ui.RoundFocus(0, 0, Math.Max(cardW, 1), cardH, Radius);   // раньше карточек — чтобы лежала под ними
            var x = 0;
            foreach (var it in items)
            {
                var w = cardW == 0 ? PillW(it.Title) : cardW;
                slots.Add(new Slot { X = x, W = w });
                if (cardW == 0) BuildPill(it, x, w); else BuildCard(it, x);
                x += w + gap;
            }
            KeepVisible();
        }

        void BuildPill(RowItem it, int x, int w)
        {
            Add(ui.RoundRect(0, 0, w, cardH, Radius, Theme.Card, false), x, 0);
            Add(ui.Text(0, 0, w, 32, Theme.T(it.Title, PillFont, bold: true, align: "center"), false), x, (cardH - 29) / 2);
        }

        void BuildCard(RowItem it, int x)
        {
            Add(ui.RoundRect(0, 0, cardW, cardH, Radius, Theme.Card, false), x, 0);   // заглушка: постеры WebP на ТВ не открываются
            var pic = ui.Picture(0, 0, cardW, cardH, false);
            Add(pic, x, 0);
            images.Set(pic, it.ImageUrl);

            if (it.Progress >= 0)
            {
                Add(ui.Rect(0, 0, cardW, 6, Track, false), x, cardH - 6);
                var fill = (int)Math.Round(cardW * Math.Min(1, it.Progress));
                if (fill > 0) Add(ui.Rect(0, 0, fill, 6, Theme.Accent, false), x, cardH - 6);
            }
            if (!string.IsNullOrEmpty(it.TimeLabel))
            {
                var bw = 20 + 9 * it.TimeLabel.Length;
                Add(ui.RoundRect(0, 0, bw, 30, 6, Shade, false), x + 14, cardH - 18 - 30);
                Add(ui.Text(0, 0, bw, 26, Theme.T(it.TimeLabel, 16, bold: true, align: "center"), false), x + 14, cardH - 18 - 30 + 3);
            }
            BuildBadge(it, x);

            if (Wide)
            {
                AddLine(it.Title, 24, Theme.TextHex, true, x, cardH + 14, 34);
                AddLine(it.Subtitle, 20, Theme.MutedHex, false, x, cardH + 14 + 32 + 4, 30);
            }
            else
            {
                AddLine(it.Title, 20, PosterTitleHex, false, x, cardH + 10, 30);
                AddLine(it.Subtitle, 17, DimHex, false, x, cardH + 10 + 27 + 4, 26);
            }
        }

        void BuildBadge(RowItem it, int x)
        {
            if (string.IsNullOrEmpty(it.Badge)) return;
            var bw = 24 + 9 * it.Badge.Length;
            Add(ui.RoundRect(0, 0, bw, 28, 6, Theme.Accent, false), x + cardW - 14 - bw, 14);
            Add(ui.Text(0, 0, bw, 24, Theme.T(it.Badge, 15, bold: true, align: "center"), false), x + cardW - 14 - bw, 14 + 3);
        }

        void AddLine(string text, int px, string color, bool bold, int x, int y, int h)
        {
            if (string.IsNullOrEmpty(text)) return;
            var l = ui.Text(0, 0, cardW, h, Theme.T(text, px, color, bold), false);
            l.IsEllipsis = true;   // длинное название — с многоточием в ширину карточки
            Add(l, x, y);
        }

        void Add(EvasObject o, int x, int y) => parts.Add(new Part { O = o, X = x, Y = y });

        // Сдвинуть ряд так, чтобы карточка в фокусе была между полями 80 px. true — сдвиг изменился.
        bool KeepVisible()
        {
            if (slots.Count == 0) return false;
            var old = offsetX;
            var s = slots[Focus];
            var right = Theme.W - 2 * Theme.Margin;
            if (s.X + s.W + offsetX > right) offsetX = right - s.X - s.W;
            if (s.X + offsetX < 0) offsetX = -s.X;
            return offsetX != old;
        }

        void UpdateFrame()
        {
            if (frame == null) return;
            if (active && slots.Count > 0)
            {
                var s = slots[Focus];
                ui.MoveFocus(frame, Theme.Margin + offsetX + s.X, screenY + HeadH, s.W, cardH);
            }
            ui.ShowFocus(frame, active && slots.Count > 0);
        }

        static int PillW(string text) => 2 * PillPad + PillCharW * (text ?? "").Length;
    }
}
