using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Журнал приложения для Stash (лаунчер на ПК) и для отладки (scripts/devlog.mjs) — в Debug и в Release.
    // У ТВ нет доступного журнала (sdb dlog выключен), поэтому строки отправляет само приложение:
    // POST http://<developerIP>:8778/log, по строке на запись. developerIP — ПК из режима разработчика, его отдаёт
    // REST-API самого ТВ (127.0.0.1:8001/api/v2/). Режим разработчика выключен или IP пуст — никуда не шлём.
    // Первым уходит /hello (после запуска и после каждого входа/выхода), затем накопленное (последние 500 строк).
    // Stash может быть выключен: ошибки глотаем, после неудачи повторяем не чаще раза в 30 с, UI не ждёт.
    // Пароль и куки в журнал не пишем никогда.
    static class DevLog
    {
        const int LogPort = 8778, ApiPort = 8001;
        const int RetryMs = 30_000;
        static readonly LogBuffer buffer = new LogBuffer();
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        static readonly SemaphoreSlim wake = new SemaphoreSlim(0);
        static readonly object gate = new object();
        static int signaled, started;
        static string host;          // куда слать; null — ещё не знаем
        static bool disabled;        // режим разработчика выключен — до перезапуска не шлём
        static bool helloQueued, helloSent;
        static User helloUser;
        static string model, tizen;
        static long retryAt, discoverAt;

        // Звать один раз при запуске.
        public static void Start()
        {
            if (Interlocked.Exchange(ref started, 1) == 1) return;
            try
            {
                Tizen.System.Information.TryGetValue("http://tizen.org/feature/platform.version", out string v);
                tizen = v;
            }
            catch (Exception) { }
            Task.Run(Pump);
        }

        public static void Write(string line)
        {
            buffer.Add(Stamp() + line);
            Wake();
        }

        // Для падений: процесс может умереть раньше, чем сработает фоновая отправка, поэтому ждём её до 3 с.
        public static void WriteNow(string line)
        {
            buffer.Add(Stamp() + line);
            var h = host;
            if (h == null || disabled) return;
            try { SendLogAsync(h).Wait(3000); } catch (Exception) { }
        }

        // Кто вошёл (null — гость). Уходит первым сообщением.
        public static void Hello(User user)
        {
            lock (gate)
            {
                helloUser = user;
                helloQueued = true;
            }
            Wake();
        }

        static void Wake()
        {
            if (Interlocked.Exchange(ref signaled, 1) == 0) wake.Release();
        }

        static async Task Pump()
        {
            while (true)
            {
                await wake.WaitAsync(RetryMs).ConfigureAwait(false);
                Interlocked.Exchange(ref signaled, 0);
                try { await Step().ConfigureAwait(false); }
                catch (Exception) { retryAt = Clock.Ms + RetryMs; }
            }
        }

        static async Task Step()
        {
            if (disabled) return;
            var now = Clock.Ms;
            if (host == null)
            {
                if (now < discoverAt) return;
                discoverAt = now + RetryMs;
                await Discover().ConfigureAwait(false);
                if (host == null) return;
            }
            if (now < retryAt) return;

            bool sendHello;
            User user;
            lock (gate)
            {
                sendHello = helloQueued;
                user = helloUser;
                helloQueued = false;
            }
            if (sendHello)
            {
                var json = StashProtocol.Hello(AppInfo.Version, model, tizen, user);
                if (!await PostAsync(host, "/hello", new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false))
                {
                    lock (gate) if (!helloQueued) { helloQueued = true; helloUser = user; }   // не затереть более свежий
                    retryAt = Clock.Ms + RetryMs;
                    return;
                }
                helloSent = true;
            }
            if (!helloSent || !buffer.HasUnsent) return;   // строки ждут первого /hello
            if (!await SendLogAsync(host).ConfigureAwait(false)) retryAt = Clock.Ms + RetryMs;
        }

        static async Task<bool> SendLogAsync(string h)
        {
            var (text, upTo) = buffer.Unsent();
            if (text.Length == 0) return true;
            var ok = await PostAsync(h, "/log", new StringContent(text, Encoding.UTF8, "text/plain")).ConfigureAwait(false);
            if (ok) buffer.MarkSent(upTo);
            return ok;
        }

        static async Task<bool> PostAsync(string h, string path, HttpContent body)
        {
            try
            {
                using (var res = await http.PostAsync("http://" + h + ":" + LogPort + path, body).ConfigureAwait(false))
                    return res.IsSuccessStatusCode;
            }
            catch (Exception) { return false; }
        }

        // developerIP из REST-API телевизора: сначала 127.0.0.1, затем свои адреса.
        static async Task Discover()
        {
            foreach (var h in new[] { "127.0.0.1" }.Concat(OwnIps()))
            {
                string json;
                try { json = await http.GetStringAsync("http://" + h + ":" + ApiPort + "/api/v2/").ConfigureAwait(false); }
                catch (Exception) { continue; }
                var info = TvInfo.Parse(json);
                if (info == null) continue;
                model = info.Model;
                if (info.LogHost == null) disabled = true;
                else host = info.LogHost;
                return;
            }
        }

        static string[] OwnIps()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                    .Select(a => a.Address.ToString()).Distinct().ToArray();
            }
            catch (Exception) { return new string[0]; }
        }

        // С миллисекундами: по логу меряем время старта и перемотки.
        static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff ");
    }
}
