using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Разбор разметки animego.me регулярками: без внешних пакетов, достаточно для known-вёрстки.
    internal static class Html
    {
        const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        static readonly Regex EntityRx = new Regex(@"&(#x[0-9a-f]+|#[0-9]+|[a-z]+);", Opts);
        static readonly Regex CommentRx = new Regex(@"<!--.*?-->", Opts);
        static readonly Regex ScriptRx = new Regex(@"<script\b.*?</script\s*>", Opts);
        static readonly Regex StyleRx = new Regex(@"<style\b.*?</style\s*>", Opts);
        static readonly Regex AnyTagRx = new Regex(@"<(?:""[^""]*""|'[^']*'|[^>""'])*>", Opts);
        static readonly Regex SpacesRx = new Regex(@"\s+", Opts);

        static readonly Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["quot"] = "\"", ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["apos"] = "'", ["nbsp"] = " ",
            ["laquo"] = "«", ["raquo"] = "»", ["mdash"] = "—", ["ndash"] = "–", ["hellip"] = "…",
        };

        // Значение атрибута из текста одного тега; null, если атрибута нет.
        public static string Attr(string tag, string name)
        {
            if (tag == null) return null;
            var m = Regex.Match(tag, @"(?<=\s)" + Regex.Escape(name) + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", Opts);
            if (!m.Success) return null;
            var v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            return Decode(v);
        }

        // Открывающие теги <tagName ...> с классом classToken (целым токеном в class).
        public static IEnumerable<string> Tags(string html, string tagName, string classToken)
        {
            foreach (Match m in OpenRx(tagName).Matches(html ?? ""))
                if (HasClass(m.Value, classToken)) yield return m.Value;
        }

        // Целые элементы от открывающего тега с классом до парного закрывающего (вложенность того же тега учтена).
        public static IEnumerable<string> Blocks(string html, string tagName, string classToken) =>
            BlocksWhere(html, tagName, tag => HasClass(tag, classToken));

        // Открывающие теги (tagName == null — любые) с атрибутом attrName; value == null — с любым значением.
        public static IEnumerable<string> TagsWithAttr(string html, string tagName, string attrName, string value = null)
        {
            foreach (Match m in OpenRx(tagName).Matches(html ?? ""))
                if (HasAttr(m.Value, attrName, value)) yield return m.Value;
        }

        // Как Blocks, но по атрибуту (см. TagsWithAttr); tagName обязателен.
        public static IEnumerable<string> BlocksWithAttr(string html, string tagName, string attrName, string value = null) =>
            BlocksWhere(html, tagName, tag => HasAttr(tag, attrName, value));

        static bool HasAttr(string tag, string attrName, string value)
        {
            var v = Attr(tag, attrName);
            return v != null && (value == null || v == value);
        }

        static IEnumerable<string> BlocksWhere(string html, string tagName, Func<string, bool> match)
        {
            html = html ?? "";
            var open = OpenRx(tagName);
            var step = StepRx(tagName);
            var pos = 0;
            while (true)
            {
                var m = open.Match(html, pos);
                if (!m.Success) yield break;
                if (!match(m.Value)) { pos = m.Index + m.Length; continue; }

                var depth = 1;
                var end = -1;
                var from = m.Index + m.Length;
                while (depth > 0)
                {
                    var s = step.Match(html, from);
                    if (!s.Success) break;
                    from = s.Index + s.Length;
                    if (s.Value.StartsWith("</", StringComparison.Ordinal)) depth--;
                    else if (!s.Value.EndsWith("/>", StringComparison.Ordinal)) depth++;
                    if (depth == 0) end = from;
                }
                if (end < 0) yield break; // не закрыт — разметка битая, отдавать нечего
                yield return html.Substring(m.Index, end - m.Index);
                pos = end;
            }
        }

        // Текст без тегов, script/style выброшены целиком, пробелы схлопнуты.
        public static string Text(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var s = CommentRx.Replace(html, "");
            s = ScriptRx.Replace(s, "");
            s = StyleRx.Replace(s, "");
            s = AnyTagRx.Replace(s, " ");
            s = Decode(s);
            return SpacesRx.Replace(s, " ").Trim();
        }

        // Только декод entities (за один проход, чтобы &amp;lt; стал &lt;, а не <).
        public static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            return EntityRx.Replace(s, m =>
            {
                var e = m.Groups[1].Value;
                if (e[0] != '#') return Named.TryGetValue(e, out var r) ? r : m.Value;
                var hex = e.Length > 1 && (e[1] == 'x' || e[1] == 'X');
                var ok = int.TryParse(e.Substring(hex ? 2 : 1), hex ? NumberStyles.HexNumber : NumberStyles.None,
                    CultureInfo.InvariantCulture, out var cp);
                if (!ok || cp <= 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return m.Value;
                return char.ConvertFromUtf32(cp);
            });
        }

        // Регулярки кэшируем по имени тега: имён в разметке единицы, а вызовов много.
        static readonly Dictionary<string, Regex> OpenCache = new Dictionary<string, Regex>();
        static readonly Dictionary<string, Regex> StepCache = new Dictionary<string, Regex>();

        static Regex Cached(Dictionary<string, Regex> cache, string tagName, Func<Regex> make)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(tagName, out var rx)) cache[tagName] = rx = make();
                return rx;
            }
        }

        static Regex StepRx(string tagName) => Cached(StepCache, tagName, () =>
            new Regex(@"<" + Regex.Escape(tagName) + @"(?=[\s/>])(?:""[^""]*""|'[^']*'|[^>""'])*>|</" + Regex.Escape(tagName) + @"\s*>", Opts));

        // tagName == null: любой тег (ключ "*" не пересекается с именами тегов).
        static Regex OpenRx(string tagName) => Cached(OpenCache, tagName ?? "*", () =>
            new Regex(@"<" + (tagName == null ? @"[a-z][\w-]*" : Regex.Escape(tagName)) + @"(?=[\s/>])(?:""[^""]*""|'[^']*'|[^>""'])*>", Opts));

        static bool HasClass(string tag, string classToken)
        {
            var cls = Attr(tag, "class");
            if (cls == null) return false;
            foreach (var t in cls.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (t == classToken) return true;
            return false;
        }
    }
}
