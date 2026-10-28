using System;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Таймер EFL на UI-потоке. Держать в поле экрана: делегат хранится здесь, иначе его соберёт GC.
    // Start перезапускает, Stop — отменяет; звать только с UI-потока.
    sealed class UiTimer
    {
        IntPtr handle;
        Func<bool> tick;
        int generation;    // какой запуск сейчас действует
        bool inTick;       // внутри своего обратного вызова: удалять таймер нельзя, EFL снимет его по false

        public bool Running => handle != IntPtr.Zero;

        // repeat — вызывать каждые sec секунд до Stop; иначе один раз.
        public void Start(double sec, Action action, bool repeat = false)
        {
            Stop();
            var mine = ++generation;
            tick = () =>
            {
                if (mine != generation) return false;   // перезапущен или остановлен из обратного вызова
                if (!repeat) handle = IntPtr.Zero;
                inTick = true;
                try { action(); }
                finally { inTick = false; }
                return repeat && mine == generation;
            };
            handle = EcoreMainloop.AddTimer(sec, tick);
        }

        public void Stop()
        {
            generation++;
            if (handle != IntPtr.Zero && !inTick) EcoreMainloop.RemoveTimer(handle);
            handle = IntPtr.Zero;
        }
    }
}
