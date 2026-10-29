using System.Collections.Generic;

namespace AnimeGo.Core
{
    // Длинный текст (рецензия, описание) для читалки на ТВ: строки не длиннее charsPerLine знаков (перенос по словам,
    // абзацы — "\n", пустая строка между абзацами сохраняется), страницы по linesPerPage строк.
    // Ширину строки считаем знаками: шрифт ТВ пропорциональный, поэтому charsPerLine берём с запасом.
    public static class TextPages
    {
        public static List<List<string>> Split(string text, int charsPerLine, int linesPerPage)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var words = para.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");   // не больше одной пустой подряд
                    continue;
                }
                var line = "";
                foreach (var w0 in words)
                {
                    var w = w0;
                    while (w.Length > charsPerLine)   // слово длиннее строки — режем
                    {
                        if (line.Length > 0) { lines.Add(line); line = ""; }
                        lines.Add(w.Substring(0, charsPerLine));
                        w = w.Substring(charsPerLine);
                    }
                    if (line.Length == 0) line = w;
                    else if (line.Length + 1 + w.Length <= charsPerLine) line += " " + w;
                    else { lines.Add(line); line = w; }
                }
                if (line.Length > 0) lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);

            var pages = new List<List<string>>();
            var page = new List<string>();
            foreach (var l in lines)
            {
                if (page.Count == 0 && l.Length == 0) continue;   // страница не начинается с пустой строки
                page.Add(l);
                if (page.Count == linesPerPage) { pages.Add(page); page = new List<string>(); }
            }
            if (page.Count > 0) pages.Add(page);
            if (pages.Count == 0) pages.Add(new List<string>());
            return pages;
        }
    }
}
