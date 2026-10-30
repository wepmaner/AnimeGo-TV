using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;

namespace AnimeGo.Tv
{
    // Главная (спека §5.2, макет Main.dc.html): шапка и ряды «Продолжить просмотр», «Давно не смотрели», «Жанры»,
    // «Новые серии · сезон», «Онгоинги», «ТОП-250». Пустые ряды не показываются, гость видит ряды с третьего.
    // ↑↓ — между шапкой и рядами, ←→ — внутри, фокус в каждом ряду свой. «Назад» — выход из приложения.
    // Данные — из App.Cache, если уже были (вернулись по «Назад»): рисуем сразу и перепроверяем сайт в фоне
    // (позиция просмотра меняется после плеера); впервые — скелетон до ответа.
    sealed class HomeScreen : IScreen
    {
        const int SectionGap = 40, ContentBottom = Theme.H - 40;
        const int MaxBadgeChecks = 3;   // запросов /player за загрузку ради метки «Новая серия»
        const string TopUrl = "/anime/top-250", TopTitle = "ТОП-250";
        const string HomeKey = "home", OngoingKey = "home:ongoing", TopKey = "home:top";
        const int ExtrasFreshSec = 600;   // «Онгоинги» и «ТОП-250» из кеша без перепроверки
        // Ряд «Жанры»: жанры каталога (без «Все аниме») и ТОП-250 в конце.
        static readonly string[] GenreRow = { "Экшен", "Комедия", "Романтика", "Фэнтези", "Приключения", "Сёнен", "Ужасы", "Детектив" };

        readonly App app;
        readonly Widgets ui;
        readonly HomeHeader header;
        readonly Row cont, stale, genres, season, ongoing, top;
        readonly Row[] rows;
        readonly Skeleton skeleton;
        readonly Dictionary<int, int> newEpisode = new Dictionary<int, int>();   // animeId → номер вышедшей серии
        MessageView overlay;   // «Нет связи» поверх экрана, если данных нет совсем
        LogoutPopup popup;
        string headerName;     // чьё имя сейчас в шапке (чтобы не перестраивать её без нужды)
        int focusRow = -1;     // −1 — шапка
        int scrollY, generation;
        bool alive, extrasStarted, rendered, headerPending, retriedExpired;

        public HomeScreen(App app)
        {
            this.app = app;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg);
            cont = new Row(ui, app.Images, 0, 480, 270, 32, RuText.Continue) { OnOk = OpenContinue };
            stale = new Row(ui, app.Images, 0, 200, 280, 32, RuText.Stale, RuText.StaleHint) { OnOk = OpenContinue };
            genres = new Row(ui, app.Images, 0, 0, 64, 16, RuText.Genres) { OnOk = OpenGenre };
            season = new Row(ui, app.Images, 0, 200, 280, 32, RuText.NewEpisodes) { OnOk = OpenTitle };
            ongoing = new Row(ui, app.Images, 0, 200, 280, 32, RuText.Ongoing) { OnOk = OpenTitle };
            top = new Row(ui, app.Images, 0, 200, 280, 32, RuText.Top) { OnOk = OpenTitle };
            rows = new[] { cont, stale, genres, season, ongoing, top };
            skeleton = BuildSkeleton(app.User != null || !app.UserKnown);
            headerPending = !app.UserKnown;
            headerName = app.User?.Name;
            header = new HomeHeader(ui, headerName, headerPending);
        }

