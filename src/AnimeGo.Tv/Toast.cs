using System;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Короткое сообщение плашкой внизу по центру; само прячется. Одновременно — одна плашка.
    // Виджеты кладутся в Widgets экрана: закрылся экран — плашка исчезла вместе с ним.
    static class Toast
    {
        const int Width = 1200, Height = 96;
        static Rectangle box;
        static Label label;
        static Widgets owner;
        static IntPtr timer;   // в поле: и для отмены, и чтобы делегат не собрал GC
        static Func<bool> tick;

        // Только с UI-потока.
        public static void Show(Widgets ui, string text, int ms = 4000)
        {
            Close();
            owner = ui;
            var x = (Theme.W - Width) / 2;
            var y = Theme.H - Theme.Margin - Height;
            box = ui.Rect(x, y, Width, Height, Theme.Card2);
            label = ui.Text(x + 32, y + 24, Width - 64, Height - 48, Theme.T(text, 36, align: "center"));
            tick = () => { timer = IntPtr.Zero; Close(); return false; };
            timer = EcoreMainloop.AddTimer(ms / 1000.0, tick);
        }

        // Поверх виджетов, созданных после плашки (ряды главной догружаются в фоне).
        public static void Raise()
        {
            if (box == null || !box.IsRealized) return;   // плашка могла остаться от закрытого экрана
            box.RaiseTop();
            label.RaiseTop();
        }

        static void Close()
        {
            if (timer != IntPtr.Zero) { EcoreMainloop.RemoveTimer(timer); timer = IntPtr.Zero; }
            owner?.Remove(box);
            owner?.Remove(label);
            owner = null;
            box = null;
            label = null;
        }
    }
}
