using System;
using System.Collections.Generic;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Скелетон: скруглённые прямоугольники цвета карточек на местах будущих карточек и строк, пока ждём сайт.
    // Плавно пульсируют (цвет между Card и чуть светлее, период PeriodMs). Hide убирает виджеты; снова Add+Show можно.
    // Виджеты лежат в Widgets экрана — закрылся экран, исчез и скелетон. Таймер — в поле (GC).
    sealed class Skeleton
    {
        const int PeriodMs = 1200;
        const double StepSec = 0.08;
        static readonly Color Lo = Theme.Card, Hi = Color.FromHex("#2a2d36");

        readonly Widgets ui;
        readonly List<Polygon> parts = new List<Polygon>();
        readonly List<(int X, int Y, int W, int H, int R)> shapes = new List<(int, int, int, int, int)>();
        readonly UiTimer timer = new UiTimer();

        public bool Visible { get; private set; }

        public Skeleton(Widgets ui) { this.ui = ui; }

        public Skeleton Add(int x, int y, int w, int h, int radius = 14)
        {
            shapes.Add((x, y, w, h, radius));
            if (Visible) parts.Add(ui.RoundRect(x, y, w, h, radius, Lo));
            return this;
        }

        // Полоска «строка текста»: высота h, скругление по высоте.
        public Skeleton Line(int x, int y, int w, int h) => Add(x, y, w, h, h / 2);

        public void Show()
        {
            if (Visible) return;
            Visible = true;
            foreach (var s in shapes) parts.Add(ui.RoundRect(s.X, s.Y, s.W, s.H, s.R, Lo));
            timer.Start(StepSec, Pulse, true);
        }

        public void Hide()
        {
            timer.Stop();
            Visible = false;
            foreach (var p in parts) ui.Remove(p);
            parts.Clear();
        }

        // Убрать и забыть фигуры: следующий Show нарисует только то, что добавят заново.
        public void Clear()
        {
            Hide();
            shapes.Clear();
        }

        void Pulse()
        {
            var t = (Clock.Ms % PeriodMs) / (double)PeriodMs;
            var k = (1 - Math.Cos(2 * Math.PI * t)) / 2;   // 0 → 1 → 0 без рывка
            var c = Color.FromRgb(Mix(Lo.R, Hi.R, k), Mix(Lo.G, Hi.G, k), Mix(Lo.B, Hi.B, k));
            foreach (var p in parts) p.Color = c;
        }

        static int Mix(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);
    }
}
