using System;
using System.IO;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class PrefsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "animego-prefs-" + Guid.NewGuid().ToString("N"));
        private string PathOf => Path.Combine(_dir, "prefs.json");

        public PrefsTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Load_MissingFile_GivesDefaults()
        {
            var p = Prefs.Load(PathOf);
            Assert.Equal("auto", p.Quality);
            Assert.False(p.SkippedLogin);
            Assert.Null(p.Seen("u1", 1));
        }

        [Fact]
        public void OpeningAt_RoundTrip_PerUserAndTitle()
        {
            var p = Prefs.Load(PathOf);
            Assert.Null(p.OpeningAt("u1", 3547));
            p.SetMainDub("u1", 3547, "59");
            p.SetOpeningAt("u1", 3547, 92);
            p.SetOpeningAt("u1", 100, 0);
            p.Save();

            var q = Prefs.Load(PathOf);
            Assert.Equal(92, q.OpeningAt("u1", 3547));
            Assert.Equal(0, q.OpeningAt("u1", 100));
            Assert.Equal("59", q.Dub("u1", 3547).Main);   // озвучки той же записи не задеты
            Assert.Null(q.Dub("u1", 100).Main);
            Assert.Null(q.OpeningAt("guest", 3547));
            Assert.Null(q.OpeningAt("u1", 1));
        }

        [Fact]
        public void OpeningAt_OldFileWithoutField_IsNull()
        {
            File.WriteAllText(PathOf, "{\"quality\":\"auto\",\"users\":[{\"key\":\"u1\",\"dubs\":[{\"animeId\":5,\"main\":\"7\"}],\"seen\":[]}]}");
            var p = Prefs.Load(PathOf);
            Assert.Equal("7", p.Dub("u1", 5).Main);
            Assert.Null(p.OpeningAt("u1", 5));
        }

        [Fact]
        public void Load_BrokenJson_GivesDefaultsAndSavable()
        {
            File.WriteAllText(PathOf, "{ не json");
            var p = Prefs.Load(PathOf);
            Assert.Equal("auto", p.Quality);
            p.Cookies = "a=b";
            p.Save();
            Assert.Equal("a=b", Prefs.Load(PathOf).Cookies);
        }

        [Fact]
        public void RoundTrip_ThroughRealFile()
        {
            var p = Prefs.Load(PathOf);
            p.Cookies = "s=1";
            p.SkippedLogin = true;
            p.Quality = "720";
            p.SetMainDub("u1", 3547, "59");
            p.SetFallbackDub("u1", 3547, "12");
            var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            p.SetSeen("u1", 3547, 4, 300, at);
            p.Save();

            var q = Prefs.Load(PathOf);
            Assert.Equal("s=1", q.Cookies);
            Assert.True(q.SkippedLogin);
            Assert.Equal("720", q.Quality);
            Assert.Equal("59", q.Dub("u1", 3547).Main);
            Assert.Equal("12", q.Dub("u1", 3547).Fallback);
            Assert.Null(q.Dub("guest", 3547).Main);
            var s = q.Seen("u1", 3547);
            Assert.Equal(4, s.Episode);
            Assert.Equal(300, s.Seconds);
            Assert.Equal(at, s.AtUtc);
            Assert.Equal(DateTimeKind.Utc, s.AtUtc.Kind);
            Assert.Null(q.Seen("guest", 3547));
        }

        [Fact]
        public void Save_Twice_Overwrites_AndLeavesNoTempFiles()
        {
            var p = Prefs.Load(PathOf);
            p.Quality = "480";
            p.Save();
            p.Quality = "1080";
            p.Save();
            Assert.Equal("1080", Prefs.Load(PathOf).Quality);
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void Save_CreatesMissingDirectory()
        {
            var path = Path.Combine(_dir, "sub", "prefs.json");
            var p = Prefs.Load(path);
            p.Quality = "480";
            p.Save();
            Assert.Equal("480", Prefs.Load(path).Quality);
        }

        [Fact]
        public void Dub_UnknownIsEmptyObject()
        {
            var d = Prefs.Load(PathOf).Dub("u1", 1);
            Assert.NotNull(d);
            Assert.Null(d.Main);
            Assert.Null(d.Fallback);
        }

        [Fact]
        public void SetFallback_DoesNotTouchMain()
        {
            var p = Prefs.Load(PathOf);
            p.SetMainDub("u1", 1, "a");
            p.SetFallbackDub("u1", 1, "b");
            Assert.Equal("a", p.Dub("u1", 1).Main);
            Assert.Equal("b", p.Dub("u1", 1).Fallback);
        }

        [Fact]
        public void SetMain_EqualToFallback_ClearsFallback()
        {
            var p = Prefs.Load(PathOf);
            p.SetMainDub("u1", 1, "a");
            p.SetFallbackDub("u1", 1, "b");
            p.SetMainDub("u1", 1, "b");
            Assert.Equal("b", p.Dub("u1", 1).Main);
            Assert.Null(p.Dub("u1", 1).Fallback);
        }

        [Fact]
        public void SetSeen_Replaces_RemoveSeen_Deletes()
        {
            var p = Prefs.Load(PathOf);
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            p.SetSeen("u1", 1, 1, 10, t);
            p.SetSeen("u1", 1, 2, 20, t.AddDays(1));
            Assert.Equal(2, p.Seen("u1", 1).Episode);
            p.RemoveSeen("u1", 1);
            Assert.Null(p.Seen("u1", 1));
            p.RemoveSeen("u1", 1);
        }
    }
}
