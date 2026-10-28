using System;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;
using Tizen.Multimedia;

namespace AnimeGo.Tv
{
    // Один Player на один адрес: новый адрес — новый плеер, старый освобождается.
    // События приходят уже на UI-поток и только от текущего плеера.
    sealed class VideoView : IDisposable
    {
        const int PreBufferMs = 500, ReBufferMs = 2000;
        readonly Window win;
        Player player;

        public VideoView(Window win) { this.win = win; }

        public event Action Playing;
        public event Action<string> Ended;   // досмотрели или прервал ТВ
        public event Action<string> Error;

        public async Task PlayAsync(string url, int startSec)
        {
            Stop();
            var p = new Player();
            player = p;
            p.ErrorOccurred += (s, e) => FireError(p, "error " + e.Error);
            p.PlaybackCompleted += (s, e) => FireEnded(p, "completed");
            p.PlaybackInterrupted += (s, e) => FireEnded(p, "interrupted " + e.Reason);
            var started = DateTime.UtcNow;
            p.BufferingProgressChanged += (s, e) =>
                DevLog.Write("buffer " + e.Percent + "% at " + Ms(started) + " ms");
            try
            {
                p.Display = new Display(win);
                p.DisplaySettings.Mode = PlayerDisplayMode.LetterBox;
                // okcdn отвечает 400 на чужой User-Agent: ссылка подписана под тот, что был у запроса к CVH (srcAg=CHROME).
                p.UserAgent = SiteApi.UserAgent;
                p.SetSource(new MediaUriSource(url));
                // Короткий стартовый буфер: иначе плеер несколько секунд копит поток перед первым кадром.
                try { p.BufferingTime = new PlayerBufferingTime(PreBufferMs, ReBufferMs); }
                catch (Exception e) { DevLog.Write("buffering time: " + e.Message); }
                await p.PrepareAsync();
                await OnUi();
                if (p != player) return;
                DevLog.Write("video: prepared at " + Ms(started) + " ms");
                if (startSec > 0)
                {
                    await p.SetPlayPositionAsync(startSec * 1000, false);
                    await OnUi();
                    if (p != player) return;
                }
                p.Start();
                DevLog.Write("video: started at " + Ms(started) + " ms");
                App.Ui(() => { if (p == player) Playing?.Invoke(); });
            }
            catch (Exception e)
            {
                FireError(p, "prepare " + e.GetType().Name + " " + e.Message);
            }
        }

        // Продолжить на UI-потоке: состояние плеера и перемотки трогаем только оттуда, не полагаясь на SynchronizationContext.
        static Task OnUi()
        {
            var tcs = new TaskCompletionSource<bool>();
            App.Ui(() => tcs.SetResult(true));
            return tcs.Task;
        }

        static int Ms(DateTime since) => (int)(DateTime.UtcNow - since).TotalMilliseconds;

        void FireEnded(Player p, string why) => App.Ui(() => { if (p == player) Ended?.Invoke(why); });
        void FireError(Player p, string why) => App.Ui(() => { if (p == player) Error?.Invoke(why); });

        public bool IsPlaying => State == PlayerState.Playing;
        public bool IsPaused => State == PlayerState.Paused;

        PlayerState State
        {
            get
            {
                try { return player?.State ?? PlayerState.Idle; }
                catch (ObjectDisposedException) { return PlayerState.Idle; }
            }
        }

        public int PositionSec
        {
            get
            {
                try { return player == null ? 0 : player.GetPlayPosition() / 1000; }
                catch (Exception) { return 0; }   // плеер ещё не готов или уже освобождён
            }
        }

        public int DurationSec
        {
            get
            {
                try { return player == null ? 0 : player.StreamInfo.GetDuration() / 1000; }
                catch (Exception) { return 0; }
            }
        }

        public void Pause()
        {
            try { if (player?.State == PlayerState.Playing) player.Pause(); }
            catch (InvalidOperationException) { }
        }

        public void Resume()
        {
            try { if (player?.State == PlayerState.Paused) player.Start(); }
            catch (InvalidOperationException) { }
        }

        // Абсолютная позиция в секундах; неточная перемотка — к ближайшему ключевому кадру, зато быстро.
        // Пока идёт перемотка, плеер отвергает новую (SeekFailed), поэтому нажатия копятся:
        // по окончании перематываем сразу на последнюю цель. Звать с UI-потока.
        // true — эта перемотка выполнена; false — слита с уже идущей (или плеера нет), её результат придёт с той.
        public async Task<bool> Seek(int sec)
        {
            var p = player;
            if (p == null) return false;
            var dur = DurationSec;
            if (sec < 0) sec = 0;
            if (dur > 0 && sec > dur - 1) sec = dur - 1;
            if (seekTarget != null && seekOwner == p) { seekTarget = sec; return false; }
            seekTarget = sec;
            seekOwner = p;
            try
            {
                int done;
                do
                {
                    done = seekTarget.Value;
                    await p.SetPlayPositionAsync(done * 1000, false);
                    await OnUi();   // проверка цикла и сброс ниже — на UI-потоке, без лишнего ожидания между ними
                }
                while (p == player && seekOwner == p && seekTarget != done);
            }
            catch (Exception e)
            {
                DevLog.Write("seek " + sec + ": " + e.GetType().Name + " " + e.Message);
                await OnUi();
            }
            // Сбросить только свою цель: плеер мог смениться, и у нового уже своя перемотка.
            if (seekOwner == p) { seekTarget = null; seekOwner = null; }
            return true;
        }

        int? seekTarget;   // куда перематываем сейчас (или куда попросили за время перемотки)
        Player seekOwner;  // плеер, чья перемотка идёт

        // Позиция с учётом идущей перемотки — от неё считать ±10 с при частых нажатиях.
        public int TargetSec => seekTarget ?? PositionSec;

        public void Stop()
        {
            var p = player;
            player = null;
            seekTarget = null;
            seekOwner = null;
            if (p == null) return;
            try
            {
                if (p.State == PlayerState.Playing || p.State == PlayerState.Paused) p.Stop();
                if (p.State == PlayerState.Ready) p.Unprepare();
            }
            catch (InvalidOperationException) { }
            p.Dispose();
        }

        public void Dispose() => Stop();
    }
}
