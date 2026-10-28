using System.Diagnostics;

namespace AnimeGo.Tv
{
    // Монотонные миллисекунды с запуска приложения. Environment.TickCount — int: через ~24,9 суток работы ТВ
    // он уходит в минус, и разности вида «now - тогда < N» ломаются (OK переставал работать).
    static class Clock
    {
        static readonly Stopwatch watch = Stopwatch.StartNew();

        public static long Ms => watch.ElapsedMilliseconds;
    }
}
