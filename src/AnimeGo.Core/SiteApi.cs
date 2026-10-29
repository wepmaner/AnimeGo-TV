using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AnimeGo.Core
{
    // Все запросы к animego.me. Ошибки наружу — только ApiException.
    public sealed partial class SiteApi
    {
        public const string Base = "https://animego.me";
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        readonly CookieJar jar;
        readonly HttpMessageHandler injected;   // null: handler свой, на куках jar
        HttpClient http;

        public SiteApi(CookieJar jar, HttpMessageHandler handler = null)
        {
            this.jar = jar;
            injected = handler;
            http = NewClient();
        }

        HttpClient NewClient()
        {
            var h = injected ?? new HttpClientHandler
            {
                CookieContainer = jar.Container,
                UseCookies = true,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var c = new HttpClient(h, injected == null) { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru,en;q=0.8");
            return c;
        }

        public Task<string> GetHtmlAsync(string path) => SendAsync(HttpMethod.Get, path, null, false);

        public Task<string> GetAjaxAsync(string path) => SendAsync(HttpMethod.Get, path, null, true);

        public async Task<User> LoginAsync(string login, string password)
        {
            var token = Csrf.NewToken();
            jar.Container.Add(new Cookie(Csrf.CookieName(token), "csrf-token", "/", "animego.me") { Secure = true });
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("_username", login),
                new KeyValuePair<string, string>("_password", password),
                new KeyValuePair<string, string>("_remember_me", "on"),
                new KeyValuePair<string, string>("_csrf_token", token),
            });
            await SendAsync(HttpMethod.Post, "/login", form, false).ConfigureAwait(false); // 302 — норма, не следуем
            return await WhoAmIAsync().ConfigureAwait(false)
                ?? throw new ApiException(ApiErrorKind.Unauthorized, RuText.BadLogin);
        }

        // null, если не вошли.
        public async Task<User> WhoAmIAsync() => HomeParser.ParseUser(await GetHtmlAsync("/").ConfigureAwait(false));

        public Task LogoutAsync()
        {
            jar.Clear();
            // Старый клиент не освобождаем: на нём могут идти запросы. Новый handler возьмёт новый Container.
            if (injected == null) http = NewClient();
            return Task.FromResult(0);
        }

        public async Task<IReadOnlyList<AnimeCard>> CatalogPageAsync(Paging p, int n) =>
            CatalogParser.CardsFromAjax(await GetAjaxAsync(p.PageUrl(n)).ConfigureAwait(false));

        public async Task<IReadOnlyList<AnimeCard>> SearchAsync(string q) =>
            CatalogParser.Cards(await GetHtmlAsync("/search/all?q=" + Uri.EscapeDataString(q)).ConfigureAwait(false));

        public async Task<PlayerInfo> PlayerAsync(int animeId) =>
            PlayerParser.Parse(await GetAjaxAsync("/player/" + Num(animeId)).ConfigureAwait(false));

        public async Task<EpisodeVideos> EpisodeVideosAsync(int episodeId) =>
            PlayerParser.ParseVideos(await GetAjaxAsync("/player/videos/" + Num(episodeId)).ConfigureAwait(false));

        // Сайт шлёт позицию через sendBeacon: без X-Requested-With.
        public Task PostProgressAsync(int episodeId, int translationId, int animeId, int currentTime, int duration)
        {
            var body = Json.Serialize(new ProgressDto
            {
                EpisodeId = episodeId, TranslationId = translationId, AnimeId = animeId,
                CurrentTime = currentTime, Duration = duration,
            });
            return SendAsync(HttpMethod.Post, "/video/progress", new StringContent(body, Encoding.UTF8, "application/json"), false);
        }

        // Переключатель: звать, только если серия ещё не отмечена.
        // Ответ {status:"success", data:{…}}; иной status или не JSON — ApiException(Server), чтобы вызывающий
        // не считал серию отмеченной.
        public async Task PostWatchedAsync(int episodeId, string translationKey)
        {
            var body = "id=" + Num(episodeId) + "&translationId=" + Uri.EscapeDataString(translationKey);
            var json = await SendAsync(HttpMethod.Post, "/anime/episode/watched",
                new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"), true).ConfigureAwait(false);
            string status;
            try { status = Json.Parse<AjaxDto>(json)?.Status; }
            catch (Exception e)   // тип ошибки разбора зависит от рантайма (Mono на ТВ, .NET на ПК)
            {
                throw new ApiException(ApiErrorKind.Server, "watched: ответ не JSON", e);
            }
            if (status != "success") throw new ApiException(ApiErrorKind.Server, "watched: status " + (status ?? "нет"));
        }

        static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        // path — относительный путь сайта или полный URL (CDN).
        async Task<string> SendAsync(HttpMethod method, string path, HttpContent content, bool ajax)
        {
            var url = path.StartsWith("http", StringComparison.Ordinal) ? path : Base + path;
            var req = new HttpRequestMessage(method, url) { Content = content };
            if (ajax)
            {
                if (req.RequestUri.Host == "animego.me") req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
            }
            else req.Headers.TryAddWithoutValidation("Accept", "text/html,*/*;q=0.8");
            var host = req.RequestUri.Host;
            if (host == "plapi.cdnvideohub.com" || host == "aniboom.one" || host.StartsWith("kodik", StringComparison.Ordinal))
                req.Headers.TryAddWithoutValidation("Referer", Base + "/");   // плееры отдают видео только «со страницы animego»

            var client = http; // Logout может подменить поле посреди запроса
            try
            {
                using (var res = await client.SendAsync(req).ConfigureAwait(false))
                {
                    var code = (int)res.StatusCode;
                    if (code == 401 || code == 403) throw new ApiException(ApiErrorKind.Unauthorized, "Нужен вход (" + code + ")");
                    if (code < 200 || code >= 400) throw new ApiException(ApiErrorKind.Server, "Сервер ответил " + code);
                    // Сайт отдаёт UTF-8; ReadAsStringAsync падает на незнакомом charset.
                    var bytes = res.Content == null ? new byte[0] : await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(bytes);
                    if (ajax && method == HttpMethod.Get)
                    {
                        var type = res.Content?.Headers.ContentType?.MediaType ?? "";
                        if (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
                            throw new ApiException(ApiErrorKind.Parse, "Ожидали JSON");
                    }
                    return text;
                }
            }
            catch (ApiException) { throw; }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException || e is ObjectDisposedException)
            {
                throw new ApiException(ApiErrorKind.Network, RuText.NoConnection, e);
            }
            catch (Exception e)
            {
                throw new ApiException(ApiErrorKind.Parse, "Неожиданная ошибка запроса", e);
            }
        }
    }
}
