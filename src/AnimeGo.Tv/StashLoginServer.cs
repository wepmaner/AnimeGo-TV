using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Вход с ПК через Stash: порт 8779 открыт, только пока на экране форма входа (Start/Stop зовёт LoginScreen).
    //   GET  /status → {"screen":"login","version":"…"}
    //   POST /login  {"login","password"} (только Content-Type: application/json — «простой» запрос со страницы
    //                в браузере сюда не дойдёт) → {"ok":true,"user":{…}} | {"ok":false,"error":"…"}
    // Тело запроса не логируем и не храним. Выхода и управления аккаунтом через сеть нет.
    sealed class StashLoginServer
    {
        public const int Port = 8779;
        const int MaxBody = 8 * 1024;

        readonly Func<string, string, Task<(User User, string Error)>> login;
        readonly Action<User> loggedIn;   // после того как ответ ушёл в Stash
        HttpListener listener;
        int busy;

        public StashLoginServer(Func<string, string, Task<(User, string)>> login, Action<User> loggedIn)
        {
            this.login = login;
            this.loggedIn = loggedIn;
        }

        public void Start()
        {
            if (listener != null) return;
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://*:" + Port + "/");
                l.Start();
                listener = l;
                DevLog.Write("stash: порт " + Port + " открыт");
                Task.Run(() => Loop(l));
            }
            catch (Exception e) { DevLog.Write("stash: порт " + Port + " не открылся — " + e.GetType().Name + " " + e.Message); }
        }

        public void Stop()
        {
            var l = listener;
            listener = null;
            if (l == null) return;
            try { l.Stop(); l.Close(); } catch (Exception) { }
            DevLog.Write("stash: порт " + Port + " закрыт");
        }

        async Task Loop(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext c;
                try { c = await l.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }   // Stop
                var _ = Task.Run(() => Handle(c));
            }
        }

        async Task Handle(HttpListenerContext c)
        {
            var path = c.Request.Url.AbsolutePath;
            var method = c.Request.HttpMethod;
            User ok = null;
            try
            {
                if (method == "GET" && path == "/status") { await Reply(c, 200, StashProtocol.Status(AppInfo.Version)).ConfigureAwait(false); return; }
                if (path != "/login" || method != "POST") { await Reply(c, 404, StashProtocol.LoginFail("Нет такого адреса")).ConfigureAwait(false); return; }
                if (!(c.Request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await Reply(c, 415, StashProtocol.LoginFail("Нужен Content-Type: application/json")).ConfigureAwait(false);
                    return;
                }
                var body = await ReadBody(c.Request).ConfigureAwait(false);
                var pair = body == null ? null : StashProtocol.ParseLogin(body);
                if (pair == null) { await Reply(c, 400, StashProtocol.LoginFail(RuText.StashNoCredentials)).ConfigureAwait(false); return; }
                if (Interlocked.Exchange(ref busy, 1) == 1) { await Reply(c, 409, StashProtocol.LoginFail(RuText.StashLoginBusy)).ConfigureAwait(false); return; }
                try
                {
                    DevLog.Write("stash: вход с ПК");
                    var (user, error) = await login(pair.Value.Login, pair.Value.Password).ConfigureAwait(false);
                    DevLog.Write(user != null ? "stash: вход ok " + user.Id : "stash: вход не удался — " + error);
                    await Reply(c, 200, user != null ? StashProtocol.LoginOk(user) : StashProtocol.LoginFail(error)).ConfigureAwait(false);
                    ok = user;
                }
                finally { Interlocked.Exchange(ref busy, 0); }
            }
            catch (Exception e) { DevLog.Write("stash: " + method + " " + path + " — " + e.GetType().Name); }
            if (ok != null) loggedIn(ok);
        }

        // null — тело больше MaxBody.
        static async Task<string> ReadBody(HttpListenerRequest r)
        {
            if (r.ContentLength64 > MaxBody) return null;
            using (var ms = new MemoryStream())
            {
                var buf = new byte[4096];
                int n;
                while ((n = await r.InputStream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > MaxBody) return null;
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        static async Task Reply(HttpListenerContext c, int code, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            c.Response.StatusCode = code;
            c.Response.ContentType = "application/json; charset=utf-8";
            c.Response.ContentLength64 = bytes.Length;
            await c.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            c.Response.Close();
        }
    }
}
