using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Кадры серий одного тайтла — для плиток на экране серий и в панели плеера. Адрес кадра: titleId CVH из источника
    // любой серии → плейлист → vkId каждой серии → thumbUrl (запрос на серию). Серии без CVH — источники самой серии:
    // есть AniBoom — его poster; нет ни одного видео — «ещё не вышла». Карта плейлиста лежит в App.Cache (30 мин),
    // сами картинки — в ImageCache по ключу без токена. Адресов узнаём не больше 3 одновременно. Всё — с UI-потока.
    sealed class EpisodeThumbs
    {
        const int MapFreshSec = 1800;
        static readonly SemaphoreSlim slots = new SemaphoreSlim(3);   // на всё приложение: экран серий и панель вместе

        sealed class Map
        {
            public Dictionary<int, string> Cvh;   // серия → vkId; null — CVH нет
            public string Aniboom;                 // адрес плеера AniBoom; null — нет
        }

        readonly App app;
        readonly int animeId;
        readonly string main;   // ключ основной озвучки — её кадры предпочитаем
        Map map;
        bool asked;
        readonly Dictionary<int, (string Url, string Key)> known = new Dictionary<int, (string, string)>();
        readonly HashSet<int> pending = new HashSet<int>(), unreleased = new HashSet<int>();
        bool alive = true;

        // Кадр серии number узнали (или выяснили, что она не вышла); 0 — карта пришла, перерисовать все плитки.
        public Action<int> Changed;

        public EpisodeThumbs(App app, int animeId, string main)
        {
            this.app = app;
            this.animeId = animeId;
            this.main = main;
        }

        public void Stop() => alive = false;

        // У серии на сайте нет ни одного видео.
        public bool IsUnreleased(int number) => unreleased.Contains(number);

        // Поставить кадр серии в картинку. true — поставлен (из кеша или по известному адресу); false — адреса ещё нет:
        // запрошен, по ответу придёт Changed, и плитку надо попросить снова.
        public bool Set(Image pic, Episode ep)
        {
            if (unreleased.Contains(ep.Number)) return false;
            if (map == null) { LoadMap(ep); return false; }
            if (known.TryGetValue(ep.Number, out var k)) { app.Images.Set(pic, k.Url, k.Key); return true; }
            if (map.Cvh != null && map.Cvh.TryGetValue(ep.Number, out var vk))
            {
                var key = "cvh-thumb-" + vk;
                if (app.Images.TrySetCached(pic, key)) return true;
                Fetch(ep.Number, key, () => app.Api.CvhThumbAsync(vk));
                return false;
            }
            // Без CVH: кадры со страницы плеера Kodik (настоящие кадры серии), иначе poster AniBoom (у части тайтлов
            // это заглушка «IMAGE NOT FOUND» — её отбрасывает ImageCache).
            var epKey = "ep-thumb-" + animeId + "-" + ep.Number;
            if (app.Images.TrySetCached(pic, epKey)) return true;
            Fetch(ep.Number, epKey, async () =>
            {
                var v = await app.EpisodeVideosAsync(ep.Id).ConfigureAwait(false);
                if (v.Sources.Count == 0) return "";   // «ещё не вышла»
                var kodik = Preferred(v, "kodik");
                var thumb = kodik == null ? null : await app.Api.KodikThumbAsync(kodik).ConfigureAwait(false);
                if (thumb != null) return thumb;
                var boom = Preferred(v, "aniboom");
                return boom == null ? null : await app.Api.AniboomPosterAsync(boom).ConfigureAwait(false);
            });
            return false;
        }

        // Адрес плеера провайдера, предпочитая основную озвучку; null — такого источника нет.
        string Preferred(EpisodeVideos v, string provider) =>
            v.Sources.Where(x => x.Provider == provider).OrderBy(x => x.TranslationKey == main ? 0 : 1)
                .Select(x => x.PlayerUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));

        // Карта плейлиста CVH (и адрес AniBoom) по источникам серии ep — один раз на тайтл.
        void LoadMap(Episode ep)
        {
            if (asked) return;
            asked = true;
            var key = "cvh-videos:" + animeId + ":" + main;
            if (app.Cache.TryGet(key, out Map cached, out var age) && age < MapFreshSec)
            {
                map = cached;
                App.Ui(() => { if (alive) Changed?.Invoke(0); });   // не из Set: вызывающий ещё строит плитки
                return;
            }
            Task.Run(async () =>
            {
                var m = new Map();
                try
                {
                    var v = await app.EpisodeVideosAsync(ep.Id).ConfigureAwait(false);
                    m.Aniboom = v.Sources.Where(x => x.Provider == "aniboom").OrderBy(x => x.TranslationKey == main ? 0 : 1)
                        .Select(x => x.PlayerUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
                    var cvh = v.Sources.Where(x => x.Provider == "cvh").OrderBy(x => x.TranslationKey == main ? 0 : 1)
                        .Select(x => Cvh.ParseIframeUrl(x.PlayerUrl)).FirstOrDefault(x => x != null);
                    if (cvh != null)
                    {
                        var (titleId, voice, cvhSeason, _) = cvh.Value;
                        m.Cvh = Cvh.EpisodeVideos(await app.Api.CvhPlaylistAsync(titleId).ConfigureAwait(false), cvhSeason, voice);
                    }
                }
                catch (Exception e) { DevLog.Write("thumbs: карта " + animeId + " — " + PlayerSources.Why(e)); }
                App.Ui(() =>
                {
                    if (m.Cvh != null || m.Aniboom != null) app.Cache.Put(key, m);
                    DevLog.Write("thumbs: " + animeId + " кадров CVH " + (m.Cvh?.Count ?? 0) + (m.Aniboom != null ? ", есть AniBoom" : ""));
                    map = m;
                    if (alive) Changed?.Invoke(0);
                });
            });
        }

        // Адрес кадра серии в фоне (не больше 3 одновременно). "" — серия не вышла, null — кадра нет, ошибки глотаем.
        void Fetch(int number, string key, Func<Task<string>> find)
        {
            if (!pending.Add(number)) return;
            Task.Run(async () =>
            {
                await slots.WaitAsync().ConfigureAwait(false);
                string url = null;
                try { if (alive) url = await find().ConfigureAwait(false); }
                catch (Exception) { }
                finally { slots.Release(); }
                App.Ui(() =>
                {
                    pending.Remove(number);
                    if (!alive || url == null) return;
                    if (url.Length == 0) unreleased.Add(number); else known[number] = (url, key);
                    Changed?.Invoke(number);
                });
            });
        }
    }
}
