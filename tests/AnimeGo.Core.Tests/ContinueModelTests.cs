using System;
using System.Collections.Generic;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class ContinueModelTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

        private static ContinueCard Card(int id, int ep = 1, int pos = 100) =>
            new ContinueCard { AnimeId = id, Slug = "a" + id, Title = "T" + id, EpisodeNumber = ep, PositionSec = pos, DurationSec = 1400 };

        private static Prefs Empty() =>
            Prefs.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json"));

        private static List<int> Ids(IEnumerable<ContinueCard> cs)
        {
            var r = new List<int>();
            foreach (var c in cs) r.Add(c.AnimeId);
            return r;
        }

        [Fact]
        public void NewCard_IsActive_AndRemembered()
        {
            var p = Empty();
            var r = ContinueModel.Split(new[] { Card(1) }, p, "u1", Now);
            Assert.Single(r.Active);
            Assert.Empty(r.Stale);
            Assert.Equal(Now, p.Seen("u1", 1).AtUtc);
            Assert.Equal(100, p.Seen("u1", 1).Seconds);
        }

        [Fact]
        public void UnchangedFor15Days_IsStale_AndSeenKept()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1) }, p, "u1", Now);
            var r = ContinueModel.Split(new[] { Card(1) }, p, "u1", Now.AddDays(15));
            Assert.Empty(r.Active);
            Assert.Single(r.Stale);
            Assert.Equal(Now, p.Seen("u1", 1).AtUtc);
        }

        [Fact]
        public void Exactly14Days_IsStale_13Days_IsActive()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1) }, p, "u1", Now);
            Assert.Single(ContinueModel.Split(new[] { Card(1) }, p, "u1", Now.AddDays(13)).Active);
            Assert.Single(ContinueModel.Split(new[] { Card(1) }, p, "u1", Now.AddDays(14)).Stale);
        }

        [Fact]
        public void ChangedPosition_After15Days_IsActive_AndTimeReset()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1) }, p, "u1", Now);
            var later = Now.AddDays(15);
            var r = ContinueModel.Split(new[] { Card(1, pos: 500) }, p, "u1", later);
            Assert.Single(r.Active);
            Assert.Empty(r.Stale);
            Assert.Equal(later, p.Seen("u1", 1).AtUtc);
            Assert.Equal(500, p.Seen("u1", 1).Seconds);
        }

        [Fact]
        public void ChangedEpisode_IsActive()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1, ep: 1) }, p, "u1", Now);
            var r = ContinueModel.Split(new[] { Card(1, ep: 2) }, p, "u1", Now.AddDays(20));
            Assert.Single(r.Active);
            Assert.Equal(2, p.Seen("u1", 1).Episode);
        }

        [Fact]
        public void VanishedCard_SeenRemoved_OtherUsersUntouched()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1), Card(2) }, p, "u1", Now);
            p.SetSeen("guest", 1, 1, 1, Now);
            ContinueModel.Split(new[] { Card(2) }, p, "u1", Now.AddDays(1));
            Assert.Null(p.Seen("u1", 1));
            Assert.NotNull(p.Seen("u1", 2));
            Assert.NotNull(p.Seen("guest", 1));
        }

        [Fact]
        public void SiteOrderKept_InBothRows()
        {
            var p = Empty();
            ContinueModel.Split(new[] { Card(1), Card(2), Card(3) }, p, "u1", Now);
            var r = ContinueModel.Split(new[] { Card(3), Card(2, pos: 9), Card(1) }, p, "u1", Now.AddDays(15));
            Assert.Equal(new[] { 2 }, Ids(r.Active));
            Assert.Equal(new[] { 3, 1 }, Ids(r.Stale));
        }
    }
}
