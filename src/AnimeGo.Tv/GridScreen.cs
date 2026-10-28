using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Что показывает сетка. FirstPageUrl null — поиск; MyList — «Мой список» с вкладками.
    // Query, Tab и Focus — состояние, с которым экран пересоздаётся по «Назад» со страницы тайтла.
    sealed class GridSource
    {
        public string Title;
        public string FirstPageUrl;
        public bool MyList;
        public string Query;
        public int Tab;
        public int Focus;

        public bool IsSearch => FirstPageUrl == null && !MyList;

        public static GridSource Search() => new GridSource { Title = RuText.Search };
        public static GridSource Catalog(string url, string title) => new GridSource { Title = title, FirstPageUrl = url };
        public static GridSource List(int tab = 0) => new GridSource { Title = RuText.MyList, MyList = true, Tab = tab, FirstPageUrl = MyListTabs.Url(tab) };
    }

    // Вкладки «Моего списка» в порядке сайта и их type в /profile/?type=N.
    static class MyListTabs
    {
        public static readonly (string Name, int Type)[] All =
            { ("Смотрю", 1), ("Запланировано", 5), ("Просмотрено", 2), ("Отложено", 3), ("Брошено", 4), ("Пересматриваю", 6) };

        public static string Url(int tab) => "/profile/?type=" + All[tab].Type;
    }

    // Каталог / жанр / поиск / «Мой список» (спека §5.3–5.4): заголовок, для списка — вкладки, для поиска — строка запроса,
    // ниже сетка постеров. Следующая страница — когда фокус дошёл до последней строки. «Назад» — на прежний экран.
    sealed class GridScreen : IScreen
    {
        const int TitleY = 40, BarY = 110, BarH = 52, QueryH = 64, PillFont = 20, CharW = 11;
        enum Area { Bar, Grid }   // Bar — вкладки или строка запроса

        readonly App app;
        readonly GridSource src;
        readonly Widgets ui;
        readonly Grid grid;
        readonly Label status;
        readonly HashSet<int> ids = new HashSet<int>();
        readonly List<EvasObject> bar = new List<EvasObject>();
        EvasObject barFrame;
        Label query;
        MessageView overlay;
        OnScreenKeyboard keyboard;
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
            ui.Text(Theme.Margin, TitleY, Theme.W - 2 * Theme.Margin, 48, Theme.T(src.Title, 30, bold: true));
            var top = src.IsSearch || src.MyList ? BarY + QueryH + 32 : BarY;
            grid = new Grid(ui, app.Images, top);
            status = ui.Text(Theme.Margin, top + 40, Theme.W - 2 * Theme.Margin, 48, "", false);
            if (src.MyList) BuildTabs();
            if (src.IsSearch) BuildQuery();
        }

        public void Show()
        {
            alive = true;
            if (!src.IsSearch) { Load(true); return; }
            if (string.IsNullOrEmpty(src.Query)) { SetArea(Area.Bar); OpenKeyboard(); }
            else Search(src.Query);   // вернулись со страницы тайтла — тот же запрос
        }

        public void Hide()
        {
            alive = false;
            generation++;
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
            restoreTo = -1;   // пользователь сам двигает фокус — не перебивать
            switch (key)
            {
                case Keys.Left: grid.Move(-1, 0); return true;
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

        // ---- данные ----

        // Первая страница. full — на весь экран «Загрузка…» (вход на экран), иначе строка статуса (смена вкладки).
        void Load(bool full)
        {
            var n = ++generation;
            var url = src.FirstPageUrl;
            DevLog.Write("grid: load " + url);
            Reset();
            if (full) ShowOverlay(new MessageView(app, RuText.Loading, null, app.Back));
            else SetStatus(RuText.Loading);
            Task.Run(async () =>
            {
                IReadOnlyList<AnimeCard> cards = null;
                Paging p = null;
                string next = null, why = null;
                try
                {
                    var html = await app.Api.GetHtmlAsync(url).ConfigureAwait(false);
                    cards = CatalogParser.Cards(html);
                    p = CatalogParser.Paging(html);
                    next = CatalogParser.NextFrameUrl(html);
                }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    if (cards == null)
                    {
                        DevLog.Write("grid: " + src.Title + " ошибка " + why);
                        if (full) ShowOverlay(new MessageView(app, RuText.NoConnection, () => Load(true), app.Back));
                        else SetStatus(RuText.NoConnection + " · " + RuText.RetryHint);
                        return;
                    }
                    ShowOverlay(null);
                    paging = p;
                    nextFrame = src.MyList ? next : null;
                    DevLog.Write("grid: " + src.Title + (src.MyList ? " " + MyListTabs.All[src.Tab].Name : "") + " " + cards.Count + " cards" +
                        (paging?.HasMore == true || nextFrame != null ? ", есть ещё" : ""));
                    Fill(cards, src.MyList ? RuText.EmptyList : RuText.NothingFound, full);
                });
            });
        }

        void Search(string q)
        {
            var n = ++generation;
            src.Query = q;
            Reset();
            SetStatus(RuText.Loading);
            Task.Run(async () =>
            {
                IReadOnlyList<AnimeCard> cards = null;
                string why = null;
                try { cards = await app.Api.SearchAsync(q).ConfigureAwait(false); }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive || n != generation) return;
                    if (cards == null)
                    {
                        DevLog.Write("search: \"" + q + "\" ошибка " + why);
                        SetStatus(RuText.NoConnection);
                        return;
                    }
                    DevLog.Write("search: \"" + q + "\" " + cards.Count);
                    Fill(cards, RuText.NothingFound, true);
                });
            });
        }

        // Фокус дошёл до последней строки — догрузить следующую страницу (в фоне, по одной за раз).
        // force — без проверки строки: догрузка ради восстановления фокуса.
        void MaybeLoadMore(bool force = false)
        {
            if (loadingMore || (!force && grid.FocusRow < grid.RowCount - 1) || !HasMore) return;
            var p = paging;
            var frameUrl = nextFrame;
            loadingMore = true;
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
                    DevLog.Write("grid: page " + (page > 0 ? page.ToString() : "frame") + " +" + fresh.Count);
                    grid.Append(fresh.Select(Item).ToList());
                    if (restoreTo >= 0) ContinueRestore();
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
            paging = null;
            nextFrame = null;
            loadingMore = false;
            restoreTo = -1;
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
            var hasBar = src.MyList || src.IsSearch;
            SetArea(!hasBar || (toGrid && fresh.Count > 0) ? Area.Grid : Area.Bar);
            Toast.Raise();
        }

        static RowItem Item(AnimeCard c) => new RowItem { Title = c.Title, ImageUrl = c.PosterUrl, Tag = c };

        static string Why(Exception e) => e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name + " " + e.Message;

        // ---- действия ----

        void OpenTitle()
        {
            if (!(grid.Current?.Tag is AnimeCard c)) return;
            DevLog.Write("open: title " + c.Id + " " + c.Url);
            var back = new GridSource
            {
                Title = src.Title, FirstPageUrl = src.FirstPageUrl, MyList = src.MyList, Query = src.Query, Tab = src.Tab,
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
