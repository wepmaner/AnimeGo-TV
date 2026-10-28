using Xunit;

namespace AnimeGo.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void Parse_AjaxDto()
        {
            var dto = Json.Parse<AjaxDto>(Fixture.Read("player_videos_44100.json"));
            Assert.Equal("success", dto.Status);
            Assert.Equal(25, dto.Data.NumVideos);
            Assert.Null(dto.Data.IsWatched);
            Assert.Contains("offcanvas-header", dto.Data.Content);
        }

        [Fact]
        public void Serialize_RoundTrip()
        {
            var s = Json.Serialize(new AjaxDto { Status = "ok", Data = new AjaxDataDto { Content = "x", IsWatched = true } });
            Assert.Equal('{', s[0]); // без BOM
            var back = Json.Parse<AjaxDto>(s);
            Assert.Equal("ok", back.Status);
            Assert.True(back.Data.IsWatched);
        }
    }
}
