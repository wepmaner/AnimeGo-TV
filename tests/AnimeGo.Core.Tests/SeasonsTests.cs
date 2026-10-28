using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class SeasonsTests
    {
        static readonly AnimeInfo Info = AnimeParser.Parse(Fixture.Read("anime_3547.html"), 3547);

        static AnimeInfo With(string title, params RelatedTitle[] related) =>
            new AnimeInfo { Id = 100, Title = title, Related = related };

        static RelatedTitle R(int id, string title, string kind = "ТВ Сериал") =>
            new RelatedTitle { Id = id, Title = title, Kind = kind };

        [Fact]
        public void Build_Fixture_HasSeasons2And3()
        {
            var s = Seasons.Build(Info);
            Assert.True(s.Count >= 2);
            Assert.Equal(2, s.Single(x => x.AnimeId == 2817).Order);
            var cur = s.Single(x => x.AnimeId == 3547);
            Assert.Equal(3, cur.Order);
            Assert.True(cur.IsCurrent);
            Assert.Equal(s.OrderBy(x => x.Order).Select(x => x.AnimeId), s.Select(x => x.AnimeId));
        }

        [Fact]
        public void Build_OtherBaseName_Excluded()
        {
            var info = new AnimeInfo
            {
                Id = Info.Id, Title = Info.Title,
                Related = Info.Related.Concat(new[] { new RelatedTitle { Id = 1, Title = "Другое" } }).ToList(),
            };
            Assert.DoesNotContain(Seasons.Build(info), x => x.AnimeId == 1);
        }

        [Fact]
        public void Build_Ova_Excluded()
        {
            var s = Seasons.Build(With("Тест 2", R(1, "Тест", "ТВ Сериал"), R(2, "Тест 3", "OVA"), R(3, "Тест 4", "Фильм"), R(4, "Тест 5", "Спешл")));
            Assert.Equal(new[] { 1, 100 }, s.Select(x => x.AnimeId).ToArray());
        }

        [Fact]
        public void Build_Ona_Included_AndKindMatchedByWholeWord()
        {
            var s = Seasons.Build(With("Тест 3", R(1, "Тест 2", "ONA"), R(2, "Тест", "Сериал"), R(3, "Тест 4", "Кинофильм")));
            Assert.Equal(new[] { 2, 1, 100, 3 }, s.Select(x => x.AnimeId).ToArray());
        }

        [Fact]
        public void Build_NoDigit_OrderIs1()
        {
            var s = Seasons.Build(With("Тест: 2", R(1, "ТЕСТ")));
            Assert.Equal(1, s.Single(x => x.AnimeId == 1).Order);
        }

        [Fact]
        public void Build_SecondPart_UsesFirstNumber()
        {
            var s = Seasons.Build(With("Тест", R(1, "Тест 2 часть 2")));
            Assert.Equal(2, s.Single(x => x.AnimeId == 1).Order);
            Assert.Equal(1, s.Single(x => x.IsCurrent).Order);
        }

        [Fact]
        public void Build_SameOrder_KeepsRelatedOrder()
        {
            var s = Seasons.Build(With("Тест 2", R(1, "Тест 2 часть 2"), R(2, "Тест 2 часть 3")));
            Assert.Equal(new[] { 100, 1, 2 }, s.Select(x => x.AnimeId).ToArray());
        }
    }
}
