using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class KodikTests
    {
        [Fact]
        public void ParseEmbed()
        {
            var e = Kodik.ParseEmbed("https://kodikplayer.com/seria/1677747/4a22a63f48513e2398a836d7a494bc05/720p?translations=false&min_age=18");
            Assert.Equal(("seria", "1677747", "4a22a63f48513e2398a836d7a494bc05"), e.Value);
            Assert.Null(Kodik.ParseEmbed("https://aniboom.one/embed/abc"));
        }

        [Fact]
        public void Form_FromPage()
        {
            var f = Kodik.Form(Fixture.Read("kodik_seria.html")).ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal("animego.me", f["d"]);
            Assert.StartsWith("332ecc44901aaa30", f["d_sign"]);
            Assert.Equal("kodikplayer.com", f["pd"]);
            Assert.Equal("https://animego.me/", f["ref"]);
            Assert.Equal("seria", f["type"]);
            Assert.Equal("1677747", f["id"]);
            Assert.Equal("4a22a63f48513e2398a836d7a494bc05", f["hash"]);
            Assert.Equal("false", f["bad_user"]);
            Assert.Equal("true", f["cdn_is_working"]);
        }

        [Fact]
        public void Form_NoParams_Throws() =>
            Assert.Throws<ApiException>(() => Kodik.Form("<html></html>").ToList());

        [Fact]
        public void Links_Decoded()
        {
            var set = Kodik.ParseLinks(Fixture.Read("kodik_ftor.json"));
            Assert.Equal(new[] { "720p", "480p", "360p" }, set.Streams.Select(s => s.Quality));
            Assert.All(set.Streams, s => Assert.True(s.IsHls));
            var hd = set.Pick("720p");
            Assert.StartsWith("https://", hd.Url);
            Assert.EndsWith("/720.mp4:hls:manifest.m3u8", hd.Url);
        }

        [Fact]
        public void Decode_AnyShift()
        {
            // "https://x.y/1.m3u8" в base64, буквы сдвинуты на 7.
            var b64 = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("//x.example/720.mp4:hls:manifest.m3u8")).TrimEnd('=');
            var shifted = new string(b64.Select(c => char.IsUpper(c) ? (char)('A' + (c - 'A' + 7) % 26) : char.IsLower(c) ? (char)('a' + (c - 'a' + 7) % 26) : c).ToArray());
            Assert.Equal("https://x.example/720.mp4:hls:manifest.m3u8", Kodik.DecodeLink(shifted));
            Assert.Null(Kodik.DecodeLink("!!!"));
        }

        [Fact]
        public void Endpoint_FromPlayerJs()
        {
            Assert.Equal("/ftor", Kodik.ParseEndpoint("x,$.ajax({type:\"POST\",url:atob(\"L2Z0b3I=\"),cache:!1"));
            Assert.Null(Kodik.ParseEndpoint("var a=1;"));
            Assert.Equal("/assets/js/app.player_single.abc.js", Kodik.PlayerJsPath("<script src=\"/assets/js/app.player_single.abc.js\"></script>"));
        }
    }
}
