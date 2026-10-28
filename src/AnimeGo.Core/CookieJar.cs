using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace AnimeGo.Core
{
    // Куки сайта. Экспорт нужен, чтобы пережить перезапуск приложения без повторного входа.
    public sealed class CookieJar
    {
        static readonly Uri SiteUri = new Uri(SiteApi.Base + "/");

        public CookieContainer Container { get; private set; } = new CookieContainer();

        // CookieContainer не умеет очищаться, поэтому Clear() подменяет его целиком;
        // SiteApi после этого пересоздаёт свой HttpClient и читает уже новый Container.
        public void Clear() => Container = new CookieContainer();

        public bool IsEmpty => Container.GetCookies(SiteUri).Count == 0;

        // Строки name\tvalue\tdomain\tpath\texpiresUtcTicks\tsecure(0|1); у сессионной куки ticks = 0.
        // В netstandard2.0 перечислить весь контейнер нельзя, поэтому берём куки, видимые на https://animego.me/.
        public string Export()
        {
            var sb = new StringBuilder();
            foreach (Cookie c in Container.GetCookies(SiteUri))
            {
                if (c.Expired || c.Name.StartsWith("__Host-csrf-token_", StringComparison.Ordinal)) continue; // одноразовые, нужны только на время входа
                var ticks = c.Expires == DateTime.MinValue ? 0 : c.Expires.ToUniversalTime().Ticks;
                sb.Append(c.Name).Append('\t').Append(c.Value).Append('\t').Append(c.Domain).Append('\t')
                  .Append(c.Path).Append('\t').Append(ticks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(c.Secure ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        public static CookieJar Import(string data)
        {
            var jar = new CookieJar();
            foreach (var line in (data ?? "").Split('\n'))
            {
                var f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 6 || f[0].Length == 0 || f[2].Length == 0) continue;
                if (!long.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) continue;
                if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) continue;
                try
                {
                    var c = new Cookie(f[0], f[1], f[3], f[2]) { Secure = f[5] == "1" };
                    if (ticks > 0) c.Expires = new DateTime(ticks, DateTimeKind.Utc);
                    if (c.Expired) continue;
                    jar.Container.Add(c);
                }
                catch (CookieException) { }
                catch (ArgumentException) { }
            }
            return jar;
        }
    }
}
