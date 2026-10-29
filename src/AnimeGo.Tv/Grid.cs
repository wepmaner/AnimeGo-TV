using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Сетка постеров 250×350 (родной размер постеров сайта) в Cols колонок на ширине Width от Left; под постером
    // название 20 px, на постере — рейтинг. Прокрутка построчная: верхняя видимая строка всегда целиком ниже Top,
    // нижние уходят за край экрана — край следующей строки видно, и понятно, что ниже есть ещё.
    // Строки выше видимой прячутся — поэтому шапке экрана не нужен фон поверх сетки.
    // Постеры запрашиваются лениво: только для строк в пределах NearRows от видимых.
    sealed class Grid
    {
        public const int CardW = 250, CardH = 350, Radius = 14;
        const int CellH = CardH + 12 + 30, Pitch = CellH + 34, Bottom = Theme.H - 40;
        const int NearRows = 2;
        const string TitleHex = "#d5d8de";
        static readonly Color Shade = Color.FromRgba(14, 15, 19, 210);

        sealed class Cell
        {
            public RowItem Item;
            public EvasObject Back, Pic, Title, BadgeBack, BadgeText;
            public bool Requested, Shown;
            public int X = int.MinValue, Y = int.MinValue;   // где стоит сейчас
        }

        readonly Widgets ui;
        readonly ImageCache images;
        readonly List<Cell> cells = new List<Cell>();
        EvasObject frame;
        int firstRow;   // верхняя видимая строка
        int placedRow = -1, placedCount;   // с какими firstRow и числом карточек раскладывали в прошлый раз
        bool active;

        public readonly int Top, Left, Cols;
        readonly int step;   // шаг колонок: карточка + зазор
        public int Focus { get; private set; }
        public int Count => cells.Count;
        public int FocusRow => Focus / Cols;
        public int RowCount => (cells.Count + Cols - 1) / Cols;
        public RowItem Current => cells.Count == 0 ? null : cells[Focus].Item;

        public Grid(Widgets ui, ImageCache images, int top, int left = Theme.Margin, int width = Theme.W - 2 * Theme.Margin, int cols = 6)
        {
            this.ui = ui;
            this.images = images;
            Top = top;
            Left = left;
            Cols = cols;
            step = CardW + (width - cols * CardW) / (cols - 1);
        }

        // Где кончается последняя строка на экране (под ней — «Загрузка…»).
        public int EndY => Top + (RowCount - firstRow) * Pitch - 34;

        public void Clear()
        {
            foreach (var c in cells) foreach (var o in Parts(c)) ui.Remove(o);
            cells.Clear();
            ui.Remove(frame);
            frame = null;
            Focus = firstRow = 0;
            placedRow = -1;
            placedCount = 0;
        }

        // Дописать карточки в конец (следующая страница каталога).
        public void Append(IReadOnlyList<RowItem> items)
        {
            if (frame == null) frame = ui.RoundFocus(0, 0, CardW, CardH, Radius);   // до карточек — чтобы лежала под ними
            foreach (var it in items)
            {
                var c = new Cell { Item = it };
                c.Back = ui.RoundRect(0, 0, CardW, CardH, Radius, Theme.Card, false);
                c.Pic = ui.Picture(0, 0, CardW, CardH, false);
                var t = ui.Text(0, 0, CardW, 30, Theme.T(it.Title, 20, TitleHex, true), false);
                t.IsEllipsis = true;
                c.Title = t;
                if (!string.IsNullOrEmpty(it.Badge))
                {
                    c.BadgeBack = ui.RoundRect(0, 0, BadgeW(it.Badge), 32, 10, Shade, false);
                    c.BadgeText = ui.Text(0, 0, BadgeW(it.Badge), 28, Theme.T(it.Badge, 18, bold: true, align: "center"), false);
                }
                cells.Add(c);
            }
            Place();
        }

        // Фокус на index (после возврата на экран), если такая карточка есть.
        public void FocusAt(int index)
        {
            if (index < 0 || index >= cells.Count) return;
            Focus = index;
            Place();
        }

        public void SetActive(bool on)
        {
            active = on;
            UpdateFrame();
        }

        // Стрелки. false — дальше некуда (↑ с первой строки — экрану, к шапке).
        public bool Move(int dx, int dy)
        {
            if (cells.Count == 0) return false;
            var next = Focus;
            if (dx != 0)
            {
                var col = Focus % Cols + dx;
                if (col < 0 || col >= Cols || Focus + dx >= cells.Count) return false;
                next = Focus + dx;
            }
            else
            {
                var row = FocusRow + dy;
                if (row < 0 || row >= RowCount) return false;
                next = Math.Min(row * Cols + Focus % Cols, cells.Count - 1);   // короткая последняя строка — на её край
            }
            Focus = next;
            Place();
            return true;
        }

        void Place()
        {
            // Строка в фокусе целиком на экране: выше — сдвигаем вверх, ниже края — вниз.
            if (FocusRow < firstRow) firstRow = FocusRow;
            while (Top + (FocusRow - firstRow) * Pitch + CellH > Bottom && firstRow < FocusRow) firstRow++;
            if (firstRow != placedRow || cells.Count != placedCount)
            {
                // Трогаем EFL только у карточек, которые сдвинулись или сменили видимость.
                for (var i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    var row = i / Cols;
                    var y = Top + (row - firstRow) * Pitch;
                    var visible = row >= firstRow && y < Theme.H;
                    if (!visible)
                    {
                        if (c.Shown) { foreach (var o in Parts(c)) o.Hide(); c.Shown = false; }
                        continue;
                    }
                    var x = Left + i % Cols * step;
                    if (x != c.X || y != c.Y)
                    {
                        c.Back.Move(x, y);   // Polygon сдвигается целиком, как в Row
                        c.Pic.Move(x, y);
                        c.Title.Move(x, y + CardH + 12);
                        c.BadgeBack?.Move(x + 12, y + 12);
                        c.BadgeText?.Move(x + 12, y + 14);
                        c.X = x;
                        c.Y = y;
                    }
                    if (!c.Shown) { foreach (var o in Parts(c)) o.Show(); c.Shown = true; }
                }
                placedRow = firstRow;
                placedCount = cells.Count;
                RequestImages();
            }
            UpdateFrame();
        }

        // Постеры строк от firstRow − NearRows до последней видимой + NearRows; каждый — один раз.
        void RequestImages()
        {
            var lastRow = firstRow + (Theme.H - Top) / Pitch + NearRows;
            var from = Math.Max(0, (firstRow - NearRows) * Cols);
            var to = Math.Min(cells.Count, (lastRow + 1) * Cols);
            for (var i = from; i < to; i++)
            {
                var c = cells[i];
                if (c.Requested) continue;
                c.Requested = true;
                images.Set((Image)c.Pic, c.Item.ImageUrl);
            }
        }

        static IEnumerable<EvasObject> Parts(Cell c)
        {
            yield return c.Back;
            yield return c.Pic;
            yield return c.Title;
            if (c.BadgeBack != null) { yield return c.BadgeBack; yield return c.BadgeText; }
        }

        static int BadgeW(string text) => 22 + 11 * text.Length;

        void UpdateFrame()
        {
            if (frame == null) return;
            var on = active && cells.Count > 0;
            if (on)
            {
                var y = Top + (FocusRow - firstRow) * Pitch;
                ui.MoveFocus(frame, Left + Focus % Cols * step, y, CardW, CardH);
            }
            ui.ShowFocus(frame, on);
        }
    }
}
