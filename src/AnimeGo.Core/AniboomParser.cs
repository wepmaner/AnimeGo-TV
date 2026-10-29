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

        // Кадр серии из data-parameters (poster): mqdefault.webp → hqdefault (крупнее, отдаётся JPEG). null — нет.
        public static string ParsePoster(string embedHtml)
        {
            var tag = Html.TagsWithAttr(embedHtml ?? "", null, "data-parameters").FirstOrDefault();
            var raw = tag == null ? null : Html.Attr(tag, "data-parameters");
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                var poster = Json.Parse<AniboomParamsDto>(raw)?.Poster;
                return string.IsNullOrEmpty(poster) ? null : poster.Replace("/mqdefault.", "/hqdefault.");
            }
            catch (Exception) { return null; }   // тип ошибки разбора зависит от рантайма
        }

        // Адрес встраивания той же озвучки для серии n: меняем episode= в адресе.
        public static string EpisodeUrl(string playerUrl, int n) =>
            System.Text.RegularExpressions.Regex.Replace(playerUrl ?? "", @"([?&])episode=\d+", "${1}episode=" + n);

        public static string EmbedUrl(string playerUrl) =>
            playerUrl != null && playerUrl.StartsWith("//", StringComparison.Ordinal) ? "https:" + playerUrl : playerUrl;
    }
}
