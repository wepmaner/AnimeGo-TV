using System;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class ProgressTrackerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void LastSent_BeforeFirstSend_IsMinusOne()
        {
            Assert.Equal(-1, new ProgressTracker(1400).LastSent);
        }

        [Fact]
        public void Tick_FirstPositionAboveZero_SendsOnce()
        {
            var t = new ProgressTracker(1400);
            Assert.False(t.Tick(0, T0));
            Assert.True(t.Tick(1, T0.AddSeconds(1)));
            Assert.Equal(1, t.LastSent);
            Assert.False(t.Tick(3, T0.AddSeconds(3)));
        }

        [Fact]
        public void Tick_SendsAfterFiveSecondsAndFiveSecondsOfProgress()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            Assert.False(t.Tick(3, T0.AddSeconds(2)));
            Assert.True(t.Tick(7, T0.AddSeconds(6)));
            Assert.Equal(7, t.LastSent);
        }

        [Fact]
        public void Tick_PositionFarEnoughButTimeTooShort_DoesNotSend()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            Assert.False(t.Tick(20, T0.AddSeconds(3)));
        }

        [Fact]
        public void OnPause_RightAfterSend_DoesNotSend()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            Assert.False(t.OnPause());
        }

        [Fact]
        public void OnPause_PositionChangedSinceLastSend_Sends()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            t.Tick(3, T0.AddSeconds(2));
            Assert.True(t.OnPause());
            Assert.Equal(3, t.LastSent);
            Assert.False(t.OnPause());
        }

        [Fact]
        public void OnSeek_SendsOldPositionAndRemembersNew()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            t.Tick(3, T0.AddSeconds(2));
            Assert.True(t.OnSeek(100));
            Assert.Equal(3, t.LastSent);
            Assert.True(t.OnPause());
            Assert.Equal(100, t.LastSent);
        }

        [Fact]
        public void OnSeek_OldPositionEqualsLastSent_DoesNotSend()
        {
            var t = new ProgressTracker(1400);
            t.Tick(10, T0);
            Assert.False(t.OnSeek(100));
        }

        [Fact]
        public void OnExit_PositionEqualsLastSent_StillSends()
        {
            var t = new ProgressTracker(1400);
            t.Tick(10, T0);
            Assert.True(t.OnExit());
        }

        [Fact]
        public void OnEnded_PositionAboveZero_Sends()
        {
            var t = new ProgressTracker(1400);
            t.Tick(10, T0);
            Assert.True(t.OnEnded());
        }

        [Fact]
        public void OnExit_AtSecondThree_SendsOnceWithLastSentThree()
        {
            var t = new ProgressTracker(1400);
            t.Tick(1, T0);
            t.Tick(3, T0.AddSeconds(2));
            Assert.True(t.OnExit());
            Assert.Equal(3, t.LastSent);
        }

        [Fact]
        public void OnExit_WithoutAnyTick_DoesNotSend()
        {
            Assert.False(new ProgressTracker(1400).OnExit());
        }

        [Fact]
        public void OnExit_PositionZero_DoesNotSend()
        {
            var t = new ProgressTracker(1400);
            t.Tick(0, T0);
            Assert.False(t.OnExit());
            Assert.False(t.OnEnded());
        }
    }

    public class WatchedTrackerTests
    {
        [Fact]
        public void Tick_79SequentialSeconds_DoesNotFire()
        {
            var w = new WatchedTracker(100);
            for (int p = 0; p <= 79; p++) Assert.False(w.Tick(p));
            Assert.False(w.Fired);
        }

        [Fact]
        public void Tick_80SequentialSeconds_FiresOnceThenFalse()
        {
            var w = new WatchedTracker(100);
            for (int p = 0; p <= 79; p++) w.Tick(p);
            Assert.True(w.Tick(80));
            Assert.True(w.Fired);
            Assert.False(w.Tick(81));
            Assert.False(w.Tick(82));
        }

        [Fact]
        public void Tick_SeekForward_IsNotCounted()
        {
            var w = new WatchedTracker(100);
            for (int p = 0; p <= 10; p++) w.Tick(p);
            Assert.False(w.Tick(90));
            for (int p = 91; p <= 95; p++) Assert.False(w.Tick(p));
            Assert.False(w.Fired);
        }

        [Fact]
        public void Tick_ReplayingSameRange_DoesNotFire()
        {
            var w = new WatchedTracker(100);
            for (int p = 0; p <= 40; p++) w.Tick(p);
            for (int p = 0; p <= 40; p++) Assert.False(w.Tick(p));
            Assert.False(w.Fired);
        }

        [Fact]
        public void Tick_TwoDistinctRangesCovering80Percent_Fires()
        {
            var w = new WatchedTracker(100);
            for (int p = 0; p <= 40; p++) Assert.False(w.Tick(p));
            Assert.False(w.Tick(50));
            for (int p = 51; p < 90; p++) Assert.False(w.Tick(p));
            Assert.True(w.Tick(90));
        }

        [Fact]
        public void Tick_ZeroDuration_NeverFires()
        {
            var w = new WatchedTracker(0);
            for (int p = 0; p < 50; p++) Assert.False(w.Tick(p));
            Assert.False(w.Fired);
        }
    }
}
