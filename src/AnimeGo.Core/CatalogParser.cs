using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Карточки каталога/поиска (.ani-list__item; в ТОП-250 это <article>), сетки главной (.ani-grid__item)
    // и «Моего списка» (.user-mylist__item) плюс данные бесконечной прокрутки.
    public static class CatalogParser
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex HrefRx = new Regex(@"^/anime/(.+)-(\d+)$", Opts);
        static readonly Regex ParamRx = new Regex(@"""(\w+)""\s*:\s*""(\w+)""", Opts);
        static readonly Regex FirstDivRx = new Regex(@"<div\b[^>]*>", Opts);
        static readonly Regex FirstARx = new Regex(@"<a\b[^>]*>", Opts);
        static readonly Regex LinkRx = new Regex(@"(<a\b[^>]*>)(.*?)</a\s*>", Opts);
        static readonly Regex PageFrameRx = new Regex(@"<turbo-frame\b[^>]*\bid\s*=\s*[""']profile-my-list-page-\d+[""'][^>]*>", Opts);

        public static IReadOnlyList<AnimeCard> Cards(string html)
        {
            var cards = new List<AnimeCard>();
            var blocks = Html.Blocks(html, "div", "ani-list__item")
                .Concat(Html.Blocks(html, "article", "ani-list__item"))
                .Concat(Html.Blocks(html, "div", "ani-grid__item"))
                .Concat(Html.Blocks(html, "div", "user-mylist__item"));   // «Мой список»: <div>, проверено на странице под аккаунтом
            foreach (var block in blocks)
            {
                var card = Card(block);
                if (card != null) cards.Add(card);
            }
            return cards;
        }

        public static IReadOnlyList<AnimeCard> CardsFromAjax(string json) =>
            Cards(Json.Parse<AjaxDto>(json).Data?.Content);

        // null, если на странице нет блока бесконечной прокрутки.
        public static Paging Paging(string html)
        {
            var template = Data(html, "url-template");
            if (template == null) return null;
            var ps = new Dictionary<string, string>();
            foreach (Match m in ParamRx.Matches(Data(html, "params") ?? ""))
                ps[m.Groups[1].Value] = m.Groups[2].Value;
            return new Paging
            {
                UrlTemplate = template,
                Page = Int(Data(html, "page")),
                PageSize = Int(Data(html, "page-size")),
                TotalItems = Int(Data(html, "total-items")),
                HasMore = Data(html, "has-more") == "true",
                Params = ps,
            };
        }

        // «Мой список» листается не бесконечной прокруткой, а ленивым turbo-frame «profile-my-list-page-N»
        // с src="/user/mylist/{userId}/N?type=…". null — следующей страницы нет.
        public static string NextFrameUrl(string html)
        {
            var tag = PageFrameRx.Match(html ?? "").Value;
            var src = Html.Attr(tag, "src");
            return string.IsNullOrEmpty(src) ? null : src;
        }

        static AnimeCard Card(string block)
        {
            var pic = Html.Tags(block, "a", "ani-list__item-picture").Concat(Html.Tags(block, "a", "ani-grid__item-picture"))
                .Concat(Html.Tags(block, "a", "user-mylist__picture")).FirstOrDefault();
            var m = HrefRx.Match(Html.Attr(pic, "href") ?? "");
            if (!m.Success) return null;

            var img = Html.Tags(block, "img", "image__img").FirstOrDefault();
            var titleBlock = Html.Blocks(block, "div", "ani-list__item-title").Concat(Html.Blocks(block, "div", "ani-grid__item-title")).FirstOrDefault();
            var titleTag = FirstARx.Match(titleBlock ?? "").Value;

            string kind = null;
            var genres = new List<string>();
            string linkText = null;   // текст первой ссылки на тайтл — название, если нет ни title, ни alt («Мой список»)
            foreach (Match a in LinkRx.Matches(block))
            {
                var href = Html.Attr(a.Groups[1].Value, "href") ?? "";
                if (href.StartsWith("/anime/type/", StringComparison.Ordinal)) kind = kind ?? Html.Text(a.Groups[2].Value);
                else if (href.StartsWith("/anime/genre/", StringComparison.Ordinal)) genres.Add(Html.Attr(a.Groups[1].Value, "title") ?? Html.Text(a.Groups[2].Value));
                else if (linkText == null && HrefRx.IsMatch(href)) linkText = NonEmpty(Html.Text(a.Groups[2].Value));
            }

            var badge = Html.Blocks(block, "div", "rating-badge").FirstOrDefault();
            var rating = badge == null ? null : Html.Text(badge);

            string original = null;
            foreach (var d in Html.Blocks(block, "div", "fw-lighter"))
            {
                var cls = Html.Attr(FirstDivRx.Match(d).Value, "class") ?? "";
                if (cls.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Contains("small")) { original = Html.Text(d); break; }
            }

            return new AnimeCard
            {
                Id = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Slug = m.Groups[1].Value,
                Title = NonEmpty(Html.Attr(titleTag, "title")) ?? NonEmpty(Html.Attr(img, "alt")) ?? NonEmpty(Html.Text(titleBlock)) ?? linkText,
                TitleOriginal = string.IsNullOrEmpty(original) ? null : original,
                PosterUrl = Html.Attr(img, "src"),
                Rating = string.IsNullOrEmpty(rating) ? null : rating,
                Kind = kind,
                Genres = genres,
            };
        }

        static string Data(string html, string name)
        {
            var m = Regex.Match(html ?? "", @"(?<=\s)data-infinite-scroll-" + name + @"-value\s*=\s*(?:""([^""]*)""|'([^']*)')", Opts);
            return m.Success ? Html.Decode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
        }

        static string NonEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        static int Int(string s) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
