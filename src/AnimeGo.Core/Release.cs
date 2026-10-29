using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeGo.Core
{
    // Когда выйдет серия: по полю «Следующий эпизод» (дата со временем) и графику выхода (дата) страницы тайтла.
    // Время — как отдаёт сайт: во времени аккаунта, гостю — во времени сайта.
    public static class Release
    {
        static readonly Regex YearRx = new Regex(@"\s+(?:19|20)\d{2}\b", RegexOptions.CultureInvariant);

        // null — серия вышла или о ней ничего не известно. When — без года: «30 окт. пт 16:30» / «30 октября».
        public static (string When, string In)? Of(AnimeInfo a, int number)
        {
            if (a == null) return null;
            if (a.Next != null && a.Next.Number == number) return (NoYear(a.Next.When), a.Next.In);
            var row = a.Schedule.FirstOrDefault(s => s.Number == number);
            if (row != null && IsUpcoming(row.Status)) return (NoYear(row.Date), row.Status);
            if (a.Next != null && number > a.Next.Number) return (null, null);   // после следующей — тоже ещё нет
            return null;
        }

        public static bool IsUpcoming(string status) =>
            status != null && status.StartsWith("через", StringComparison.OrdinalIgnoreCase);

        static string NoYear(string s) => string.IsNullOrEmpty(s) ? s : YearRx.Replace(s, "").Trim();

        // «выйдет 30 окт. пт 16:30» / «ещё не вышла» — подпись под плиткой серии.
        public static string Label((string When, string In) r) =>
            string.IsNullOrEmpty(r.When) ? RuText.NotReleased : RuText.WillAir + " " + r.When;
    }
}
