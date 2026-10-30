using System;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Колесико ожидания, как на YouTube: дуга 270° толщиной thick, вращается на StepDeg каждые StepSec.
    // Полигон (у EFL нет дуг), точки пересчитываются на каждом шаге. Таймер — в поле (GC). Звать с UI-потока.
    sealed class Spinner
    {
        const double StepSec = 0.04;
        const int StepDeg = 14, SweepDeg = 270, Segments = 24;

        readonly Polygon arc;
        readonly int r, thick;
        int cx, cy;
        readonly UiTimer timer = new UiTimer();
        int angle;

        public bool Visible { get; private set; }

        public Spinner(Widgets ui, int cx, int cy, int r, int thick, Color color)
        {
            this.cx = cx;
            this.cy = cy;
            this.r = r;
            this.thick = thick;
            arc = ui.RoundRect(cx - r, cy - r, 2 * r, 2 * r, r, color, false);
        }

        public Spinner(Widgets ui, int cx, int cy, int r = 36, int thick = 6) : this(ui, cx, cy, r, thick, Theme.Text) { }

        public void Show()
        {
            if (Visible) return;
            Visible = true;
            Draw();
            arc.Show();
            timer.Start(StepSec, () => { angle = (angle + StepDeg) % 360; Draw(); }, true);
        }

        public void Hide()
        {
            timer.Stop();
            Visible = false;
            arc.Hide();
        }

        public void Raise() => arc.RaiseTop();

        // Переставить центр (спиннер догрузки сетки — под последней строкой).
        public void MoveTo(int x, int y)
        {
            cx = x;
            cy = y;
            if (Visible) Draw();
        }

        // Контур: внешняя дуга по часовой, затем внутренняя обратно.
        void Draw()
        {
            arc.ClearPoints();
            var ro = r;
            var ri = Math.Max(1, r - thick);
            for (var i = 0; i <= Segments; i++) Point(ro, angle + SweepDeg * i / (double)Segments);
            for (var i = Segments; i >= 0; i--) Point(ri, angle + SweepDeg * i / (double)Segments);
        }

        void Point(int radius, double deg)
        {
            var a = deg * Math.PI / 180;
            arc.AddPoint((int)Math.Round(cx + radius * Math.Cos(a)), (int)Math.Round(cy + radius * Math.Sin(a)));
        }
    }
}
