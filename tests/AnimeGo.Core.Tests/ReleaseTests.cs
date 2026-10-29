using Xunit;

namespace AnimeGo.Core.Tests
{
    public class ReleaseTests
    {
        static readonly AnimeInfo Ongoing = AnimeParser.Parse(Fixture.Read("anime_3815.html"), 3815);

        [Fact]
        public void NextEpisode_WithTime()
        {
            var r = Release.Of(Ongoing, 4).Value;
            Assert.Equal("30 окт. пт 16:30", r.When);
            Assert.Equal("через 19 часов", r.In);
            Assert.Equal("выйдет 30 окт. пт 16:30", Release.Label(r));
        }

        [Fact]
        public void Aired_Null()
        {
            Assert.Null(Release.Of(Ongoing, 3));
            Assert.Null(Release.Of(Ongoing, 1));
        }

        [Fact]
        public void AfterNext_UnknownDate()
        {
            var r = Release.Of(Ongoing, 5).Value;
            Assert.Null(r.When);
            Assert.Equal("ещё не вышла", Release.Label(r));
        }

        [Fact]
        public void ScheduleOnly_DateWithoutYear()
        {
            var a = new AnimeInfo { Schedule = new[] { new ScheduledEpisode { Number = 7, Date = "6 ноября 2026", Status = "через 8 дней" } } };
            Assert.Equal(("6 ноября", "через 8 дней"), Release.Of(a, 7).Value);
            Assert.Null(Release.Of(a, 6));
            Assert.Null(Release.Of(null, 1));
        }
    }
}
