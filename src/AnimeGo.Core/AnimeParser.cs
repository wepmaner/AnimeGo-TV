using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Страница тайтла: заголовок, описание, постер, жанры, рейтинг, поля информации, главные герои, кадры,
    // блок «Связанное» (.seasons-item) и рецензии (.reviews-item).
    // Комментарии (/comment/{thread}/{page}) не разбираем: на ТВ их не показываем.
    public static class AnimeParser
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex H1Rx = new Regex(@"<h1\b[^>]*>(.*?)</h1\s*>", Opts);
        static readonly Regex LiRx = new Regex(@"<li\b[^>]*>(.*?)</li\s*>", Opts);
        static readonly Regex SpanRx = new Regex(@"<span\b[^>]*>(.*?)</span\s*>", Opts);
        static readonly Regex HrefRx = new Regex(@"^/anime/(.+)-(\d+)$", Opts);
        static readonly Regex YearRx = new Regex(@"\b(?:19|20)\d{2}\b", Opts);
        static readonly Regex LabelRx = new Regex(@"<div\b[^>]*class=""[^""]*text-opacity-75[^""]*""[^>]*>\s*([^<]+?)\s*</div>", Opts);
        static readonly Regex HeroRx = new Regex(
            @"href=""/character/[^""]*""[^>]*>(.*?)</a>(?:\s*<span\b[^>]*>\s*\(\s*<a\b[^>]*href=""/person/[^""]*""[^>]*>(.*?)</a>)?", Opts);
        static readonly Regex BrRx = new Regex(@"<br\s*/?>|</p\s*>", Opts);
        static readonly Regex CommaRx = new Regex(@"\s+,", Opts);
        static readonly Regex BlankLinesRx = new Regex(@"\n{3,}", Opts);
        static readonly Regex NextRx = new Regex(
            @"Следующий эпизод.*?(<span\b[^>]*class=""b-tooltipped""[^>]*>)\s*([^<]+?)\s*</span>(?:\s*<span\b[^>]*>[^<]*?(\d+)\s*сери)?", Opts);
        static readonly Regex RowRx = new Regex(@"data-label=""(\d+)\.""", Opts);
        static readonly Regex RowDateRx = new Regex(@"<span class=""text-truncate""[^>]*><span>([^<]+)</span>", Opts);
        static readonly Regex RowStatusRx = new Regex(@"text-center lh-lg"">\s*([^<]*?)\s*</div>", Opts);

        public static AnimeInfo Parse(string html, int id)
        {
            var h1 = H1Rx.Match(html ?? "");
            if (!h1.Success) throw new ApiException(ApiErrorKind.Parse, "На странице тайтла нет заголовка");

            var info = Html.Blocks(html, "div", "entity__info").FirstOrDefault() ?? "";
            var year = YearRx.Match(Field(info, "Выпуск") ?? "");
            var rating = Html.Blocks(html, "div", "entity-rating__info").FirstOrDefault();
            var score = Html.Text(Html.Blocks(rating, "span", "entity-rating__value").FirstOrDefault());
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
                Rating = score.Length == 0 ? null : score,
                Votes = NullIfEmpty(Html.Text(Html.Blocks(rating, "div", "entity-rating__count").FirstOrDefault())),
                Fields = Fields(info),
                Characters = Characters(info),
                Screenshots = Screenshots(html),
                Reviews = Reviews(html),
                Next = Next(html),
                Schedule = Schedule(html),
            };
        }

        static NextEpisode Next(string html)
        {
            var m = NextRx.Match(html);
            if (!m.Success || !m.Groups[3].Success) return null;
            return new NextEpisode
            {
                Number = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                When = Html.Decode(m.Groups[2].Value.Trim()),
                In = NullIfEmpty(Html.Attr(m.Groups[1].Value, "data-bs-title")?.Trim()),
            };
        }

        // Таблица .schedule-episodes-table: строки начинаются с data-label="N."; в строке дата и статус.
        static IReadOnlyList<ScheduledEpisode> Schedule(string html)
        {
            var at = html.IndexOf("schedule-episodes-table", StringComparison.Ordinal);
            if (at < 0) return new ScheduledEpisode[0];
            var table = html.Substring(at);
            var rows = RowRx.Matches(table).Cast<Match>().ToList();
            var list = new List<ScheduledEpisode>();
            for (var i = 0; i < rows.Count; i++)
            {
                var end = i + 1 < rows.Count ? rows[i + 1].Index : Math.Min(table.Length, rows[i].Index + 6000);
                var row = table.Substring(rows[i].Index, end - rows[i].Index);
                var date = RowDateRx.Match(row);
                var status = RowStatusRx.Match(row);
                list.Add(new ScheduledEpisode
                {
                    Number = int.Parse(rows[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    Date = date.Success ? Html.Decode(date.Groups[1].Value.Trim()) : null,
                    Status = status.Success ? NullIfEmpty(Html.Decode(status.Groups[1].Value.Trim())) : null,
                });
            }
            return list;
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        // Все пары «метка → значение» блока информации; значение — следующий div после метки.
        static IReadOnlyList<InfoField> Fields(string info)
        {
            var list = new List<InfoField>();
            foreach (Match m in LabelRx.Matches(info))
            {
                var value = Html.BlocksWithAttr(info.Substring(m.Index + m.Length), "div", "class").FirstOrDefault();
                var text = CommaRx.Replace(Html.Text(value), ",");
                if (text.Length > 0) list.Add(new InfoField { Label = Html.Decode(m.Groups[1].Value), Value = text });
            }
            return list;
        }

        static IReadOnlyList<Character> Characters(string info)
        {
            var m = LabelRx.Matches(info).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == "Главные герои");
            if (m == null) return new Character[0];
            var value = Html.BlocksWithAttr(info.Substring(m.Index + m.Length), "div", "class").FirstOrDefault() ?? "";
            return HeroRx.Matches(value).Cast<Match>()
                .Select(h => new Character { Name = Html.Text(h.Groups[1].Value), Actor = NullIfEmpty(Html.Text(h.Groups[2].Value)) })
                .Where(c => c.Name.Length > 0).ToList();
        }

        // Кадр 640×360 из <source srcset>, иначе <img src> (там 320×180).
        static IReadOnlyList<string> Screenshots(string html)
        {
            var list = new List<string>();
            foreach (var item in Html.Blocks(html, "div", "screenshots__item"))
            {
                var src = Html.Attr(Html.TagsWithAttr(item, "source", "srcset").FirstOrDefault(), "srcset")
                    ?? Html.Attr(Html.Tags(item, "img", "screenshots__img").FirstOrDefault(), "src");
                if (!string.IsNullOrEmpty(src) && !list.Contains(src)) list.Add(src);
            }
            return list;
        }

        static IReadOnlyList<Review> Reviews(string html)
        {
            var list = new List<Review>();
            foreach (var item in Html.Blocks(html, "div", "reviews-item"))
            {
                var title = Html.Text(Html.Blocks(item, "div", "reviews-item__title").FirstOrDefault());
                if (title.Length == 0) continue;   // заготовка-скелетон
                var text = Paragraphs(Html.Blocks(item, "div", "reviews-item__body").FirstOrDefault());
                list.Add(new Review
                {
                    Title = title,
                    Author = NullIfEmpty(Html.Text(Html.Blocks(item, "div", "reviews-item__name").FirstOrDefault())),
                    Date = NullIfEmpty(Html.Text(Html.Blocks(item, "div", "reviews-item__date").FirstOrDefault())),
                    Useful = NullIfEmpty(Html.Text(Html.Blocks(item, "div", "fw-semibold").FirstOrDefault())),
                    Score = NullIfEmpty(Html.Text(Html.Blocks(item, "span", "review-rating__current").FirstOrDefault())),
                    Text = text,
                    Spoilers = text.Substring(0, Math.Min(80, text.Length)).IndexOf("спойлер", StringComparison.OrdinalIgnoreCase) >= 0,
                });
            }
            return list;
        }

        // Текст с абзацами: <br> и </p> — переводы строк, остальные теги — пробелы.
        static string Paragraphs(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var open = Html.TagsWithAttr(html, "div", "class").FirstOrDefault() ?? "";   // в атрибутах бывает «->»
            var inner = html.Substring(open.Length);
            var lines = BrRx.Replace(inner, "\n").Split('\n').Select(l => Html.Text(l));
            return BlankLinesRx.Replace(string.Join("\n", lines), "\n\n").Trim();
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
