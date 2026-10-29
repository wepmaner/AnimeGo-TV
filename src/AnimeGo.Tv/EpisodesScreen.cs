using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Выбор серии (макет episodes.html): сверху мини-постер, название и «просмотрено X из N»; слева сезоны
    // (Seasons.Build по «Связанному», как в плеере), справа серии плитками 4×3 = страница из 12, вкладки страниц сверху
    // переключаются сами. Плитка — кадр серии (CVH, иначе AniBoom), номер, «просмотрена» или «смотрите · 12:40» с полоской.
    // OK — плеер с этой серии; ← с первой колонки — к сезонам; «Назад» — на страницу тайтла.
    sealed class EpisodesScreen : IScreen
    {
        const int Top = 48, SideX = Theme.Margin, SideW = 360, SideY = 186, ItemH = 76, ItemGap = 8;
        const int GridX = SideX + SideW + 48, GridW = Theme.W - Theme.Margin - GridX, TabsY = 186, GridY = 254;
        const int Cols = 4, Rows = 3, PerPage = Cols * Rows, Gap = 28, TileW = (GridW - (Cols - 1) * Gap) / Cols, TileH = TileW * 9 / 16;
        const int Pitch = TileH + 12 + 32 + 26, Radius = 16;
        const double SideDelay = 0.4;
        static readonly Color Shade = Color.FromRgba(14, 15, 19, 190), Dim = Color.FromRgba(14, 15, 19, 150), Track = Color.FromHex("#55575c");
        static readonly Color CurrentBack = Color.FromHex("#2a1a1d");
        enum Area { Side, Grid }

        sealed class SeasonItem
        {
            public Season Season;
            public string Url;
            public PlayerInfo Info;      // null — ещё не загрузили
            public AnimeInfo Anime;      // страница тайтла сезона: график выхода серий; null — нет
            public bool Failed;
            public Dictionary<int, string> Videos;   // серия → vkId CVH (для кадров); null — нет CVH
            public string Aniboom;                   // AniBoom есть у сезона — кадры серий без CVH берём из него
            public readonly HashSet<int> Unreleased = new HashSet<int>();   // у серии на сайте нет ни одного видео
            public readonly HashSet<int> Asked = new HashSet<int>();        // кадр уже запрашивали (не повторять при перерисовке)
            public bool VideosAsked;
        }

        readonly App app;
        readonly int animeId;
        readonly string url;
        readonly AnimeInfo anime;
        readonly ContinueCard siteResume;
        readonly int resumeEp, startSeason, startEpisode;
        readonly Widgets ui;
        readonly List<SeasonItem> seasons = new List<SeasonItem>();
        readonly List<EvasObject> side = new List<EvasObject>(), page = new List<EvasObject>(), tabs = new List<EvasObject>();
        readonly List<(Image Pic, Episode Ep, int X, int Y, bool Labeled)> pics = new List<(Image, Episode, int, int, bool)>();
        readonly UiTimer sideTimer = new UiTimer();
        readonly SemaphoreSlim thumbSlots = new SemaphoreSlim(3);
        Label sub, status;
        EvasObject frame;
        Area area = Area.Grid;
        int season, sideFocus, pageNo, focus, generation;   // focus — индекс серии в списке сезона
        bool alive;

        // startSeason/startEpisode — вернуться на то же место (из плеера по «Назад»); иначе текущий сезон и серия resumeEp.
        public EpisodesScreen(App app, int animeId, string url, AnimeInfo anime, int resumeEp, ContinueCard siteResume,
            int startSeason = 0, int startEpisode = 0)
        {
            this.app = app;
            this.animeId = animeId;
            this.url = url;
            this.anime = anime;
            this.resumeEp = resumeEp;
            this.siteResume = siteResume;
            this.startSeason = startSeason;
            this.startEpisode = startEpisode;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg);
        }

        public void Show()
        {
            alive = true;
            ui.RoundRect(Theme.Margin, Top, 64, 90, 10, Theme.Card);
            app.Images.Set(ui.Picture(Theme.Margin, Top, 64, 90), anime?.PosterUrl);
            var t = ui.Text(Theme.Margin + 88, Top + 6, Theme.W - 2 * Theme.Margin - 88, 54, Theme.T(anime?.Title ?? "", 40, bold: true));
            t.IsEllipsis = true;
            sub = ui.Text(Theme.Margin + 88, Top + 58, 1200, 34, "");
            status = ui.Text(GridX, GridY + 40, GridW, 48, "", false);
            frame = ui.RoundFocus(0, 0, TileW, TileH, Radius);

            var list = anime != null ? Seasons.Build(anime) : new[] { new Season { AnimeId = animeId, Order = 1, IsCurrent = true } };
            var urls = new Dictionary<int, string>();
            foreach (var r in anime?.Related ?? new RelatedTitle[0]) urls[r.Id] = "/anime/" + r.Slug + "-" + r.Id;
            foreach (var s in list.OrderBy(s => s.Order))
            {
                var u = s.AnimeId == animeId ? url : urls.TryGetValue(s.AnimeId, out var x) ? x : null;
                if (u != null) seasons.Add(new SeasonItem { Season = s, Url = u, Anime = s.AnimeId == animeId ? anime : null });
            }
            if (seasons.Count == 0) seasons.Add(new SeasonItem { Season = new Season { AnimeId = animeId, Order = 1, IsCurrent = true }, Url = url });
            var want = startSeason != 0 ? startSeason : animeId;
            season = Math.Max(0, seasons.FindIndex(s => s.Season.AnimeId == want));
            sideFocus = season;
            BuildSide();
            OpenSeason(season, startEpisode != 0 ? startEpisode : seasons[season].Season.AnimeId == animeId ? resumeEp : 0);
        }

        public void Hide()
        {
            alive = false;
            generation++;
            sideTimer.Stop();
            ui.Dispose();
        }

        // ---- данные ----

        // Серии сезона (PlayerInfo); затем страница с серией want (0 — первая непросмотренная).
        void OpenSeason(int i, int want)
        {
            var n = ++generation;
            season = i;
            var s = seasons[i];
            ClearPage();
            UpdateSub();
            if (s.Info != null) { ShowSeason(want); return; }
            SetStatus(RuText.Loading);
            Task.Run(async () =>
            {
                PlayerInfo pi = null;
                AnimeInfo page = s.Anime;
                var pageTask = page != null ? Task.FromResult(page) : LoadPage(s);
                try { pi = await app.Api.PlayerAsync(s.Season.AnimeId).ConfigureAwait(false); }
                catch (Exception e) { DevLog.Write("episodes: сезон " + s.Season.AnimeId + " ошибка " + PlayerSources.Why(e)); }
                page = await pageTask.ConfigureAwait(false);
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    s.Anime = page;
                    s.Info = pi;
                    s.Failed = pi == null;
                    if (pi == null) { SetStatus(RuText.NoConnection); return; }
                    DevLog.Write("episodes: сезон " + s.Season.AnimeId + " серий " + pi.Episodes.Count);
                    ShowSeason(want);
                });
            });
        }

        void ShowSeason(int want)
        {
            var eps = Episodes;
            UpdateSub();
            BuildSide();   // теперь известно число серий
            if (eps.Count == 0) { SetStatus(RuText.NoEpisodes); return; }
            SetStatus(null);
            var target = want > 0 ? eps.FindIndex(e => e.Number == want) : -1;
            if (target < 0) target = Math.Max(0, eps.FindIndex(e => !e.Watched));
            focus = target;
            pageNo = focus / PerPage;
            BuildPage();
            LoadVideos();
        }

        // Страница тайтла сезона — ради графика выхода; не вышло — без графика.
        async Task<AnimeInfo> LoadPage(SeasonItem s)
        {
            try { return AnimeParser.Parse(await app.Api.GetHtmlAsync(s.Url).ConfigureAwait(false), s.Season.AnimeId); }
            catch (Exception) { return null; }
        }

        // Когда выйдет серия (null — вышла): график страницы тайтла или «нет видео» с сайта.
        (string When, string In)? Upcoming(SeasonItem s, int number) =>
            Release.Of(s.Anime, number) ?? (s.Unreleased.Contains(number) ? ((string)null, (string)null) : ((string, string)?)null);

        List<Episode> Episodes => seasons[season].Info?.Episodes.OrderBy(e => e.Number).ToList() ?? new List<Episode>();

        // Кадры серий: titleId CVH из адреса источника любой серии → плейлист → vkId серий → thumbUrl каждой.
        // Серии без CVH — источники самой серии: есть AniBoom — его poster, нет ни одного видео — «ещё не вышла».
        void LoadVideos()
        {
            var s = seasons[season];
            if (s.VideosAsked) { RequestThumbs(); return; }
            s.VideosAsked = true;
            var first = s.Info.Episodes.FirstOrDefault();
            if (first == null) return;
            var main = app.Prefs.Dub(app.UserKey, s.Season.AnimeId).Main ?? app.Prefs.Dub(app.UserKey, animeId).Main;
            var n = generation;
            Task.Run(async () =>
            {
                Dictionary<int, string> map = null;
                string boom = null;
                try
                {
                    var v = await app.Api.EpisodeVideosAsync(first.Id).ConfigureAwait(false);
                    boom = v.Sources.Where(x => x.Provider == "aniboom").OrderBy(x => x.TranslationKey == main ? 0 : 1)
                        .Select(x => x.PlayerUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
                    var cvh = v.Sources.Where(x => x.Provider == "cvh")
                        .OrderBy(x => x.TranslationKey == main ? 0 : 1)
                        .Select(x => Cvh.ParseIframeUrl(x.PlayerUrl)).FirstOrDefault(x => x != null);
                    if (cvh != null)
                    {
                        var (titleId, voice, cvhSeason, _) = cvh.Value;
                        map = Cvh.EpisodeVideos(await app.Api.CvhPlaylistAsync(titleId).ConfigureAwait(false), cvhSeason, voice);
                    }
                }
                catch (Exception e) { DevLog.Write("episodes: кадры " + s.Season.AnimeId + " — " + PlayerSources.Why(e)); }
                App.Ui(() =>
                {
                    s.Videos = map;
                    s.Aniboom = boom;
                    DevLog.Write("episodes: кадров CVH " + (map?.Count ?? 0) + (boom != null ? ", есть AniBoom" : ""));
                    if (alive && n == generation) RequestThumbs();
                });
            });
        }

        // Кадры плиток текущей страницы: из кеша сразу, иначе узнаём адрес кадра (не больше 3 запросов одновременно).
        void RequestThumbs()
        {
            var s = seasons[season];
            if (!s.VideosAsked || (s.Videos == null && s.Aniboom == null)) return;
            var n = generation;
            var main = app.Prefs.Dub(app.UserKey, s.Season.AnimeId).Main ?? app.Prefs.Dub(app.UserKey, animeId).Main;
            foreach (var t in pics)
            {
                var (pic, ep) = (t.Pic, t.Ep);
                if (Upcoming(s, ep.Number) != null) continue;   // подписана при раскладке
                if (s.Videos != null && s.Videos.TryGetValue(ep.Number, out var vk))
                {
                    var key = "cvh-thumb-" + vk;
                    if (app.Images.TrySetCached(pic, key) || !s.Asked.Add(ep.Number)) continue;
                    Fetch(n, () => app.Api.CvhThumbAsync(vk), thumb => SetThumb(ep.Number, thumb, key));
                    continue;
                }
                var boomKey = "aniboom-thumb-" + s.Season.AnimeId + "-" + ep.Number;
                if (app.Images.TrySetCached(pic, boomKey) || !s.Asked.Add(ep.Number)) continue;
                Fetch(n, async () =>
                {
                    var v = await app.Api.EpisodeVideosAsync(ep.Id).ConfigureAwait(false);
                    if (v.Sources.Count == 0) return "";   // «ещё не вышла»
                    var boom = v.Sources.Where(x => x.Provider == "aniboom").OrderBy(x => x.TranslationKey == main ? 0 : 1)
                        .Select(x => x.PlayerUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
                    return boom == null ? null : await app.Api.AniboomPosterAsync(boom).ConfigureAwait(false);
                }, thumb =>
                {
                    if (thumb.Length > 0) { SetThumb(ep.Number, thumb, boomKey); return; }
                    s.Unreleased.Add(ep.Number);
                    BuildPage();   // перерисовать с подписью «ещё не вышла»
                });
            }
        }

        // Кадр — в плитку серии, которая на экране сейчас (страницу могли перерисовать, пока шёл запрос).
        void SetThumb(int number, string thumb, string key)
        {
            foreach (var t in pics) if (t.Ep.Number == number) { app.Images.Set(t.Pic, thumb, key); return; }
            seasons[season].Asked.Remove(number);   // перелистнули: спросим снова, когда страница вернётся
        }

        // Запрос адреса кадра в фоне (не больше 3 одновременно); null — кадра нет, ошибки глотаем.
        void Fetch(int n, Func<Task<string>> find, Action<string> done)
        {
            Task.Run(async () =>
            {
                await thumbSlots.WaitAsync().ConfigureAwait(false);
                string thumb = null;
                try { if (n == generation) thumb = await find().ConfigureAwait(false); }
                catch (Exception) { }
                finally { thumbSlots.Release(); }
                if (thumb != null) App.Ui(() => { if (alive && n == generation) done(thumb); });
            });
        }


        // ---- раскладка ----

        void BuildSide()
        {
            foreach (var o in side) ui.Remove(o);
            side.Clear();
            for (var i = 0; i < seasons.Count; i++)
            {
                var s = seasons[i];
                var y = SideY + i * (ItemH + ItemGap);
                var focused = area == Area.Side && i == sideFocus;
                var current = i == season;
                if (focused || current) side.Add(ui.RoundRect(SideX, y, SideW, ItemH, 18, focused ? Theme.Text : Theme.Card));
                var main = focused ? Theme.BgHex : current ? Theme.TextHex : Theme.MutedHex;
                var dim = focused ? "#3a3e48" : "#8a909b";
                var name = seasons.Count > 1 ? RuText.SeasonNo(s.Season.Order) : RuText.Episodes;
                var count = s.Info != null ? RuText.EpisodesCount(s.Info.Episodes.Count) : s.Season.Title;
                var l1 = ui.Text(SideX + 24, y + 8, SideW - 40, 36, Theme.T(name, 25, main, true));
                var l2 = ui.Text(SideX + 24, y + 42, SideW - 40, 28, Theme.T(count ?? "", 18, dim));
                l1.IsEllipsis = l2.IsEllipsis = true;
                side.Add(l1);
                side.Add(l2);
            }
        }

        void ClearPage()
        {
            foreach (var o in page.Concat(tabs)) ui.Remove(o);
            page.Clear();
            tabs.Clear();
            pics.Clear();
            ui.ShowFocus(frame, false);
        }

        void BuildPage()
        {
            ClearPage();
            var eps = Episodes;
            var pages = (eps.Count + PerPage - 1) / PerPage;
            if (pages > 1)
            {
                var x = GridX;
                for (var p = 0; p < pages; p++)
                {
                    var from = eps[p * PerPage].Number;
                    var to = eps[Math.Min(eps.Count, (p + 1) * PerPage) - 1].Number;
                    var label = from == to ? from.ToString() : from + "–" + to;
                    var w = 40 + 12 * label.Length;
                    tabs.Add(ui.RoundRect(x, TabsY, w, 42, 21, p == pageNo ? Theme.Card2 : Theme.Card));
                    tabs.Add(ui.Text(x, TabsY + 7, w, 30, Theme.T(label, 20, p == pageNo ? Theme.TextHex : Theme.MutedHex, true, "center")));
                    x += w + 10;
                }
            }
            var current = CurrentEpisode;
            var start = pageNo * PerPage;
            for (var i = start; i < Math.Min(eps.Count, start + PerPage); i++)
            {
                var e = eps[i];
                var x = GridX + (i - start) % Cols * (TileW + Gap);
                var y = GridY + (i - start) / Cols * Pitch;
                var upcoming = Upcoming(seasons[season], e.Number);
                var isCurrent = e.Number == current && !e.Watched && upcoming == null;
                if (isCurrent) page.Add(ui.RoundRect(x - 4, y - 4, TileW + 8, TileH + 8, Radius + 4, Theme.Accent));
                page.Add(ui.RoundRect(x, y, TileW, TileH, Radius, isCurrent ? CurrentBack : Theme.Card));
                var pic = ui.Picture(x, y, TileW, TileH);
                page.Add(pic);
                var picIndex = pics.Count;
                pics.Add((pic, e, x, y, false));
                if (e.Watched || upcoming != null) page.Add(ui.Rect(x, y, TileW, TileH, Dim));
                var num = e.Number.ToString();
                var nw = 36 + 26 * num.Length;
                page.Add(ui.RoundRect(x + 12, y + TileH - 12 - 54, nw, 54, 12, Shade));
                page.Add(ui.Text(x + 12, y + TileH - 12 - 52, nw, 50, Theme.T(num, 36, e.Watched ? "#8a909b" : Theme.TextHex, true, "center")));
                string label = null, color = "#8a909b";
                if (upcoming != null) label = Release.Label(upcoming.Value);
                else if (e.Watched) label = RuText.EpisodeWatched;
                else if (isCurrent)
                {
                    var pos = Position(e.Number);
                    label = pos > 0 ? RuText.NowWatching + " · " + RuText.Time(pos) : RuText.NowWatching;
                    color = Theme.AccentHex;
                    var dur = siteResume != null && siteResume.AnimeId == seasons[season].Season.AnimeId && siteResume.EpisodeNumber == e.Number ? siteResume.DurationSec : 0;
                    if (pos > 0 && dur > 0)
                    {
                        page.Add(ui.Rect(x, y + TileH - 6, TileW, 6, Track));
                        page.Add(ui.Rect(x, y + TileH - 6, Math.Max(1, TileW * Math.Min(pos, dur) / dur), 6, Theme.Accent));
                    }
                }
                if (label != null)
                {
                    pics[picIndex] = (pic, e, x, y, true);
                    var l = ui.Text(x, y + TileH + 12, TileW, 34, Theme.T(label, 22, color, true));
                    l.IsEllipsis = true;
                    page.Add(l);
                }
            }
            UpdateFocus();
            RequestThumbs();
            Toast.Raise();
        }

        // Серия «где остановились»: в текущем тайтле — resumeEp; в других сезонах — первая непросмотренная.
        int CurrentEpisode
        {
            get
            {
                var s = seasons[season];
                if (s.Season.AnimeId == animeId && resumeEp > 0) return resumeEp;
                return s.Info?.Episodes.Where(e => !e.Watched).Select(e => e.Number).DefaultIfEmpty(0).Min() ?? 0;
            }
        }

        // Место остановки (с): с сайта (карточка «Продолжить»), иначе из «виденного» на ТВ.
        int Position(int number)
        {
            var id = seasons[season].Season.AnimeId;
            if (siteResume != null && siteResume.AnimeId == id && siteResume.EpisodeNumber == number) return siteResume.PositionSec;
            var seen = app.Prefs.Seen(app.UserKey, id);
            return seen != null && seen.Episode == number ? seen.Seconds : 0;
        }

        void UpdateSub()
        {
            var s = seasons[season];
            var eps = s.Info?.Episodes;
            var text = eps == null ? RuText.Episodes : RuText.Episodes + " · " + RuText.WatchedOf(eps.Count(e => e.Watched), eps.Count);
            sub.Text = Theme.T(text, 22, Theme.MutedHex);
        }

        void UpdateFocus()
        {
            var eps = Episodes;
            var on = area == Area.Grid && eps.Count > 0;
            if (on)
            {
                var i = focus - pageNo * PerPage;
                ui.MoveFocus(frame, GridX + i % Cols * (TileW + Gap), GridY + i / Cols * Pitch, TileW, TileH, Radius);
            }
            ui.ShowFocus(frame, on);
        }

        void SetStatus(string text)
        {
            if (text == null) { status.Hide(); return; }
            status.Text = Theme.T(text, 28, Theme.MutedHex);
            status.Show();
        }

        // ---- клавиши ----

        public bool OnKey(string key)
        {
            if (key == Keys.Back) { app.Back(); return true; }
            return area == Area.Side ? SideKey(key) : GridKey(key);
        }

        bool GridKey(string key)
        {
            var eps = Episodes;
            if (eps.Count == 0)
            {
                if (key == Keys.Left || key == Keys.Ok) SetArea(Area.Side);
                return true;
            }
            var col = (focus - pageNo * PerPage) % Cols;
            switch (key)
            {
                case Keys.Left:
                    if (col == 0) SetArea(Area.Side); else MoveTo(focus - 1);
                    return true;
                case Keys.Right: if (col < Cols - 1 && focus + 1 < eps.Count) MoveTo(focus + 1); return true;
                case Keys.Up: if (focus - Cols >= 0) MoveTo(focus - Cols); return true;
                case Keys.Down:
                    if (focus + Cols < eps.Count) MoveTo(focus + Cols);
                    else if ((focus / Cols + 1) * Cols < eps.Count) MoveTo(eps.Count - 1);   // короткая последняя строка
                    return true;
                case Keys.Ok: Play(eps[focus]); return true;
            }
            return false;
        }

        void MoveTo(int i)
        {
            focus = i;
            var p = i / PerPage;
            if (p != pageNo) { pageNo = p; BuildPage(); }
            else UpdateFocus();
        }

        bool SideKey(string key)
        {
            switch (key)
            {
                case Keys.Up: case Keys.Down:
                    var i = Math.Max(0, Math.Min(sideFocus + (key == Keys.Up ? -1 : 1), seasons.Count - 1));
                    if (i == sideFocus) return true;
                    sideFocus = i;
                    BuildSide();
                    sideTimer.Start(SideDelay, () => { if (alive && sideFocus != season) { OpenSeason(sideFocus, 0); BuildSide(); } });
                    return true;
                case Keys.Right: case Keys.Ok:
                    sideTimer.Stop();
                    if (sideFocus != season) OpenSeason(sideFocus, 0);
                    else if (seasons[season].Failed) OpenSeason(season, 0);   // после ошибки — повторить
                    SetArea(Area.Grid);
                    return true;
                case Keys.Left: return true;
            }
            return false;
        }

        void SetArea(Area a)
        {
            area = a;
            if (a == Area.Side) sideFocus = season;
            BuildSide();
            UpdateFocus();
        }

        void Play(Episode e)
        {
            var s = seasons[season];
            var upcoming = Upcoming(s, e.Number);
            if (upcoming != null)
            {
                Toast.Show(ui, string.IsNullOrEmpty(upcoming.Value.When) ? RuText.NotReleased : RuText.EpisodeAirs(e.Number, upcoming.Value.When));
                return;
            }
            var pos = Position(e.Number);
            DevLog.Write("open: anime " + s.Season.AnimeId + " ep " + e.Number + " pos " + pos + " (экран серий)");
            var id = s.Season.AnimeId;
            var number = e.Number;
            app.Open(new PlayerScreen(app, id, s.Url, number, pos),
                () => new EpisodesScreen(app, animeId, url, anime, resumeEp, siteResume, id, number));
        }
    }
}
