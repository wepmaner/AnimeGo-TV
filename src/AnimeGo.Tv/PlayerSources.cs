using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Источники видео серии (спека §7): у выбранной озвучки — cvh, затем aniboom; kodik не играем.
    static class PlayerSources
    {
        public static bool Playable(VideoSource s) => s.Provider == "cvh" || s.Provider == "aniboom";

        public static List<VideoSource> For(string key, IReadOnlyList<VideoSource> all) =>
            all.Where(s => s.TranslationKey == key && Playable(s)).OrderBy(s => s.Provider == "cvh" ? 0 : 1).ToList();

        // Название озвучки: из списка озвучек плеера, иначе из источника, иначе сам ключ.
        public static string Title(string key, PlayerInfo info, EpisodeVideos v) =>
            info?.Translations.FirstOrDefault(t => t.Key == key)?.Title
            ?? v?.Sources.FirstOrDefault(s => s.TranslationKey == key)?.TranslationTitle ?? key;

        // Ссылки CVH подписаны по IP и времени: запрашивать прямо перед воспроизведением, не кешировать.
        public static async Task<StreamSet> FetchAsync(SiteApi api, VideoSource src)
        {
            if (src.Provider != "cvh") return await api.AniboomStreamsAsync(src).ConfigureAwait(false);
            var ifr = Cvh.ParseIframeUrl(src.PlayerUrl) ?? throw new ApiException(ApiErrorKind.Parse, "адрес CVH");
            return await api.CvhStreamsAsync(src, ifr.season, ifr.episode).ConfigureAwait(false);
        }

        public static string Why(Exception e) => e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name + " " + e.Message;
    }
}
