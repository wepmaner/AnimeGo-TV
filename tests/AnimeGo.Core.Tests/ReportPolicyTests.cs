using System;
using System.Collections.Generic;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class ReportPolicyTests
    {
        private const int Duration = 1400;
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Обвязка как ProgressReporter: время идёт на 1 с за тик, отправки и отметки копятся в списках.
        private sealed class Run
        {
            public readonly ReportPolicy P;
            public readonly List<int> Posts = new List<int>();
            public int WatchedPosts;
            private DateTime now = T0;

            public Run(int startSec = 0, bool canSend = true, bool canMark = true, bool alreadyWatched = false, int duration = Duration)
            {
                P = new ReportPolicy(duration, startSec, canSend, canMark, alreadyWatched, new WatchedTracker(duration));
            }

            public int? Tick(int pos)
            {
                now = now.AddSeconds(1);
                var r = P.Tick(pos, now, out var mark);
                if (mark) WatchedPosts++;
                return Add(r);
            }

            public void Play(int from, int to) { for (int s = from; s <= to; s++) Tick(s); }

            public int? Pause() => Add(P.Pause());
            public int? Seek(int t) => Add(P.Seek(t));
            public int? Ended() => Add(P.Ended());
            public int? Exit() => Add(P.Exit());

            private int? Add(int? r) { if (r != null) Posts.Add(r.Value); return r; }
        }

        [Fact]
        public void Resume_FirstRealPosition594_NoPostUntil599()
        {
            // Возобновили с 600, плеер встал на ключевой кадр 594: 5 с считаются от 594, пол 590 не мешает.
            var r = new Run(startSec: 600);
            for (int s = 594; s <= 598; s++) Assert.Null(r.Tick(s));
            Assert.Equal(599, r.Tick(599));
            Assert.Equal(new[] { 599 }, r.Posts);
        }

        [Fact]
        public void Resume_PlayerFarBeforeFloor_NoPostUntilFloor()
        {
            // Плеер встал на 580 при старте с 600: 5 с проходят на 585, но ниже пола 590 позиция не уходит.
            var r = new Run(startSec: 600);
            r.Play(580, 589);
            Assert.Empty(r.Posts);
            Assert.Equal(590, r.Tick(590));
        }

        [Fact]
        public void ZeroPositionsBeforeStart_DoNotStartGate()
        {
            var r = new Run();
            r.Tick(0); r.Tick(0);
            r.Play(1, 5);
            Assert.Empty(r.Posts);
            Assert.Equal(6, r.Tick(6));
        }

        [Fact]
        public void ExitAt3Seconds_NoPost()
        {
            var r = new Run();
            r.Play(1, 3);
            Assert.Null(r.Exit());
            Assert.Empty(r.Posts);
        }

        [Fact]
        public void ExitAt8Seconds_ExitPostsExactlyOnce()
        {
            // Первая отправка — тик на 6 (1 + 5 с), выход на 8 шлёт 8 один раз; повторный Exit молчит.
            var r = new Run();
            r.Play(1, 8);
            Assert.Equal(new[] { 6 }, r.Posts);
            Assert.Equal(8, r.Exit());
            Assert.Null(r.Exit());
            Assert.Null(r.Tick(9));
            Assert.Equal(new[] { 6, 8 }, r.Posts);
        }

        [Fact]
        public void ExitRightAfterTickThatSent_NoDuplicate()
        {
            var r = new Run();
            r.Play(1, 6);
            Assert.Null(r.Exit());
            Assert.Equal(new[] { 6 }, r.Posts);
        }

        [Fact]
        public void EndedThenExitAtSamePosition_OnePost()
        {
            var r = new Run();
            r.Play(1, 8);
            r.Posts.Clear();
            Assert.Equal(8, r.Ended());
            Assert.Null(r.Exit());
            Assert.Equal(new[] { 8 }, r.Posts);
        }

        [Fact]
        public void RewindBelowFloorAfterSeek_Posts()
        {
            var r = new Run(startSec: 600);
            r.Play(600, 612);   // ушли 605 и 610, 612 — ещё нет
            r.Posts.Clear();
            Assert.Equal(612, r.Seek(100));   // уходит позиция, с которой перематывают
            r.Play(100, 106);
            Assert.Contains(r.Posts, p => p >= 100 && p < 590);   // ниже прежнего пола — уходит
            Assert.Equal(106, r.Exit());
        }

        [Fact]
        public void SeekWhileBelowFloor_ThenPause_SendsSeekTargetNotOlderPosition()
        {
            var r = new Run(startSec: 600);
            r.Play(595, 602);                 // ушла 600, трекер знает 602
            r.Tick(588);                      // плеер сам отскочил ниже пола: не шлём
            Assert.Equal(new[] { 600 }, r.Posts);
            Assert.Null(r.Pause());           // ниже пола и пауза молчит
            Assert.Null(r.Seek(700));         // с 588 не шлём, но трекер знает новое место
            Assert.Equal(700, r.Pause());     // раньше уходила старая 602
            Assert.Equal(new[] { 600, 700 }, r.Posts);
        }

        [Fact]
        public void SeekBeforeGate_RestartsGateFromWherePlayerLands()
        {
            var r = new Run();
            r.Play(1, 3);
            Assert.Null(r.Seek(300));
            r.Play(302, 306);
            Assert.Empty(r.Posts);
            Assert.Equal(307, r.Tick(307));
        }

        [Fact]
        public void Guest_SendsNothingAndMarksNothing()
        {
            var r = new Run(canSend: false, canMark: false, duration: 100);
            r.Play(1, 100);
            r.Pause(); r.Seek(10); r.Ended(); r.Exit();
            Assert.Empty(r.Posts);
            Assert.Equal(0, r.WatchedPosts);
        }

        [Fact]
        public void NoTranslationId_SendsNoProgress()
        {
            // Нет translationId: canSendProgress = false; отметка «просмотрено» от озвучки не зависит.
            var r = new Run(canSend: false, canMark: true, duration: 100);
            r.Play(1, 50);
            r.Pause(); r.Seek(70); r.Play(70, 90); r.Ended(); r.Exit();
            Assert.Empty(r.Posts);
        }

        [Fact]
        public void UnknownDuration_SendsNothing()
        {
            var r = new Run(duration: 0);
            r.Play(1, 20);
            r.Exit();
            Assert.Empty(r.Posts);
            Assert.Equal(0, r.WatchedPosts);
        }

        [Fact]
        public void AlreadyWatchedEpisode_NoWatchedPost()
        {
            var r = new Run(alreadyWatched: true, duration: 100);
            r.Play(1, 100);
            Assert.Equal(0, r.WatchedPosts);
        }

        [Fact]
        public void EightyPercentCoverage_ExactlyOneWatched()
        {
            var r = new Run(duration: 100);
            r.Play(1, 79);
            Assert.Equal(0, r.WatchedPosts);
            r.Tick(80);
            Assert.Equal(1, r.WatchedPosts);
            r.Play(81, 100);
            r.Seek(0); r.Play(1, 100);
            Assert.Equal(1, r.WatchedPosts);
        }

        [Fact]
        public void WatchedTrackerSharedAcrossSources_FiresOncePerEpisode()
        {
            var w = new WatchedTracker(100);
            var a = new ReportPolicy(100, 0, true, true, false, w);
            int marks = 0;
            for (int s = 1; s <= 50; s++) { a.Tick(s, T0.AddSeconds(s), out var m); if (m) marks++; }
            a.Exit();
            var b = new ReportPolicy(100, 50, true, true, false, w);   // сменили источник, та же серия
            for (int s = 51; s <= 100; s++) { b.Tick(s, T0.AddSeconds(s), out var m); if (m) marks++; }
            Assert.Equal(1, marks);
        }
    }
}
