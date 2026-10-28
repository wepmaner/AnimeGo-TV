using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Страница тайтла: заголовок, описание, постер, жанры, поля информации и блок «Связанное» (.seasons-item).
    public static class AnimeParser
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex H1Rx = new Regex(@"<h1\b[^>]*>(.*?)</h1\s*>", Opts);
        static readonly Regex LiRx = new Regex(@"<li\b[^>]*>(.*?)</li\s*>", Opts);
        static readonly Regex SpanRx = new Regex(@"<span\b[^>]*>(.*?)</span\s*>", Opts);
        static readonly Regex HrefRx = new Regex(@"^/anime/(.+)-(\d+)$", Opts);
        static readonly Regex YearRx = new Regex(@"\b(?:19|20)\d{2}\b", Opts);

        public static AnimeInfo Parse(string html, int id)
        {
            var h1 = H1Rx.Match(html ?? "");
            if (!h1.Success) throw new ApiException(ApiErrorKind.Parse, "На странице тайтла нет заголовка");

            var info = Html.Blocks(html, "div", "entity__info").FirstOrDefault() ?? "";
            var year = YearRx.Match(Field(info, "Выпуск") ?? "");
            return new AnimeInfo
            {
                Id = id,
                Title = Html.Text(h1.Groups[1].Value),
                TitleOriginal = Original(html),
                Description = Html.Text(Html.Blocks(html, "div", "description").FirstOrDefault()),
                PosterUrl = Poster(html),
                Genres = Genres(info),
                Kind = Field(info, "Тип"),
                Year = year.Success ? year.Value : null,
                Related = Related(html),
            };
        }

        static string Original(string html)
        {
            var li = LiRx.Match(Html.Blocks(html, "div", "entity__title-synonyms").FirstOrDefault() ?? "");
            var t = li.Success ? Html.Text(li.Groups[1].Value) : "";
            return t.Length == 0 ? null : t;
        }

        // Крупный постер из блока .entity__poster; запасной вариант — og:image.
        static string Poster(string html)
        {
            var img = Html.Tags(Html.Blocks(html, "div", "entity__poster").FirstOrDefault(), "img", "image__img").FirstOrDefault();
            var src = Html.Attr(img, "src");
            if (!string.IsNullOrEmpty(src)) return src;
            var meta = Html.TagsWithAttr(html, "meta", "property", "og:image").FirstOrDefault();
            return Html.Attr(meta, "content");
        }

        // Ссылки жанров/тем только из блока информации: в шапке и фильтрах такие ссылки тоже бывают.
        static IReadOnlyList<string> Genres(string info)
        {
            var list = new List<string>();
            foreach (var a in Html.BlocksWithAttr(info, "a", "href"))
            {
                if (!(Html.Attr(a, "href") ?? "").StartsWith("/anime/genre/", System.StringComparison.Ordinal)) continue;
                var name = Html.Text(a);
                if (name.Length > 0 && !list.Contains(name)) list.Add(name);
            }
            return list;
        }

        // Значение строки «метка → значение» в .entity-field (метка и значение — соседние div).
        static string Field(string info, string label)
        {
            var m = Regex.Match(info, @"<div\b[^>]*>\s*" + Regex.Escape(label) + @"\s*</div>\s*<div\b[^>]*>(.*?)</div>", Opts);
            if (!m.Success) return null;
            var t = Html.Text(m.Groups[1].Value);
            return t.Length == 0 ? null : t;
        }

        static IReadOnlyList<RelatedTitle> Related(string html)
        {
            var list = new List<RelatedTitle>();
            foreach (var block in Html.Blocks(html, "div", "seasons-item"))
            {
                var link = Html.Blocks(block, "a", "seasons__link").FirstOrDefault();
                var m = HrefRx.Match(Html.Attr(link, "href") ?? "");
                if (!m.Success) continue;

                // Первые два осмысленных span в .seasons__info: тип и год (разделитель "/" пропускаем).
                var spans = SpanRx.Matches(Html.Blocks(block, "div", "seasons__info").FirstOrDefault() ?? "")
                    .Cast<Match>().Select(s => Html.Text(s.Groups[1].Value)).Where(t => t.Length > 0 && t != "/").ToList();
                var title = Html.Text(link);
                list.Add(new RelatedTitle
                {
                    Id = int.Parse(m.Groups[2].Value),
                    Slug = m.Groups[1].Value,
                    Title = title,
                    Kind = spans.Count > 0 ? spans[0] : null,
                    Year = spans.Count > 1 ? spans[1] : null,
                    PosterUrl = Html.Attr(Html.Tags(block, "img", "seasons__img").FirstOrDefault(), "src"),
                });
            }
            return list;
        }
    }
}
