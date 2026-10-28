using System;

namespace AnimeGo.Core
{
    // Что и когда отправлять на сайт по одной серии в одном источнике (спека §6, API-findings «Отправка позиции»).
    // Только решения, без запросов: ProgressReporter на ТВ исполняет то, что вернули методы.
    // Методы возвращают позицию для отправки или null — слать нечего.
    //
    // Правила:
    // - Не шлём совсем, если отправлять некуда (гость, у источника нет translationId, длительность неизвестна).
    // - Первая отправка — только когда видео реально прошло 5 с от первой позиции > 0, которую показал плеер
    //   (неточная перемотка на старте ставит его на ключевой кадр, бывает на 5–6 с дальше или раньше).
    // - Пол startSec − 10: пока пользователь сам не перематывал, ниже не шлём — плеер встал раньше места,
    //   где остановились, и позицию на сайте откатывать нельзя. Первая перемотка пол снимает.
    //   Пол закрывает только отправку: трекер позицию всё равно узнаёт, чтобы пауза потом не прислала старую.
    // - Ended и Exit не повторяют уже ушедшую позицию; после Exit экземпляр молчит.
    // - «Просмотрено» (покрыто 80 %) — один раз за серию и только если серия ещё не отмечена; гость не отмечает.
    public sealed class ReportPolicy
    {
        public const int GateSec = 5, FloorSlackSec = 10;

        private readonly ProgressTracker progress;
        private readonly WatchedTracker watched;
        private readonly bool canSend, canMark;
        private int floor;
        private bool floorLogged, watchedDone, closed, live;
        private int? gateFrom;   // откуда считать 5 с; null — от первой позиции, которую покажет плеер

        // canSendProgress — не гость и есть translationId; canMarkWatched — не гость.
        // watched — общий на серию: смена источника не обнуляет набранное.
        public ReportPolicy(int durationSec, int startSec, bool canSendProgress, bool canMarkWatched,
            bool alreadyWatched, WatchedTracker watched)
        {
            floor = startSec - FloorSlackSec;
            canSend = canSendProgress && durationSec > 0;
            canMark = canMarkWatched;
            watchedDone = alreadyWatched;
            this.watched = watched ?? throw new ArgumentNullException(nameof(watched));
            progress = new ProgressTracker(durationSec);
        }

        // Для отладочного лога (на ТВ — DevLog); может быть null.
        public Action<string> Log { get; set; }

        // Последняя известная позиция (после того как прошли 5 с).
        public int LastPos { get; private set; }

        public bool Closed => closed;

        // Раз в секунду. markWatched — пора отметить серию просмотренной (вернётся true один раз).
        public int? Tick(int pos, DateTime nowUtc, out bool markWatched)
        {
            markWatched = false;
            if (closed) return null;
            if (watched.Tick(pos) && canMark && !watchedDone)
            {
                watchedDone = true;
                markWatched = true;
            }
            if (!live)
            {
                if (pos <= 0) return null;
                if (gateFrom == null) gateFrom = pos;
                if (pos < gateFrom + GateSec) return null;
            }
            live = true;
            LastPos = pos;
            if (!Above(pos)) { progress.Observe(pos); return null; }
            return progress.Tick(pos, nowUtc) ? Out(progress.LastSent) : null;
        }

        public int? Pause() => !closed && live && Above(LastPos) && progress.OnPause() ? Out(progress.LastSent) : null;

        // Перед перемоткой: уходит позиция, с которой перематывают. До первой отправки — просто новый отсчёт.
        public int? Seek(int target)
        {
            if (closed) return null;
            var oldOk = LastPos >= floor;
            floor = int.MinValue;   // перемотал сам — дальше любая позиция законна
            if (!live) { gateFrom = null; return null; }   // отсчёт заново от места, куда реально встанет плеер
            LastPos = target;
            if (oldOk) return progress.OnSeek(target) ? Out(progress.LastSent) : null;
            progress.Observe(target);
            return null;
        }

        public int? Ended() => !closed && Fresh() && Above(LastPos) && progress.OnEnded() ? Out(progress.LastSent) : null;

        // Последняя отправка; дальше экземпляр молчит.
        public int? Exit()
        {
            if (closed) return null;
            closed = true;
            return Fresh() && Above(LastPos) && progress.OnExit() ? Out(progress.LastSent) : null;
        }

        // Эта позиция уже ушла (тик за секунду до выхода, Ended перед Exit) — второй раз не слать.
        private bool Fresh() => live && LastPos > 0 && LastPos != progress.LastSent;

        // Пол проверяется до трекера: отброшенная позиция не должна считаться им отправленной.
        private bool Above(int pos)
        {
            if (pos >= floor) return true;
            if (!floorLogged) Log?.Invoke("progress: floor " + pos + " < " + floor + " — не шлём");
            floorLogged = true;
            return false;
        }

        // Трекер считает отправку состоявшейся; наружу она уходит, только если есть куда слать.
        private int? Out(int pos) => canSend && pos > 0 ? pos : (int?)null;
    }
}
