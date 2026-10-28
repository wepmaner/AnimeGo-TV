using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Плеер (спека §5.6, §7): полноэкранное видео, панель EpisodePanel, отсчёт до следующей серии.
    // Серия: /player → /player/videos → озвучка (DubChooser) → источники cvh, затем aniboom → поток → VideoView.
    // Источник не завёлся (ошибка, нет Playing за 10 с) — следующий. Позицию шлёт ProgressReporter.
    sealed partial class PlayerScreen : IScreen
    {
        const int StartTimeoutSec = 10, IdleSec = 5, EndLeadSec = 10, Countdown = 5, SkipSec = 90, SkipUntilSec = 240;

        readonly App app;
        readonly int animeId, startNumber, startSec;
        readonly string animeUrl;
        readonly bool openEpisodes;
        readonly Widgets ui;
        readonly VideoView video;
        readonly EpisodePanel panel;
        readonly Rectangle blackout;
        readonly Label loading;
        readonly EndOverlay end;
        // Таймеры EFL — в полях (GC), все останавливаются в Hide.
        readonly UiTimer tick = new UiTimer(), idle = new UiTimer(), watchdog = new UiTimer(), failTimer = new UiTimer();

        PlayerInfo info;
        readonly SeasonRows seasons;
        Episode episode;
        string dubKey, dubTitle, quality = "—";
        List<VideoSource> sources = new List<VideoSource>();
        VideoSource source;
        StreamSet streams;
        Stream stream;             // что играет: качество — для панели качества
        long streamsAt;             // когда получен streams (Clock.Ms): свежий — смена качества без запроса
        EpisodeVideos videos;      // источники текущей серии — для панели озвучек
        ProgressReporter reporter;
        WatchedTracker watched;    // одна на серию: смена источника не обнуляет просмотренные секунды
        Episode watchedFor;
        int attempt, number, srcIndex, resumeSec, lastPos, nextNumber;
        long clickAt, lastSeekKeyAt, holdFrom, lastSeekAt;
        int lastSeekDir;
        bool alive, pending, failed, endShown, endCancelled;

        public PlayerScreen(App app, int animeId, string animeUrl, int episodeNumber, int startSec, bool openEpisodes = false)
        {
            this.app = app;
            this.animeId = animeId;
            this.animeUrl = animeUrl;
            startNumber = episodeNumber;
            this.startSec = Math.Max(0, startSec);
            this.openEpisodes = openEpisodes;
            ui = new Widgets(app.Win);
            video = new VideoView(app.Win);
            blackout = ui.Rect(0, 0, Theme.W, Theme.H, Color.Black);
            panel = new EpisodePanel(ui);
            seasons = new SeasonRows(app, animeId, animeUrl, panel) { Loaded = UpdateTitle };
            loading = ui.Text(Theme.Margin, Theme.H - Theme.Margin - 40, 600, 40, Theme.T(RuText.Loading, 26, Theme.MutedHex), false);
            end = new EndOverlay(ui) { Watch = WatchNext, Cancel = () => endCancelled = true };
        }

        public void Show()
        {
            alive = true;
            video.Playing += OnPlaying;
            video.Error += OnError;
            video.Ended += OnEnded;
            tick.Start(1, Tick, true);
            seasons.Load();
            var _ = PlayEpisodeAsync(startNumber, startSec);
            if (openEpisodes) ShowPanel(EpisodePanel.Mode.Full, EpisodePanel.Zone.Rows);
        }

        public void Hide()
        {
            alive = false;
            CloseChoice();
            seasons.Stop();
            FinishReporter();
            foreach (var t in new[] { tick, idle, watchdog, failTimer }) t.Stop();
            end.Hide();
            video.Stop();
            ui.Dispose();
        }

        // ---- запуск серии ----

        public Task PlayEpisodeAsync(int num, int start, string forcedTranslationKey = null)
        {
            var n = ++attempt;
            CloseChoice();
            FinishReporter();
            watchdog.Stop();
            end.Hide();
            video.Stop();
            number = num;
            episode = null;
            source = null;
            pending = true;
            failed = false;
            endShown = endCancelled = false;
            lastPos = 0;
            clickAt = Clock.Ms;
            blackout.Show();
            loading.Show();
            UpdateTitle();
            var pref = app.Prefs.Dub(app.UserKey, animeId);
            var cached = info;
            return Task.Run(async () =>
            {
                PlayerInfo pi = cached;
                EpisodeVideos v = null;
                Episode ep = null;
                string why = null;
                try
                {
                    if (pi == null) pi = await app.Api.PlayerAsync(animeId).ConfigureAwait(false);
                    ep = pi.Episodes.FirstOrDefault(e => e.Number == num);
                    if (ep != null) v = await app.Api.EpisodeVideosAsync(ep.Id).ConfigureAwait(false);
                    else why = "нет серии " + num + " из " + pi.Episodes.Count;
                }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != attempt) return;
                    if (pi != null && info == null) { info = pi; seasons.SetInfo(pi); }
                    if (v == null) { DevLog.Write("play: anime " + animeId + " ep " + num + " ошибка " + why); Fail(RuText.NoConnection); return; }
                    Choose(n, ep, v, pref, forcedTranslationKey, start);
                });
            });
        }

        void Choose(int n, Episode ep, EpisodeVideos v, DubPref pref, string forced, int start)
        {
            string key;
            if (forced != null && PlayerSources.For(forced, v.Sources).Count > 0) key = forced;
            else
            {
                var d = DubChooser.Choose(pref, v.Sources);
                key = d.TranslationKey;
                if (key == null)
                {
                    DevLog.Write("play: ep " + ep.Number + " нет озвучки, доступно " + d.Available.Count);
                    var studio = PlayerSources.Title(pref.Main ?? pref.Fallback, info, v) ?? RuText.Dub;
                    Forced(ep, v, d.Available, RuText.NoDubChoose(studio, ep.Number), start);
                    return;
                }
                if (d.UsedFallback) Toast.Show(ui, RuText.SwitchedDub(PlayerSources.Title(pref.Main, info, v) ?? RuText.Dub, ep.Number, PlayerSources.Title(key, info, v) ?? RuText.Dub));
            }
            episode = ep;
            videos = v;
            dubKey = key;
            dubTitle = PlayerSources.Title(key, info, v);
            sources = PlayerSources.For(key, v.Sources);
            panel.SetCurrent(animeId, ep.Number);
            TrySource(n, 0, start);
        }

        void TrySource(int n, int i, int start)
        {
            if (i >= sources.Count) { SourcesFailed(start); return; }
            var src = sources[i];
            var want = app.Prefs.Quality;
            srcIndex = i;
            pending = true;
            Task.Run(async () =>
            {
                StreamSet set = null;
                string why = null;
                try { set = await PlayerSources.FetchAsync(app.Api, src).ConfigureAwait(false); }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != attempt) return;
                    var st = set?.Pick(want);
                    if (st == null) { NextSource(n, why ?? "нет потоков"); return; }
                    source = src;
                    streams = set;
                    streamsAt = Clock.Ms;
                    StartStream(n, st, start);
                });
            });
        }

        void StartStream(int n, Stream st, int start)
        {
            stream = st;
            quality = st.Quality == "auto" ? RuText.AutoQuality : st.Quality;
            resumeSec = start;
            UpdateTitle();
            DevLog.Write("play: anime " + animeId + " ep " + number + " dub " + dubKey + " src " + source.Provider + " q " + st.Quality + " start " + start);
            watchdog.Start(StartTimeoutSec, () => NextSource(n, "timeout"));
            var _ = video.PlayAsync(st.Url, start);
        }

        void NextSource(int n, string why)
        {
            if (!alive || n != attempt || failed) return;
            DevLog.Write("source: failed " + sources[srcIndex].Provider + " " + why);
            watchdog.Stop();
            var at = pending || lastPos <= 0 ? resumeSec : lastPos;   // упал посреди просмотра — продолжить с того же места
            FinishReporter();
            video.Stop();
            pending = true;
            blackout.Show();
            loading.Show();
            if (srcIndex + 1 < sources.Count) Toast.Show(ui, RuText.SwitchedSource);
            TrySource(n, srcIndex + 1, at);
        }

        void OnPlaying()
        {
            if (!alive || !pending) return;
            pending = false;
            watchdog.Stop();
            blackout.Hide();
            loading.Hide();
            DevLog.Write("play: playing +" + (Clock.Ms - clickAt) + " ms");
            UpdateButtons();
        }

        void OnError(string why)
        {
            if (alive) NextSource(attempt, why);
        }

        void OnEnded(string why)
        {
            if (!alive) return;
            DevLog.Write("video: ended " + why);
            if (why != "completed") { reporter?.Pause(); UpdateButtons(); return; }
            reporter?.Ended();
            if (AskNextIfPending()) return;
            if (!endCancelled && !end.Visible) ShowEnd(true);
        }

        void Fail(string message)
        {
            failed = true;
            pending = false;
            watchdog.Stop();
            FinishReporter();
            video.Stop();
            loading.Hide();
            blackout.Show();
            Toast.Show(ui, message);
            failTimer.Start(3, Exit);   // нет связи или выбрать нечего — тост и выход
        }

        void FinishReporter()
        {
            reporter?.Exit();
            reporter = null;
        }

        void Exit()
        {
            FinishReporter();
            app.Back();
        }

        // ---- раз в секунду ----

        void Tick()
        {
            if (pending || failed || episode == null || source == null) return;
            var p = video.PositionSec;
            var d = Duration;
            if (p > 0) lastPos = p;
            if (reporter == null && d > 0 && video.IsPlaying)
            {
                if (watchedFor != episode) { watched = new WatchedTracker(d); watchedFor = episode; }
                reporter = new ProgressReporter(app, animeId, episode, source, d, watched, resumeSec);
            }
            if (video.IsPlaying || video.IsPaused) reporter?.Tick(p);
            panel.SetPosition(video.TargetSec, d);
            panel.SetSkip(video.TargetSec < SkipUntilSec);
            if (d > 0 && p < d - EndLeadSec - 5) endCancelled = false;
            if (d > 0 && p >= d - EndLeadSec && video.IsPlaying && !endShown && !endCancelled) ShowEnd(false);
        }

        int Duration
        {
            get
            {
                var d = video.DurationSec;
                return d > 0 ? d : streams?.DurationSec ?? 0;
            }
        }

        // ---- клавиши ----

        public bool OnKey(string key)
        {
            if (choice != null && key != Keys.Stop && key != Keys.Exit && !key.StartsWith("XF86Audio", StringComparison.Ordinal))
                return choice.OnKey(key);   // панель выбора модальна; громкость и Stop — как обычно
            switch (key)
            {
                case Keys.Play: case Keys.Pause: case Keys.PlayPause: TogglePause(); return true;
                case Keys.Stop: Exit(); return true;
                case Keys.FF: SeekKey(1); return true;
                case Keys.Rew: SeekKey(-1); return true;
            }
            if (key.StartsWith("XF86Audio", StringComparison.Ordinal) || key == Keys.Exit) return false;   // громкость — телевизору
            if (failed) { if (key == Keys.Back) Exit(); return true; }
            if (end.Visible) return end.OnKey(key);
            if (!panel.Visible)
            {
                // ↓ — полная панель с фокусом на кнопках; любая другая — компактная (полоса + кнопки внизу).
                switch (key)
                {
                    case Keys.Back: Exit(); return true;
                    case Keys.Down: ShowPanel(EpisodePanel.Mode.Full, EpisodePanel.Zone.Buttons); return true;
                    case Keys.Ok: TogglePause(); break;
                    case Keys.Left: SeekKey(-1); break;
                    case Keys.Right: SeekKey(1); break;
                }
                if (!panel.Visible) ShowPanel(EpisodePanel.Mode.Compact, EpisodePanel.Zone.Bar);
                return true;
            }
            RestartIdle();
            switch (key)
            {
                case Keys.Back: BackInPanel(); break;
                case Keys.Up: panel.Up(); break;
                case Keys.Down:
                    var was = panel.State;
                    panel.Down();
                    if (was != panel.State) DevLog.Write("panel: full rows " + panel.RowCount);
                    break;
                case Keys.Left: case Keys.Right:
                    var dx = key == Keys.Left ? -1 : 1;
                    if (panel.Focus == EpisodePanel.Zone.Bar) SeekKey(dx); else panel.Move(dx);
                    break;
                case Keys.Ok: PanelOk(); break;
            }
            return true;
        }

        void PanelOk()
        {
            if (panel.Focus == EpisodePanel.Zone.Bar) { TogglePause(); return; }
            if (panel.Focus == EpisodePanel.Zone.Buttons)
            {
                switch (panel.Button)
                {
                    case EpisodePanel.Pause: TogglePause(); break;
                    case EpisodePanel.Dub: OpenDubs(); break;
                    case EpisodePanel.Quality: OpenQuality(); break;
                    case EpisodePanel.Next: PlayNext(); break;
                    case EpisodePanel.Skip: SkipOpening(); break;
                }
                return;
            }
            var f = panel.Focused();
            if (f == null) return;
            var (row, ep) = f.Value;
            if (row.Season.AnimeId != animeId)
            {
                DevLog.Write("open: anime " + row.Season.AnimeId + " ep " + ep.Number + " (другой сезон)");
                FinishReporter();
                app.Navigate(new PlayerScreen(app, row.Season.AnimeId, row.Url, ep.Number, 0));
                return;
            }
            HidePanel();
            if (ep.Number != number || failed) { var _ = PlayEpisodeAsync(ep.Number, 0); }
        }

        void TogglePause()
        {
            if (pending || failed || choice != null) return;   // под панелью выбора видео стоит
            if (video.IsPlaying)
            {
                video.Pause();
                reporter?.Pause();
            }
            else if (video.IsPaused) video.Resume();
            UpdateButtons();
        }

        // ±10 с; удержание (повтор KeyDown чаще 450 мс) — шаг 20/40/80 с после 1/2/3 с удержания.
        void SeekKey(int dir)
        {
            long now = Clock.Ms;
            if (dir != lastSeekDir || now - lastSeekKeyAt > 450) holdFrom = now;
            lastSeekDir = dir;
            lastSeekKeyAt = now;
            var held = now - holdFrom;
            var step = held >= 3000 ? 80 : held >= 2000 ? 40 : held >= 1000 ? 20 : 10;
            SeekBy(dir * step, false);
        }

        void SeekBy(int delta, bool single)
        {
            if (pending || failed || episode == null || choice != null) return;
            var d = Duration;
            var target = Math.Max(0, video.TargetSec + delta);
            if (d > 0) target = Math.Min(target, d - 1);
            long now = Clock.Ms;
            if (single || now - lastSeekAt > 1000) reporter?.Seek(target);   // одна отправка на серию нажатий
            lastSeekAt = now;
            var _ = video.Seek(target);
            panel.SetPosition(target, d);
        }

        // «Пропустить опенинг» (спека §9): API времени опенинга не даёт, поэтому учимся по нажатиям. Первое нажатие
        // запоминает, где у тайтла начинается опенинг; дальше нажатие до него (или в первые 10 с) — сразу за опенинг.
        void SkipOpening()
        {
            if (pending || failed || episode == null) return;
            var pos = video.TargetSec;
            var at = app.Prefs.OpeningAt(app.UserKey, animeId);
            int target;
            if (at != null && pos <= at.Value + 10)
            {
                target = at.Value + SkipSec;
                DevLog.Write("skip: " + pos + " → " + target + " (опенинг с " + at + ")");
            }
            else
            {
                target = pos + SkipSec;
                // Запоминаем только более раннее начало: позднее нажатие (уже посреди опенинга) не портит выученное.
                var learn = pos < SkipUntilSec && (at == null || pos < at.Value);
                if (learn) { app.Prefs.SetOpeningAt(app.UserKey, animeId, pos); app.SavePrefs(); }
                DevLog.Write("skip: " + pos + " → " + target + (learn ? " (запомнил опенинг с " + pos + ")" : ""));
            }
            SeekBy(target - pos, true);
        }

        // ---- панель ----

        void ShowPanel(EpisodePanel.Mode mode, EpisodePanel.Zone zone)
        {
            panel.SetPosition(video.TargetSec, Duration);
            panel.Show(mode, zone);
            Toast.Raise();   // панель поднимает свои виджеты наверх — плашка не должна оказаться под ней
            DevLog.Write(panel.State == EpisodePanel.Mode.Full ? "panel: full rows " + panel.RowCount : "panel: compact");
            RestartIdle();
        }

        // «Назад»: полная → компактная, компактная → скрыть.
        void BackInPanel()
        {
            if (panel.State == EpisodePanel.Mode.Full)
                ShowPanel(EpisodePanel.Mode.Compact, panel.Focus == EpisodePanel.Zone.Rows ? EpisodePanel.Zone.Buttons : panel.Focus);
            else HidePanel();
        }

        void HidePanel()
        {
            idle.Stop();
            panel.Hide();
        }

        // 5 с без нажатий: панель прячется, если фокус не в рядах; на паузе полная сворачивается в компактную.
        void RestartIdle() => idle.Start(IdleSec, () =>
        {
            if (!panel.Visible || panel.Focus == EpisodePanel.Zone.Rows) return;
            if (!video.IsPaused) panel.Hide();
            else if (panel.State == EpisodePanel.Mode.Full) ShowPanel(EpisodePanel.Mode.Compact, panel.Focus);
        });

        void UpdateButtons() => panel.SetButtons(video.IsPaused, dubTitle ?? "—", quality);

        void UpdateTitle()
        {
            var cur = seasons.Current;
            var parts = new List<string>();
            if (seasons.List.Count > 1 && cur != null) parts.Add(RuText.SeasonNo(cur.Season.Order));
            parts.Add(RuText.Episode(number));
            if (dubTitle != null) parts.Add(dubTitle);
            if (source != null) parts.Add(quality);
            panel.SetTitle(seasons.Anime?.Title ?? "", string.Join(" · ", parts));
            UpdateButtons();
        }

        // ---- конец серии ----

        int NextNumber() => info?.Episodes.Where(e => e.Number > number).Select(e => e.Number).DefaultIfEmpty(0).Min() ?? 0;

        void PlayNext()
        {
            var next = NextNumber();
            if (next == 0) { Toast.Show(ui, RuText.NoNextEpisode); return; }
            HidePanel();
            var _ = PlayEpisodeAsync(next, 0);
        }

        // За 10 с до конца или по PlaybackCompleted. Следующей нет — по окончании выходим.
        void ShowEnd(bool completed)
        {
            if (choice != null) return;
            endShown = true;
            nextNumber = NextNumber();
            if (nextNumber == 0) { if (completed) ToTitle(); return; }
            idle.Stop();
            panel.Hide();
            end.Show(Countdown);
            CheckNextDub(nextNumber);
        }

        // Спека §5.6: следующей серии нет — на страницу тайтла (вместо плеера; «Назад» с неё — туда, откуда пришли).
        void ToTitle()
        {
            FinishReporter();
            DevLog.Write("end: следующей нет → тайтл " + animeId);
            app.Navigate(new TitleScreen(app, animeId, animeUrl));
        }

        void WatchNext()
        {
            DevLog.Write("end: следующая серия " + nextNumber);
            var _ = PlayEpisodeAsync(nextNumber, 0);
        }

        static string Why(Exception e) => PlayerSources.Why(e);
    }
}