        // Пока ждём сайт: у вошедшего (или пока неизвестно) — ряд широких кадров «Продолжить», ниже постеры;
        // у гостя — «таблетки» жанров и постеры. Рисуется под шапкой, как настоящие ряды.
        Skeleton BuildSkeleton(bool loggedIn)
        {
            var s = new Skeleton(ui);
            var y = HomeHeader.Bottom;
            if (loggedIn)
            {
                s.Line(Theme.Margin, y + 6, 360, 30);
                for (var i = 0; i < 4; i++) s.Add(Theme.Margin + i * (480 + 32), y + Row.HeadH, 480, 270, 14);
                y += cont.Height + SectionGap;
            }
            else
            {
                s.Line(Theme.Margin, y + 6, 160, 30);
                var x = Theme.Margin;
                foreach (var w in new[] { 140, 160, 170, 160, 190, 130, 130, 160 }) { s.Add(x, y + Row.HeadH, w, 64, 14); x += w + 16; }
                y += genres.Height + SectionGap;
            }
            s.Line(Theme.Margin, y + 6, 420, 30);
            for (var i = 0; i < 8; i++) s.Add(Theme.Margin + i * (200 + 32), y + Row.HeadH, 200, 280, 10);
            return s;
        }

        public void Show()
        {
            alive = true;
            header.SetActive(true);
            Load();
        }

        public void Hide()
        {
            alive = false;
            skeleton.Hide();
            overlay?.Hide();
            overlay = null;
            ui.Dispose();
        }

        public bool OnKey(string key)
        {
            if (overlay != null) return overlay.OnKey(key);   // «Повторить» / «Назад» — выход
            if (popup != null) return popup.OnKey(key);
            switch (key)
            {
                case Keys.Back: app.Exit(); return true;
                case Keys.Up: MoveRow(-1); return true;
                case Keys.Down: MoveRow(1); return true;
                case Keys.Left: MoveItem(-1); return true;
                case Keys.Right: MoveItem(1); return true;
                case Keys.Ok: Ok(); return true;
            }
            return false;
        }

        // ---- данные ----

