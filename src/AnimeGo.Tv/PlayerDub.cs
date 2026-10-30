using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Плеер: панели выбора озвучки и качества (спека §5.6, макет PlayerDub.dc.html).
    // Обычная панель (кнопка «Озвучка») — выбор становится основной; принудительная (у серии нет ни основной, ни
    // запасной, или все источники выбранной упали) — запасной, основная не меняется. Под панелью видео на паузе.
    sealed partial class PlayerScreen
    {
        const int FreshStreamsMs = 10 * 60 * 1000;   // ссылки моложе — смена качества без нового запроса
        static readonly string[] Qualities = { "1080p", "720p", "480p", "360p", "240p" };

        ChoicePanel choice;
        bool resumeOnClose;
        Action askNext;      // автопереход: у следующей серии надо спросить озвучку — панель по окончании текущей
        int askAttempt;

        int Here => Math.Max(0, video.TargetSec > 0 ? video.TargetSec : lastPos);

        void CloseChoice()
        {
            choice?.Close();
            choice = null;
        }

        // pause — видео на паузу (продолжится, если закрыть без выбора), внизу компактная панель с кнопками.
        ChoicePanel OpenChoice(string title, string hint, List<ChoiceItem> items, bool pause)
        {
            CloseChoice();
            resumeOnClose = false;
            if (pause)
            {
                resumeOnClose = video.IsPlaying;
                video.Pause();
                reporter?.Pause();
                ShowPanel(EpisodePanel.Mode.Compact, EpisodePanel.Zone.Buttons);
                idle.Stop();
                UpdateButtons();
            }
            return choice = new ChoicePanel(ui, title, hint, items);
        }

        // «Назад» в обычной панели: закрыть, видео — как было.
        void DismissChoice()
        {
            CloseChoice();
            if (resumeOnClose) video.Resume();
            resumeOnClose = false;
            UpdateButtons();
            RestartIdle();
        }

        // ---- озвучка ----

        void OpenDubs()
        {
            if (pending || failed || episode == null || videos == null) return;
            var items = DubItems(videos, null, dubKey);
            var c = OpenChoice(RuText.Dub, RuText.DubHint(seasons.Anime?.Title ?? "", Here), items, true);
            DevLog.Write("dub: panel " + items.Count + " items (manual)");
            c.OnClose = DismissChoice;
            c.OnPick = it =>
            {
                SetMain(it.Key);
                if (it.Key == dubKey) DismissChoice(); else SwitchDub(it.Key);
            };
            c.OnMakeMain = it =>
            {
                SetMain(it.Key);
                if (it.Key == dubKey) c.SetItems(DubItems(videos, null, dubKey)); else SwitchDub(it.Key);
            };
        }

        // Та же серия с того же места в другой озвучке.
        void SwitchDub(string key)
        {
            var at = Here;
            HidePanel();
            var _ = PlayEpisodeAsync(number, at, key);
        }

        void SetMain(string key)
        {
            app.Prefs.SetMainDub(app.UserKey, animeId, key);   // была запасной — запасная очищается (Prefs)
            app.SavePrefs();
            DevLog.Write("dub: main " + key);
        }

        // Принудительная панель: видео остановлено, в списке только доступные. «Назад» — выход из плеера.
        // autoplay — панель для следующей серии по окончании текущей: текущая не трогается, «Назад» просто закрывает панель.
        void Forced(Episode ep, EpisodeVideos v, IReadOnlyList<string> available, string hint, int start, bool autoplay = false)
        {
            var items = DubItems(v, available, null);
            if (autoplay)
            {
                if (items.Count == 0) return;
                end.Hide();
                HidePanel();
                var a = OpenChoice(RuText.Dub, hint, items, false);
                DevLog.Write("dub: panel " + items.Count + " items (forced, autoplay) ep " + ep.Number);
                a.OnClose = () => { CloseChoice(); endCancelled = true; };
                Choices(a, ep, start);
                return;
            }
            failed = true;
            pending = false;
            watchdog.Stop();
            end.Hide();
            FinishReporter();
            video.Stop();
            loading.Hide();
            blackout.Show();
            HidePanel();
            if (items.Count == 0) { Fail(RuText.SourceFailed); return; }
            var c = OpenChoice(RuText.Dub, hint, items, false);
            DevLog.Write("dub: panel " + items.Count + " items (forced) ep " + ep.Number);
            c.OnClose = () => { CloseChoice(); Exit(); };
            Choices(c, ep, start);
        }

        // Выбор в принудительной панели — запасная, «Сделать основной» — основная; затем серия в этой озвучке.
        void Choices(ChoicePanel c, Episode ep, int start)
        {
            c.OnPick = it =>
            {
                app.Prefs.SetFallbackDub(app.UserKey, animeId, it.Key);
                app.SavePrefs();
                DevLog.Write("dub: fallback " + it.Key);
                var _ = PlayEpisodeAsync(ep.Number, start, it.Key);
            };
            c.OnMakeMain = it =>
            {
                SetMain(it.Key);
                var _ = PlayEpisodeAsync(ep.Number, start, it.Key);
            };
        }

        // Все источники озвучки не завелись — выбрать другую (кроме упавшей); других нет — тост и выход.
        void SourcesFailed(int start)
        {
            DevLog.Write("source: все источники " + dubKey + " упали");
            var avail = videos == null ? new List<string>()
                : DubChooser.Choose(new DubPref(), videos.Sources).Available.Where(k => k != dubKey).ToList();
            if (avail.Count == 0 || episode == null) { Fail(RuText.SourceFailed); return; }
            Forced(episode, videos, avail, RuText.SourceFailed + ". " + RuText.ChooseDub, start);
        }

        // Автопереход: пока идёт отсчёт, узнать, есть ли у следующей серии основная/запасная. Нет — отсчёт убрать
        // (сам не перейдёт), досмотреть текущую и по её окончании показать панель (AskNextIfPending).
        void CheckNextDub(int next)
        {
            var ep = info?.Episodes.FirstOrDefault(e => e.Number == next);
            if (ep == null) return;
            var n = attempt;
            var pref = app.Prefs.Dub(app.UserKey, animeId);
            Task.Run(async () =>
            {
                EpisodeVideos v = null;
                try { v = await app.EpisodeVideosAsync(ep.Id).ConfigureAwait(false); }
                catch (Exception e) { DevLog.Write("end: серия " + next + " ошибка " + Why(e)); }
                App.Ui(() =>
                {
                    if (!alive || n != attempt || !end.Visible || choice != null || v == null) return;
                    var d = DubChooser.Choose(pref, v.Sources);
                    if (d.TranslationKey != null || d.Available.Count == 0) return;
                    DevLog.Write("end: у серии " + next + " нет озвучки — панель по окончании текущей");
                    var studio = PlayerSources.Title(pref.Main ?? pref.Fallback, info, v) ?? RuText.Dub;
                    end.Hide();
                    askAttempt = attempt;
                    askNext = () => Forced(ep, v, d.Available, RuText.NoDubChoose(studio, next), 0, true);
                });
            });
        }

        // Текущая серия кончилась: если ждёт вопрос про озвучку следующей — показать панель. true — показана.
        bool AskNextIfPending()
        {
            var a = askNext;
            askNext = null;
            if (a == null || askAttempt != attempt || choice != null) return false;
            a();
            return true;
        }

        // Строки панели: озвучки, у которых у серии есть хоть один источник (only — только эти ключи),
        // группы по типу, справа провайдеры; без известного провайдера — серая «недоступно в приложении».
        List<ChoiceItem> DubItems(EpisodeVideos v, IReadOnlyList<string> only, string current)
        {
            var pref = app.Prefs.Dub(app.UserKey, animeId);
            var list = new List<(string Key, string Title, string Kind)>();
            foreach (var t in info?.Translations ?? new Translation[0])
                if (v.Sources.Any(s => s.TranslationKey == t.Key)) list.Add((t.Key, t.Title, t.Kind));
            foreach (var s in v.Sources)
                if (s.TranslationKey != null && list.All(x => x.Key != s.TranslationKey)) list.Add((s.TranslationKey, s.TranslationTitle ?? s.TranslationKey, ""));
            if (only != null) list = list.Where(x => only.Contains(x.Key)).ToList();

            return list.Select(x =>
            {
                var provs = v.Sources.Where(s => s.TranslationKey == x.Key).Select(s => s.Provider).Distinct()
                    .OrderBy(p => VideoSource.Rank(p) < 0 ? 9 : VideoSource.Rank(p)).ToList();
                var ok = provs.Any(p => VideoSource.Rank(p) >= 0);
                var marks = new List<string>();
                if (x.Key == current) marks.Add(RuText.Now);
                if (x.Key == pref.Main) marks.Add(RuText.Main);
                if (x.Key == pref.Fallback) marks.Add(RuText.Fallback);
                return new ChoiceItem
                {
                    Key = x.Key,
                    Title = x.Title,
                    Group = Group(x.Kind),
                    Enabled = ok,
                    Note = ok ? string.Join(" · ", provs.Select(ProviderName)) : RuText.Unavailable,
                    Mark = marks.Count > 0 ? string.Join(" · ", marks) : null,
                    Current = x.Key == current,
                    IsMain = x.Key == pref.Main,
                };
            }).OrderBy(i => GroupRank(i.Group)).ToList();   // OrderBy устойчив: внутри группы — порядок сайта
        }

        static string Group(string kind)
        {
            switch (kind)
            {
                case "Дубляж": return "ДУБЛЯЖ";
                case "Многоголосый": return "МНОГОГОЛОСАЯ";
                case "Субтитры": return "СУБТИТРЫ";
                default: return (kind ?? "").ToUpperInvariant();
            }
        }

        static int GroupRank(string g) => g == "" ? 0 : g == "ДУБЛЯЖ" ? 1 : g == "МНОГОГОЛОСАЯ" ? 2 : g == "СУБТИТРЫ" ? 3 : 4;

        static string ProviderName(string p) => p == "cvh" ? "CVH" : p == "aniboom" ? "AniBoom" : p == "kodik" ? "Kodik" : p;

        // ---- качество ----

        void OpenQuality()
        {
            if (pending || failed || streams == null || stream == null) return;
            var items = new List<ChoiceItem> { new ChoiceItem { Key = "auto", Title = RuText.AutoHls } };
            foreach (var q in Qualities)
                if (streams.Streams.Any(s => s.Quality == q)) items.Add(new ChoiceItem { Key = q, Title = q });
            foreach (var it in items)
                if (it.Key == stream.Quality) { it.Current = true; it.Mark = RuText.Now; }
            var c = OpenChoice(RuText.Quality, RuText.QualityHint(Here), items, true);
            DevLog.Write("quality: panel " + items.Count + " items");
            c.OnClose = DismissChoice;
            c.OnPick = it =>
            {
                app.Prefs.Quality = it.Key;
                app.SavePrefs();
                DevLog.Write("quality: " + it.Key);
                var st = streams.Pick(it.Key);
                if (st == null || st.Url == stream.Url) { DismissChoice(); return; }
                Reload(Clock.Ms - streamsAt < FreshStreamsMs ? st : null);
            };
        }

        // Тот же источник с текущего места: st — готовый поток из streams; null — запросить ссылки заново.
        void Reload(Stream st)
        {
            var at = Here;
            var n = ++attempt;
            CloseChoice();
            HidePanel();
            FinishReporter();
            watchdog.Stop();
            video.Stop();
            pending = true;
            endShown = endCancelled = false;
            lastPos = 0;
            clickAt = Clock.Ms;
            blackout.Show();
            loading.Show();
            if (st != null) StartStream(n, st, at); else TrySource(n, srcIndex, at);
        }
    }
}
