using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class StashTests
    {
        [Fact]
        public void TvInfo_FromApi()
        {
            var t = TvInfo.Parse(Fixture.Read("tv_api_v2.json"));
            Assert.True(t.DeveloperMode);
            Assert.Equal("192.168.0.41", t.DeveloperIp);
            Assert.Equal("UE50TU7140UXRU", t.Model);
            Assert.Equal("192.168.0.41", t.LogHost);
        }

        [Theory]
        [InlineData("{\"device\":{\"developerMode\":\"0\",\"developerIP\":\"192.168.0.41\"}}")]
        [InlineData("{\"device\":{\"developerMode\":\"1\",\"developerIP\":\"\"}}")]
        [InlineData("{\"device\":{\"developerMode\":\"1\"}}")]
        public void TvInfo_NoLogHost(string json) => Assert.Null(TvInfo.Parse(json).LogHost);

        [Theory]
        [InlineData("")]
        [InlineData("<html>")]
        [InlineData("{}")]
        public void TvInfo_Garbage_Null(string json) => Assert.Null(TvInfo.Parse(json));

        [Fact]
        public void Buffer_KeepsLast500_AndUnsent()
        {
            var b = new LogBuffer();
            for (var i = 1; i <= 600; i++) b.Add("l" + i);
            var (text, upTo) = b.Unsent();
            var lines = text.Split('\n');
            Assert.Equal(500, lines.Length);
            Assert.Equal("l101", lines[0]);
            Assert.Equal("l600", lines[499]);
            b.Add("l601");   // пришла, пока отправляли
            b.MarkSent(upTo);
            Assert.True(b.HasUnsent);
            Assert.Equal("l601", b.Unsent().Text);
            b.MarkSent(b.Unsent().UpTo);
            Assert.False(b.HasUnsent);
            Assert.Equal("", b.Unsent().Text);
        }

        [Fact]
        public void Hello_Json()
        {
            Assert.Equal("{\"app\":\"ru.animego.tv\",\"version\":\"1.0.1\",\"model\":\"UE50TU7140UXRU\",\"tizen\":\"5.5\",\"user\":{\"id\":\"7\",\"name\":\"x\"}}",
                StashProtocol.Hello("1.0.1", "UE50TU7140UXRU", "5.5", new User { Id = 7, Name = "x" }));
            Assert.EndsWith("\"user\":null}", StashProtocol.Hello("1.0.1", null, "5.5", null));
        }

        [Fact]
        public void Login_Json()
        {
            Assert.Equal("{\"screen\":\"login\",\"version\":\"1.0.1\"}", StashProtocol.Status("1.0.1"));
            Assert.Equal("{\"ok\":true,\"user\":{\"id\":\"7\",\"name\":\"x\"}}", StashProtocol.LoginOk(new User { Id = 7, Name = "x" }));
            Assert.Equal("{\"ok\":false,\"error\":\"Неверный логин\"}", StashProtocol.LoginFail("Неверный логин"));
            Assert.Equal(("u", "p w"), StashProtocol.ParseLogin("{\"login\":\" u \",\"password\":\"p w\"}").Value);
            Assert.Null(StashProtocol.ParseLogin("{\"login\":\"u\"}"));
            Assert.Null(StashProtocol.ParseLogin("login=u&password=p"));
        }

        // Версия в коде совпадает с манифестом: её шлём в /hello и /status, по ней Stash сравнивает с релизом.
        [Fact]
        public void Version_MatchesManifest()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "AnimeGo.Tv", "tizen-manifest.xml"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var xml = File.ReadAllText(Path.Combine(dir.FullName, "src", "AnimeGo.Tv", "tizen-manifest.xml"));
            var m = Regex.Match(xml, "<manifest[^>]*\\sversion=\"([^\"]+)\"");
            Assert.Equal(AppInfo.Version, m.Groups[1].Value);
        }
    }
}
