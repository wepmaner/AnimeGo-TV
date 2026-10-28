using System;
using System.Linq;
using System.Net;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class CookieJarTests
    {
        static readonly Uri Site = new Uri("https://animego.me/");

        [Fact]
        public void Export_Import_KeepsDomainPathAndExpiry()
        {
            var jar = new CookieJar();
            var exp = new DateTime(2099, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            jar.Container.Add(new Cookie("REMEMBERME", "v1", "/", ".animego.me") { Expires = exp, Secure = true });
            jar.Container.Add(new Cookie("sess", "v2", "/", "animego.me"));

            var back = CookieJar.Import(jar.Export());

            var cookies = back.Container.GetCookies(Site).Cast<Cookie>().ToDictionary(c => c.Name);
            Assert.Equal("v1", cookies["REMEMBERME"].Value);
            Assert.Equal(exp, cookies["REMEMBERME"].Expires.ToUniversalTime());
            Assert.True(cookies["REMEMBERME"].Secure);
            Assert.Equal("v2", cookies["sess"].Value);
            Assert.False(back.IsEmpty);
        }

        [Fact]
        public void Export_SkipsExpired()
        {
            var jar = new CookieJar();
            jar.Container.Add(new Cookie("old", "x", "/", "animego.me") { Expires = DateTime.UtcNow.AddDays(-1) });
            Assert.Equal("", jar.Export());
        }

        [Fact]
        public void Import_SkipsBrokenLines()
        {
            var good = "a\t1\tanimego.me\t/\t0\t0";
            var jar = CookieJar.Import("garbage\n\n" + "b\t2\t\t/\t0\t0\n" + "c\t3\tanimego.me\t/\tabc\t0\n" + good);
            var names = jar.Container.GetCookies(Site).Cast<Cookie>().Select(c => c.Name).ToList();
            Assert.Equal(new[] { "a" }, names);
        }

        [Fact]
        public void Import_NullIsEmpty() => Assert.True(CookieJar.Import(null).IsEmpty);

        [Fact]
        public void Clear_EmptiesJar()
        {
            var jar = new CookieJar();
            jar.Container.Add(new Cookie("a", "1", "/", "animego.me"));
            jar.Clear();
            Assert.True(jar.IsEmpty);
        }
    }
}
