using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // CVH (cdnvideohub): чистая часть — адреса и разбор ответов, без сети.
    public static class Cvh
    {
        static readonly Regex IframeRx = new Regex(
            @"/cdn-iframe/(\d+)/([^/?#]+)/(\d+)/(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static (int titleId, string voice, int season, int episode)? ParseIframeUrl(string playerUrl)
        {
            var m = IframeRx.Match(playerUrl ?? "");
            if (!m.Success) return null;
            string voice;
            try { voice = Uri.UnescapeDataString(m.Groups[2].Value); }
            catch (UriFormatException) { voice = m.Groups[2].Value; }
            return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), voice,
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        public static string PlaylistUrl(int titleId) =>
            "https://plapi.cdnvideohub.com/api/v1/player/sv/playlist?pub=747&aggr=mali&id=" +
            titleId.ToString(CultureInfo.InvariantCulture);

        public static string VideoUrl(string vkId) =>
            "https://plapi.cdnvideohub.com/api/v1/player/sv/video/" + vkId;

        public static CvhPlaylistDto ParsePlaylist(string json)
        {
            var p = ParseJson<CvhPlaylistDto>(json, "плейлист CVH");
            if (p.Items == null) p.Items = new List<CvhItemDto>();
            return p;
        }

        public static CvhItemDto FindItem(CvhPlaylistDto p, string voice, int season, int episode)
        {
            var want = Norm(voice);
            return p.Items.FirstOrDefault(i =>
                i.Season == season && i.Episode == episode && Norm(i.VoiceStudio) == want);
        }

        public static StreamSet ParseVideo(string json)
        {
            var v = ParseJson<CvhVideoDto>(json, "видео CVH");
            var src = v.Sources ?? new CvhSourcesDto();
            var list = new List<Stream>();
            Add(list, src.HlsUrl, "auto", true);
            Add(list, src.MpegFullHdUrl, "1080p", false);
            Add(list, src.MpegHighUrl, "720p", false);
            Add(list, src.MpegMediumUrl, "480p", false);
            Add(list, src.MpegLowUrl, "360p", false);
            Add(list, src.MpegLowestUrl, "240p", false);
            return new StreamSet { Streams = list, DurationSec = v.Duration ?? 0 };
        }

        static void Add(List<Stream> list, string url, string quality, bool hls)
        {
            if (!string.IsNullOrEmpty(url)) list.Add(new Stream { Url = url, Quality = quality, IsHls = hls });
        }

        static string Norm(string s) =>
            new string((s ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

        static T ParseJson<T>(string json, string what) where T : class
        {
            try
            {
                var r = Json.Parse<T>(json ?? "");
                if (r != null) return r;
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException)
            {
                throw new ApiException(ApiErrorKind.Parse, "Не удалось разобрать " + what, e);
            }
            throw new ApiException(ApiErrorKind.Parse, "Пустой ответ: " + what);
        }
    }
}
