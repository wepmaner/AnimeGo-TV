using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;
using Tizen.Applications;

namespace AnimeGo.Tv
{
    interface IScreen
    {
        void Show();
        void Hide();              // освобождает свои виджеты; экран больше не используется
        bool OnKey(string key);   // true — нажатие обработано
    }

    // Имена клавиш пульта (EcoreKeyEventArgs.KeyName). Каждое нажатие пишется в DevLog — так их и проверяли.
    static class Keys
    {
        public const string Up = "Up", Down = "Down", Left = "Left", Right = "Right", Ok = "Return", Select = "Select", Cancel = "Cancel", Back = "XF86Back", Exit = "XF86Exit";
        // Back и Exit подтверждены по DevLog. FF/Rew/Stop/PlayPause на Samsung Smart Remote нет (имена не проверены).
        public const string Play = "XF86AudioPlay", Pause = "XF86AudioPause", PlayPause = "XF86PlayBack", Stop = "XF86AudioStop";
        public const string FF = "XF86AudioNext", Rew = "XF86AudioPrev";
    }

    sealed class App : CoreUIApplication
    {
        public Window Win;
        public SiteApi Api;
        public Prefs Prefs;
        public CookieJar Jar;
        // null — гость. Смена (и первое значение после запуска) уходит в Stash как /hello.
        public User User
        {
            get => user;
            set
            {
                var changed = !userKnown || user?.Id != value?.Id;
                user = value;
                userKnown = true;
                if (changed) DevLog.Hello(value);
            }
        }
        User user;
        bool userKnown;
        // false — куки есть, но сайт ещё не сказал, кто мы (до первого ответа главной); шапка тогда не пишет «Войти».
        public bool UserKnown => userKnown;
        public string PendingToast;   // сообщение для следующего экрана главной (показывает он)
        public ImageCache Images;
        // Разобранные ответы сайта: экран, на который вернулись, рисуется из кеша без «Загрузки» (спека 2026-10-30).
        public readonly DataCache Cache = new DataCache();
        public string UserKey => User?.Id.ToString() ?? "guest";

        // Сессия сменилась (вход, выход, куки протухли): «Продолжить», «Мой список» и отметки — другого пользователя.
        public void ForgetUserData() => Cache.Clear();

        // Общие запросы с кешем: свежее freshSec секунд — из памяти, иначе с сайта (и в кеш). Ошибки — наружу, как у Api.
        // Объекты общие: Episode.Watched, выставленный плеером, виден и странице тайтла, и экрану серий.
        public Task<PlayerInfo> PlayerInfoAsync(int animeId, int freshSec = 120) =>
            CachedAsync("player:" + animeId, freshSec, () => Api.PlayerAsync(animeId));

        public Task<AnimeInfo> AnimeInfoAsync(string url, int animeId, int freshSec = 600) =>
            CachedAsync("title:" + url, freshSec, async () => AnimeParser.Parse(await Api.GetHtmlAsync(url).ConfigureAwait(false), animeId));

        public Task<EpisodeVideos> EpisodeVideosAsync(int episodeId, int freshSec = 300) =>
            CachedAsync("videos:" + episodeId, freshSec, () => Api.EpisodeVideosAsync(episodeId));

        public async Task<T> CachedAsync<T>(string key, int freshSec, Func<Task<T>> fetch) where T : class
        {
            if (Cache.TryGet(key, out T hit, out var age) && age < freshSec) return hit;
            var fresh = await fetch().ConfigureAwait(false);
            Cache.Put(key, fresh);
            return fresh;
        }
        IScreen screen;
        // «Назад»: как пересоздать экраны, с которых пришли (сверху — последний). Экраны не храним живыми — их виджеты
        // освобождает Hide, поэтому возвращаемся на свежий экран с теми же параметрами.
        readonly List<Func<IScreen>> history = new List<Func<IScreen>>();
        const int MaxHistory = 20;
        EcoreEvent<EcoreKeyEventArgs> keys;   // поле, а не локальная: иначе GC соберёт обработчик, и EFL упадёт на следующей клавише

