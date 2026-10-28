using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Всё раскладывается абсолютными координатами на экране 1920×1080.
    // Экран собирает свои виджеты в Widgets и освобождает их разом.
    sealed class Widgets
    {
        readonly Window win;
        readonly List<EvasObject> all = new List<EvasObject>();
        readonly Dictionary<EvasObject, EvasObject> gaps = new Dictionary<EvasObject, EvasObject>();   // рамка фокуса → её зазор
        readonly Dictionary<EvasObject, int> radii = new Dictionary<EvasObject, int>();   // скруглённая рамка → радиус карточки

        public Widgets(Window win) { this.win = win; }

        public Rectangle Rect(int x, int y, int w, int h, Color c, bool show = true) =>
            Place(new Rectangle(win) { Color = c }, x, y, w, h, show);

        public Label Text(int x, int y, int w, int h, string markup, bool show = true) =>
            Place(new Label(win) { Text = markup }, x, y, w, h, show);

        // Картинка с обрезкой по рамке (постер, кадр). Содержимое ставит ImageCache.
        public Image Picture(int x, int y, int w, int h, bool show = true) =>
            Place(new Image(win) { IsFixedAspect = true, CanFillOutside = true, IsSmooth = true }, x, y, w, h, show);

        // Рамка фокуса вокруг карточки (x, y, w, h): белый прямоугольник, поверх него тёмный зазор.
        // Звать ДО создания самой карточки — EFL кладёт новые объекты выше старых.
        // Возвращает белую рамку; показывать/прятать — через ShowFocus, чтобы зазор не остался поверх видео.
        public Rectangle Focus(int x, int y, int w, int h, Color gap, bool show = false)
        {
            var b = Theme.FocusBorder + Theme.FocusGap;
            var frame = Rect(x - b, y - b, w + 2 * b, h + 2 * b, Theme.Focus, show);
            gaps[frame] = Rect(x - Theme.FocusGap, y - Theme.FocusGap, w + 2 * Theme.FocusGap, h + 2 * Theme.FocusGap, gap, show);
            return frame;
        }

        // Прямоугольник со скруглёнными углами: залитый Polygon (у Rectangle в EFL радиуса нет).
        // Polygon не растягивается: форму меняет только Shape; Move сдвигает его целиком.
        public Polygon RoundRect(int x, int y, int w, int h, int radius, Color c, bool show = true)
        {
            var p = new Polygon(win) { Color = c };
            Shape(p, x, y, w, h, radius);
            if (show) p.Show();
            all.Add(p);
            return p;
        }

        public Polygon Circle(int cx, int cy, int r, Color c, bool show = true) => RoundRect(cx - r, cy - r, 2 * r, 2 * r, r, c, show);

        // Точки контура: по дуге на угол, от 6 до 16 отрезков в зависимости от радиуса.
        public static void Shape(Polygon p, int x, int y, int w, int h, int radius)
        {
            p.ClearPoints();
            var r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
            if (r == 0)
            {
                p.AddPoint(x, y); p.AddPoint(x + w, y); p.AddPoint(x + w, y + h); p.AddPoint(x, y + h);
                return;
            }
            var n = Math.Max(6, Math.Min(16, r / 2));
            int[] cx = { x + w - r, x + w - r, x + r, x + r };
            int[] cy = { y + r, y + h - r, y + h - r, y + r };
            for (var k = 0; k < 4; k++)
                for (var i = 0; i <= n; i++)
                {
                    var a = Math.PI / 2 * (k - 1 + (double)i / n);   // −90°…0°, 0°…90°, 90°…180°, 180°…270°
                    p.AddPoint((int)Math.Round(cx[k] + r * Math.Cos(a)), (int)Math.Round(cy[k] + r * Math.Sin(a)));
                }
        }

        // Скруглённая рамка фокуса: белый контур радиусом radius + 8 и зазор radius + 4 вокруг карточки с радиусом radius.
        // Звать ДО создания карточки, как Focus.
        public Polygon RoundFocus(int x, int y, int w, int h, int radius, Color gap, bool show = false)
        {
            var b = Theme.FocusBorder + Theme.FocusGap;
            var frame = RoundRect(x - b, y - b, w + 2 * b, h + 2 * b, radius + b, Theme.Focus, show);
            gaps[frame] = RoundRect(x - Theme.FocusGap, y - Theme.FocusGap, w + 2 * Theme.FocusGap, h + 2 * Theme.FocusGap, radius + Theme.FocusGap, gap, show);
            radii[frame] = radius;
            return frame;
        }

        public Polygon RoundFocus(int x, int y, int w, int h, int radius, bool show = false) => RoundFocus(x, y, w, h, radius, Theme.Bg, show);

        public void ShowFocus(EvasObject frame, bool on)
        {
            if (on) frame.Show(); else frame.Hide();
            if (gaps.TryGetValue(frame, out var g)) { if (on) g.Show(); else g.Hide(); }
        }

        public Rectangle Focus(int x, int y, int w, int h, bool show = false) => Focus(x, y, w, h, Theme.Bg, show);

        // Переставить рамку фокуса (и её зазор) на карточку (x, y, w, h); radius ≥ 0 — новый радиус скруглённой рамки.
        public void MoveFocus(EvasObject frame, int x, int y, int w, int h, int radius = -1)
        {
            var b = Theme.FocusBorder + Theme.FocusGap;
            if (frame is Polygon p && radii.TryGetValue(p, out var r))
            {
                if (radius >= 0) radii[p] = r = radius;
                Shape(p, x - b, y - b, w + 2 * b, h + 2 * b, r + b);
                if (gaps.TryGetValue(p, out var pg))
                    Shape((Polygon)pg, x - Theme.FocusGap, y - Theme.FocusGap, w + 2 * Theme.FocusGap, h + 2 * Theme.FocusGap, r + Theme.FocusGap);
                return;
            }
            frame.Move(x - b, y - b);
            frame.Resize(w + 2 * b, h + 2 * b);
            if (!gaps.TryGetValue(frame, out var g)) return;
            g.Move(x - Theme.FocusGap, y - Theme.FocusGap);
            g.Resize(w + 2 * Theme.FocusGap, h + 2 * Theme.FocusGap);
        }

        // Поднять объект наверх (рамку фокуса — вместе с зазором поверх неё).
        public void Raise(EvasObject o)
        {
            o.RaiseTop();
            if (gaps.TryGetValue(o, out var g)) g.RaiseTop();
        }

        public T Place<T>(T o, int x, int y, int w, int h, bool show) where T : EvasObject
        {
            o.Move(x, y);
            o.Resize(w, h);
            if (show) o.Show();
            all.Add(o);
            return o;
        }

        // Показать всё созданное (для экранов, где видимы все виджеты сразу).
        public void ShowAll()
        {
            foreach (var o in all) o.Show();
        }

        public void Remove(EvasObject o)
        {
            if (o == null) return;
            if (o.IsRealized) o.Unrealize();
            all.Remove(o);
            radii.Remove(o);
            if (gaps.TryGetValue(o, out var g)) { gaps.Remove(o); Remove(g); }   // рамка — вместе с зазором
        }

        public void Dispose()
        {
            foreach (var o in all) if (o.IsRealized) o.Unrealize();
            all.Clear();
            gaps.Clear();
            radii.Clear();
        }
    }
}
