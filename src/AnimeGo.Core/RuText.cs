using System.Globalization;

namespace AnimeGo.Core
{
    // Все строки интерфейса.
    public static class RuText
    {
        public const string Continue = "Продолжить просмотр";
        public const string Stale = "Давно не смотрели";
        public const string StaleHint = "· больше 14 дней";
        public const string Genres = "Жанры";
        public const string NewEpisodes = "Новые серии";
        public const string Ongoing = "Онгоинги";
        public const string Top = "ТОП-250";
        public const string Login = "Войти";
        public const string LoginWithout = "Продолжить без входа";
        public const string NoConnection = "Нет связи с animego.me";
        public const string Retry = "Повторить";
        public const string Dub = "Озвучка";
        public const string Quality = "Качество";
        public const string NextEpisode = "Следующая серия";
        public const string SkipOpening = "Пропустить опенинг";
        public const string MakeMain = "Сделать основной";
        public const string Main = "основная";
        public const string Fallback = "запасная";
        public const string NewEpisodeBadge = "Новая серия";
        public const string BadLogin = "Неверный логин или пароль";
        public const string SourceFailed = "Не удалось включить видео в этой озвучке";
        public const string Unavailable = "недоступно в приложении";
        public const string SwitchedSource = "Переключил источник";
        public const string Loading = "Загрузка…";
        public const string Search = "Поиск";
        public const string MyList = "Мой список";
        public const string Catalog = "Каталог";
        public const string SearchHint = "OK — ввести название";
        public const string NothingFound = "Ничего не нашлось";
        public const string EmptyList = "Список пуст";
        public const string RetryHint = "OK — повторить";
        public const string Watch = "Смотреть";
        public const string Episodes = "Серии";
        public const string Related = "Связанное";
        public const string NoNextEpisode = "Следующей серии пока нет";
        public const string Cancel = "Отмена";
        public const string NowWatching = "смотрите";
        public const string SeasonWatched = "просмотрен";
        public const string AutoQuality = "Авто";
        public const string PauseLabel = "Пауза";
        public const string Play = "Играть";
        public const string Now = "сейчас";
        public const string ChooseDub = "Выберите озвучку";
        public const string AutoHls = "Авто (HLS)";
        public const string ChoiceKeys = "↑↓ выбрать · OK применить · Назад закрыть";

        public static string NextIn(int s) => "Следующая серия через " + s.ToString(CultureInfo.InvariantCulture);

        public static string SeasonNo(int n) => n.ToString(CultureInfo.InvariantCulture) + " сезон";

        // 1 серия, 2 серии, 5 серий, 21 серия.
        public static string EpisodesCount(int n)
        {
            var d = n % 10;
            var dd = n % 100;
            var word = d == 1 && dd != 11 ? "серия" : d >= 2 && d <= 4 && (dd < 12 || dd > 14) ? "серии" : "серий";
            return n.ToString(CultureInfo.InvariantCulture) + " " + word;
        }

        public static string Episode(int n) => "Серия " + n.ToString(CultureInfo.InvariantCulture);

        // 05:08, а от часа — 1:05:08.
        public static string Time(int s)
        {
            var h = s / 3600;
            var m = s % 3600 / 60;
            var sec = s % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, sec)
                : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", m, sec);
        }

        public static string Remaining(int s) => "−" + Time(s);

        public static string NoDub(string studio, int ep) =>
            studio + " серию " + ep.ToString(CultureInfo.InvariantCulture) + " пока не озвучила";

        // Подсказки панели выбора (макет PlayerDub.dc.html).
        public static string DubHint(string title, int sec) =>
            "Выбор запомнится для «" + title + "». Видео продолжится с " + Time(sec) + ".";

        public static string QualityHint(int sec) => "Выбор запомнится для всех тайтлов. Видео продолжится с " + Time(sec) + ".";

        public static string NoDubChoose(string studio, int ep) => NoDub(studio, ep) + ". " + ChooseDub;

        public static string SwitchedDub(string from, int ep, string to) =>
            NoDub(from, ep) + " — включил " + to;
    }
}
