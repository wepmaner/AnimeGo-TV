using System;

namespace AnimeGo.Core
{
    // Когда слать позицию просмотра на сайт (правила списаны с JS сайта, API-findings.md).
    // Метод, вернувший true, считает отправку состоявшейся: обновляет LastSent.
    public sealed class ProgressTracker
    {
        private const int MinDeltaSec = 5;
        private const int MinIntervalSec = 5;

        private DateTime lastSentAtUtc;
        private DateTime lastNow;
        private int lastPos;

        // durationSec принят для совместимости с WatchedTracker; правила отправки от длительности не зависят.
        public ProgressTracker(int durationSec) { LastSent = -1; }

        // Позиция последней отправки; -1, пока ничего не отправлено.
        public int LastSent { get; private set; }

        // Вызывается таймером раз в секунду.
        public bool Tick(int positionSec, DateTime nowUtc)
        {
            lastPos = positionSec;
            lastNow = nowUtc;
            if (positionSec <= 0) return false;
            if (LastSent < 0) return Send(positionSec);
            if (Math.Abs(positionSec - LastSent) >= MinDeltaSec
                && (nowUtc - lastSentAtUtc).TotalSeconds >= MinIntervalSec)
                return Send(positionSec);
            return false;
        }

        // Позицию слать нельзя (ниже пола ReportPolicy), но знать её надо: иначе следующая пауза/перемотка
        // отправит старую позицию. Ничего не отправляет.
        public void Observe(int positionSec) { lastPos = positionSec; }

        public bool OnPause() => lastPos > 0 && lastPos != LastSent && Send(lastPos);

        // Отправляет позицию до перемотки; новая становится текущей.
        public bool OnSeek(int newPositionSec)
        {
            int old = lastPos;
            lastPos = newPositionSec;
            return old > 0 && old != LastSent && Send(old);
        }

        public bool OnEnded() => lastPos > 0 && Send(lastPos);

        public bool OnExit() => lastPos > 0 && Send(lastPos);

        private bool Send(int pos)
        {
            LastSent = pos;
            lastSentAtUtc = lastNow;
            return true;
        }
    }

    // Когда пора отметить серию просмотренной: покрыто >= 80 % длительности.
    // Считаются различные просмотренные секунды (как ranges на сайте): повтор и перемотка не засчитываются.
    public sealed class WatchedTracker
    {
        private const int MaxStepSec = 2;

        private readonly int duration;
        private readonly bool[] covered;
        private int coveredCount;
        private int lastPos;

        public WatchedTracker(int durationSec)
        {
            duration = durationSec;
            covered = new bool[durationSec > 0 ? durationSec : 0];
        }

        public bool Fired { get; private set; }

        public bool Tick(int positionSec)
        {
            int step = positionSec - lastPos;
            int from = lastPos;
            lastPos = positionSec;
            if (Fired || duration <= 0) return false;
            if (step > 0 && step <= MaxStepSec)
            {
                for (int s = from + 1; s <= positionSec && s <= duration; s++)
                {
                    if (s < 1 || covered[s - 1]) continue;
                    covered[s - 1] = true;
                    coveredCount++;
                }
            }
            if (coveredCount * 5 < duration * 4) return false;
            Fired = true;
            return true;
        }
    }
}
