using Xunit;

namespace AnimeGo.Core.Tests
{
    public class RuTextTests
    {
        [Fact]
        public void Time_UnderHour_TwoDigitMinutes() => Assert.Equal("05:08", RuText.Time(308));

        [Fact]
        public void Time_OverHour_HoursWithoutLeadingZero() => Assert.Equal("1:05:08", RuText.Time(3908));

        [Fact]
        public void Episode_HasPrefix() => Assert.Equal("Серия 22", RuText.Episode(22));

        [Fact]
        public void DubHint_TitleAndTime() =>
            Assert.Equal("Выбор запомнится для «Необъятный океан 3». Видео продолжится с 05:08.", RuText.DubHint("Необъятный океан 3", 308));

        [Fact]
        public void NoDubChoose_AsksToChoose() =>
            Assert.Equal("JAM CLUB серию 13 пока не озвучила. Выберите озвучку", RuText.NoDubChoose("JAM CLUB", 13));

        [Theory]
        [InlineData("27K", "27 тыс. оценок")]
        [InlineData("1.2K", "1,2 тыс. оценок")]
        [InlineData("3M", "3 млн оценок")]
        [InlineData("1", "1 оценка")]
        [InlineData("23", "23 оценки")]
        [InlineData("512", "512 оценок")]
        [InlineData("11", "11 оценок")]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Votes(string v, string expected) => Assert.Equal(expected, RuText.Votes(v));
    }
}
