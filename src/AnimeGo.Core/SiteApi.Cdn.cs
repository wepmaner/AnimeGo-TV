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

        public async Task<StreamSet> AniboomStreamsAsync(VideoSource s) =>
            AniboomParser.Parse(await GetHtmlAsync(AniboomParser.EmbedUrl(s.PlayerUrl)).ConfigureAwait(false));
    }
}
