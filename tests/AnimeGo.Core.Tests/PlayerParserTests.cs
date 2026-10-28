using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class PlayerParserTests
    {
        [Fact]
        public void Parse_ReadsEpisodesTranslationsAndSources()
        {
            var p = PlayerParser.Parse(Fixture.Read("player_3547.json"));
            Assert.Equal(3547, p.AnimeId);
            Assert.Equal(12, p.Episodes.Count);
            Assert.Equal(44100, p.Episodes[0].Id);
            Assert.Equal(1, p.Episodes[0].Number);
            Assert.False(p.Episodes[0].Watched);
            Assert.Equal(14, p.Translations.Count);
            Assert.Contains(p.Translations, t => t.Key == "59" && t.Id == 59 && t.Title == "JAM CLUB");
            Assert.DoesNotContain(p.Translations, t => t.Title.Contains("ошибка"));
            Assert.Equal(25, p.Sources.Count);
            var cvh = p.Sources.First(s => s.Provider == "cvh" && s.TranslationKey == "59");
            Assert.Equal("https://animego.me/cdn-iframe/62542/Jam Club/3/1", cvh.PlayerUrl);
            Assert.Equal("JAM CLUB", cvh.TranslationTitle);
            Assert.Equal(12, p.EpisodesTotal);
            Assert.Equal(new[] { "cvh", "aniboom" }, p.ProviderPriority);
            Assert.Equal("/player/3547/continuation/1", p.ContinuationUrl);
            Assert.Equal("/player/3547/episodes", p.EpisodesUrl);
        }

        [Fact]
        public void ParseVideos_ReadsSources()
        {
            var v = PlayerParser.ParseVideos(Fixture.Read("player_videos_44100.json"));
            Assert.Null(v.IsWatched);
            Assert.Equal(25, v.NumVideos);
            Assert.Equal(25, v.Sources.Count);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"status\":\"error\",\"message\":\"x\"}")]
        public void Invalid_ThrowsParseError(string json)
        {
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() => PlayerParser.Parse(json)).Kind);
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() => PlayerParser.ParseVideos(json)).Kind);
        }

        static string Wrap(string html) =>
            "{\"status\":\"success\",\"data\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(html) + "}}";

        [Fact]
        public void Parse_TranslationTitle_DropsErrorSuffix()
        {
            var p = PlayerParser.Parse(Wrap("<button data-anime-player-target=\"translation\" data-translation=\"5\" data-translation-id=\"5\"><span>Дубляж AniLibria</span><span>(ошибка)</span></button>"));
            var t = Assert.Single(p.Translations);
            Assert.Equal("Дубляж AniLibria", t.Title);
            Assert.Equal("Дубляж", t.Kind);
        }

        [Fact]
        public void Parse_SourceUrl_ProtocolRelativeBecomesHttps()
        {
            var p = PlayerParser.Parse(Wrap("<button data-anime-player-target=\"provider\" data-provider-slug=\"kodik\" data-player=\"//host/x?a=1&amp;b=2\"></button>"));
            Assert.Equal("https://host/x?a=1&b=2", Assert.Single(p.Sources).PlayerUrl);
        }

        [Theory]
        [InlineData("{\"item\":{\"episode\":7,\"poster\":\"https:\\/\\/img\",\"duration\":1440}}", 7)]
        [InlineData("{\"item\":{\"poster\":\"x\",\"episode\": 12}}", 12)]
        public void ParseContinuation_ReadsEpisode(string json, int expected)
        {
            Assert.Equal(expected, PlayerParser.ParseContinuation(json));
        }

        [Theory]
        [InlineData("{\"item\":[]}")]
        [InlineData("{\"item\":null}")]
        [InlineData("{\"item\":{\"episode\":0}}")]
        [InlineData("not json")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseContinuation_NoEpisode_ReturnsNull(string json)
        {
            Assert.Null(PlayerParser.ParseContinuation(json));
        }

        static PlayerInfo Info(int count, params int[] watched) => new PlayerInfo
        {
            Episodes = Enumerable.Range(1, count).Select(n => new Episode { Id = 100 + n, Number = n, Watched = watched.Contains(n) }).ToList(),
        };

        [Fact]
        public void ResumeEpisode_AfterLastWatched()
        {
            // Как у живого тайтла: просмотрены 1–11 из 12, а continuation сайта отдаёт серию 1.
            Assert.Equal(12, Info(12, Enumerable.Range(1, 11).ToArray()).ResumeEpisode(1));
        }

        [Fact]
        public void ResumeEpisode_NothingWatched_First()
        {
            Assert.Equal(1, Info(12).ResumeEpisode(null));
            Assert.Equal(1, new PlayerInfo().ResumeEpisode(null));
        }

        [Fact]
        public void ResumeEpisode_AllWatched_Last()
        {
            Assert.Equal(3, Info(3, 1, 2, 3).ResumeEpisode(1));
        }

        [Fact]
        public void ResumeEpisode_ContinuationFurther_Wins()
        {
            Assert.Equal(5, Info(12, 1).ResumeEpisode(5));
        }

        [Fact]
        public void ResumeEpisode_GapInWatched_AfterLast()
        {
            Assert.Equal(6, Info(12, 1, 5).ResumeEpisode(null));
        }
    }
}
