using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    // Новые поля страницы тайтла: рейтинг, поля информации, герои, кадры, рецензии (anime_280.html, гостевая).
    public class AnimeDetailsTests
    {
        static readonly AnimeInfo Info = AnimeParser.Parse(Fixture.Read("anime_280.html"), 280);

        [Fact]
        public void Rating()
        {
            Assert.Equal("9,4", Info.Rating);
            Assert.Equal("27K", Info.Votes);
        }

        [Fact]
        public void Fields_InSiteOrder()
        {
            Assert.Equal("Тип", Info.Fields[0].Label);
            Assert.Equal("Сериал", Info.Field("Тип"));
            Assert.Equal("148", Info.Field("Эпизоды"));
            Assert.Equal("Экшен, Приключения, Фэнтези, Сёнен", Info.Field("Жанры"));
            Assert.Equal("Madhouse", Info.Field("Студия"));
            Assert.Equal("Осень 2011", Info.Field("Сезон"));
            Assert.Equal("16+", Info.Field("Возраст"));
            Assert.Equal("AniDUB, AniLiberty", Info.Field("Озвучка"));
            Assert.Null(Info.Field("Нет такого"));
        }

        [Fact]
        public void Characters()
        {
            Assert.Equal(4, Info.Characters.Count);
            Assert.Equal("Гон Фрикс", Info.Characters[0].Name);
            Assert.Equal("Мэгуми Хан", Info.Characters[0].Actor);
            Assert.Equal("Киллуа Золдик", Info.Characters[3].Name);
        }

        [Fact]
        public void Screenshots()
        {
            Assert.Equal(4, Info.Screenshots.Count);
            Assert.Equal("https://img.cdngos.com/v/640x360/screenshot/5a/70/5a709a0190df7", Info.Screenshots[0]);
        }

        [Fact]
        public void Reviews()
        {
            Assert.Equal(2, Info.Reviews.Count);
            var r = Info.Reviews[0];
            Assert.Equal("Хантер х Хантер и незабываемые впечатления", r.Title);
            Assert.Equal("0andrey_izm0", r.Author);
            Assert.Equal("25 января 2026", r.Date);
            Assert.Equal("66 из 76 сочли этот отзыв полезным", r.Useful);
            Assert.Equal("10", r.Score);
            Assert.StartsWith("Не хочу затягивать этот отзыв", r.Text);
            Assert.Contains("\n\n", r.Text);
            Assert.DoesNotContain("<", r.Text);
            Assert.DoesNotContain("Развернуть", r.Text);
            Assert.False(r.Spoilers);
            Assert.True(Info.Reviews[1].Spoilers);
        }

        // Старая страница без рецензий и героев — пустые списки, не исключения.
        [Fact]
        public void OldPage_Empty()
        {
            var a = AnimeParser.Parse(Fixture.Read("anime_3547.html"), 3547);
            Assert.Empty(a.Reviews);
            Assert.Equal(4, a.Screenshots.Count);
            Assert.NotNull(a.Rating);
        }

        // Онгоинг: «Следующий эпизод» и график (anime_3815.html, гостевая страница 29.10.2026).
        [Fact]
        public void NextEpisode_AndSchedule()
        {
            var a = AnimeParser.Parse(Fixture.Read("anime_3815.html"), 3815);
            Assert.Equal(4, a.Next.Number);
            Assert.Equal("30 окт. 2026 пт 16:30", a.Next.When);
            Assert.Equal("через 19 часов", a.Next.In);
            Assert.Equal(new[] { 4, 3, 2, 1 }, a.Schedule.Select(x => x.Number));
            Assert.Equal("30 октября 2026", a.Schedule[0].Date);
            Assert.Equal("через 19 часов", a.Schedule[0].Status);
        }

        [Fact]
        public void Released_NoNext() => Assert.Null(Info.Next);
    }
}
