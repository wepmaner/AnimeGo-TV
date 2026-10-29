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

        public const string StashGotLogin = "Данные для входа получены из Stash";
        public const string ConnectingAnimego = "Соединяемся с animego.me";
        public const string SigningIn = "Входим в аккаунт…";
        public const string StashLoginBusy = "Вход уже идёт";
        public const string StashLoginTitle = "Или войдите с компьютера";
        public const string StashLoginHint = "В Stash на вкладке «Samsung TV». Пока открыт этот экран, логин и пароль придут сюда сами.";
        public const string StashLoginClosed = "Экран входа уже закрыт";
        public const string StashNoCredentials = "Нет логина или пароля";

        public static string SignedInAs(string name) => string.IsNullOrEmpty(name) ? "Готово" : "Готово, вы вошли как " + name;

        public const string Screens = "Кадры";
        public const string NoEpisodes = "Серий пока нет";
        public const string EpisodeWatched = "просмотрена";
        public const string NotReleased = "ещё не вышла";
        public const string WillAir = "выйдет";
        public const string Schedule = "График выхода";
        public const string Aired = "вышла";

        public static string EpisodeAirs(int n, string when) => n.ToString(CultureInfo.InvariantCulture) + " серия выйдет " + when;

        public static string WatchedOf(int watched, int total) =>
            "просмотрено " + watched.ToString(CultureInfo.InvariantCulture) + " из " + total.ToString(CultureInfo.InvariantCulture);
        public const string About = "О тайтле";
        public const string MainCharacters = "Главные герои";
        public const string Reviews = "Рецензии и отзывы";
        public const string Spoilers = "спойлеры";
        public const string DescriptionTitle = "Описание";

        public static string WatchEpisode(int n) => "Смотреть · серия " + n.ToString(CultureInfo.InvariantCulture);

        public static string ReviewsButton(int n) => "Отзывы · " + n.ToString(CultureInfo.InvariantCulture);

        public static string Of(int i, int n) => i.ToString(CultureInfo.InvariantCulture) + " / " + n.ToString(CultureInfo.InvariantCulture);

        // Число оценок как на сайте («27K», «1.2K», «512») → «27 тыс. оценок», «512 оценок»; null — null.
        public static string Votes(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0) return null;
            var last = char.ToUpperInvariant(v[v.Length - 1]);
            if (last == 'K') return v.Substring(0, v.Length - 1).Replace('.', ',') + " тыс. оценок";
            if (last == 'M') return v.Substring(0, v.Length - 1).Replace('.', ',') + " млн оценок";
            if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return v;
            var d = n % 10;
            var dd = n % 100;
            var word = d == 1 && dd != 11 ? "оценка" : d >= 2 && d <= 4 && (dd < 12 || dd > 14) ? "оценки" : "оценок";
            return v + " " + word;
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
