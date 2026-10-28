using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Главная: «Продолжить просмотр», сетка сезона и пользователь из data-user на <html>.
    public static class HomeParser
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex HrefRx = new Regex(@"^/anime/(.+)-(\d+)(?:[#?].*)?$", Opts);
        static readonly Regex TimeRx = new Regex(@"(\d+(?::\d+)+)\s*/\s*(\d+(?::\d+)+)", Opts);
        static readonly Regex NumberRx = new Regex(@"\d+", Opts);

        public static HomePage Parse(string html)
        {
            var cards = new List<ContinueCard>();
            foreach (var section in Html.Blocks(html, "section", "continue-watching-section"))
                foreach (var link in Html.Blocks(section, "a", "cw__link"))
                {
                    var card = Card(link);
                    if (card != null) cards.Add(card);
                }

            var season = Html.Blocks(html, "section", "season-section").FirstOrDefault() ?? "";
            var linkTag = Html.Tags(season, "a", "homepage-header__link").FirstOrDefault();
            var title = Html.Blocks(season, "h2", "season-title").FirstOrDefault();
            return new HomePage
            {
                User = ParseUser(html),
                Continue = cards,
                SeasonTitle = title == null ? null : Html.Text(title),
                SeasonUrl = linkTag == null ? null : Html.Attr(linkTag, "href"),
                Season = CatalogParser.Cards(season),
            };
        }

        // null, если не вошли.
        public static User ParseUser(string html)
        {
            foreach (var tag in Html.TagsWithAttr(html, "html", "data-user"))
            {
                try
                {
                    var dto = Json.Parse<UserDto>(Html.Attr(tag, "data-user"));
                    if (dto != null && dto.Id > 0) return new User { Id = dto.Id, Name = dto.Username };
                }
                catch (SerializationException) { }
            }
            return null;
        }

        static ContinueCard Card(string link)
        {
            var open = Html.TagsWithAttr(link, "a", "href").FirstOrDefault();
            var m = HrefRx.Match(Html.Attr(open, "href") ?? "");
            if (!m.Success) return null;

            var time = TimeRx.Match(Html.Text(FirstBlock(link, "span", "cw__time")));
            var ep = NumberRx.Match(Html.Text(FirstBlock(link, "span", "cw__episode")));
            if (!time.Success || !ep.Success) return null;

            var img = Html.Tags(link, "img", "cw__img").FirstOrDefault();
            var title = Html.Text(FirstBlock(link, "div", "cw__title"));
            if (title.Length == 0) title = Html.Attr(img, "alt") ?? "";

            return new ContinueCard
            {
                AnimeId = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Slug = m.Groups[1].Value,
                Title = title,
                EpisodeNumber = int.Parse(ep.Value, CultureInfo.InvariantCulture),
                PositionSec = Seconds(time.Groups[1].Value),
                DurationSec = Seconds(time.Groups[2].Value),
                FrameUrl = Html.Attr(img, "src"),
            };
        }

        static string FirstBlock(string html, string tag, string cls) => Html.Blocks(html, tag, cls).FirstOrDefault();

        // mm:ss или h:mm:ss.
        static int Seconds(string t) =>
            t.Split(':').Aggregate(0, (acc, part) => acc * 60 + int.Parse(part, CultureInfo.InvariantCulture));
    }
}
