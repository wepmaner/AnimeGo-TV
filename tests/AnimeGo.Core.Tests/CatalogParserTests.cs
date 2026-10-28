using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class CatalogParserTests
    {
        [Fact]
        public void Cards_Catalog_ParsesTwentyCards()
        {
            var cards = CatalogParser.Cards(Fixture.Read("catalog_ongoing_page1.html"));
            Assert.Equal(20, cards.Count);
            var c = cards[0];
            Assert.Equal(4149, c.Id);
            Assert.Equal("legenda-o-belom-kote-3", c.Slug);
            Assert.Equal("/anime/legenda-o-belom-kote-3-4149", c.Url);
            Assert.Equal("Легенда о Белом Коте 3", c.Title);
            Assert.Equal("Da Li Si Rizhi 3rd Season", c.TitleOriginal);
            Assert.Equal("ONA", c.Kind);
            Assert.Contains("Комедия", c.Genres);
            Assert.StartsWith("https://img.cdngos.com/v/250x350/", c.PosterUrl);
        }

        [Fact]
        public void Cards_Top250_ParsesArticleCards()
        {
            // В ТОП-250 карточки — <article class="anime-top__item ani-list__item">, а не <div>.
            var cards = CatalogParser.Cards(Fixture.Read("catalog_top250.html"));
            Assert.Equal(25, cards.Count);
            Assert.Equal(280, cards[0].Id);
            Assert.Equal("ohotnik-h-ohotnik-2-s2", cards[0].Slug);
            Assert.Equal("Охотник х Охотник 2", cards[0].Title);
            Assert.Contains("Приключения", cards[0].Genres);
        }

        [Fact]
        public void Paging_ReadsInfiniteScrollData()
        {
            var p = CatalogParser.Paging(Fixture.Read("catalog_ongoing_page1.html"));
            Assert.Equal("/anime/status/ongoing/__page__", p.UrlTemplate);
            Assert.Equal(1, p.Page);
            Assert.Equal(20, p.PageSize);
            Assert.Equal(81, p.TotalItems);
            Assert.True(p.HasMore);
            Assert.Equal("/anime/status/ongoing/2?entities=true", p.PageUrl(2));
        }

        [Fact]
        public void PageUrl_WithoutParams_HasNoQuery()
        {
            var p = new Paging { UrlTemplate = "/x/__page__" };
            Assert.Equal("/x/3", p.PageUrl(3));
        }

        [Fact]
        public void CardsFromAjax_ParsesTwentyCards()
        {
            Assert.Equal(20, CatalogParser.CardsFromAjax(Fixture.Read("catalog_ongoing_page2.json")).Count);
        }

        [Fact]
        public void Cards_Search_FindsKnownIds()
        {
            var ids = CatalogParser.Cards(Fixture.Read("search_okean.html")).Select(c => c.Id).ToList();
            Assert.Contains(3547, ids);
            Assert.Contains(2817, ids);
            Assert.Contains(645, ids);
        }

        [Fact]
        public void Cards_HomeGrid_ParsesSeasonCard()
        {
            var c = CatalogParser.Cards(Fixture.Read("home_anon.html")).Single(x => x.Id == 3815);
            Assert.Equal("Свадьба светлячков", c.Title);
            Assert.Equal("Hotaru no Yomeiri", c.TitleOriginal);
            Assert.Equal("8.6", c.Rating);
            Assert.Null(c.Kind);
            Assert.Empty(c.Genres);
        }

        [Fact]
        public void Cards_MyList_ParsesUserMylistItems()
        {
            // Разметка «Моего списка» по API-findings.md: .user-mylist__item, a.user-mylist__picture, img.image__img.
            const string html = "<div class=\"user-mylist__item\"><a class=\"user-mylist__picture\" href=\"/anime/re-zero-109\">" +
                "<img class=\"image__img\" src=\"https://img.cdngos.com/v/250x350/anime/x.jpg\" alt=\"Re:Zero\"></a>" +
                "<div class=\"user-mylist__body\"><a href=\"/anime/re-zero-109\">Re:Zero. Жизнь с нуля</a><div>Просмотрено 11 / 19</div></div></div>" +
                "<div class=\"user-mylist__item\"><a class=\"user-mylist__picture\" href=\"/anime/bez-alt-5\"><img class=\"image__img\" src=\"p.jpg\"></a>" +
                "<a href=\"/anime/bez-alt-5\">  Без alt  </a></div>";
            var cards = CatalogParser.Cards(html);
            Assert.Equal(2, cards.Count);
            Assert.Equal(109, cards[0].Id);
            Assert.Equal("re-zero", cards[0].Slug);
            Assert.Equal("Re:Zero", cards[0].Title);
            Assert.Equal("https://img.cdngos.com/v/250x350/anime/x.jpg", cards[0].PosterUrl);
            Assert.Equal("Без alt", cards[1].Title);
        }

        [SkippableFact]
        public void Cards_MyListPrivate_HasCards()
        {
            var html = Fixture.ReadPrivate("mylist_all.html");
            Skip.If(html == null, "нет приватной фикстуры mylist_all.html (scripts/login-sample.mjs --path \"/profile/?type=all\" --out mylist_all.html)");
            var cards = CatalogParser.Cards(html);
            Assert.NotEmpty(cards);
            Assert.All(cards, c =>
            {
                Assert.True(c.Id > 0);
                Assert.False(string.IsNullOrWhiteSpace(c.Title));
                Assert.StartsWith("https://img.cdngos.com/", c.PosterUrl);
            });
            // Карточки не задваиваются (блок ищется в нескольких тегах).
            Assert.Equal(cards.Count, cards.Select(c => c.Id).Distinct().Count());
        }

        [Fact]
        public void NextFrameUrl_ReadsLazyPageFrame()
        {
            const string html = "<div>x</div><turbo-frame id=\"profile-my-list-page-2\"\n  class=\"profile-my-list__page-frame\"\n  src=\"/user/mylist/7/2?type=all\"\n  loading=\"lazy\"></turbo-frame>";
            Assert.Equal("/user/mylist/7/2?type=all", CatalogParser.NextFrameUrl(html));
            Assert.Null(CatalogParser.NextFrameUrl("<turbo-frame id=\"header-notifications-dropdown\" src=\"/x\"></turbo-frame>"));
            Assert.Null(CatalogParser.NextFrameUrl(null));
        }

        [SkippableFact]
        public void NextFrameUrl_MyListPrivate_PointsToSecondPage()
        {
            var html = Fixture.ReadPrivate("mylist_all.html");
            Skip.If(html == null, "нет приватной фикстуры mylist_all.html");
            Assert.Matches(@"^/user/mylist/\d+/2\?type=all$", CatalogParser.NextFrameUrl(html));
        }
    }
}
