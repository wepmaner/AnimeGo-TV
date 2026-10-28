using Xunit;

namespace AnimeGo.Core.Tests
{
    public class AniboomParserTests
    {
        [Fact]
        public void Parse_Fixture()
        {
            var s = AniboomParser.Parse(Fixture.Read("aniboom_embed.html"));
            Assert.Single(s.Streams);
            Assert.Equal("https://sophia.ya-ligh123.site/mk/mkPdyoERMOQ/master.m3u8", s.Streams[0].Url);
            Assert.Equal("auto", s.Streams[0].Quality);
            Assert.True(s.Streams[0].IsHls);
            Assert.Equal(1439, s.DurationSec);
        }

        [Fact]
        public void Parse_NoParameters_Throws() =>
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() => AniboomParser.Parse("<html></html>")).Kind);

        [Fact]
        public void Parse_NoHls_Throws() =>
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() =>
                AniboomParser.Parse("<div data-parameters=\"{&quot;duration&quot;:5}\"></div>")).Kind);

        [Fact]
        public void EmbedUrl()
        {
            Assert.Equal("https://aniboom.one/embed/x?a=1", AniboomParser.EmbedUrl("//aniboom.one/embed/x?a=1"));
            Assert.Equal("https://aniboom.one/embed/x", AniboomParser.EmbedUrl("https://aniboom.one/embed/x"));
        }
    }
}
