using System.Linq;
using AnimeGo.Core;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class HomeParserTests
    {
        const string OneCard = @"
<section class=""continue-watching-section container-xxl"">
  <a href=""/anime/some-slug-123#player"" class=""text-decoration-none cw__link"">
    <div class=""cw__poster"">
      <button class=""cw__remove btn"" data-anime-id=""123"" data-ajax-url=""/video/progress/remove"">x</button>
      <img src=""https://img.cdngos.com/episode/aa/bb/cc"" alt=""Название &amp; тест"" class=""cw__img"">
      <div class=""cw__overlay"">
        <div class=""cw__meta"">
          <span class=""cw__episode"">Серия 22</span>
          <span class=""cw__time text-truncate"">
              00:14 / 24:04
          </span>
        </div>
        <div class=""progress cw__progress""><div class=""progress-bar cw__progress-bar"" style=""width: 0.97%;""></div></div>
      </div>
    </div>
    <div class=""cw__title"">
        Название &amp; тест
    </div>
  </a>
</section>";

        [Fact]
        public void Synthetic_Card()
        {
            var page = HomeParser.Parse(OneCard);
            var c = Assert.Single(page.Continue);
            Assert.Equal(123, c.AnimeId);
            Assert.Equal("some-slug", c.Slug);
            Assert.Equal(22, c.EpisodeNumber);
            Assert.Equal(14, c.PositionSec);
            Assert.Equal(1444, c.DurationSec);
            Assert.Equal("Название & тест", c.Title);
            Assert.Equal("https://img.cdngos.com/episode/aa/bb/cc", c.FrameUrl);
        }

        [Fact]
        public void Synthetic_HourTime()
        {
            var page = HomeParser.Parse(OneCard.Replace("00:14 / 24:04", "1:02:03 / 1:30:00"));
            var c = Assert.Single(page.Continue);
            Assert.Equal(3723, c.PositionSec);
            Assert.Equal(5400, c.DurationSec);
        }

        [Fact]
        public void Synthetic_BrokenCards_Skipped()
        {
            var noTime = OneCard.Replace("00:14 / 24:04", "");
            var noHref = OneCard.Replace("/anime/some-slug-123#player", "/somewhere");
            Assert.Empty(HomeParser.Parse(noTime).Continue);
            Assert.Empty(HomeParser.Parse(noHref).Continue);
        }

        [Fact]
        public void Anonymous_Home()
        {
            var page = HomeParser.Parse(Fixture.Read("home_anon.html"));
            Assert.Null(page.User);
            Assert.NotNull(page.Continue);
            Assert.Empty(page.Continue);
            Assert.Equal("/anime/season/2026/fall", page.SeasonUrl);
            Assert.Equal("Аниме осеннего сезона", page.SeasonTitle);
            Assert.True(page.Season.Count >= 8);
        }

        [Fact]
        public void ParseUser_FromEncodedDataUser()
        {
            var html = "<html lang=\"ru\" data-user=\"&#x7B;&quot;id&quot;:42,&quot;username&quot;:&quot;neo&quot;&#x7D;\"><body></body></html>";
            var u = HomeParser.ParseUser(html);
            Assert.Equal(42, u.Id);
            Assert.Equal("neo", u.Name);
            Assert.Null(HomeParser.ParseUser("<html><body></body></html>"));
        }

        [SkippableFact]
        public void Authenticated_Home_Structure()
        {
            var html = Fixture.ReadPrivate("home_auth.html");
            Skip.If(html == null, "нет private/home_auth.html");
            var page = HomeParser.Parse(html);
            Assert.True(page.User != null && page.User.Id > 0 && !string.IsNullOrEmpty(page.User.Name));
            Assert.True(page.Continue.Count >= 1);
            foreach (var c in page.Continue)
            {
                Assert.True(c.AnimeId > 0);
                Assert.False(string.IsNullOrEmpty(c.Slug));
                Assert.False(string.IsNullOrEmpty(c.Title));
                Assert.True(c.EpisodeNumber >= 1);
                Assert.True(c.DurationSec > 0);
                Assert.InRange(c.PositionSec, 0, c.DurationSec);
                Assert.StartsWith("https://img.cdngos.com/episode/", c.FrameUrl);
            }
            Assert.True(page.Season.Count >= 1);
        }
    }
}
