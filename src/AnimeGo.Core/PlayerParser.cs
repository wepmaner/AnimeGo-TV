using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Ответы /player/{id}: серии, озвучки и источники из HTML в data.content.
    public static class PlayerParser
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex QuotedRx = new Regex(@"""([^""]*)""", Opts);
        static readonly Regex ErrorRx = new Regex(@"\(ошибка\)", Opts);
        static readonly Regex ContinuationRx = new Regex(@"""item""\s*:\s*\{[^{}]*?""episode""\s*:\s*(\d+)", Opts);

        public static PlayerInfo Parse(string json)
        {
            var html = Content(json).Content ?? "";
            var root = Html.TagsWithAttr(html, null, "data-anime-player-anime-id-value").FirstOrDefault();
            return new PlayerInfo
            {
                AnimeId = Int(Html.Attr(root, "data-anime-player-anime-id-value")),
                Episodes = Episodes(html),
                Translations = Translations(html),
                Sources = Sources(html),
                EpisodesTotal = Int(Html.Attr(root, "data-anime-player-episodes-total-value")),
                ProviderPriority = Priority(Html.Attr(root, "data-anime-player-initial-provider-priority-value")),
                ContinuationUrl = Html.Attr(root, "data-anime-player-continuation-url-value"),
                EpisodesUrl = Html.Attr(root, "data-anime-player-episodes-url-value"),
            };
        }

        public static EpisodeVideos ParseVideos(string json)
        {
            var data = Content(json);
            return new EpisodeVideos
            {
                IsWatched = data.IsWatched,
                NumVideos = data.NumVideos ?? 0,
                Sources = Sources(data.Content ?? ""),
            };
        }

        // GET /player/{id}/continuation/1: {"item":{"episode":N,…}} — с какой серии продолжать. null — сайт не знает
        // (item пустой/null, ответ не JSON). Регуляркой: пустой item PHP отдаёт как [], DataContract на нём падает.
        public static int? ParseContinuation(string json)
        {
            var m = ContinuationRx.Match(json ?? "");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : (int?)null;
        }

        static AjaxDataDto Content(string json)
        {
            AjaxDto dto;
            try { dto = Json.Parse<AjaxDto>(json); }
            catch (Exception e) { throw new ApiException(ApiErrorKind.Parse, "Не удалось разобрать ответ плеера", e); }
            if (dto == null || dto.Status != "success" || dto.Data == null)
                throw new ApiException(ApiErrorKind.Parse, "Плеер вернул ошибку: " + (dto?.Message ?? "нет данных"));
            return dto.Data;
        }

        static IReadOnlyList<Episode> Episodes(string html)
        {
            var list = new List<Episode>();
            foreach (var tag in Html.TagsWithAttr(html, null, "data-episode"))
            {
                if (!int.TryParse(Html.Attr(tag, "data-episode"), NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
                list.Add(new Episode
                {
                    Id = id,
                    Number = Int(Html.Attr(tag, "data-episode-number")),
                    Watched = Html.Attr(tag, "data-episode-watched") == "true",
                    Page = Int(Html.Attr(tag, "data-episode-page")),
                });
            }
            return list;
        }

        static IReadOnlyList<Translation> Translations(string html)
        {
            var list = new List<Translation>();
            foreach (var block in Html.BlocksWithAttr(html, "button", "data-anime-player-target", "translation"))
            {
                var tag = Html.TagsWithAttr(block, "button", "data-anime-player-target", "translation").First();
                var key = Html.Attr(tag, "data-translation");
                if (string.IsNullOrEmpty(key)) continue;
                var title = ErrorRx.Replace(Html.Text(block), "").Trim();
                list.Add(new Translation
                {
                    Key = key,
                    Id = NullableInt(Html.Attr(tag, "data-translation-id")),
                    Title = title,
                    Kind = KindOf(title),
                });
            }
            return list;
        }

        static IReadOnlyList<VideoSource> Sources(string html)
        {
            var list = new List<VideoSource>();
            foreach (var tag in Html.TagsWithAttr(html, "button", "data-anime-player-target", "provider"))
            {
                var url = Html.Attr(tag, "data-player");
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
                list.Add(new VideoSource
                {
                    Provider = Html.Attr(tag, "data-provider-slug"),
                    PlayerUrl = url,
                    TranslationKey = Html.Attr(tag, "data-ptranslation"),
                    TranslationId = NullableInt(Html.Attr(tag, "data-translation-id")),
                    TranslationTitle = Html.Attr(tag, "data-translation-title"),
                });
            }
            return list;
        }

        static IReadOnlyList<string> Priority(string value)
        {
            var list = new List<string>();
            foreach (Match m in QuotedRx.Matches(Html.Decode(value ?? ""))) list.Add(m.Groups[1].Value);
            return list;
        }

        static string KindOf(string text)
        {
            if (text.IndexOf("Дубляж", StringComparison.OrdinalIgnoreCase) >= 0) return "Дубляж";
            if (text.IndexOf("Многоголос", StringComparison.OrdinalIgnoreCase) >= 0) return "Многоголосый";
            if (text.IndexOf("Субтитр", StringComparison.OrdinalIgnoreCase) >= 0) return "Субтитры";
            return "";
        }

        static int Int(string s) => NullableInt(s) ?? 0;

        static int? NullableInt(string s) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
    }
}
