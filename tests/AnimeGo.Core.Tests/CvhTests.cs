using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class CvhTests
    {
        [Fact]
        public void ParseIframeUrl_Space()
        {
            var r = Cvh.ParseIframeUrl("https://animego.me/cdn-iframe/62542/Jam Club/3/1");
            Assert.Equal((62542, "Jam Club", 3, 1), r.Value);
        }

        [Fact]
        public void ParseIframeUrl_Encoded()
        {
            var r = Cvh.ParseIframeUrl("https://animego.me/cdn-iframe/62542/Jam%20Club/3/1");
            Assert.Equal((62542, "Jam Club", 3, 1), r.Value);
        }

        [Fact]
        public void ParseIframeUrl_NotCvh() =>
            Assert.Null(Cvh.ParseIframeUrl("https://aniboom.one/embed/abc?episode=1"));

        [Fact]
        public void Urls()
        {
            Assert.Equal("https://plapi.cdnvideohub.com/api/v1/player/sv/playlist?pub=747&aggr=mali&id=62542", Cvh.PlaylistUrl(62542));
            Assert.Equal("https://plapi.cdnvideohub.com/api/v1/player/sv/video/123", Cvh.VideoUrl("123"));
        }

        [Fact]
        public void Playlist_AndFindItem()
        {
            var p = Cvh.ParsePlaylist(Fixture.Read("cvh_playlist_62542.json"));
            Assert.True(p.Items.Count > 20);
            Assert.Equal("15266286492336", Cvh.FindItem(p, "Студийная Банда", 3, 1).VkId);
            Assert.Equal("15266286492336", Cvh.FindItem(p, "студийнаябанда", 3, 1).VkId);
            Assert.Null(Cvh.FindItem(p, "Нет такой", 3, 1));
        }

        [Fact]
        public void FindItem_IgnoresCaseSpacesDashes()
        {
            var p = Cvh.ParsePlaylist("{\"items\":[{\"vkId\":\"1\",\"voiceStudio\":\"Jam Club\",\"season\":1,\"episode\":1}]}");
            Assert.NotNull(Cvh.FindItem(p, "JAM-CLUB", 1, 1));
            Assert.NotNull(Cvh.FindItem(p, "jamclub", 1, 1));
        }

        [Fact]
        public void BadJson_Throws()
        {
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() => Cvh.ParsePlaylist("<html>")).Kind);
            Assert.Equal(ApiErrorKind.Parse, Assert.Throws<ApiException>(() => Cvh.ParseVideo("")).Kind);
        }

        [Fact]
        public void ParseVideo()
        {
            var s = Cvh.ParseVideo(Fixture.Read("cvh_video_redacted.json"));
            Assert.Equal(1440, s.DurationSec);
            Assert.True(s.Streams.Single(x => x.Quality == "auto").IsHls);
            Assert.Contains(s.Streams, x => x.Quality == "1080p");
            Assert.Equal(new[] { "auto", "1080p", "720p", "480p", "360p", "240p" }, s.Streams.Select(x => x.Quality));
            Assert.Equal("720p", s.Pick("720p").Quality);
            Assert.Equal("auto", s.Pick("4k").Quality);
            Assert.Equal("auto", s.Pick("auto").Quality);
        }

        [Fact]
        public void Pick_NearestLowerThenHigher()
        {
            var s = Cvh.ParseVideo("{\"duration\":1,\"sources\":{\"mpegFullHdUrl\":\"a\",\"mpegLowUrl\":\"b\"}}");
            Assert.Equal("360p", s.Pick("480p").Quality);
            Assert.Equal("360p", s.Pick("360p").Quality);
            Assert.Equal("360p", s.Pick("240p").Quality);
            Assert.Equal("1080p", s.Pick("4k").Quality);
        }
    }
}
