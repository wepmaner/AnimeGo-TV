using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Сезон в панели плеера: тайтл сайта и его серии (грузятся, когда ряд впервые виден).
    sealed class SeasonRow
    {
        public Season Season;
        public string Url;
        public PlayerInfo Info;     // null — ещё не загружены
        public bool Loading, Failed;
        public int Focus = -1;      // −1 — ещё не ставили
        public int First;           // первая видимая плитка
        public EpisodeThumbs Thumbs;   // кадры серий для плиток
    }

    // Сезоны тайтла для панели плеера: страница тайтла → «Связанное» → Seasons.Build. Страницу не разобрали —
    // один ряд с текущим тайтлом. Серии текущего — из PlayerInfo экрана (SetInfo), остальных — лениво.
    sealed class SeasonRows
    {
        readonly App app;
        readonly int animeId;
        readonly string url;
        readonly EpisodePanel panel;
        PlayerInfo info;
        bool alive = true;

        public List<SeasonRow> List { get; private set; } = new List<SeasonRow>();
        public AnimeInfo Anime { get; private set; }
        public Action Loaded;

        public SeasonRows(App app, int animeId, string url, EpisodePanel panel)
        {
            this.app = app;
            this.animeId = animeId;
            this.url = url;
            this.panel = panel;
            panel.NeedInfo = LoadRow;
        }

        public SeasonRow Current => List.FirstOrDefault(r => r.Season.AnimeId == animeId);

        public void Stop()
        {
            alive = false;
            foreach (var r in List) r.Thumbs?.Stop();
        }

        public void SetInfo(PlayerInfo pi)
        {
            info = pi;
            var row = Current;
            if (row != null) { row.Info = pi; panel.RowLoaded(row); }
        }

        public void Load()
        {
            Task.Run(async () =>
            {
                AnimeInfo a = null;
                try { a = await app.AnimeInfoAsync(url, animeId).ConfigureAwait(false); }   // страница тайтла обычно уже в кеше
                catch (Exception e) { DevLog.Write("player: страница тайтла " + animeId + " ошибка " + PlayerSources.Why(e)); }
                App.Ui(() =>
                {
                    if (!alive) return;
                    Anime = a;
                    List = Build(a);
                    DevLog.Write("player: сезонов " + List.Count);
                    panel.SetSeasons(List);
                    Loaded?.Invoke();
                });
            });
        }

        // Текущий тайтл первым, затем остальные сезоны по порядку.
        List<SeasonRow> Build(AnimeInfo a)
        {
            var seasons = a != null ? Seasons.Build(a) : new[] { new Season { AnimeId = animeId, Title = "", Order = 1, IsCurrent = true } };
            var urls = new Dictionary<int, string>();
            foreach (var r in a?.Related ?? new RelatedTitle[0]) urls[r.Id] = "/anime/" + r.Slug + "-" + r.Id;
            var rows = seasons.OrderBy(s => s.IsCurrent ? 0 : 1).Select(s => new SeasonRow
            {
                Season = s,
                Url = s.IsCurrent ? url : urls.TryGetValue(s.AnimeId, out var u) ? u : null,
                Info = s.IsCurrent ? info : null,
            }).Where(r => r.Url != null).ToList();
            foreach (var r in rows)
            {
                var row = r;
                var main = app.Prefs.Dub(app.UserKey, r.Season.AnimeId).Main ?? app.Prefs.Dub(app.UserKey, animeId).Main;
                r.Thumbs = new EpisodeThumbs(app, r.Season.AnimeId, main) { Changed = n => panel.ThumbChanged(row, n) };
            }
            return rows;
        }

        void LoadRow(SeasonRow r)
        {
            if (r.Season.AnimeId == animeId) return;   // текущий заполнит экран через SetInfo
            r.Loading = true;
            Task.Run(async () =>
            {
                PlayerInfo pi = null;
                try { pi = await app.PlayerInfoAsync(r.Season.AnimeId).ConfigureAwait(false); }
                catch (Exception e) { DevLog.Write("player: сезон " + r.Season.AnimeId + " ошибка " + PlayerSources.Why(e)); }
                App.Ui(() =>
                {
                    if (!alive) return;
                    r.Loading = false;
                    r.Info = pi;
                    r.Failed = pi == null;
                    if (pi != null) DevLog.Write("player: сезон " + r.Season.AnimeId + " серий " + pi.Episodes.Count);
                    panel.RowLoaded(r);
                });
            });
        }
    }
}