        protected override void OnCreate()
        {
            base.OnCreate();
            current = this;
            Win = new Window("animego");
            Win.Show();
            // Окно прозрачное: под ним — плоскость видео плеера. Фон рисует каждый экран сам.
            Win.SetContent(new Background(Win) { Color = Color.Transparent });
            keys = new EcoreEvent<EcoreKeyEventArgs>(EcoreEventType.KeyDown, EcoreKeyEventArgs.Create);
            keys.On += (s, e) => OnKey(e.KeyName);

            Prefs = Prefs.Load(Path.Combine(DirectoryInfo.Data, "prefs.json"));
            Jar = CookieJar.Import(Prefs.Cookies);
            Api = new SiteApi(Jar);
            Images = new ImageCache(DirectoryInfo.Cache);
            DevLog.Write("sync ctx: " + (System.Threading.SynchronizationContext.Current?.GetType().Name ?? "none"));
            DevLog.Write("start, cookies " + (string.IsNullOrEmpty(Prefs.Cookies) ? "нет" : "есть"));
            DevLog.Start();
            StartSession();
        }

        // Куки есть — сразу главная со скелетоном: её ответ «/» и скажет, кто мы (раньше тот же запрос ходил дважды:
        // whoami под «Загрузкой…», потом главная). Нет — экран входа (если не отказывались) или гостем.
        void StartSession()
        {
            if (string.IsNullOrEmpty(Prefs.Cookies)) User = null;   // гость: Stash узнаёт об этом из /hello
            if (!string.IsNullOrEmpty(Prefs.Cookies) || Prefs.SkippedLogin) ShowHome(); else Navigate(new LoginScreen(this));
        }

        // Сайт не узнал нашу сессию (401/403 или в ответе нет пользователя): куки сброшены, дальше гостем.
        public void SessionExpired()
        {
            DevLog.Write("session: недействительна, куки сброшены");
            Prefs.Cookies = null;
            SavePrefs();
            var _ = Api.LogoutAsync();
            User = null;
            ForgetUserData();
        }

        public void ShowHome()
        {
            history.Clear();
            Navigate(new HomeScreen(this));
        }

        // Перейти на next; «Назад» с него вернёт на экран, который соберёт back.
        public void Open(IScreen next, Func<IScreen> back)
        {
            history.Add(back);
            if (history.Count > MaxHistory) history.RemoveAt(0);   // цепочка «Связанного» не растёт бесконечно
            Navigate(next);
        }

        // На предыдущий экран; истории нет — на главную.
        public void Back()
        {
            if (history.Count == 0) { ShowHome(); return; }
            var back = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Navigate(back());
        }

        public void SavePrefs()
        {
            try { Prefs.Save(); }
            catch (Exception e) { DevLog.WriteNow("prefs save: " + e.GetType().Name + " " + e.Message); }
        }

        public void Navigate(IScreen next)
        {
            DevLog.Write("nav: " + screen?.GetType().Name + " -> " + next.GetType().Name);
            screen?.Hide();
            screen = next;
            screen.Show();
        }

        // Из любого потока — выполнить на главном (UI) потоке.
        // Исключение в действии — как в OnKey: в лог и сообщение на экран, а не тихое падение приложения.
        public static void Ui(Action a) => EcoreMainloop.PostAndWakeUp(() =>
        {
            try { a(); }
            catch (Exception e)
            {
                if (current != null) current.Crash("ui", e);
                else DevLog.WriteNow("ui crash: " + e);
            }
        });

        static App current;   // для Ui: показать сообщение об ошибке

        // Время последнего OK (Clock.Ms): Return и Select приходят парой за ~8 мс.
        long okAt = -1_000_000;

        void OnKey(string name)
        {
            var key = name == Keys.Select ? Keys.Ok : name;   // Select — то же OK
            if (key == Keys.Ok)
            {
                long now = Clock.Ms, dt = now - okAt;
                if (dt >= 0 && dt < 50) { DevLog.Write("key " + name + " (dup)"); return; }   // вторая половина пары
                okAt = now;
            }
            DevLog.Write("key " + name);
            try
            {
                if (screen != null && screen.OnKey(key)) return;
                if (key == Keys.Back) Exit();
            }
            catch (Exception e) { Crash("key " + key, e); }
        }

        // Не падать: показать сообщение, «Назад» — выход.
        void Crash(string where, Exception e)
        {
            DevLog.WriteNow(where + " crash: " + e);
            try { screen?.Hide(); } catch (Exception h) { DevLog.WriteNow("hide: " + h); }
            screen = null;
            try { Navigate(new MessageView(this, RuText.SourceFailed, null, Exit)); }
            catch (Exception m) { DevLog.WriteNow("crash view: " + m); }
        }

        protected override void OnTerminate()
        {
            screen?.Hide();
            screen = null;
            base.OnTerminate();
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => DevLog.WriteNow("UNHANDLED: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => DevLog.WriteNow("UNOBSERVED: " + e.Exception);
            DevLog.WriteNow("main");
            Elementary.Initialize();
            Elementary.ThemeOverlay();
            new App().Run(args);
        }
    }
}
