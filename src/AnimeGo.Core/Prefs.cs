using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace AnimeGo.Core
{
    public sealed class DubPref
    {
        public string Main;
        public string Fallback;
    }

    public sealed class Seen
    {
        public int Episode;
        public int Seconds;
        public DateTime AtUtc;
    }

    // Локальные настройки: единственный JSON-файл (спека §8). Записи про озвучки и «виденное» — по аккаунтам.
    public sealed class Prefs
    {
        private readonly string _path;
        private PrefsDto _d;

        private Prefs(string path, PrefsDto d) { _path = path; _d = d; }

        public string Cookies { get => _d.Cookies; set => _d.Cookies = value; }
        public bool SkippedLogin { get => _d.SkippedLogin; set => _d.SkippedLogin = value; }
        public string Quality { get => _d.Quality; set => _d.Quality = value; }

        // Ничего не бросает: нет файла, битый JSON или ошибка чтения дают пустые настройки.
        public static Prefs Load(string path)
        {
            PrefsDto d = null;
            try
            {
                if (File.Exists(path)) d = Json.Parse<PrefsDto>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { d = null; }
            return new Prefs(path, Normalize(d));
        }

        private static PrefsDto Normalize(PrefsDto d)
        {
            if (d == null) d = new PrefsDto();
            if (string.IsNullOrEmpty(d.Quality)) d.Quality = "auto";
            if (d.Users == null) d.Users = new List<UserPrefsDto>();
            foreach (var u in d.Users)
            {
                if (u.Dubs == null) u.Dubs = new List<DubDto>();
                if (u.Seen == null) u.Seen = new List<SeenDto>();
            }
            return d;
        }

        // Атомарно: на ТВ может отключиться питание посреди записи.
        public void Save()
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(_d), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }

        public DubPref Dub(string userKey, int animeId)
        {
            var e = FindDub(userKey, animeId);
            return new DubPref { Main = e?.Main, Fallback = e?.Fallback };
        }

        public void SetMainDub(string userKey, int animeId, string key)
        {
            var e = GetOrAddDub(userKey, animeId);
            e.Main = key;
            if (key != null && key == e.Fallback) e.Fallback = null;
        }

        public void SetFallbackDub(string userKey, int animeId, string key)
        {
            GetOrAddDub(userKey, animeId).Fallback = key;
        }

        // Где у тайтла начинается опенинг (с), по первому нажатию «Пропустить опенинг»; null — ещё не знаем.
        public int? OpeningAt(string userKey, int animeId) => FindDub(userKey, animeId)?.OpeningAt;

        public void SetOpeningAt(string userKey, int animeId, int seconds)
        {
            GetOrAddDub(userKey, animeId).OpeningAt = seconds;
        }

        public Seen Seen(string userKey, int animeId)
        {
            var u = FindUser(userKey);
            if (u == null) return null;
            foreach (var s in u.Seen)
                if (s.AnimeId == animeId)
                    return new Seen { Episode = s.Episode, Seconds = s.Seconds, AtUtc = new DateTime(s.AtUtcTicks, DateTimeKind.Utc) };
            return null;
        }

        public void SetSeen(string userKey, int animeId, int episode, int seconds, DateTime atUtc)
        {
            var u = GetOrAddUser(userKey);
            SeenDto e = null;
            foreach (var s in u.Seen) if (s.AnimeId == animeId) { e = s; break; }
            if (e == null) { e = new SeenDto { AnimeId = animeId }; u.Seen.Add(e); }
            e.Episode = episode;
            e.Seconds = seconds;
            e.AtUtcTicks = atUtc.ToUniversalTime().Ticks;
        }

        public void RemoveSeen(string userKey, int animeId)
        {
            FindUser(userKey)?.Seen.RemoveAll(s => s.AnimeId == animeId);
        }

        // Для ContinueModel: animeId всех записей «виденного» этого аккаунта.
        internal List<int> SeenIds(string userKey)
        {
            var r = new List<int>();
            var u = FindUser(userKey);
            if (u != null) foreach (var s in u.Seen) r.Add(s.AnimeId);
            return r;
        }

        private UserPrefsDto FindUser(string userKey)
        {
            foreach (var u in _d.Users) if (u.Key == userKey) return u;
            return null;
        }

        private UserPrefsDto GetOrAddUser(string userKey)
        {
            var u = FindUser(userKey);
            if (u == null) { u = new UserPrefsDto { Key = userKey }; _d.Users.Add(u); }
            return u;
        }

        private DubDto FindDub(string userKey, int animeId)
        {
            var u = FindUser(userKey);
            if (u == null) return null;
            foreach (var x in u.Dubs) if (x.AnimeId == animeId) return x;
            return null;
        }

        private DubDto GetOrAddDub(string userKey, int animeId)
        {
            var e = FindDub(userKey, animeId);
            if (e == null) { e = new DubDto { AnimeId = animeId }; GetOrAddUser(userKey).Dubs.Add(e); }
            return e;
        }
    }

    [DataContract]
    internal class PrefsDto
    {
        [DataMember(Name = "cookies")] public string Cookies { get; set; }
        [DataMember(Name = "skippedLogin")] public bool SkippedLogin { get; set; }
        [DataMember(Name = "quality")] public string Quality { get; set; }
        [DataMember(Name = "users")] public List<UserPrefsDto> Users { get; set; } = new List<UserPrefsDto>();
    }

    [DataContract]
    internal class UserPrefsDto
    {
        [DataMember(Name = "key")] public string Key { get; set; }
        [DataMember(Name = "dubs")] public List<DubDto> Dubs { get; set; } = new List<DubDto>();
        [DataMember(Name = "seen")] public List<SeenDto> Seen { get; set; } = new List<SeenDto>();
    }

    [DataContract]
    internal class DubDto
    {
        [DataMember(Name = "animeId")] public int AnimeId { get; set; }
        [DataMember(Name = "main")] public string Main { get; set; }
        [DataMember(Name = "fallback")] public string Fallback { get; set; }
        [DataMember(Name = "openingAt")] public int? OpeningAt { get; set; }
    }

    [DataContract]
    internal class SeenDto
    {
        [DataMember(Name = "animeId")] public int AnimeId { get; set; }
        [DataMember(Name = "episode")] public int Episode { get; set; }
        [DataMember(Name = "seconds")] public int Seconds { get; set; }
        [DataMember(Name = "atUtcTicks")] public long AtUtcTicks { get; set; }
    }
}
