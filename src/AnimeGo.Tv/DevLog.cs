using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AnimeGo.Tv
{
    // Только в Debug-сборке: у ТВ нет доступного журнала, поэтому строки отправляются
    // на компьютер разработчика (scripts/devlog.mjs). В Release вызовы вырезаются компилятором.
    static class DevLog
    {
        public const string Host = "http://192.168.0.41:8778";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // Синхронно — для падений: процесс может умереть раньше, чем уйдёт асинхронная отправка.
        [Conditional("DEVLOG")]
        public static void WriteNow(string line)
        {
            try { http.PostAsync(Host + "/log", new StringContent(Stamp() + line, Encoding.UTF8)).Wait(3000); }
            catch { }
        }

        [Conditional("DEVLOG")]
        public static void Write(string line)
        {
            var text = Stamp() + line;
            Task.Run(async () =>
            {
                try { await http.PostAsync(Host + "/log", new StringContent(text, Encoding.UTF8)); }
                catch { }
            });
        }

        // С миллисекундами: по логу меряем время старта и перемотки.
        static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff ");
    }
}