        // Главная страница сайта. Есть в кеше — рисуем сразу, иначе скелетон; в любом случае запрашиваем заново
        // (после плеера позиция и порядок «Продолжить» другие). Ошибка при показанном кеше — только в лог.
        void Load()
        {
            var n = ++generation;
            var hit = app.Cache.TryGet(HomeKey, out HomePage cached, out _);
            if (hit) Render(cached, false); else skeleton.Show();
            Task.Run(async () =>
            {
                HomePage home = null;
                string why = null;
                var expired = false;
                try { home = HomeParser.Parse(await app.Api.GetHtmlAsync("/").ConfigureAwait(false)); }
                catch (ApiException e) when (e.Kind == ApiErrorKind.Unauthorized) { expired = true; }
                catch (Exception e) { why = e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name + " " + e.Message; }
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    if (expired && !retriedExpired)
                    {
                        retriedExpired = true;
                        app.SessionExpired();
                        Load();   // ещё раз, уже гостем
                        return;
                    }
                    if (home != null)
                    {
                        Render(home, true);
                        app.Cache.Put(HomeKey, home);   // после Render: SessionExpired внутри него чистит кеш
                        return;
                    }
                    DevLog.Write("home: ошибка " + (expired ? "Unauthorized" : why));
                    if (hit) return;   // на экране кеш — достаточно
                    skeleton.Hide();
                    ShowOverlay(new MessageView(app, RuText.NoConnection, Load, app.Exit));   // Split не зовём: данных нет
                });
            });
        }

        // fresh — ответ сайта только что; false — из кеша (куки и сессию не трогаем).
        void Render(HomePage home, bool fresh)
        {
            skeleton.Hide();
            ShowOverlay(null);
            // Сайт знает лучше: куки есть, а пользователя в ответе нет — сессия кончилась.
            if (fresh && home.User == null && !string.IsNullOrEmpty(app.Prefs.Cookies)) app.SessionExpired();
            else app.User = home.User;
            IReadOnlyList<ContinueCard> active = new ContinueCard[0], old = new ContinueCard[0];
            if (app.User != null)
            {
                // Пустой список при входе — скорее сменилась разметка или сессия полуживая, чем всё досмотрено:
                // Split тогда стёр бы все записи «виденного», поэтому его не зовём.
                if (home.Continue != null && home.Continue.Count > 0)
                {
                    var split = ContinueModel.Split(home.Continue, app.Prefs, app.UserKey, DateTime.UtcNow);
                    active = split.Active;
                    old = split.Stale;
                }
                else DevLog.Write("home: continue empty, seen untouched");
                if (fresh)
                {
                    app.Prefs.Cookies = app.Jar.Export();   // сайт мог обновить куки сессии
                    app.SavePrefs();
                }
            }
            if (headerPending || headerName != app.User?.Name)
            {
                headerPending = false;
                headerName = app.User?.Name;
                header.Build(headerName);
            }
            cont.SetItems(active.Select(ContinueItem).ToList());
            stale.SetItems(old.Select(c => new RowItem { Title = c.Title, Subtitle = RuText.Episode(c.EpisodeNumber), ImageUrl = c.FrameUrl, Tag = c }).ToList());
            genres.SetItems(GenreRow.Select(t => new RowItem { Title = t, Tag = Array.FindIndex(Genres.All, g => g.Title == t) })
                .Append(new RowItem { Title = TopTitle, Tag = -1 }).ToList());
            var when = SeasonName(home.SeasonTitle);
            season.SetTitle(when == null ? RuText.NewEpisodes : RuText.NewEpisodes + " · " + when);
            season.SetItems(Posters(home.Season));
            DevLog.Write("home: " + (fresh ? "сайт" : "кеш") + ", continue " + active.Count + ", stale " + old.Count + ", season " + season.Items.Count +
                (app.User == null ? " (гость)" : ""));

            // Впервые — фокус на первый ряд; при обновлении остаёмся где были, если ряд не опустел.
            if (!rendered || (focusRow >= 0 && rows[focusRow].IsEmpty)) focusRow = FirstRow();
            rendered = true;
            ApplyFocus();
            if (app.PendingToast != null) { Toast.Show(ui, app.PendingToast); app.PendingToast = null; }
            CheckNewEpisodes(active);
            LoadExtras();
        }

        RowItem ContinueItem(ContinueCard c) => new RowItem
        {
            Title = c.Title,
            Subtitle = RuText.Episode(c.EpisodeNumber),   // озвучку не пишем: в настройках только её ключ, без названия
            ImageUrl = c.FrameUrl,
            Progress = c.DurationSec > 0 ? c.Progress : -1,
            TimeLabel = c.DurationSec > 0 ? RuText.Time(c.PositionSec) + " / " + RuText.Time(c.DurationSec) : null,
            Badge = newEpisode.ContainsKey(c.AnimeId) ? RuText.NewEpisodeBadge : null,   // уже знаем — не теряем при перерисовке
            Tag = c,
        };

        static List<RowItem> Posters(IReadOnlyList<AnimeCard> cards) =>
            (cards ?? new AnimeCard[0]).Select(a => new RowItem { Title = a.Title, ImageUrl = a.PosterUrl, Tag = a }).ToList();

        // «Аниме осеннего сезона» → «осень 2026».
        static string SeasonName(string siteTitle)
        {
            var t = (siteTitle ?? "").ToLowerInvariant();
            var name = t.Contains("осен") ? "осень" : t.Contains("зим") ? "зима" : t.Contains("весен") ? "весна" : t.Contains("лет") ? "лето" : null;
            return name == null ? null : name + " " + DateTime.Now.Year;
        }

        // «Новая серия»: досмотренные почти до конца карточки — есть ли на сайте серия дальше. В фоне, не больше трёх.
        // /player — через кеш приложения: при обновлении главной из кеша запросы не повторяются.
        void CheckNewEpisodes(IReadOnlyList<ContinueCard> active)
        {
            var n = generation;
            foreach (var c in active.Where(c => c.Progress >= 0.8 && !newEpisode.ContainsKey(c.AnimeId)).Take(MaxBadgeChecks))
                Task.Run(async () =>
                {
                    try
                    {
                        var info = await app.PlayerInfoAsync(c.AnimeId).ConfigureAwait(false);
                        var next = info.Episodes.Where(e => e.Number > c.EpisodeNumber).Select(e => e.Number).DefaultIfEmpty(0).Min();
                        DevLog.Write("badge: anime " + c.AnimeId + " ep " + c.EpisodeNumber + " → " + (next > 0 ? "новая " + next : "нет"));
                        if (next == 0) return;
                        App.Ui(() =>
                        {
                            var index = cont.Items.ToList().FindIndex(i => i.Tag is ContinueCard k && k.AnimeId == c.AnimeId);
                            if (!alive || n != generation || index < 0 || cont.Items[index].Badge != null) return;
                            newEpisode[c.AnimeId] = next;
                            cont.Items[index].Badge = RuText.NewEpisodeBadge;
                            cont.AddBadge(index);
                            Layout();
                        });
                    }
                    catch (Exception e) { DevLog.Write("badge: anime " + c.AnimeId + " " + e.GetType().Name + " " + e.Message); }
                });
        }

        // Ряды ниже макета — после главной, по очереди, чтобы не мешать ей. Из кеша — сразу; на сайт — если нет
        // или старше ExtrasFreshSec.
        void LoadExtras()
        {
            if (extrasStarted) return;
            extrasStarted = true;
            var needOngoing = !FromCache(ongoing, OngoingKey);
            var needTop = !FromCache(top, TopKey);
            if (!needOngoing && !needTop) return;
            Task.Run(async () =>
            {
                if (needOngoing) await LoadRow(ongoing, "/anime/status/ongoing", OngoingKey).ConfigureAwait(false);
                if (needTop) await LoadRow(top, "/anime/top-250", TopKey).ConfigureAwait(false);
            });
        }

        // true — ряд показан из кеша и он свежий; показан, но устарел — false (перезапросим, экран не трогаем).
        bool FromCache(Row row, string key)
        {
            if (!app.Cache.TryGet(key, out IReadOnlyList<AnimeCard> cards, out var age)) return false;
            row.SetItems(Posters(cards));
            Layout();
            return age < ExtrasFreshSec;
        }

        async Task LoadRow(Row row, string url, string key)
        {
            try
            {
                var cards = CatalogParser.Cards(await app.Api.GetHtmlAsync(url).ConfigureAwait(false));
                DevLog.Write("home: " + key + " " + cards.Count);
                App.Ui(() =>
                {
                    app.Cache.Put(key, cards);
                    if (!alive) return;
                    row.SetItems(Posters(cards));
                    Layout();
                });
            }
            catch (Exception e) { DevLog.Write("home: " + key + " " + e.GetType().Name + " " + e.Message); }
        }

        // ---- раскладка и фокус ----

        // Ряды сверху вниз с зазором 40; прокрутка — чтобы ряд в фокусе был целиком между шапкой и низом экрана.
        void Layout()
        {
            var y = HomeHeader.Bottom;
            foreach (var r in rows)
            {
                r.Y = y;
                if (!r.IsEmpty) y += r.Height + SectionGap;
            }
            if (focusRow < 0) scrollY = 0;
            else
            {
                var r = rows[focusRow];
                if (r.Y + r.Height - scrollY > ContentBottom) scrollY = r.Y + r.Height - ContentBottom;
                if (r.Y - scrollY < HomeHeader.Bottom) scrollY = r.Y - HomeHeader.Bottom;
            }
            foreach (var r in rows) r.Place(scrollY);
            header.Raise();
            popup?.Raise();
            Toast.Raise();
        }

        void ApplyFocus()
        {
            header.SetActive(focusRow < 0);
            for (var i = 0; i < rows.Length; i++) rows[i].SetActive(i == focusRow);
            Layout();
        }

        int FirstRow()
        {
            for (var i = 0; i < rows.Length; i++) if (!rows[i].IsEmpty) return i;
            return -1;
        }

        void MoveRow(int d)
        {
            var next = focusRow;
            do next += d; while (next >= 0 && next < rows.Length && rows[next].IsEmpty);
            if (next >= rows.Length) return;
            if (next < 0) next = -1;
            if (next == focusRow) return;
            focusRow = next;
            ApplyFocus();
            DevLog.Write(focusRow < 0 ? "focus: header " + header.Focus : "focus: row " + focusRow + " item " + rows[focusRow].Focus);
        }

        void MoveItem(int d)
        {
            if (focusRow < 0) header.Move(d); else rows[focusRow].Move(d);
        }

        void Ok()
        {
            if (focusRow >= 0) { rows[focusRow].Ok(); return; }
            var i = header.Focus;
            if (i == 0) return;   // мы и так на «Главной»
            if (i < HomeHeader.UserItem) { OpenTab(i); return; }
            if (app.User == null) { app.Navigate(new LoginScreen(app)); return; }
            popup = new LogoutPopup(ui, Logout, ClosePopup);
        }

        // ---- действия ----

        void OpenContinue(RowItem it)
        {
            var c = (ContinueCard)it.Tag;
            int ep = c.EpisodeNumber, pos = c.PositionSec;
            if (it.Badge != null && newEpisode.TryGetValue(c.AnimeId, out var next)) { ep = next; pos = 0; }   // новая серия — с начала
            DevLog.Write("open: anime " + c.AnimeId + " ep " + ep + " pos " + pos);
            var url = "/anime/" + c.Slug + "-" + c.AnimeId;
            app.Open(new PlayerScreen(app, c.AnimeId, url, ep, pos), () => new HomeScreen(app));
        }

        void OpenTitle(RowItem it)
        {
            var a = (AnimeCard)it.Tag;
            DevLog.Write("open: title " + a.Id + " " + a.Url);
            // Тайтл есть в «Продолжить» — «Смотреть» начнёт с позиции сайта, а не только с «виденного» на ТВ.
            var resume = cont.Items.Concat(stale.Items).Select(i => i.Tag).OfType<ContinueCard>().FirstOrDefault(c => c.AnimeId == a.Id);
            app.Open(new TitleScreen(app, a.Id, a.Url, resume), () => new HomeScreen(app));
        }

        void OpenGenre(RowItem it)
        {
            var genre = (int)it.Tag;
            DevLog.Write("open: grid " + (genre < 0 ? TopUrl : Genres.All[genre].Url) + " " + it.Title);
            OpenGrid(genre < 0 ? GridSource.Catalog(TopUrl, TopTitle) : GridSource.Catalog(genre, false));
        }

        // Вкладки шапки: 1 — «Поиск», 2 — «Мой список» (гостю — вход), 3 — «Каталог».
        void OpenTab(int i)
        {
            DevLog.Write("open: tab " + HomeHeader.Tabs[i]);
            if (i == 1) OpenGrid(GridSource.Search());
            else if (i == 3) OpenGrid(GridSource.Catalog(0, true));
            else if (app.User == null) app.Navigate(new LoginScreen(app));
            else OpenGrid(GridSource.List());
        }

        void OpenGrid(GridSource src) => app.Open(new GridScreen(app, src), () => new HomeScreen(app));

        void Logout()
        {
            ClosePopup();
            DevLog.Write("logout");
            var _ = app.Api.LogoutAsync();
            app.User = null;
            app.Prefs.Cookies = null;
            app.SavePrefs();
            app.ForgetUserData();
            generation++;   // ответы «Новой серии» для прежнего аккаунта больше не нужны
            newEpisode.Clear();
            cont.SetItems(new RowItem[0]);
            stale.SetItems(new RowItem[0]);
            headerName = null;
            header.Build(null);
            focusRow = FirstRow();   // фокус был на шапке («Выйти»): на первый непустой ряд, как после загрузки
            ApplyFocus();
        }

        void ClosePopup()
        {
            popup?.Close();
            popup = null;
        }

        void ShowOverlay(MessageView next)
        {
            overlay?.Hide();
            overlay = next;
            overlay?.Show();
        }
    }
}
