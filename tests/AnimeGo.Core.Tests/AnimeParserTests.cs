using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class AnimeParserTests
    {
        static readonly AnimeInfo Info = AnimeParser.Parse(Fixture.Read("anime_3547.html"), 3547);

        [Fact]
        public void Parse_ReadsTitleAndDescription()
        {
            Assert.Equal(3547, Info.Id);
            Assert.Equal("Необъятный океан 3", Info.Title);
            Assert.Equal("Необъятный океан 3 сезон", Info.TitleOriginal);
            Assert.True(Info.Description.Length > 100);
            Assert.StartsWith("Иори Китахара", Info.Description);
        }

        [Fact]
        public void Parse_ReadsPosterKindYear()
        {
            Assert.StartsWith("https://img.cdngos.com/", Info.PosterUrl);
            Assert.Equal("Сериал", Info.Kind);
            Assert.Equal("2026", Info.Year);
        }

        [Fact]
        public void Parse_ReadsGenresWithoutDuplicates()
        {
            Assert.Contains("Комедия", Info.Genres);
            Assert.Contains("Сэйнэн", Info.Genres);
            Assert.Equal(Info.Genres.Count, Info.Genres.Distinct().Count());
        }

        [Fact]
        public void Parse_ReadsRelated()
        {
            var r = Info.Related.Single(x => x.Id == 2817);
            Assert.Equal("Необъятный океан 2", r.Title);
            Assert.Equal("neobyatnyy-okean-2", r.Slug);
            Assert.Equal("Сериал", r.Kind);
            Assert.Equal("2025", r.Year);
            Assert.StartsWith("https://img.cdngos.com/", r.PosterUrl);
        }

        [Fact]
        public void Parse_SkipsBrokenRelated()
        {
            var html = "<h1>T</h1><div class=\"seasons-item\"><a class=\"seasons__link\" href=\"/x\">A</a></div>";
            Assert.Empty(AnimeParser.Parse(html, 1).Related);
        }

        [Fact]
        public void Parse_NoH1_Throws()
        {
            var ex = Assert.Throws<ApiException>(() => AnimeParser.Parse("<html></html>", 1));
            Assert.Equal(ApiErrorKind.Parse, ex.Kind);
        }
    }
}
