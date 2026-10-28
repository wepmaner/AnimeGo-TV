using System;
using System.Linq;
using System.Runtime.Serialization;

namespace AnimeGo.Core
{
    // Страница-встраивание AniBoom: поток лежит в data-parameters (JSON в HTML-entities).
    public static class AniboomParser
    {
        public static StreamSet Parse(string embedHtml)
        {
            var tag = Html.TagsWithAttr(embedHtml ?? "", null, "data-parameters").FirstOrDefault();
            var raw = tag == null ? null : Html.Attr(tag, "data-parameters");
            if (string.IsNullOrEmpty(raw))
                throw new ApiException(ApiErrorKind.Parse, "В плеере AniBoom нет data-parameters");
            AniboomParamsDto p;
            AniboomSrcDto hls = null;
            try
            {
                p = Json.Parse<AniboomParamsDto>(raw);
                if (!string.IsNullOrEmpty(p?.Hls)) hls = Json.Parse<AniboomSrcDto>(p.Hls);
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException)
            {
                throw new ApiException(ApiErrorKind.Parse, "Не удалось разобрать data-parameters AniBoom", e);
            }
            if (string.IsNullOrEmpty(hls?.Src))
                throw new ApiException(ApiErrorKind.Parse, "В плеере AniBoom нет hls");
            return new StreamSet
            {
                Streams = new[] { new Stream { Url = hls.Src, Quality = "auto", IsHls = true } },
                DurationSec = p.Duration ?? 0
            };
        }

        public static string EmbedUrl(string playerUrl) =>
            playerUrl != null && playerUrl.StartsWith("//", StringComparison.Ordinal) ? "https:" + playerUrl : playerUrl;
    }
}
