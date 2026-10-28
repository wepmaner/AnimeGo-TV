using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Сетка постеров 140×196 по 10 в строке (зазор 32, поля 80), под постером название 17 px — как узкие карточки Row.
    // Прокрутка построчная: верхняя видимая строка всегда целиком ниже Top, нижние могут уходить за край экрана.
    // Строки выше видимой прячутся — поэтому шапке экрана не нужен фон поверх сетки.
    // Постеры запрашиваются лениво: только для строк в пределах NearRows от видимых.
    sealed class Grid
    {
        public const int Cols = 10, CardW = 140, CardH = 196, Gap = 32, Radius = 10;
        const int CellH = CardH + 10 + 26, Pitch = CellH + Gap, Bottom = Theme.H - 40;
        const int NearRows = 2;
        const string TitleHex = "#d5d8de";

        sealed class Cell
        {
            public RowItem Item;
            public EvasObject Back, Pic, Title;
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

        public readonly int Top;
        public int Focus { get; private set; }
        public int Count => cells.Count;
        public int FocusRow => Focus / Cols;
        public int RowCount => (cells.Count + Cols - 1) / Cols;
        public RowItem Current => cells.Count == 0 ? null : cells[Focus].Item;

        public Grid(Widgets ui, ImageCache images, int top)
        {
            this.ui = ui;
            this.images = images;
            Top = top;
        }

        public void Clear()
        {
            foreach (var c in cells) { ui.Remove(c.Back); ui.Remove(c.Pic); ui.Remove(c.Title); }
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
                var t = ui.Text(0, 0, CardW, 26, Theme.T(it.Title, 17, TitleHex), false);
                t.IsEllipsis = true;
                c.Title = t;
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
                        if (c.Shown) { c.Back.Hide(); c.Pic.Hide(); c.Title.Hide(); c.Shown = false; }
                        continue;
                    }
                    var x = Theme.Margin + i % Cols * (CardW + Gap);
                    if (x != c.X || y != c.Y)
                    {
                        c.Back.Move(x, y);   // Polygon сдвигается целиком, как в Row
                        c.Pic.Move(x, y);
                        c.Title.Move(x, y + CardH + 10);
                        c.X = x;
                        c.Y = y;
                    }
                    if (!c.Shown) { c.Back.Show(); c.Pic.Show(); c.Title.Show(); c.Shown = true; }
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

        void UpdateFrame()
        {
            if (frame == null) return;
            var on = active && cells.Count > 0;
            if (on)
            {
                var y = Top + (FocusRow - firstRow) * Pitch;
                ui.MoveFocus(frame, Theme.Margin + Focus % Cols * (CardW + Gap), y, CardW, CardH);
            }
            ui.ShowFocus(frame, on);
        }
    }
}
