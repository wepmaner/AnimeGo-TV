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
        static readonly Color Shade = Color.FromRgba(0, 0, 0, 170), Dim = Color.FromRgba(11, 12, 16, 150);   // плашка номера на кадре; затемнение просмотренных
        const string SubHex = "#c9cdd4", DimHex = "#9aa0ab", TileHex = "#d5d8de", GreenHex = "#4ade80", DarkHex = "#0e0f13";

        readonly Widgets ui;
        readonly List<EvasObject> band = new List<EvasObject>();   // всё, кроме рядов и колец фокуса
        readonly List<EvasObject> rowParts = new List<EvasObject>();
        readonly Dictionary<(int Anime, int Number), (Image Pic, Episode Ep)> tilePics = new Dictionary<(int, int), (Image, Episode)>();   // плитки на экране
        readonly Dictionary<SeasonRow, (EvasObject Frame, int Ty)> rowFrames = new Dictionary<SeasonRow, (EvasObject, int)>();   // рамка фокуса каждого построенного ряда
        readonly Rectangle bg, fill;
        readonly Polygon track, knob, knobRing, btnRing, btnGap;
        readonly Label title, sub, posText, leftText;
        readonly Polygon[] btnBack = new Polygon[5];
        readonly Label[] btnText = new Label[5];
        readonly Polygon playIcon, pauseL, pauseR;   // значки кнопки паузы: глифов ▶/❚❚ в шрифте ТВ может не быть
        readonly int[] btnX = new int[5], btnW = new int[5];
        readonly string[] btnLabel = new string[5];
        IReadOnlyList<SeasonRow> rows = new SeasonRow[0];
        int curAnime, curNumber, pos, dur, rowTop;
        bool paused;
        bool skip = true;   // «Пропустить опенинг» — только в начале серии (решает экран)

        public Mode State { get; private set; } = Mode.Hidden;
        public bool Visible => State != Mode.Hidden;
        public Zone Focus { get; private set; } = Zone.Bar;
        public int Button { get; private set; }
        public int Row { get; private set; }
        public int RowCount => rows.Count;
        public Action<SeasonRow> NeedInfo;   // ряд стал виден, а серий ещё нет
        public Func<SeasonRow, int, (string When, string In)?> Upcoming;   // серия ещё не вышла — когда выйдет

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
            playIcon = Add(ui.RoundRect(0, 0, 10, 10, 0, Theme.Focus, false));   // форму задаёт Layout (треугольник)
            pauseL = Add(ui.RoundRect(0, 0, 9, 30, 3, Theme.Focus, false));
            pauseR = Add(ui.RoundRect(0, 0, 9, 30, 3, Theme.Focus, false));
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
            this.paused = paused;
            btnLabel[Pause] = "";   // круглая кнопка со значком ▶ или ❚❚ (полигоны, см. Layout)
            btnLabel[Dub] = RuText.Dub + ": " + dub + "  ›";
            btnLabel[Quality] = RuText.Quality + ": " + quality;
            btnLabel[Next] = RuText.NextEpisode;
            btnLabel[Skip] = RuText.SkipOpening;
            var x = M;
            for (var i = 0; i < 5; i++)
            {
                btnX[i] = x;
                btnW[i] = i == Pause ? BtnH : 60 + 12 * Math.Max(btnLabel[i].Length, 6);
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
            // Значки в центре круглой кнопки: треугольник чуть правее центра (оптически), две полоски 9×30.
            int cx = btnX[Pause] + BtnH / 2, cy = BtnY + BtnH / 2;
            playIcon.ClearPoints();
            playIcon.AddPoint(cx - 11, cy - 16);
            playIcon.AddPoint(cx + 17, cy);
            playIcon.AddPoint(cx - 11, cy + 16);
            Widgets.Shape(pauseL, cx - 13, cy - 15, 9, 30, 3);
            Widgets.Shape(pauseR, cx + 4, cy - 15, 9, 30, 3);
            SetPosition(pos, dur);
            UpdateButtons();
        }

        // ---- фокус ----

        // ↑: ряды → кнопки → полоса. false — выше некуда (фокус уже на полосе): экран закрывает панель, остаётся чистое видео.
        public bool Up()
        {
            if (Focus == Zone.Bar) return false;
            if (Focus == Zone.Rows && Row > 0) { Row--; if (!RefreshFocus()) BuildRows(); return true; }
            Focus = Focus == Zone.Rows ? Zone.Buttons : Zone.Bar;
            RefreshFocus();
            UpdateButtons();
            return true;
        }

        // ↓ из кнопок раскрывает Full и ставит фокус в ряды.
        public void Down()
        {
            if (Focus == Zone.Bar) { Focus = Zone.Buttons; UpdateButtons(); return; }
            if (Focus == Zone.Buttons) { if (rows.Count > 0) Show(Mode.Full, Zone.Rows); return; }
            if (Row < rows.Count - 1) { Row++; if (!RefreshFocus()) BuildRows(); }
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
            // Плитки не перестраиваем, пока окно видимых не сдвинулось: иначе кадры перечитываются и моргают.
            var first = r.First;
            if (r.Focus < first) first = r.Focus;
            if (r.Focus >= first + VisibleTiles) first = r.Focus - VisibleTiles + 1;
            if (first == r.First && rowFrames.TryGetValue(r, out var f))
            {
                ui.MoveFocus(f.Frame, M + (r.Focus - r.First) * (TileW + TileGap), f.Ty, TileW, TileH);
                return;
            }
            BuildRows();
        }

        // Рамки фокуса рядов без перестройки плиток: видна только у ряда в фокусе. false — этого ряда на экране нет.
        bool RefreshFocus()
        {
            if (State != Mode.Full || rows.Count == 0) return true;
            foreach (var kv in rowFrames) ui.ShowFocus(kv.Value.Frame, Focus == Zone.Rows && rows[Row] == kv.Key);
            return Focus != Zone.Rows || rowFrames.ContainsKey(rows[Row]);
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
            // Значок паузы — цветом текста кнопки; на паузе показываем ▶ («играть»), иначе ❚❚.
            var pauseOn = Visible && Focus == Zone.Buttons && Button == Pause;
            playIcon.Color = pauseL.Color = pauseR.Color = pauseOn ? Color.FromHex(DarkHex) : Theme.Focus;
            if (Visible && paused) { playIcon.Show(); pauseL.Hide(); pauseR.Hide(); }
            else if (Visible) { playIcon.Hide(); pauseL.Show(); pauseR.Show(); }
            else { playIcon.Hide(); pauseL.Hide(); pauseR.Hide(); }
            if (Visible && Focus == Zone.Buttons)
            {
                int x = btnX[Button], w = btnW[Button], y = BtnY;
                Widgets.Shape(btnRing, x - 8, y - 8, w + 16, BtnH + 16, BtnH / 2 + 8);
                Widgets.Shape(btnGap, x - 4, y - 4, w + 8, BtnH + 8, BtnH / 2 + 4);
                btnRing.Show(); btnGap.Show();
                btnRing.RaiseTop(); btnGap.RaiseTop();
                btnBack[Button].RaiseTop(); btnText[Button].RaiseTop();
                if (Button == Pause) { playIcon.RaiseTop(); pauseL.RaiseTop(); pauseR.RaiseTop(); }
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
            tilePics.Clear();
            rowFrames.Clear();
        }

        // Кадр серии number ряда r узнали (0 — карта кадров пришла): в плитку, если она на экране; «не вышла» — перерисовать.
        public void ThumbChanged(SeasonRow r, int number)
        {
            if (State != Mode.Full) return;
            if (r.Thumbs.IsUnreleased(number)) { BuildRows(); return; }
            if (number == 0)   // карта пришла: кадры в плитки этого ряда, остальное не трогаем (иначе кадры соседнего ряда моргнут)
            {
                foreach (var kv in tilePics) if (kv.Key.Anime == r.Season.AnimeId) r.Thumbs.Set(kv.Value.Pic, kv.Value.Ep);
                return;
            }
            if (tilePics.TryGetValue((r.Season.AnimeId, number), out var t)) r.Thumbs.Set(t.Pic, t.Ep);
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
            rowFrames[r] = (frame, ty);
            for (var i = r.First; i < Math.Min(eps.Count, r.First + VisibleTiles); i++)
            {
                var x = M + (i - r.First) * (TileW + TileGap);
                var e = eps[i];
                var playing = current && e.Number == curNumber;
                var upcoming = playing ? null : Upcoming?.Invoke(r, e.Number) ?? (r.Thumbs != null && r.Thumbs.IsUnreleased(e.Number) ? ((string)null, (string)null) : ((string, string)?)null);
                // Текущая — красная рамка 3 px и красноватая подложка: не путать с белой рамкой фокуса.
                if (playing) rowParts.Add(ui.RoundRect(x - 3, ty - 3, TileW + 6, TileH + 6, 13, Theme.Accent));
                rowParts.Add(ui.RoundRect(x, ty, TileW, TileH, 10, playing ? PlayingBg : e.Watched || upcoming != null ? WatchedBg : Theme.Card2));
                var n = e.Number.ToString();
                if (upcoming != null)
                {
                    // Не вышла: номер и дата выхода (без «выйдет» — плитка узкая).
                    rowParts.Add(ui.Text(x, ty + 20, TileW, 34, Theme.T(n, 22, DimHex, true, "center")));
                    var when = ui.Text(x + 8, ty + 60, TileW - 16, 30,
                        Theme.T(string.IsNullOrEmpty(upcoming.Value.When) ? RuText.NotReleased : upcoming.Value.When, 17, DimHex, align: "center"));
                    when.IsEllipsis = true;
                    rowParts.Add(when);
                    if (i == r.Focus) ui.MoveFocus(frame, x, ty, TileW, TileH);
                    continue;
                }
                // Кадр серии (EpisodeThumbs ставит из кеша или когда узнает адрес), просмотренные затемнены,
                // номер — в плашке слева снизу, как на экране серий.
                var pic = ui.Picture(x, ty, TileW, TileH);
                rowParts.Add(pic);
                tilePics[(r.Season.AnimeId, e.Number)] = (pic, e);
                r.Thumbs?.Set(pic, e);
                if (e.Watched && !playing) rowParts.Add(ui.Rect(x, ty, TileW, TileH, Dim));
                var text = playing ? n + " · " + RuText.NowWatching : n;
                var tw = 16 + 11 * text.Length + (e.Watched && !playing ? 24 : 0);
                var label = playing ? Theme.T(text, 18, "#ffffff", true, "center")
                    : e.Watched ? "<align=center><font_size=18 color=" + TileHex + " font_weight=Bold>" + n + " <color=" + GreenHex + ">✓</color></font_size></align>"
                    : Theme.T(n, 18, TileHex, true, "center");
                rowParts.Add(ui.RoundRect(x + 8, ty + TileH - 8 - 30, tw, 30, 8, Shade));
                rowParts.Add(ui.Text(x + 8, ty + TileH - 8 - 30 + 3, tw, 26, label));
                if (playing && dur > 0)
                    rowParts.Add(ui.Rect(x, ty + TileH - 5, Math.Max(1, (int)((long)TileW * Math.Min(pos, dur) / dur)), 5, Theme.Focus));   // на красной подложке полоса белая
                if (i == r.Focus) ui.MoveFocus(frame, x, ty, TileW, TileH);
            }
            ui.ShowFocus(frame, active);
        }
    }
}
