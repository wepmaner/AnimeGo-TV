using System;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Позиция и отметка «просмотрено» одной серии в одном источнике: исполняет решения ReportPolicy (Core,
    // там же правила: 5 с от первой позиции, пол startSec − 10, без повторов на Ended/Exit, гость не шлёт).
    // Создаётся, когда видео пошло и длительность известна; серия сменилась — Exit и новый экземпляр,
    // так позиция одной серии никогда не уходит на сайт с id другой.
    // Все методы — с UI-потока; запросы — в фоне.
    sealed class ProgressReporter
    {
        readonly App app;
        readonly int animeId, duration;
        readonly Episode episode;
        readonly int? translationId;
        readonly string translationKey, userKey;
        readonly ReportPolicy policy;

        public ProgressReporter(App app, int animeId, Episode episode, VideoSource source, int durationSec, WatchedTracker watched, int startSec)
        {
            this.app = app;
            this.animeId = animeId;
            this.episode = episode;
            duration = durationSec;
            translationId = source.TranslationId;
            translationKey = source.TranslationKey;
            var guest = app.User == null;
            userKey = app.UserKey;
            // watched — общий на серию; уже отмеченную серию второй раз не трогаем (на сайте это переключатель).
            policy = new ReportPolicy(durationSec, startSec, !guest && translationId != null, !guest, episode.Watched, watched)
            {
                Log = m => DevLog.Write(m),   // лямбда: DevLog.Write — [Conditional], делегат на него не создать
            };
            if (translationId == null) DevLog.Write("progress: нет translationId у " + source.Provider + " — позицию не шлём");
        }

        public void Tick(int pos)
        {
            var send = policy.Tick(pos, DateTime.UtcNow, out var mark);
            if (mark) MarkWatched(pos);
            Send(send);
        }

        public void Pause() => Send(policy.Pause());

        // Перед перемоткой: уходит позиция, с которой перематывают.
        public void Seek(int target) => Send(policy.Seek(target));

        public void Ended() => Send(policy.Ended());

        // Последняя отправка; дальше экземпляр молчит.
        public void Exit()
        {
            if (policy.Closed) return;
            var sent = policy.Exit();
            DevLog.Write("exit: pos " + policy.LastPos + (sent != null ? "" : " (не шлём: уже ушла, видео не прошло 5 с или некуда)"));
            Send(sent);
        }

        void Send(int? at)
        {
            if (at == null || translationId == null) return;
            int pos = at.Value;
            int ep = episode.Id, tid = translationId.Value, number = episode.Number;
            Task.Run(async () =>
            {
                try
                {
                    await app.Api.PostProgressAsync(ep, tid, animeId, pos, duration).ConfigureAwait(false);
                    DevLog.Write("progress: sent " + pos + "/" + duration);
                    App.Ui(() => Remember(number, pos));
                }
                catch (Exception e) { DevLog.Write("progress: " + pos + " ошибка " + Why(e)); }
            });
        }

        // Отметку ставит JS сайта, а сервер — переключатель: сначала спросить, не стоит ли она уже.
        void MarkWatched(int pos)
        {
            int ep = episode.Id, number = episode.Number;
            var key = translationKey;
            Task.Run(async () =>
            {
                try
                {
                    var v = await app.Api.EpisodeVideosAsync(ep).ConfigureAwait(false);
                    if (v.IsWatched == true) DevLog.Write("watched: already");
                    else
                    {
                        await app.Api.PostWatchedAsync(ep, key).ConfigureAwait(false);
                        DevLog.Write("watched: posted ep " + number);
                    }
                    App.Ui(() =>
                    {
                        episode.Watched = true;
                        Remember(number, pos);
                    });
                }
                catch (Exception e) { DevLog.Write("watched: ep " + number + " ошибка " + Why(e)); }
            });
        }

        void Remember(int number, int pos)
        {
            app.Prefs.SetSeen(userKey, animeId, number, pos, DateTime.UtcNow);
            app.SavePrefs();
        }

        static string Why(Exception e) => PlayerSources.Why(e);
    }
}
