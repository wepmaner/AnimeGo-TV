using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Что показывает сетка. FirstPageUrl null — поиск; MyList — «Мой список» с вкладками; Browse — каталог со списком
    // жанров слева (Genre — индекс в Genres.All). Query, Tab, Genre и Focus — состояние, с которым экран пересоздаётся
    // по «Назад» со страницы тайтла.
    sealed class GridSource
    {
        public string Title;
        public string FirstPageUrl;
        public bool MyList;
        public bool Browse;
        public bool StartInSide;   // каталог из шапки: фокус сначала в списке жанров
        public string Query;
        public int Tab;
        public int Genre;
        public int Focus;

        public bool IsSearch => FirstPageUrl == null && !MyList;

        public static GridSource Search() => new GridSource { Title = RuText.Search };
        public static GridSource Catalog(string url, string title) => new GridSource { Title = title, FirstPageUrl = url };
        public static GridSource Catalog(int genre, bool startInSide) => new GridSource
        {
            Title = Genres.All[genre].Title, FirstPageUrl = Genres.All[genre].Url, Browse = true, Genre = genre, StartInSide = startInSide,
        };
        public static GridSource List(int tab = 0) => new GridSource { Title = RuText.MyList, MyList = true, Tab = tab, FirstPageUrl = MyListTabs.Url(tab) };
    }

    // Список каталога: «Все аниме» и жанры сайта (/anime/genre/{slug}).
    static class Genres
    {
        public static readonly (string Title, string Url)[] All =
        {
            ("Все аниме", "/anime"), ("Экшен", "/anime/genre/action"), ("Приключения", "/anime/genre/adventure"),
            ("Комедия", "/anime/genre/comedy"), ("Драма", "/anime/genre/drama"), ("Фэнтези", "/anime/genre/fantasy"),
            ("Фантастика", "/anime/genre/sci-fi"), ("Романтика", "/anime/genre/romance"), ("Повседневность", "/anime/genre/slice-of-life"),
            ("Сверхъестественное", "/anime/genre/supernatural"), ("Детектив", "/anime/genre/mystery"), ("Триллер", "/anime/genre/suspense"),
            ("Ужасы", "/anime/genre/horror"), ("Спорт", "/anime/genre/sports"), ("Исекай", "/anime/genre/isekai"),
            ("Сёнен", "/anime/genre/shounen"), ("Сэйнэн", "/anime/genre/seinen"),
        };
    }

    // Вкладки «Моего списка» в порядке сайта и их type в /profile/?type=N.
    static class MyListTabs
    {
        public static readonly (string Name, int Type)[] All =
            { ("Смотрю", 1), ("Запланировано", 5), ("Просмотрено", 2), ("Отложено", 3), ("Брошено", 4), ("Пересматриваю", 6) };

        public static string Url(int tab) => "/profile/?type=" + All[tab].Type;
    }

    // Что уже показали по этому адресу или запросу: все догруженные страницы и состояние пагинации. Лежит в App.Cache,
    // чтобы по «Назад» со страницы тайтла сетка вернулась на то же место без запросов и «Загрузки».
    sealed class GridCache
    {
        public List<AnimeCard> Cards = new List<AnimeCard>();
        public Paging Paging;
        public string NextFrame;
    }

    // Каталог / поиск / «Мой список» (спека §5.3–5.4): заголовок, для списка — вкладки, для поиска — строка запроса,
    // ниже сетка постеров по 6 в ряд. Каталог (Browse) — слева список жанров, справа сетка по 5.
    // Следующая страница грузится заранее, когда до конца осталось 2 строки. «Назад» — на прежний экран.
    // Данные из App.Cache рисуются сразу; старше FreshSec — первая страница перепроверяется в фоне.
    sealed class GridScreen : IScreen
    {
        const int TitleY = 40, BarY = 110, BarH = 52, QueryH = 64, PillFont = 20, CharW = 11;
        const int SideW = 360, SideGap = 48, ItemH = 50, ItemGap = 4, GridLeft = Theme.Margin + SideW + SideGap;
        const double SideDelay = 0.5;   // с: жанр под фокусом открывается, когда список перестали листать
        const int FreshSec = 600;
        enum Area { Bar, Grid, Side }   // Bar — вкладки или строка запроса, Side — список жанров

        readonly App app;
        readonly GridSource src;
        readonly Widgets ui;
        readonly Grid grid;
        readonly Label status, head;
        readonly Spinner more;   // догрузка следующей страницы — под последней строкой
        readonly List<EvasObject> side = new List<EvasObject>();
        readonly UiTimer sideTimer = new UiTimer();
        int sideFocus;
        readonly HashSet<int> ids = new HashSet<int>();
        readonly List<EvasObject> bar = new List<EvasObject>();
        EvasObject barFrame;
        Label query;
        MessageView overlay;
        OnScreenKeyboard keyboard;
        GridCache cache;      // запись кеша того, что сейчас в сетке (тот же объект, что в App.Cache)
        Paging paging;        // бесконечная прокрутка каталога; null — её нет
        string nextFrame;     // следующая страница «Моего списка»; null — нет
        Area area = Area.Grid;
        int generation;
        int restoreTo = -1, restorePages;   // фокус, который ждёт догрузки страниц (вернулись со страницы тайтла)
        bool alive, loadingMore;
        const int MaxRestorePages = 10;

        bool HasMore => paging?.HasMore == true || nextFrame != null;

        public GridScreen(App app, GridSource src)
        {
            this.app = app;
            this.src = src;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg);
            var left = src.Browse ? GridLeft : Theme.Margin;
            if (src.Browse) ui.Text(Theme.Margin, TitleY, SideW, 48, Theme.T(RuText.Catalog, 30, bold: true));
            head = ui.Text(left, TitleY, Theme.W - Theme.Margin - left, 48, Theme.T(src.Title, 30, bold: true));
            var top = src.IsSearch || src.MyList ? BarY + QueryH + 32 : BarY;
            grid = src.Browse ? new Grid(ui, app.Images, top, GridLeft, Theme.W - Theme.Margin - GridLeft, 5) : new Grid(ui, app.Images, top);
            status = ui.Text(left, top + 40, Theme.W - Theme.Margin - left, 48, "", false);
            more = new Spinner(ui, left + 14, 0, 14, 4, Theme.Muted);
            sideFocus = src.Genre;
            if (src.Browse) BuildSide();
            if (src.MyList) BuildTabs();
            if (src.IsSearch) BuildQuery();
        }

        public void Show()
        {
            alive = true;
            if (!src.IsSearch) { Load(true); return; }
            if (string.IsNullOrEmpty(src.Query)) { SetArea(Area.Bar); OpenKeyboard(); }
            else Load(false, true);   // вернулись со страницы тайтла — тот же запрос
        }

        public void Hide()
        {
            alive = false;
            generation++;
            sideTimer.Stop();
            more.Hide();
            grid.HideSkeleton();
            keyboard?.Close();
            keyboard = null;
            overlay?.Hide();
            overlay = null;
            ui.Dispose();
        }

        public bool OnKey(string key)
        {
            if (keyboard != null) return keyboard.OnKey(key);
            if (overlay != null) return overlay.OnKey(key);
            if (key == Keys.Back) { app.Back(); return true; }
            if (area == Area.Bar) return BarKey(key);
            if (area == Area.Side) return SideKey(key);
            restoreTo = -1;   // пользователь сам двигает фокус — не перебивать
            switch (key)
            {
                case Keys.Left: if (!grid.Move(-1, 0) && src.Browse) { sideFocus = src.Genre; SetArea(Area.Side); } return true;
                case Keys.Right: grid.Move(1, 0); return true;
                case Keys.Down: grid.Move(0, 1); MaybeLoadMore(); return true;
                case Keys.Up: if (!grid.Move(0, -1) && (src.MyList || src.IsSearch)) SetArea(Area.Bar); return true;
                case Keys.Ok: OpenTitle(); return true;
            }
            return false;
        }

        bool BarKey(string key)
        {
            switch (key)
            {
                case Keys.Down: if (grid.Count > 0) SetArea(Area.Grid); return true;
                case Keys.Left: case Keys.Right:
                    if (!src.MyList) return true;
                    var tab = Math.Max(0, Math.Min(src.Tab + (key == Keys.Left ? -1 : 1), MyListTabs.All.Length - 1));
                    if (tab == src.Tab) return true;
                    src.Tab = tab;
                    src.FirstPageUrl = MyListTabs.Url(tab);
                    src.Focus = 0;
                    BuildTabs();
                    Load(false);
                    return true;
                case Keys.Ok:
                    if (src.IsSearch) OpenKeyboard();
                    else if (grid.Count == 0) Load(false);   // после ошибки — повторить
                    return true;
                case Keys.Up: return true;
            }
            return false;
        }

        // Список жанров: ↑↓ — по списку (жанр открывается через SideDelay), → и OK — в сетку (сразу открыв жанр под фокусом).
        bool SideKey(string key)
        {
            switch (key)
            {
                case Keys.Up: case Keys.Down:
                    var i = Math.Max(0, Math.Min(sideFocus + (key == Keys.Up ? -1 : 1), Genres.All.Length - 1));
                    if (i == sideFocus) return true;
                    sideFocus = i;
                    BuildSide();
                    sideTimer.Start(SideDelay, () => SelectGenre(sideFocus, false));
                    return true;
                case Keys.Right: case Keys.Ok:
                    sideTimer.Stop();
                    if (sideFocus != src.Genre) SelectGenre(sideFocus, true);
                    else if (grid.Count > 0) SetArea(Area.Grid);
                    else if (key == Keys.Ok) Load(false);   // после ошибки — повторить
                    return true;
                case Keys.Left: return true;
            }
            return false;
        }

        void SelectGenre(int i, bool toGrid)
        {
            if (!alive || i == src.Genre) return;
            src.Genre = i;
            src.Title = Genres.All[i].Title;
            src.FirstPageUrl = Genres.All[i].Url;
            src.Focus = 0;
            head.Text = Theme.T(src.Title, 30, bold: true);
            BuildSide();
            Load(false, toGrid);
        }

        // ---- данные ----

        // Первая страница (или результаты поиска). full — вход на экран: ошибка без кеша на весь экран, иначе строкой статуса.
        void Load(bool full) => Load(full, full && !src.StartInSide);

        string CacheKey => src.IsSearch ? "search:" + src.Query : "grid:" + src.FirstPageUrl;
        string Empty => src.MyList ? RuText.EmptyList : RuText.NothingFound;
        string What => src.IsSearch ? "search \"" + src.Query + "\"" : "grid " + src.Title + (src.MyList ? " " + MyListTabs.All[src.Tab].Name : "");

        void Load(bool full, bool toGrid)
        {
            var n = ++generation;
            var key = CacheKey;
            var q = src.Query;
            var url = src.FirstPageUrl;
            DevLog.Write(What + ": load");
            Reset();
            var hit = app.Cache.TryGet(key, out GridCache c, out var age);
            if (hit)
            {
                // Всё, что успели долистать, — сразу; фокус восстановится без догрузки страниц.
                cache = c;
                paging = c.Paging;
                nextFrame = c.NextFrame;
                DevLog.Write(What + ": кеш, " + c.Cards.Count + " cards, " + (int)age + " с");
                Fill(c.Cards, Empty, toGrid);
                if (age < FreshSec) return;
            }
            else grid.ShowSkeleton();
            Task.Run(async () =>
            {
                IReadOnlyList<AnimeCard> cards = null;
                Paging p = null;
                string next = null, why = null;
                try
                {
                    if (src.IsSearch) cards = await app.Api.SearchAsync(q).ConfigureAwait(false);
                    else
                    {
                        var html = await app.Api.GetHtmlAsync(url).ConfigureAwait(false);
                        cards = CatalogParser.Cards(html);
                        p = CatalogParser.Paging(html);
                        next = src.MyList ? CatalogParser.NextFrameUrl(html) : null;
                    }
                }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    if (cards == null)
                    {
                        DevLog.Write(What + ": ошибка " + why);
                        if (hit) return;   // на экране кеш
                        grid.HideSkeleton();
                        if (full) ShowOverlay(new MessageView(app, RuText.NoConnection, () => Load(true), app.Back));
                        else SetStatus(RuText.NoConnection + " · " + RuText.RetryHint);
                        return;
                    }
                    if (hit)
                    {
                        // Начало не изменилось — оставляем всё долистанное, запись просто помолодела.
                        if (SamePrefix(cache.Cards, cards)) { app.Cache.Put(key, cache); DevLog.Write(What + ": без изменений"); return; }
                        DevLog.Write(What + ": изменилось — заново");
                        src.Focus = grid.Focus;
                        generation++;   // догрузка страницы к старому списку, если идёт, уже не нужна
                        Reset();
                    }
                    ShowOverlay(null);
                    cache = new GridCache { Cards = cards.ToList(), Paging = p, NextFrame = next };
                    app.Cache.Put(key, cache);
                    paging = p;
                    nextFrame = next;
                    DevLog.Write(What + ": " + cards.Count + " cards" + (HasMore ? ", есть ещё" : ""));
                    Fill(cards, Empty, toGrid);
                });
            });
        }

        static bool SamePrefix(List<AnimeCard> have, IReadOnlyList<AnimeCard> first)
        {
            if (have.Count < first.Count) return false;
            for (var i = 0; i < first.Count; i++) if (have[i].Id != first[i].Id) return false;
            return true;
        }

        void Search(string q)
        {
            src.Query = q;
            Load(false, true);
        }

        // До конца осталось ≤ 2 строк — догрузить следующую страницу (в фоне, по одной за раз), чтобы ниже всегда
        // было что показать. force — без проверки строки: догрузка ради восстановления фокуса.
        void MaybeLoadMore(bool force = false)
        {
            if (loadingMore || (!force && grid.FocusRow < grid.RowCount - 2) || !HasMore) return;
            var p = paging;
            var frameUrl = nextFrame;
            loadingMore = true;
            ShowMore(true);
            var n = generation;
            var page = p != null && p.HasMore ? p.Page + 1 : 0;
            Task.Run(async () =>
            {
                IReadOnlyList<AnimeCard> cards = null;
                string next = null, why = null;
                try
                {
                    if (page > 0) cards = await app.Api.CatalogPageAsync(p, page).ConfigureAwait(false);
                    else
                    {
                        var html = await app.Api.GetHtmlAsync(frameUrl).ConfigureAwait(false);
                        cards = CatalogParser.Cards(html);
                        next = CatalogParser.NextFrameUrl(html);
                    }
                }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    loadingMore = false;   // при ошибке — повторим на следующем нажатии
                    ShowMore(false);
                    if (cards == null)
                    {
                        DevLog.Write("grid: page " + (page > 0 ? page.ToString() : "frame") + " ошибка " + why);
                        if (restoreTo >= 0) FinishRestore();
                        return;
                    }
                    var fresh = cards.Where(c => ids.Add(c.Id)).ToList();
                    if (page > 0)
                    {
                        p.Page = page;
                        p.HasMore = fresh.Count > 0 && (p.TotalItems == 0 || ids.Count < p.TotalItems);
                    }
                    else nextFrame = fresh.Count > 0 && next != frameUrl ? next : null;
                    if (cache != null) { cache.Cards.AddRange(fresh); cache.NextFrame = nextFrame; }   // Paging — тот же объект
                    DevLog.Write("grid: page " + (page > 0 ? page.ToString() : "frame") + " +" + fresh.Count);
                    grid.Append(fresh.Select(Item).ToList());
                    if (restoreTo >= 0) ContinueRestore();
                    else MaybeLoadMore();   // страница короткая — сразу следующую
                });
            });
        }

        // Вернулись на экран с фокусом дальше первой страницы: догружаем страницы, пока карточка не появится
        // (не больше MaxRestorePages), затем ставим фокус.
        void ContinueRestore()
        {
            restorePages++;
            if (restoreTo < grid.Count || !HasMore || restorePages >= MaxRestorePages) FinishRestore();
            else MaybeLoadMore(true);
        }

        void FinishRestore()
        {
            var target = Math.Min(restoreTo, grid.Count - 1);
            DevLog.Write("grid: " + src.Title + " restore focus " + target + " (ждали " + restoreTo + ") after " + restorePages + " pages");
            restoreTo = -1;
            grid.FocusAt(target);
            MaybeLoadMore();
        }

        void Reset()
        {
            grid.Clear();
            ids.Clear();
            cache = null;
            paging = null;
            nextFrame = null;
            loadingMore = false;
            restoreTo = -1;
            ShowMore(false);
        }

        // Спиннер под последней строкой, если она на экране.
        void ShowMore(bool on)
        {
            var y = grid.EndY + 16;
            if (!on || grid.Count == 0 || y > Theme.H - 44) { more.Hide(); return; }
            more.MoveTo(grid.Left + 14, y + 16);
            more.Show();
        }

        // toGrid — фокус в сетку (вход на экран, результаты поиска); при смене вкладки он остаётся на вкладках.
        void Fill(IReadOnlyList<AnimeCard> cards, string empty, bool toGrid)
        {
            var fresh = cards.Where(c => ids.Add(c.Id)).ToList();
            grid.Append(fresh.Select(Item).ToList());
            SetStatus(fresh.Count == 0 ? empty : null);
            if (src.Focus < grid.Count) grid.FocusAt(src.Focus);
            else if (HasMore) { restoreTo = src.Focus; restorePages = 0; }
            else grid.FocusAt(grid.Count - 1);   // список стал короче — на последнюю
            src.Focus = 0;   // восстановили (или восстанавливаем) — дальше не нужен
            if (restoreTo >= 0) MaybeLoadMore(true);
            else MaybeLoadMore();   // одна строка — она же последняя
            var hasBar = src.MyList || src.IsSearch || src.Browse;
            SetArea(!hasBar || (toGrid && fresh.Count > 0) ? Area.Grid : src.Browse ? Area.Side : Area.Bar);
            src.StartInSide = false;
            Toast.Raise();
        }

        static RowItem Item(AnimeCard c) => new RowItem { Title = c.Title, ImageUrl = c.PosterUrl, Badge = c.Rating, Tag = c };

        static string Why(Exception e) => e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name + " " + e.Message;

        // ---- действия ----

        void OpenTitle()
        {
            if (!(grid.Current?.Tag is AnimeCard c)) return;
            DevLog.Write("open: title " + c.Id + " " + c.Url);
            var back = new GridSource
            {
                Title = src.Title, FirstPageUrl = src.FirstPageUrl, MyList = src.MyList, Browse = src.Browse, Genre = src.Genre,
                Query = src.Query, Tab = src.Tab,
                Focus = grid.Focus,
            };
            app.Open(new TitleScreen(app, c.Id, c.Url), () => new GridScreen(app, back));
        }

        void OpenKeyboard()
        {
            keyboard = new OnScreenKeyboard(ui, app.Win, false, src.Query);
            keyboard.Changed += SetQuery;
            keyboard.Done += q =>
            {
                keyboard = null;
                q = (q ?? "").Trim();
                SetQuery(q);
                if (q.Length >= 2) Search(q);
            };
            keyboard.Cancelled += () => { keyboard = null; SetQuery(src.Query); };
        }

        // ---- шапка: вкладки / строка запроса ----

        void BuildTabs()
        {
            foreach (var o in bar) ui.Remove(o);
            bar.Clear();
            ui.Remove(barFrame);
            barFrame = ui.RoundFocus(0, 0, BarH, BarH, BarH / 2);
            var x = Theme.Margin;
            for (var i = 0; i < MyListTabs.All.Length; i++)
            {
                var name = MyListTabs.All[i].Name;
                var w = 52 + CharW * name.Length;
                var current = i == src.Tab;
                bar.Add(ui.RoundRect(x, BarY, w, BarH, BarH / 2, current ? Theme.Text : Theme.Card));
                bar.Add(ui.Text(x, BarY + 12, w, 32, Theme.T(name, PillFont, current ? Theme.BgHex : "#d5d8de", current, "center")));
                if (current) ui.MoveFocus(barFrame, x, BarY, w, BarH, BarH / 2);
                x += w + 16;
            }
            ui.ShowFocus(barFrame, area == Area.Bar);
        }

        // Список жанров: текущий — на подложке, под фокусом — белая «таблетка».
        void BuildSide()
        {
            foreach (var o in side) ui.Remove(o);
            side.Clear();
            for (var i = 0; i < Genres.All.Length; i++)
            {
                var y = BarY + i * (ItemH + ItemGap);
                var focused = area == Area.Side && i == sideFocus;
                var current = i == src.Genre;
                if (focused || current) side.Add(ui.RoundRect(Theme.Margin, y, SideW, ItemH, ItemH / 2, focused ? Theme.Text : Theme.Card));
                var color = focused ? Theme.BgHex : current ? Theme.TextHex : Theme.MutedHex;
                side.Add(ui.Text(Theme.Margin + 26, y + 7, SideW - 40, 36, Theme.T(Genres.All[i].Title, 25, color, focused || current)));
            }
        }

        void BuildQuery()
        {
            const int w = 1000;
            barFrame = ui.RoundFocus(Theme.Margin, BarY, w, QueryH, 14);
            ui.RoundRect(Theme.Margin, BarY, w, QueryH, 14, Theme.Card);
            query = ui.Text(Theme.Margin + 28, BarY + 14, w - 56, 40, "");
            query.IsEllipsis = true;
            SetQuery(src.Query);
        }

        void SetQuery(string q) =>
            query.Text = string.IsNullOrEmpty(q) ? Theme.T(RuText.SearchHint, 24, Theme.MutedHex) : Theme.T(q, 26, bold: true);

        void SetArea(Area a)
        {
            area = a;
            grid.SetActive(a == Area.Grid);
            if (src.Browse) BuildSide();
            if (barFrame != null) ui.ShowFocus(barFrame, a == Area.Bar);
        }

        void SetStatus(string text)
        {
            if (text == null) { status.Hide(); return; }
            status.Text = Theme.T(text, 28, Theme.MutedHex);
            status.Show();
        }

        void ShowOverlay(MessageView next)
        {
            overlay?.Hide();
            overlay = next;
            overlay?.Show();
        }
    }
}
