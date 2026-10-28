using System.Collections.Generic;
using System.Globalization;

namespace AnimeGo.Core
{
    public sealed class AnimeCard
    {
        public int Id;
        public string Slug;
        public string Title;
        public string TitleOriginal;   // null, если на сайте нет
        public string PosterUrl;
        public string Rating;          // как на сайте ("7.2"), null без бейджа
        public string Kind;            // тип (ТВ, ONA...), null в сетке
        public IReadOnlyList<string> Genres = new string[0];

        public string Url => "/anime/" + Slug + "-" + Id;
    }

    public sealed class Paging
    {
        public string UrlTemplate;     // с подстановкой __page__
        public int Page;
        public int PageSize;
        public int TotalItems;
        public bool HasMore;
        public IReadOnlyDictionary<string, string> Params = new Dictionary<string, string>();

        public string PageUrl(int n)
        {
            var url = UrlTemplate.Replace("__page__", n.ToString(CultureInfo.InvariantCulture));
            var parts = new List<string>();
            foreach (var kv in Params) parts.Add(kv.Key + "=" + kv.Value);
            return parts.Count == 0 ? url : url + "?" + string.Join("&", parts);
        }
    }

    public sealed class Episode
    {
        public int Id;                 // data-episode
        public int Number;
        public bool Watched;
        public int Page;               // страница списка серий
    }

    public sealed class Translation
    {
        public string Key;
        public int? Id;
        public string Title;
        public string Kind;            // "Дубляж"/"Многоголосый"/"Субтитры" из текста, иначе ""
    }

    public sealed class VideoSource
    {
        public string Provider;        // cvh|aniboom|kodik
        public string PlayerUrl;       // с https:
        public string TranslationKey;
        public int? TranslationId;
        public string TranslationTitle;
    }

    public sealed class PlayerInfo
    {
        public int AnimeId;
        public IReadOnlyList<Episode> Episodes = new Episode[0];
        public IReadOnlyList<Translation> Translations = new Translation[0];
        public IReadOnlyList<VideoSource> Sources = new VideoSource[0];
        public int EpisodesTotal;
        public IReadOnlyList<string> ProviderPriority = new string[0];
        public string ContinuationUrl;
        public string EpisodesUrl;

        // С какой серии «Смотреть». Число в ContinuationUrl сайт не двигает (у тайтла с просмотренными 1–11 там /1,
        // а /continuation/N просто отдаёт серию N), поэтому главное — отметки: серия после последней просмотренной.
        // Ничего не просмотрено — первая; всё просмотрено — последняя (догнали онгоинг: не начинать заново,
        // пересмотр с первой — один выбор в списке серий). continuation — номер из ответа continuation, если он больше.
        public int ResumeEpisode(int? continuation)
        {
            var numbers = new List<int>();
            var lastWatched = 0;
            foreach (var e in Episodes)
            {
                if (e.Number <= 0) continue;
                numbers.Add(e.Number);
                if (e.Watched && e.Number > lastWatched) lastWatched = e.Number;
            }
            var next = 0;
            foreach (var n in numbers) if (n > lastWatched && (next == 0 || n < next)) next = n;
            var ep = next > 0 ? next : lastWatched > 0 ? lastWatched : 1;
            return continuation.HasValue && continuation.Value > ep ? continuation.Value : ep;
        }
    }

    public sealed class ContinueCard
    {
        public int AnimeId;
        public string Slug;
        public string Title;
        public int EpisodeNumber;
        public int PositionSec;
        public int DurationSec;
        public string FrameUrl;
        public double Progress => DurationSec > 0 ? (double)PositionSec / DurationSec : 0;
    }

    public sealed class HomePage
    {
        public User User;                          // null: гость
        public IReadOnlyList<ContinueCard> Continue;
        public string SeasonTitle;
        public string SeasonUrl;
        public IReadOnlyList<AnimeCard> Season;
    }

    public sealed class EpisodeVideos
    {
        public bool? IsWatched;
        public int NumVideos;
        public IReadOnlyList<VideoSource> Sources = new VideoSource[0];
    }

    public sealed class RelatedTitle
    {
        public int Id;
        public string Slug;
        public string Title;
        public string Kind;            // "Сериал", "Фильм"... null, если нет
        public string Year;            // null, если нет
        public string PosterUrl;
    }

    public sealed class AnimeInfo
    {
        public int Id;
        public string Title;
        public string TitleOriginal;   // первый синоним под заголовком, null если нет
        public string Description;
        public string PosterUrl;
        public IReadOnlyList<string> Genres = new string[0];
        public string Kind;            // из поля «Тип», null если нет
        public string Year;            // первый год из поля «Выпуск», null если нет
        public IReadOnlyList<RelatedTitle> Related = new RelatedTitle[0];
    }

    public sealed class Season
    {
        public int AnimeId;
        public string Title;
        public int Order;
        public bool IsCurrent;
    }

    public sealed class User
    {
        public int Id;
        public string Name;
    }

    public sealed class Stream
    {
        public string Url;
        public string Quality;         // auto|1080p|720p|480p|360p|240p
        public bool IsHls;
    }

    public sealed class StreamSet
    {
        static readonly string[] Known = { "1080p", "720p", "480p", "360p", "240p" };

        public IReadOnlyList<Stream> Streams = new Stream[0];
        public int DurationSec;

        // Точное совпадение; для известного качества — ближайшее ниже, затем выше;
        // иначе (auto, незнакомая строка) — auto, а без него самое высокое.
        public Stream Pick(string preferredQuality)
        {
            if (Streams.Count == 0) return null;
            foreach (var s in Streams)
                if (s.Quality == preferredQuality) return s;
            var want = System.Array.IndexOf(Known, preferredQuality);
            if (want >= 0)
            {
                for (var i = want + 1; i < Known.Length; i++)
                    foreach (var s in Streams) if (s.Quality == Known[i]) return s;
                for (var i = want - 1; i >= 0; i--)
                    foreach (var s in Streams) if (s.Quality == Known[i]) return s;
            }
            foreach (var s in Streams) if (s.Quality == "auto") return s;
            foreach (var q in Known)
                foreach (var s in Streams) if (s.Quality == q) return s;
            return Streams[0];
        }
    }
}
