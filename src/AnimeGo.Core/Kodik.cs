using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Kodik (kodikplayer.com): чистая часть, без сети. Как получить поток (проверено 29.10.2026, API-findings.md):
    // 1) GET страницы плеера (data-player) с Referer animego → в ней urlParams (подписи домена) и vInfo (type, id, hash);
    // 2) POST этих полей на эндпоинт из app.player_single.*.js (сейчас atob("L2Z0b3I=") = /ftor) → JSON links по качествам;
    // 3) src в links — base64 со сдвигом латинских букв (Цезарь, сдвиг меняется) → //…/720.mp4:hls:manifest.m3u8.
    public static class Kodik
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.CultureInvariant;
        static readonly Regex EmbedRx = new Regex(@"kodik[a-z]*\.[a-z]+/(seria|serial|video)/(\d+)/([0-9a-z]+)", Opts | RegexOptions.IgnoreCase);
        static readonly Regex ParamsRx = new Regex(@"urlParams\s*=\s*'(\{.*?\})'\s*;", Opts);
        static readonly Regex InfoRx = new Regex(@"vInfo\.(type|id|hash)\s*=\s*['""]([^'""]*)['""]", Opts);
        static readonly Regex LinkRx = new Regex(@"""(\d{3,4})""\s*:\s*\[\s*\{\s*""src""\s*:\s*""([^""]+)""", Opts);
        static readonly Regex EndpointRx = new Regex(@"type:""POST"",url:atob\(""([A-Za-z0-9+/=]+)""\)", Opts);
        static readonly Regex PlayerJsRx = new Regex(@"src=""(/assets/js/app\.player_single[^""]*\.js)""", Opts);

        public const string DefaultEndpoint = "/ftor";

        public static (string type, string id, string hash)? ParseEmbed(string playerUrl)
        {
            var m = EmbedRx.Match(playerUrl ?? "");
            if (!m.Success) return null;
            return (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        }

        // Поля POST-запроса за ссылками — как их шлёт сам плеер.
        public static IEnumerable<KeyValuePair<string, string>> Form(string html)
        {
            var m = ParamsRx.Match(html ?? "");
            if (!m.Success) throw new ApiException(ApiErrorKind.Parse, "Kodik: нет urlParams");
            KodikParamsDto p;
            try { p = Json.Parse<KodikParamsDto>(m.Groups[1].Value); }
            catch (Exception e) { throw new ApiException(ApiErrorKind.Parse, "Kodik: urlParams не JSON", e); }
            var info = InfoRx.Matches(html).Cast<Match>().GroupBy(x => x.Groups[1].Value).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
            if (!info.ContainsKey("type") || !info.ContainsKey("id") || !info.ContainsKey("hash"))
                throw new ApiException(ApiErrorKind.Parse, "Kodik: нет vInfo");
            return new[]
            {
                Pair("d", p.D), Pair("d_sign", p.DSign), Pair("pd", p.Pd), Pair("pd_sign", p.PdSign),
                Pair("ref", Uri.UnescapeDataString(p.Ref ?? "")), Pair("ref_sign", p.RefSign),
                Pair("bad_user", "false"), Pair("cdn_is_working", "true"),
                Pair("type", info["type"]), Pair("hash", info["hash"]), Pair("id", info["id"]), Pair("info", "{}"),
            };
        }

        static KeyValuePair<string, string> Pair(string k, string v) => new KeyValuePair<string, string>(k, v ?? "");

        // Ответ эндпоинта → потоки HLS по качествам, от лучшего. Нет ни одной ссылки — ApiException(Parse).
        public static StreamSet ParseLinks(string json)
        {
            var at = (json ?? "").IndexOf("\"links\"", StringComparison.Ordinal);
            var list = new List<Stream>();
            if (at >= 0)
                foreach (Match m in LinkRx.Matches(json.Substring(at)))
                {
                    var url = DecodeLink(m.Groups[2].Value.Replace(@"\/", "/"));
                    var q = m.Groups[1].Value + "p";
                    if (url != null && list.All(s => s.Quality != q)) list.Add(new Stream { Url = url, Quality = q, IsHls = url.Contains("m3u8") });
                }
            if (list.Count == 0) throw new ApiException(ApiErrorKind.Parse, "Kodik: нет ссылок");
            return new StreamSet
            {
                Streams = list.OrderByDescending(s => int.Parse(s.Quality.TrimEnd('p'), CultureInfo.InvariantCulture)).ToList(),
            };
        }

        // Перебираем сдвиг: правильный даёт base64, который раскодируется в адрес. null — не вышло.
        public static string DecodeLink(string src)
        {
            if (string.IsNullOrEmpty(src)) return null;
            for (var shift = 0; shift < 26; shift++)
            {
                var b = new StringBuilder(src.Length + 3);
                foreach (var c in src)
                    b.Append(c >= 'a' && c <= 'z' ? (char)('a' + (c - 'a' + shift) % 26)
                        : c >= 'A' && c <= 'Z' ? (char)('A' + (c - 'A' + shift) % 26) : c);
                while (b.Length % 4 != 0) b.Append('=');
                string text;
                try { text = Encoding.UTF8.GetString(Convert.FromBase64String(b.ToString())); }
                catch (FormatException) { continue; }
                if (text.StartsWith("//", StringComparison.Ordinal) && text.All(ch => ch > ' ' && ch < 127)) return "https:" + text;
                if (text.StartsWith("http", StringComparison.Ordinal) && text.All(ch => ch > ' ' && ch < 127)) return text;
            }
            return null;
        }

        // Эндпоинт из кода плеера: url:atob("L2Z0b3I=") → "/ftor". null — не нашли.
        public static string ParseEndpoint(string js)
        {
            var m = EndpointRx.Match(js ?? "");
            if (!m.Success) return null;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)); }
            catch (FormatException) { return null; }
        }

        public static string PlayerJsPath(string html)
        {
            var m = PlayerJsRx.Match(html ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    [DataContract]
    internal class KodikParamsDto
    {
        [DataMember(Name = "d")] public string D { get; set; }
        [DataMember(Name = "d_sign")] public string DSign { get; set; }
        [DataMember(Name = "pd")] public string Pd { get; set; }
        [DataMember(Name = "pd_sign")] public string PdSign { get; set; }
        [DataMember(Name = "ref")] public string Ref { get; set; }
        [DataMember(Name = "ref_sign")] public string RefSign { get; set; }
    }
}
