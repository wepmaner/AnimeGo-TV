using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class SiteApiTests
    {
        static HttpResponseMessage Html(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };

        static string Header(HttpRequestMessage r, string name) =>
            r.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;

        [Fact]
        public async Task Login_PostsFormWithCsrfCookie_ThenReadsUser()
        {
            var jar = new CookieJar();
            var h = new FakeHandler(r => r.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Found)
                : Html("<html lang=\"ru\" data-user='{\"id\":7,\"username\":\"x\"}'><body></body></html>"));
            var api = new SiteApi(jar, h);

            var user = await api.LoginAsync("u", "p");

            Assert.Equal(7, user.Id);
            Assert.Equal("x", user.Name);
            Assert.Equal(2, h.Requests.Count);
            var post = h.Requests[0];
            Assert.Equal("https://animego.me/login", post.RequestUri.ToString());
            var body = await post.Content.ReadAsStringAsync();
            var m = Regex.Match(body, @"^_username=u&_password=p&_remember_me=on&_csrf_token=(.+)$");
            Assert.True(m.Success, body);
            var token = Uri.UnescapeDataString(m.Groups[1].Value);
            Assert.True(token.Length >= 24);
            // Заголовок Cookie добавляет сам HttpClientHandler, которого в FakeHandler нет: проверяем контейнер.
            var c = jar.Container.GetCookies(new Uri("https://animego.me/")).Cast<Cookie>().Single(x => x.Name == "__Host-csrf-token_" + token);
            Assert.Equal("csrf-token", c.Value);
            Assert.Equal("GET", h.Requests[1].Method.Method);
            Assert.Equal("https://animego.me/", h.Requests[1].RequestUri.ToString());
        }

        [Fact]
        public async Task Login_WithoutDataUser_IsUnauthorized()
        {
            var h = new FakeHandler(r => r.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Found) : Html("<html lang=\"ru\"></html>"));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).LoginAsync("u", "p"));
            Assert.Equal(ApiErrorKind.Unauthorized, e.Kind);
            Assert.Equal(RuText.BadLogin, e.Message);
        }

        [Fact]
        public async Task WhoAmI_Anonymous_IsNull()
        {
            var h = new FakeHandler(r => Html(Fixture.Read("home_anon.html")));
            Assert.Null(await new SiteApi(new CookieJar(), h).WhoAmIAsync());
        }

        [Fact]
        public async Task DefaultHeaders_AndHtmlAccept()
        {
            var h = new FakeHandler(r => Html("ok"));
            await new SiteApi(new CookieJar(), h).GetHtmlAsync("/x");
            var r0 = h.Requests[0];
            Assert.Contains("Chrome/130.0", r0.Headers.UserAgent.ToString());
            Assert.Equal("ru,en;q=0.8", Header(r0, "Accept-Language").Replace(" ", ""));
            Assert.Equal("text/html,*/*;q=0.8", Header(r0, "Accept").Replace(" ", ""));
            Assert.Null(Header(r0, "X-Requested-With"));
        }

        [Fact]
        public async Task GetAjax_SendsAjaxHeaders()
        {
            var h = new FakeHandler(r => FakeHandler.Json("{}"));
            Assert.Equal("{}", await new SiteApi(new CookieJar(), h).GetAjaxAsync("/player/1"));
            Assert.Equal("XMLHttpRequest", Header(h.Requests[0], "X-Requested-With"));
            Assert.Equal("application/json", Header(h.Requests[0], "Accept"));
        }

        [Fact]
        public async Task GetAjax_HtmlResponse_IsParseError()
        {
            var h = new FakeHandler(r => Html("<html>login</html>"));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).GetAjaxAsync("/player/1"));
            Assert.Equal(ApiErrorKind.Parse, e.Kind);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, ApiErrorKind.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden, ApiErrorKind.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError, ApiErrorKind.Server)]
        [InlineData(HttpStatusCode.NotFound, ApiErrorKind.Server)]
        public async Task StatusCodes_MapToKinds(HttpStatusCode code, ApiErrorKind kind)
        {
            var h = new FakeHandler(r => FakeHandler.Status(code));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).GetHtmlAsync("/"));
            Assert.Equal(kind, e.Kind);
        }

        [Fact]
        public async Task NetworkFailure_IsNetworkError()
        {
            var h = new FakeHandler(r => throw new HttpRequestException("down"));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).GetHtmlAsync("/"));
            Assert.Equal(ApiErrorKind.Network, e.Kind);
            Assert.Equal(RuText.NoConnection, e.Message);
        }

        [Fact]
        public async Task PostProgress_SendsExactJson_WithoutAjaxHeader()
        {
            var h = new FakeHandler(r => FakeHandler.Json("{}"));
            await new SiteApi(new CookieJar(), h).PostProgressAsync(44100, 59, 3547, 123, 1440);
            var r0 = h.Requests[0];
            Assert.Equal(HttpMethod.Post, r0.Method);
            Assert.Equal("https://animego.me/video/progress", r0.RequestUri.ToString());
            Assert.Equal("application/json", r0.Content.Headers.ContentType.MediaType);
            Assert.Equal("{\"episodeId\":44100,\"translationId\":59,\"animeId\":3547,\"currentTime\":123,\"duration\":1440}",
                await r0.Content.ReadAsStringAsync());
            Assert.Null(Header(r0, "X-Requested-With"));
        }

        [Fact]
        public async Task PostWatched_SendsFormWithAjaxHeader()
        {
            var h = new FakeHandler(r => FakeHandler.Json("{\"status\":\"success\",\"data\":{\"watchedEpisodeNumber\":3}}"));
            await new SiteApi(new CookieJar(), h).PostWatchedAsync(44100, "59");
            var r0 = h.Requests[0];
            Assert.Equal("https://animego.me/anime/episode/watched", r0.RequestUri.ToString());
            Assert.Equal("application/x-www-form-urlencoded", r0.Content.Headers.ContentType.MediaType);
            Assert.Equal("id=44100&translationId=59", await r0.Content.ReadAsStringAsync());
            Assert.Equal("XMLHttpRequest", Header(r0, "X-Requested-With"));
        }

        [Theory]
        [InlineData("{\"status\":\"error\",\"message\":\"x\"}")]
        [InlineData("{}")]
        [InlineData("<html></html>")]
        public async Task PostWatched_NotSuccess_ThrowsServer(string body)
        {
            var h = new FakeHandler(r => FakeHandler.Json(body));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).PostWatchedAsync(44100, "59"));
            Assert.Equal(ApiErrorKind.Server, e.Kind);
        }

        [Fact]
        public async Task Search_EscapesQuery_AndParsesCards()
        {
            var h = new FakeHandler(r => Html(Fixture.Read("search_okean.html")));
            var cards = await new SiteApi(new CookieJar(), h).SearchAsync("необъятный океан");
            Assert.Equal("/search/all?q=" + Uri.EscapeDataString("необъятный океан"), h.Requests[0].RequestUri.PathAndQuery);
            Assert.NotEmpty(cards);
        }

        [Fact]
        public async Task CatalogPage_UsesPagingUrl()
        {
            var h = new FakeHandler(r => FakeHandler.Json(Fixture.Read("catalog_ongoing_page2.json")));
            var p = new Paging { UrlTemplate = "/anime/season/2026/autumn/page/__page__" };
            var cards = await new SiteApi(new CookieJar(), h).CatalogPageAsync(p, 2);
            Assert.Equal("https://animego.me/anime/season/2026/autumn/page/2", h.Requests[0].RequestUri.ToString());
            Assert.NotEmpty(cards);
        }

        [Fact]
        public async Task Player_AndEpisodeVideos_UseAjaxUrls()
        {
            var h = new FakeHandler(r => FakeHandler.Json(r.RequestUri.AbsolutePath.StartsWith("/player/videos/")
                ? Fixture.Read("player_videos_44100.json") : Fixture.Read("player_3547.json")));
            var api = new SiteApi(new CookieJar(), h);
            var info = await api.PlayerAsync(3547);
            var vids = await api.EpisodeVideosAsync(44100);
            Assert.Equal("https://animego.me/player/3547", h.Requests[0].RequestUri.ToString());
            Assert.Equal("https://animego.me/player/videos/44100", h.Requests[1].RequestUri.ToString());
            Assert.Equal(3547, info.AnimeId);
            Assert.NotEmpty(vids.Sources);
        }

        [Fact]
        public async Task CvhStreams_PlaylistThenVideo_WithReferer()
        {
            var h = new FakeHandler(r => FakeHandler.Json(r.RequestUri.AbsolutePath.EndsWith("/playlist")
                ? Fixture.Read("cvh_playlist_62542.json") : Fixture.Read("cvh_video_redacted.json")));
            var src = new VideoSource { Provider = "cvh", PlayerUrl = "https://animego.me/cdn-iframe/62542/Студийная Банда/3/1" };
            var set = await new SiteApi(new CookieJar(), h).CvhStreamsAsync(src, 3, 1);
            Assert.Equal(Cvh.PlaylistUrl(62542), h.Requests[0].RequestUri.ToString());
            Assert.Equal(Cvh.VideoUrl("15266286492336"), h.Requests[1].RequestUri.ToString());
            Assert.All(h.Requests, r => Assert.Equal("https://animego.me/", Header(r, "Referer")));
            Assert.All(h.Requests, r => Assert.Null(Header(r, "X-Requested-With")));
            Assert.NotEmpty(set.Streams);
        }

        [Fact]
        public async Task CvhStreams_MissingEpisode_IsServerError()
        {
            var h = new FakeHandler(r => FakeHandler.Json(Fixture.Read("cvh_playlist_62542.json")));
            var src = new VideoSource { PlayerUrl = "https://animego.me/cdn-iframe/62542/Студийная Банда/3/1" };
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).CvhStreamsAsync(src, 3, 999));
            Assert.Equal(ApiErrorKind.Server, e.Kind);
            Assert.Equal("Нет видео в CVH", e.Message);
        }

        [Fact]
        public async Task AniboomStreams_FetchesEmbedWithReferer()
        {
            var h = new FakeHandler(r => Html(Fixture.Read("aniboom_embed.html")));
            var src = new VideoSource { Provider = "aniboom", PlayerUrl = "//aniboom.one/embed/abc?episode=1" };
            var set = await new SiteApi(new CookieJar(), h).AniboomStreamsAsync(src);
            Assert.Equal("https://aniboom.one/embed/abc?episode=1", h.Requests[0].RequestUri.ToString());
            Assert.Equal("https://animego.me/", Header(h.Requests[0], "Referer"));
            Assert.NotEmpty(set.Streams);
        }

        [Fact]
        public async Task Logout_ClearsJar()
        {
            var jar = new CookieJar();
            jar.Container.Add(new Cookie("a", "1", "/", "animego.me"));
            await new SiteApi(jar, new FakeHandler(r => Html(""))).LogoutAsync();
            Assert.True(jar.IsEmpty);
        }

        [Fact]
        public async Task Logout_WithOwnHandler_DropsCsrfCookieFromContainer()
        {
            var jar = new CookieJar();
            var api = new SiteApi(jar, new FakeHandler(r => r.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Found) : Html("<html data-user='{\"id\":7,\"username\":\"x\"}'></html>")));
            await api.LoginAsync("u", "p");
            Assert.Single(jar.Container.GetCookies(new Uri("https://animego.me/")));

            var own = new SiteApi(jar);
            await own.LogoutAsync();

            Assert.Empty(jar.Container.GetCookies(new Uri("https://animego.me/")));
            Assert.True(jar.IsEmpty);
        }

        [Fact]
        public async Task TwoLogins_ExportHasNoCsrfCookies()
        {
            var jar = new CookieJar();
            var api = new SiteApi(jar, new FakeHandler(r => r.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Found) : Html("<html data-user='{\"id\":7,\"username\":\"x\"}'></html>")));
            await api.LoginAsync("u", "p");
            await api.LoginAsync("u", "p");
            Assert.DoesNotContain("__Host-csrf-token_", jar.Export());
        }

        [Fact]
        public async Task ObjectDisposed_IsNetworkError()
        {
            var h = new FakeHandler(r => throw new ObjectDisposedException("client"));
            var e = await Assert.ThrowsAsync<ApiException>(() => new SiteApi(new CookieJar(), h).GetHtmlAsync("/"));
            Assert.Equal(ApiErrorKind.Network, e.Kind);
        }

        [Fact]
        public async Task UnknownCharset_DoesNotLeakRawException()
        {
            var h = new FakeHandler(r =>
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("привет") };
                res.Content.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=bogus-123");
                return res;
            });
            Assert.Equal("привет", await new SiteApi(new CookieJar(), h).GetHtmlAsync("/"));
        }
    }
}
