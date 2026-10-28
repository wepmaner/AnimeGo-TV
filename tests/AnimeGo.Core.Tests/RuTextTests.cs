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
    }
}
