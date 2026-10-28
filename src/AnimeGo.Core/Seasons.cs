using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Сезоны сериала из блока «Связанное»: тайтлы с тем же базовым именем (название до первой цифры).
    public static class Seasons
    {
        static readonly Regex DigitsRx = new Regex(@"\d+");
        static readonly string[] NotSeasonKinds = { "ova", "фильм", "спешл", "special", "movie" };

        public static IReadOnlyList<Season> Build(AnimeInfo current)
        {
            var baseName = BaseName(current.Title);
            var list = new List<Season> { Make(current.Id, current.Title, true) };
            foreach (var r in current.Related)
            {
                if (r.Id == current.Id || r.Title == null || IsNotSeason(r.Kind)) continue;
                if (BaseName(r.Title) == baseName) list.Add(Make(r.Id, r.Title, false));
            }
            return list.OrderBy(s => s.Order).ToList();   // OrderBy стабилен
        }

        static Season Make(int id, string title, bool isCurrent) =>
            new Season { AnimeId = id, Title = title, Order = OrderOf(title), IsCurrent = isCurrent };

        static bool IsNotSeason(string kind)
        {
            if (kind == null) return false;
            // По целым словам типа, а не по подстроке.
            var words = Regex.Split(kind.ToLowerInvariant(), @"[^\p{L}\p{N}]+");
            return words.Any(w => NotSeasonKinds.Contains(w));
        }

        static string BaseName(string title)
        {
            title = title ?? "";
            var m = DigitsRx.Match(title);
            var head = m.Success ? title.Substring(0, m.Index) : title;
            return head.TrimEnd(' ', ':', '-', '–', '—').ToLowerInvariant();
        }

        // Первое число в названии; нет числа — 1.
        static int OrderOf(string title)
        {
            var m = DigitsRx.Match(title ?? "");
            return m.Success && int.TryParse(m.Value, out var n) ? n : 1;
        }
    }
}
