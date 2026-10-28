using System;
using System.Collections.Generic;
using System.Linq;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Панель плеера (макет Player.dc.html, раскладка как у YouTube на ТВ). Сверху — название и «N сезон · Серия N ·
    // озвучка · качество». Снизу — одна полоса-блок: прогресс с временем и сразу под ним ряд кнопок.
    // Compact: этот блок у нижнего края экрана, рядов нет. Full: блок поднимается (тёмная подложка от y 480 до низа),
    // под ним ряды сезонов с плитками серий 200×112. Фокус: полоса → кнопки → ряды (↓ из кнопок раскрывает Full).
    sealed class EpisodePanel
    {
        public enum Mode { Hidden, Compact, Full }
        public enum Zone { Bar, Buttons, Rows }
        public const int Pause = 0, Dub = 1, Quality = 2, Next = 3, Skip = 4;

        const int M = Theme.Margin, FullTop = 480, CompactTop = 840, BarW = Theme.W - 2 * M;
        const int BarDy = 32, TimeDy = 18, BtnDy = 104;   // от верха блока: полоса, времена (от полосы), кнопки
        const int BtnH = 72, BtnGap = 16, RowY = 682, RowStep = 182, TileY = 44, TileW = 200, TileH = 112, TileGap = 20;
        const int VisibleTiles = 8, VisibleRows = 2;
        static readonly Color PanelBg = Color.FromRgba(11, 12, 16, 235), GapBg = Color.FromHex("#0b0c10"), TopBg = Color.FromRgba(0, 0, 0, 140);
        static readonly Color Track = Color.FromHex("#404144"), PlayingBg = Color.FromHex("#663138"), BtnBg = Color.FromHex("#2f3034"), WatchedBg = Color.FromHex("#181a20");
        const string SubHex = "#c9cdd4", DimHex = "#9aa0ab", TileHex = "#d5d8de", GreenHex = "#4ade80", DarkHex = "#0e0f13";

        readonly Widgets ui;
        readonly List<EvasObject> band = new List<EvasObject>();   // всё, кроме рядов и колец фокуса
        readonly List<EvasObject> rowParts = new List<EvasObject>();
        readonly Rectangle bg, fill;
        readonly Polygon track, knob, knobRing, btnRing, btnGap;
        readonly Label title, sub, posText, leftText;
        readonly Polygon[] btnBack = new Polygon[5];
        readonly Label[] btnText = new Label[5];
        readonly int[] btnX = new int[5], btnW = new int[5];
        readonly string[] btnLabel = new string[5];
        IReadOnlyList<SeasonRow> rows = new SeasonRow[0];
        int curAnime, curNumber, pos, dur, rowTop;
        bool skip = true;   // «Пропустить опенинг» — только в начале серии (решает экран)

        public Mode State { get; private set; } = Mode.Hidden;
        public bool Visible => State != Mode.Hidden;
        public Zone Focus { get; private set; } = Zone.Bar;
        public int Button { get; private set; }
        public int Row { get; private set; }
        public int RowCount => rows.Count;
        public Action<SeasonRow> NeedInfo;   // ряд стал виден, а серий ещё нет

        int Top => State == Mode.Full ? FullTop : CompactTop;
        int BarY => Top + BarDy;
        int BtnY => Top + BtnDy;

        public EpisodePanel(Widgets ui)
        {
            this.ui = ui;
            bg = Add(ui.Rect(0, CompactTop, Theme.W, Theme.H - CompactTop, PanelBg, false));
            Add(ui.Rect(0, 0, Theme.W, 150, TopBg, false));
            title = Add(ui.Text(M, 40, BarW, 48, "", false));
            title.IsEllipsis = true;
            sub = Add(ui.Text(M, 94, BarW, 32, "", false));

            track = Add(ui.RoundRect(M, 0, BarW, 8, 4, Track, false));
            fill = Add(ui.Rect(M, 0, 1, 8, Theme.Accent, false));
            knobRing = ui.Circle(M, 0, 20, Theme.Accent, false);
            knob = Add(ui.Circle(M, 0, 12, Theme.Focus, false));
            posText = Add(ui.Text(M, 0, 400, 30, "", false));
            leftText = Add(ui.Text(Theme.W - M - 400, 0, 400, 30, "", false));

            btnRing = ui.RoundRect(0, 0, 10, BtnH + 16, BtnH / 2 + 8, Theme.Accent, false);
            btnGap = ui.RoundRect(0, 0, 10, BtnH + 8, BtnH / 2 + 4, GapBg, false);   // непрозрачный: иначе кольцо просвечивает
            for (var i = 0; i < 5; i++)
            {
                btnBack[i] = Add(ui.RoundRect(0, 0, 10, BtnH, BtnH / 2, BtnBg, false));
                btnText[i] = Add(ui.Text(0, 0, 10, 36, "", false));
            }
            SetButtons(false, "—", "—");
        }

        T Add<T>(T o) where T : EvasObject { band.Add(o); return o; }

        // ---- данные ----

        public void SetTitle(string name, string line)
        {
            title.Text = Theme.T(name, 34, bold: true);
            sub.Text = Theme.T(line, 21, SubHex);
        }

        public void SetButtons(bool paused, string dub, string quality)
        {
            btnLabel[Pause] = paused ? RuText.Play : RuText.PauseLabel;   // текстом: глифов ▶/❚❚ в шрифте ТВ может не быть
            btnLabel[Dub] = RuText.Dub + ": " + dub + "  ›";
            btnLabel[Quality] = RuText.Quality + ": " + quality;
            btnLabel[Next] = RuText.NextEpisode;
            btnLabel[Skip] = RuText.SkipOpening;
            var x = M;
            for (var i = 0; i < 5; i++)
            {
                btnX[i] = x;
                btnW[i] = 60 + 12 * Math.Max(btnLabel[i].Length, 6);
                x += btnW[i] + BtnGap;
            }
            Layout();
        }

        // Показать/спрятать «Пропустить опенинг»; фокус с пропавшей кнопки — на соседнюю.
        public void SetSkip(bool on)
        {
            if (on == skip) return;
            skip = on;
            if (!skip && Button == Skip) Button = Next;
            Layout();
        }

        int ButtonCount => skip ? 5 : 4;

        public void SetSeasons(IReadOnlyList<SeasonRow> list)
        {
            rows = list;
            Row = Math.Max(0, Math.Min(Row, rows.Count - 1));
            BuildRows();
        }

        public void SetCurrent(int animeId, int number)
        {
            curAnime = animeId;
            curNumber = number;
            foreach (var r in rows) if (r.Season.AnimeId == animeId) r.Focus = -1;   // фокус ряда — на новую текущую
            BuildRows();
        }

        public void RowLoaded(SeasonRow r)
        {
            if (State == Mode.Full && rows.Skip(rowTop).Take(VisibleRows).Contains(r)) BuildRows();
        }

        public void SetPosition(int p, int d)
        {
            pos = Math.Max(0, p);
            dur = Math.Max(0, d);
            if (!Visible) return;
            var y = BarY;
            var w = dur > 0 ? (int)((long)BarW * Math.Min(pos, dur) / dur) : 0;
            fill.Resize(Math.Max(1, w), 8);
            Widgets.Shape(knob, M + w - 12, y - 8, 24, 24, 12);
            Widgets.Shape(knobRing, M + w - 20, y - 16, 40, 40, 20);
            posText.Text = Theme.T(RuText.Time(pos), 20, SubHex, true);
            leftText.Text = Theme.T(dur > 0 ? RuText.Remaining(Math.Max(0, dur - pos)) : "", 20, SubHex, true, "right");
        }

        // ---- показ ----

        // Compact/Full с фокусом zone (Rows — только в Full; ставит фокус на ряд текущего тайтла).
        public void Show(Mode mode, Zone zone)
        {
            if (mode == Mode.Hidden) { Hide(); return; }
            if (zone == Zone.Rows && rows.Count == 0) zone = Zone.Buttons;
            if (zone == Zone.Rows) { mode = Mode.Full; Row = Math.Max(0, rows.ToList().FindIndex(r => r.Season.AnimeId == curAnime)); }
            State = mode;
            Focus = zone;
            foreach (var o in band) { o.Show(); o.RaiseTop(); }
            Layout();
            BuildRows();
        }

        public void Hide()
        {
            State = Mode.Hidden;
            foreach (var o in band) o.Hide();
            knobRing.Hide();
            btnRing.Hide();
            btnGap.Hide();
            ClearRows();
        }

        // Блок «полоса + кнопки» на своей высоте (Compact — внизу, Full — от y 480), подложка под ним до низа.
        void Layout()
        {
            var top = Top;
            bg.Move(0, top);
            bg.Resize(Theme.W, Theme.H - top);
            var y = BarY;
            Widgets.Shape(track, M, y, BarW, 8, 4);
            fill.Move(M, y);
            posText.Move(M, y + 8 + TimeDy);
            leftText.Move(Theme.W - M - 400, y + 8 + TimeDy);
            for (var i = 0; i < 5; i++)
            {
                Widgets.Shape(btnBack[i], btnX[i], BtnY, btnW[i], BtnH, BtnH / 2);
                btnText[i].Move(btnX[i], BtnY + 19);
                btnText[i].Resize(btnW[i], 36);
            }
            SetPosition(pos, dur);
            UpdateButtons();
        }

        // ---- фокус ----

        public void Up()
        {
            if (Focus == Zone.Rows && Row > 0) { Row--; BuildRows(); return; }
            Focus = Focus == Zone.Rows ? Zone.Buttons : Zone.Bar;
            BuildRows();
            UpdateButtons();
        }

        // ↓ из кнопок раскрывает Full и ставит фокус в ряды.
        public void Down()
        {
            if (Focus == Zone.Bar) { Focus = Zone.Buttons; UpdateButtons(); return; }
            if (Focus == Zone.Buttons) { if (rows.Count > 0) Show(Mode.Full, Zone.Rows); return; }
            if (Row < rows.Count - 1) { Row++; BuildRows(); }
        }

        // ←→ на кнопках и в рядах (полосу перематывает экран).
        public void Move(int dx)
        {
            if (Focus == Zone.Buttons) { Button = Math.Max(0, Math.Min(ButtonCount - 1, Button + dx)); UpdateButtons(); return; }
            if (Focus != Zone.Rows || rows.Count == 0) return;
            var r = rows[Row];
            var n = r.Info?.Episodes.Count ?? 0;
            if (n == 0) return;
            r.Focus = Math.Max(0, Math.Min(n - 1, r.Focus + dx));
            BuildRows();
        }

        // Серия под фокусом в рядах; null — ряд ещё пуст.
        public (SeasonRow Row, Episode Episode)? Focused()
        {
            if (Focus != Zone.Rows || Row >= rows.Count) return null;
            var r = rows[Row];
            if (r.Info == null || r.Focus < 0 || r.Focus >= r.Info.Episodes.Count) return null;
            return (r, r.Info.Episodes[r.Focus]);
        }

        void UpdateButtons()
        {
            for (var i = 0; i < 5; i++)
            {
                var on = Visible && Focus == Zone.Buttons && Button == i;
                btnBack[i].Color = on ? Theme.Focus : BtnBg;
                btnText[i].Text = Theme.T(btnLabel[i], 22, on ? DarkHex : "#ffffff", true, "center");
            }
            if (Visible && skip) { btnBack[Skip].Show(); btnText[Skip].Show(); }
            else { btnBack[Skip].Hide(); btnText[Skip].Hide(); }
            if (Visible && Focus == Zone.Buttons)
            {
                int x = btnX[Button], w = btnW[Button], y = BtnY;
                Widgets.Shape(btnRing, x - 8, y - 8, w + 16, BtnH + 16, BtnH / 2 + 8);
                Widgets.Shape(btnGap, x - 4, y - 4, w + 8, BtnH + 8, BtnH / 2 + 4);
                btnRing.Show(); btnGap.Show();
                btnRing.RaiseTop(); btnGap.RaiseTop();
                btnBack[Button].RaiseTop(); btnText[Button].RaiseTop();
            }
            else { btnRing.Hide(); btnGap.Hide(); }
            if (Visible && Focus == Zone.Bar)
            {
                knobRing.Show();
                knobRing.RaiseTop();
                knob.RaiseTop();
            }
            else knobRing.Hide();
        }

        // ---- ряды сезонов ----

        void ClearRows()
        {
            foreach (var o in rowParts) ui.Remove(o);
            rowParts.Clear();
        }

        // Под блоком кнопок видны два ряда; фокусный — всегда среди них. Плитки строятся только видимые (у длинных сериалов их сотни).
        void BuildRows()
        {
            ClearRows();
            if (State != Mode.Full || rows.Count == 0) return;   // ряды — только в полной панели
            if (Row < rowTop) rowTop = Row;
            if (Row >= rowTop + VisibleRows) rowTop = Row - VisibleRows + 1;
            for (var i = rowTop; i < Math.Min(rows.Count, rowTop + VisibleRows); i++)
                BuildRow(rows[i], RowY + (i - rowTop) * RowStep, Focus == Zone.Rows && Row == i);
            Toast.Raise();
        }

        void BuildRow(SeasonRow r, int y, bool active)
        {
            var name = rows.Count > 1 ? RuText.SeasonNo(r.Season.Order) : RuText.Episodes;
            string tail;
            if (r.Info == null)
            {
                tail = r.Failed ? RuText.NoConnection : RuText.Loading;
                if (!r.Loading && !r.Failed) NeedInfo?.Invoke(r);
            }
            else tail = r.Info.Episodes.Count > 0 && r.Info.Episodes.All(e => e.Watched)
                ? RuText.SeasonWatched : RuText.EpisodesCount(r.Info.Episodes.Count);
            rowParts.Add(ui.Text(M, y, BarW, 34, "<align=left><font_size=22 color=#ffffff font_weight=Bold>" + Theme.Escape(name) +
                "</font_size><font_size=22 color=" + DimHex + "> · " + Theme.Escape(tail) + "</font_size></align>"));
            if (r.Info == null) return;

            var eps = r.Info.Episodes;
            var current = r.Season.AnimeId == curAnime;
            if (r.Focus < 0)
            {
                var want = current ? curNumber : r.Info.ResumeEpisode(null);
                r.Focus = Math.Max(0, eps.ToList().FindIndex(e => e.Number == want));
            }
            r.Focus = Math.Min(r.Focus, Math.Max(0, eps.Count - 1));
            if (r.Focus < r.First) r.First = r.Focus;
            if (r.Focus >= r.First + VisibleTiles) r.First = r.Focus - VisibleTiles + 1;

            var ty = y + TileY;
            var frame = ui.RoundFocus(0, ty, TileW, TileH, 10, GapBg);   // раньше плиток — чтобы лежала под ними
            rowParts.Add(frame);
            for (var i = r.First; i < Math.Min(eps.Count, r.First + VisibleTiles); i++)
            {
                var x = M + (i - r.First) * (TileW + TileGap);
                var e = eps[i];
                var playing = current && e.Number == curNumber;
                // Текущая — красная рамка 3 px и красноватая подложка: не путать с белой рамкой фокуса.
                if (playing) rowParts.Add(ui.RoundRect(x - 3, ty - 3, TileW + 6, TileH + 6, 13, Theme.Accent));
                rowParts.Add(ui.RoundRect(x, ty, TileW, TileH, 10, playing ? PlayingBg : e.Watched ? WatchedBg : Theme.Card2));
                var n = e.Number.ToString();
                var label = playing ? Theme.T(n + " · " + RuText.NowWatching, 20, "#ffffff", true, "center")
                    : e.Watched ? "<align=center><font_size=22 color=" + DimHex + " font_weight=Bold>" + n + "  <color=" + GreenHex + ">✓</color></font_size></align>"
                    : Theme.T(n, 22, TileHex, true, "center");
                rowParts.Add(ui.Text(x, ty + (TileH - 32) / 2, TileW, 34, label));
                if (playing && dur > 0)
                    rowParts.Add(ui.Rect(x, ty + TileH - 5, Math.Max(1, (int)((long)TileW * Math.Min(pos, dur) / dur)), 5, Theme.Focus));   // на красной подложке полоса белая
                if (active && i == r.Focus) ui.MoveFocus(frame, x, ty, TileW, TileH);
            }
            ui.ShowFocus(frame, active);
        }
    }
}
