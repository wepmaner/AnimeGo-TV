using System;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class DataCacheTests
    {
        static DateTime now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        static DataCache New(int max = 120) => new DataCache(() => now, max);

        [Fact]
        public void Miss_WhenEmpty()
        {
            var c = New();
            Assert.False(c.TryGet("home", out HomePage _, out var age));
            Assert.Equal(0, age);
        }

        [Fact]
        public void Hit_ReturnsValueAndAge()
        {
            var c = New();
            var page = new HomePage { SeasonTitle = "осень" };
            c.Put("home", page);
            now = now.AddSeconds(42);
            Assert.True(c.TryGet("home", out HomePage got, out var age));
            Assert.Same(page, got);
            Assert.Equal(42, age);
        }

        [Fact]
        public void WrongType_IsMiss()
        {
            var c = New();
            c.Put("x", new HomePage());
            Assert.False(c.TryGet("x", out AnimeInfo _, out _));
        }

        [Fact]
        public void Put_Replaces_AndResetsAge()
        {
            var c = New();
            c.Put("k", 1);
            now = now.AddSeconds(10);
            c.Put("k", 2);
            Assert.True(c.TryGet("k", out int v, out var age));
            Assert.Equal(2, v);
            Assert.Equal(0, age);
        }

        [Fact]
        public void Remove_And_Clear()
        {
            var c = New();
            c.Put("a", 1);
            c.Put("b", 2);
            c.Remove("a");
            Assert.False(c.TryGet("a", out int _, out _));
            Assert.True(c.TryGet("b", out int _, out _));
            c.Clear();
            Assert.False(c.TryGet("b", out int _, out _));
            Assert.Equal(0, c.Count);
        }

        [Fact]
        public void Overflow_EvictsOldest()
        {
            var c = New(3);
            c.Put("a", 1);
            now = now.AddSeconds(1);
            c.Put("b", 2);
            now = now.AddSeconds(1);
            c.Put("c", 3);
            now = now.AddSeconds(1);
            c.Put("a", 11);   // обновили — стала самой свежей
            now = now.AddSeconds(1);
            c.Put("d", 4);    // четвёртая: уходит b
            Assert.Equal(3, c.Count);
            Assert.False(c.TryGet("b", out int _, out _));
            Assert.True(c.TryGet("a", out int a, out _));
            Assert.Equal(11, a);
            Assert.True(c.TryGet("c", out int _, out _));
            Assert.True(c.TryGet("d", out int _, out _));
        }

        [Fact]
        public void NullValue_IsNotStored()
        {
            var c = New();
            c.Put("n", null);
            Assert.False(c.TryGet("n", out object _, out _));
            Assert.Equal(0, c.Count);
        }
    }
}
