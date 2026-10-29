using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AnimeGo.Core
{
    // Запросы к видео-CDN: свои адреса, но те же заголовки и ошибки.
    public sealed partial class SiteApi
    {
        public async Task<StreamSet> CvhStreamsAsync(VideoSource s, int season, int episode)
        {
            var iframe = Cvh.ParseIframeUrl(s.PlayerUrl)
                ?? throw new ApiException(ApiErrorKind.Parse, "Не разобрали адрес плеера CVH");
            var playlist = Cvh.ParsePlaylist(await GetAjaxAsync(Cvh.PlaylistUrl(iframe.titleId)).ConfigureAwait(false));
            var item = Cvh.FindItem(playlist, iframe.voice, season, episode)
                ?? throw new ApiException(ApiErrorKind.Server, "Нет видео в CVH");
            return Cvh.ParseVideo(await GetAjaxAsync(Cvh.VideoUrl(item.VkId)).ConfigureAwait(false));
        }

        public async Task<CvhPlaylistDto> CvhPlaylistAsync(int titleId) =>
            Cvh.ParsePlaylist(await GetAjaxAsync(Cvh.PlaylistUrl(titleId)).ConfigureAwait(false));

        // Кадр серии или null (нет кадра, нет связи): картинка необязательна.
        public async Task<string> CvhThumbAsync(string vkId)
        {
            try { return Cvh.ParseThumb(await GetAjaxAsync(Cvh.VideoUrl(vkId)).ConfigureAwait(false)); }
            catch (ApiException) { return null; }
        }

        // Кадр серии AniBoom или null.
        public async Task<string> AniboomPosterAsync(string playerUrl)
        {
            try { return AniboomParser.ParsePoster(await GetHtmlAsync(AniboomParser.EmbedUrl(playerUrl)).ConfigureAwait(false)); }
            catch (ApiException) { return null; }
        }

        public async Task<StreamSet> AniboomStreamsAsync(VideoSource s) =>
            AniboomParser.Parse(await GetHtmlAsync(AniboomParser.EmbedUrl(s.PlayerUrl)).ConfigureAwait(false));

        // Эндпоинт Kodik за ссылками: найденный в коде плеера после 404 на прежнем. Один на приложение.
        static string kodikEndpoint = Kodik.DefaultEndpoint;

        public async Task<StreamSet> KodikStreamsAsync(VideoSource s)
        {
            if (Kodik.ParseEmbed(s.PlayerUrl) == null) throw new ApiException(ApiErrorKind.Parse, "Не разобрали адрес плеера Kodik");
            var origin = "https://" + new Uri(s.PlayerUrl).Host;
            var html = await GetHtmlAsync(s.PlayerUrl).ConfigureAwait(false);
            var form = Kodik.Form(html).ToList();
            var endpoint = kodikEndpoint;
            try { return Kodik.ParseLinks(await PostFormAsync(origin + endpoint, form).ConfigureAwait(false)); }
            catch (ApiException e) when (e.Kind == ApiErrorKind.Server)
            {
                // Kodik иногда переименовывает эндпоинт: берём новый из кода плеера и пробуем ещё раз.
                var js = Kodik.PlayerJsPath(html);
                var found = js == null ? null : Kodik.ParseEndpoint(await GetHtmlAsync(origin + js).ConfigureAwait(false));
                if (found == null || found == endpoint) throw;
                kodikEndpoint = found;
                return Kodik.ParseLinks(await PostFormAsync(origin + found, form).ConfigureAwait(false));
            }
        }

        Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form) =>
            SendAsync(HttpMethod.Post, url, new FormUrlEncodedContent(form), true);

        // JSON aniskip для серии или null: нет таймингов (сервис отвечает 404), нет связи — плеер обойдётся без них.
        public async Task<string> AniskipAsync(int malId, int episode)
        {
            try { return await GetAjaxAsync(Aniskip.Url(malId, episode)).ConfigureAwait(false); }
            catch (ApiException) { return null; }
        }
    }
}
