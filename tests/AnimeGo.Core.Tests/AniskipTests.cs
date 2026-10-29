using Xunit;

namespace AnimeGo.Core.Tests
{
    public class AniskipTests
    {
        [Fact]
        public void Url() =>
            Assert.Equal("https://api.aniskip.com/v2/skip-times/62542/1?types=op&types=ed&episodeLength=0", Aniskip.Url(62542, 1));

        // ep 1: op 163.75–248.726 при episodeLength 1420 (ED в ответе — от другого релиза, его не берём).
        [Fact]
        public void Opening_SameLength()
        {
            var r = Aniskip.ParseOpening(Fixture.Read("aniskip_62542_1.json"), 1420);
            Assert.Equal(164, r.StartSec);
            Assert.Equal(249, r.EndSec);
        }

        // Студийная Банда: 1440 с — вставка 20 с в начале, опенинг сдвинут.
        [Fact]
        public void Opening_ShiftedByDubInsert()
        {
            var r = Aniskip.ParseOpening(Fixture.Read("aniskip_62542_1.json"), 1440);
            Assert.Equal(184, r.StartSec);
            Assert.Equal(269, r.EndSec);
        }

        [Fact]
        public void Opening_ShiftTooBig_Null() =>
            Assert.Null(Aniskip.ParseOpening(Fixture.Read("aniskip_62542_1.json"), 1500));

        [Fact]
        public void Opening_DubShorter_Null() =>
            Assert.Null(Aniskip.ParseOpening(Fixture.Read("aniskip_62542_1.json"), 1400));

        [Fact]
        public void Opening_AtStart()
        {
            var r = Aniskip.ParseOpening(Fixture.Read("aniskip_62542_2.json"), 1440);
            Assert.Equal(0, r.StartSec);
            Assert.Equal(95, r.EndSec);
        }

        [Fact]
        public void NotFound_Null() =>
            Assert.Null(Aniskip.ParseOpening(Fixture.Read("aniskip_62542_5.json"), 1440));

        [Theory]
        [InlineData("")]
        [InlineData("<html>")]
        [InlineData("{\"found\":true}")]
        [InlineData("{\"found\":true,\"results\":[{\"skipType\":\"ed\",\"interval\":{\"startTime\":1300,\"endTime\":1400},\"episodeLength\":1420}]}")]
        [InlineData("{\"found\":true,\"results\":[{\"skipType\":\"op\",\"interval\":{\"startTime\":100,\"endTime\":90},\"episodeLength\":1420}]}")]
        public void Garbage_Null(string json) => Assert.Null(Aniskip.ParseOpening(json, 1420));

        [Fact]
        public void UnknownDuration_Null() =>
            Assert.Null(Aniskip.ParseOpening(Fixture.Read("aniskip_62542_1.json"), 0));
    }
}
