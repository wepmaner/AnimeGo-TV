using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Страница тайтла (макет title.html). Сверху на фоне затемнённого кадра: постер 300×420, тип · год · серии,
    // название, оригинал, рейтинг и число оценок, статус · возраст · студия, жанры, описание в 3 строки (OK — читалка),
    // кнопки «Смотреть · серия N», «Серии», «Отзывы · N». Ниже, прокруткой: «Кадры» (OK — на весь экран), «О тайтле»
    // с главными героями, «Связанное», «Рецензии и отзывы» (OK — читалка).
    // url нужен целиком: по одному id сайт страницу не отдаёт (/anime/{id} — 404).
    sealed class TitleScreen : IScreen
    {
        const int PosterW = 300, PosterH = 420, HeroY = 96, TextX = Theme.Margin + PosterW + 56, TextW = 1180;
        const int BackdropH = 820, DescY = HeroY + 316, DescLines = 3, DescLineH = 36, DescChars = 92;
        const int BtnY = DescY + DescLines * DescLineH + 34, BtnH = 68, BtnGap = 20;
        const int SectionGap = 56, HeadH = Row.HeadH, ViewTop = 60, ViewBottom = Theme.H - 40;
        const int ReviewW = 864, ReviewH = 300, ReviewGap = 32, ReviewChars = 70, ReviewLines = 4;
        const int FieldW = 340, FieldPitchX = 370, FieldPitchY = 78, MaxCast = 6;
        const string SoftHex = "#d5d8de", DimHex = "#8a909b";
        static readonly Color ChipColor = Color.FromRgba(42, 45, 54, 230);
        // Что показать в «О тайтле» и в каком порядке (остальное — в шапке или не нужно на ТВ).
        static readonly string[] AboutFields =
            { "Студия", "Сезон", "Статус", "Выпуск", "Первоисточник", "Режиссёр", "Автор оригинала", "Длительность", "Озвучка", "Возраст" };
        enum Zone { Desc, Buttons, Shots, About, Schedule, Related, Reviews }   // About, Schedule — без рамки, только прокрутка
        enum Btn { Watch, Episodes, Reviews }

        struct Part { public EvasObject O; public int X, Y; }

        readonly App app;
        readonly int id;
        readonly string url;
        readonly Widgets ui;
        readonly ContinueCard siteResume;   // карточка «Продолжить» этого тайтла с главной: где остановились по сайту
        readonly List<Part> parts = new List<Part>();
        readonly List<Part> btnParts = new List<Part>();
        readonly List<(Part Part, int Card)> reviewParts = new List<(Part, int)>();
        readonly List<Zone> zones = new List<Zone>();
        readonly List<(Btn Kind, int X, int W)> buttons = new List<(Btn, int, int)>();
        MessageView overlay;
        ReaderView reader;
        GalleryView gallery;
        AnimeInfo info;
        EvasObject descFrame, btnFrame, reviewFrame;
        Row shots, related;
        int zone, button, reviewFocus, reviewOffset, reviewsY, aboutY, aboutBottom, scheduleY, scheduleBottom, scrollY, resumeEp;
        bool alive, busy, longDesc;

        public TitleScreen(App app, int animeId, string url, ContinueCard siteResume = null)
        {
            this.siteResume = siteResume;
            this.app = app;
            id = animeId;
            this.url = url;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg);
        }

        public void Show()
        {
            alive = true;
            Load();
        }

        public void Hide()
        {
            alive = false;
            reader?.Hide();
            gallery?.Hide();
            overlay?.Hide();
            overlay = null;
            ui.Dispose();
        }

        public bool OnKey(string key)
        {
            if (reader != null) return reader.OnKey(key);
            if (gallery != null) return gallery.OnKey(key);
            if (overlay != null) return overlay.OnKey(key);
            if (info == null) return false;
            var z = zones[zone];
            switch (key)
            {
                case Keys.Back: app.Back(); return true;
                case Keys.Up: GoZone(zone - 1); return true;
                case Keys.Down: GoZone(zone + 1); return true;
                case Keys.Left: case Keys.Right:
                    var dx = key == Keys.Left ? -1 : 1;
                    if (z == Zone.Buttons) { button = Math.Max(0, Math.Min(button + dx, buttons.Count - 1)); UpdateFocus(); }
                    else if (z == Zone.Shots) shots.Move(dx);
                    else if (z == Zone.Related) related.Move(dx);
                    else if (z == Zone.Reviews) MoveReview(dx);
                    return true;
                case Keys.Ok: Ok(z); return true;
            }
            return false;
        }

        // ---- данные ----

        void Load()
        {
            ShowOverlay(new MessageView(app, RuText.Loading, null, app.Back));
            Task.Run(async () =>
            {
                AnimeInfo a = null;
                string why = null;
                try { a = AnimeParser.Parse(await app.Api.GetHtmlAsync(url).ConfigureAwait(false), id); }
                catch (Exception e) { why = Why(e); }
                App.Ui(() =>
                {
                    if (!alive) return;
                    if (a == null)
                    {
                        DevLog.Write("title: " + id + " ошибка " + why);
                        ShowOverlay(new MessageView(app, RuText.NoConnection, Load, app.Back));
                        return;
                    }
                    DevLog.Write("title: " + id + " ok, related " + a.Related.Count + ", кадров " + a.Screenshots.Count + ", рецензий " + a.Reviews.Count);
                    ShowOverlay(null);
                    info = a;
                    Build();
                    ResolveInBackground();
                });
            });
        }

        // Номер серии для «Смотреть · серия N» — заранее, чтобы кнопка сразу его показывала.
        void ResolveInBackground() => Task.Run(async () =>
        {
            var ep = await ResolveAsync().ConfigureAwait(false);
            App.Ui(() =>
            {
                if (!alive || ep == 0) return;
                resumeEp = ep;
                BuildButtons();
                Place();
            });
        });

        // Серия, с которой продолжать: по отметкам «просмотрено» из /player и ответу continuation сайта
        // (PlayerInfo.ResumeEpisode). 0 — нет связи.
        async Task<int> ResolveAsync()
        {
            try
            {
                var player = await app.Api.PlayerAsync(id).ConfigureAwait(false);
                int? site = null;
                if (!string.IsNullOrEmpty(player.ContinuationUrl))
                    site = PlayerParser.ParseContinuation(await app.Api.GetAjaxAsync(player.ContinuationUrl).ConfigureAwait(false));
                var ep = player.ResumeEpisode(site);
                DevLog.Write("title: continuation " + (site?.ToString() ?? "нет") + ", просмотрено " + player.Episodes.Count(e => e.Watched) +
                    " из " + player.Episodes.Count + " → серия " + ep);
                return ep;
            }
            catch (Exception e)
            {
                DevLog.Write("title: player " + id + " ошибка " + Why(e));
                return 0;
            }
        }

        // ---- раскладка ----

        T Add<T>(T o, int x, int y) where T : EvasObject
        {
            parts.Add(new Part { O = o, X = x, Y = y });
            return o;
        }

        Label AddText(int x, int y, int w, int h, string markup)
        {
            var l = Add(ui.Text(0, 0, w, h, markup, false), x, y);
            l.IsEllipsis = true;
            return l;
        }

        void Build()
        {
            BuildHero();
            var y = BtnY + BtnH + 60;
            if (info.Screenshots.Count > 0)
            {
                shots = new Row(ui, app.Images, y, 400, 225, 28, RuText.Screens) { OnOk = it => OpenGallery((int)it.Tag) };
                shots.SetItems(info.Screenshots.Select((s, i) => new RowItem { ImageUrl = s, Tag = i }).ToList());
                y += shots.Height + SectionGap - 60;   // под кадрами пустое место подписей
            }
            y = BuildAbout(y);
            y = BuildSchedule(y);
            var rel = info.Related.Where(r => r.Id != id).Select(r => new RowItem
            {
                Title = r.Title,
                Subtitle = string.Join(" · ", new[] { r.Kind, r.Year }.Where(s => !string.IsNullOrEmpty(s))),
                ImageUrl = r.PosterUrl,
                Tag = r,
            }).ToList();
            if (rel.Count > 0)
            {
                related = new Row(ui, app.Images, y, 200, 280, 32, RuText.Related) { OnOk = OpenRelated };
                related.SetItems(rel);
                y += related.Height + SectionGap;
            }
            if (info.Reviews.Count > 0) BuildReviews(y);

            if (longDesc) zones.Add(Zone.Desc);
            zones.Add(Zone.Buttons);
            if (shots != null) zones.Add(Zone.Shots);
            if (aboutBottom > aboutY) zones.Add(Zone.About);
            if (scheduleBottom > scheduleY) zones.Add(Zone.Schedule);
            if (related != null) zones.Add(Zone.Related);
            if (info.Reviews.Count > 0) zones.Add(Zone.Reviews);
            zone = zones.IndexOf(Zone.Buttons);
            Place();
            Toast.Raise();
        }

        void BuildHero()
        {
            // Фон: первый кадр (или постер), затемнённый слева направо полосами и к низу — в цвет фона.
            Add(ui.Picture(0, 0, Theme.W, BackdropH, false), 0, 0);
            app.Images.Set((Image)parts[parts.Count - 1].O, info.Screenshots.FirstOrDefault() ?? info.PosterUrl);
            const int strips = 48, sw = Theme.W / strips;
            for (var i = 0; i < strips; i++)
                Add(ui.Rect(0, 0, sw, BackdropH, Color.FromRgba(14, 15, 19, 248 - 100 * i / (strips - 1)), false), i * sw, 0);
            const int fades = 12, fh = 24;
            for (var i = 0; i < fades; i++)
                Add(ui.Rect(0, 0, Theme.W, fh, Color.FromRgba(14, 15, 19, 30 + 225 * i / (fades - 1)), false), 0, BackdropH - (fades - i) * fh);

            Add(ui.RoundRect(0, 0, PosterW, PosterH, 16, Theme.Card, false), Theme.Margin, HeroY);
            app.Images.Set(Add(ui.Picture(0, 0, PosterW, PosterH, false), Theme.Margin, HeroY), info.PosterUrl);

            var eps = Leading(info.Field("Эпизоды"));
            var eyebrow = new[] { info.Kind, info.Year, eps > 0 ? RuText.EpisodesCount(eps) : null }.Where(s => !string.IsNullOrEmpty(s));
            AddText(TextX, HeroY, TextW, 32, Theme.T(string.Join(" · ", eyebrow), 22, Theme.MutedHex, true));
            AddText(TextX, HeroY + 34, TextW, 76, Theme.T(info.Title, 56, bold: true));
            if (!string.IsNullOrEmpty(info.TitleOriginal)) AddText(TextX, HeroY + 112, TextW, 34, Theme.T(info.TitleOriginal, 24, DimHex));

            var y = HeroY + 170;
            var x = TextX;
            if (info.Rating != null)
            {
                var w = 70 + 17 * info.Rating.Length;
                Add(ui.RoundRect(0, 0, w, 48, 12, Theme.Accent, false), x, y);
                Add(ui.Text(0, 0, w, 44, "<align=center><font_size=30 color=#ffffff font_weight=Bold>" + Theme.Escape(info.Rating) +
                    "</font_size><font_size=18 color=#ffffff> / 10</font_size></align>", false), x, y + 3);
                x += w + 20;
            }
            var meta = new[] { RuText.Votes(info.Votes), info.Field("Статус"), info.Field("Возраст"), info.Field("Студия") }
                .Where(s => !string.IsNullOrEmpty(s));
            AddText(x, y + 8, TextX + TextW - x, 36, Theme.T(string.Join("  ·  ", meta), 22, SoftHex));

            x = TextX;
            foreach (var g in info.Genres.Take(5))
            {
                var w = 40 + 11 * g.Length;
                Add(ui.RoundRect(0, 0, w, 42, 21, ChipColor, false), x, HeroY + 240);
                Add(ui.Text(0, 0, w, 32, Theme.T(g, 19, SoftHex, true, "center"), false), x, HeroY + 247);
                x += w + 10;
            }

            var desc = (info.Description ?? "").Trim();
            var cut = Cut(desc, DescLines);
            longDesc = cut != desc;
            descFrame = Add(ui.RoundFocus(0, 0, TextW + 24, DescLines * DescLineH + 16, 14), TextX - 12, DescY - 8);
            var d = Add(ui.Text(0, 0, TextW, DescLines * DescLineH, Theme.T(cut, 24, SoftHex), false), TextX, DescY);
            d.LineWrapType = WrapType.Word;

            btnFrame = ui.RoundFocus(0, 0, 1, BtnH, BtnH / 2);
            BuildButtons();
        }

        void BuildButtons()
        {
            foreach (var p in btnParts) ui.Remove(p.O);
            btnParts.Clear();
            buttons.Clear();
            var list = new List<(Btn, string)>
            {
                (Btn.Watch, resumeEp > 0 ? WatchLabel(resumeEp) : RuText.Watch),
                (Btn.Episodes, RuText.Episodes),
            };
            if (info.Reviews.Count > 0) list.Add((Btn.Reviews, RuText.ReviewsButton(info.Reviews.Count)));
            var x = TextX;
            foreach (var (kind, label) in list)
            {
                var w = Math.Max(200, 72 + 13 * label.Length);
                btnParts.Add(new Part { O = ui.RoundRect(0, 0, w, BtnH, BtnH / 2, kind == Btn.Watch ? Theme.Accent : Theme.Card2, false), X = x, Y = BtnY });
                btnParts.Add(new Part { O = ui.Text(0, 0, w, 36, Theme.T(label, 24, bold: true, align: "center"), false), X = x, Y = BtnY + 16 });
                buttons.Add((kind, x, w));
                x += w + BtnGap;
            }
            button = Math.Min(button, buttons.Count - 1);
        }

        // «О тайтле»: поля сайта в 3 колонки, справа «Главные герои» с сэйю. Возвращает y под блоком.
        int BuildAbout(int y)
        {
            var fields = AboutFields.Select(l => (Label: l, Value: Short(l, info.Field(l)))).Where(f => f.Value != null).ToList();
            if (info.Next != null)
                fields.Insert(0, (RuText.NextEpisode, info.Next.Number + " серия · " + info.Next.When + (info.Next.In != null ? " (" + info.Next.In + ")" : "")));
            fields = fields.Take(9).ToList();
            if (fields.Count == 0 && info.Characters.Count == 0) return y;
            aboutY = y;
            AddText(Theme.Margin, y, 800, 48, Theme.T(RuText.About, 30, bold: true));
            var top = y + HeadH;
            for (var i = 0; i < fields.Count; i++)
            {
                var fx = Theme.Margin + i % 3 * FieldPitchX;
                var fy = top + i / 3 * FieldPitchY;
                AddText(fx, fy, FieldW, 28, Theme.T(fields[i].Label, 18, DimHex, true));
                AddText(fx, fy + 28, FieldW, 36, Theme.T(fields[i].Value, 23, SoftHex, true));
            }
            var h = (fields.Count + 2) / 3 * FieldPitchY;
            if (info.Characters.Count > 0)
            {
                var cx = Theme.Margin + 3 * FieldPitchX + 30;
                var cw = Theme.W - Theme.Margin - cx;
                AddText(cx, top, cw, 28, Theme.T(RuText.MainCharacters, 18, DimHex, true));
                var cast = info.Characters.Take(MaxCast).ToList();
                for (var i = 0; i < cast.Count; i++)
                {
                    var c = cast[i];
                    var markup = "<align=left><font_size=23 color=" + SoftHex + " font_weight=Bold>" + Theme.Escape(c.Name) + "</font_size>" +
                        (c.Actor == null ? "" : "<font_size=20 color=" + DimHex + ">   " + Theme.Escape(c.Actor) + "</font_size>") + "</align>";
                    AddText(cx, top + 34 + i * 40, cw, 36, markup);
                }
                h = Math.Max(h, 34 + cast.Count * 40);
            }
            aboutBottom = top + h;
            return top + h + SectionGap;
        }

        // «График выхода» с сайта: «4 серия · 30 октября 2026 · через 19 часов» / «… · вышла». Не больше 8 строк.
        int BuildSchedule(int y)
        {
            var rows = info.Schedule.Take(8).ToList();
            if (rows.Count == 0 || !rows.Any(r => Release.IsUpcoming(r.Status))) return y;   // всё вышло — график не нужен
            scheduleY = y;
            AddText(Theme.Margin, y, 800, 48, Theme.T(RuText.Schedule, 30, bold: true));
            var top = y + HeadH;
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var ry = top + i * 46;
                var upcoming = Release.IsUpcoming(r.Status);
                AddText(Theme.Margin, ry, 220, 36, Theme.T(r.Number + " серия", 23, upcoming ? Theme.TextHex : SoftHex, true));
                AddText(Theme.Margin + 240, ry, 360, 36, Theme.T(r.Date ?? "", 23, SoftHex));
                AddText(Theme.Margin + 620, ry, 400, 36, Theme.T(upcoming ? r.Status : RuText.Aired, 23, upcoming ? Theme.AccentHex : DimHex, upcoming));
            }
            scheduleBottom = top + rows.Count * 46;
            return scheduleBottom + SectionGap;
        }

        // Длинные перечисления (озвучки) — первые три и «ещё N».
        static string Short(string label, string value)
        {
            if (string.IsNullOrEmpty(value) || label != "Озвучка") return value;
            var all = value.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            return all.Length <= 3 ? value : string.Join(", ", all.Take(3)) + " и ещё " + (all.Length - 3);
        }

        void BuildReviews(int y)
        {
            AddText(Theme.Margin, y, 1200, 48, "<align=left><font_size=30 color=" + Theme.TextHex + " font_weight=Bold>" + RuText.Reviews +
                "</font_size><font_size=24 color=" + DimHex + ">   " + info.Reviews.Count + "</font_size></align>");
            reviewsY = y + HeadH;
            reviewFrame = ui.RoundFocus(0, 0, ReviewW, ReviewH, 18);
            for (var i = 0; i < info.Reviews.Count; i++)
            {
                var r = info.Reviews[i];
                void Card(EvasObject o, int dx, int dy) => reviewParts.Add((new Part { O = o, X = dx, Y = dy }, i));
                Label Line(int dx, int dy, int w, int h, string markup)
                {
                    var l = ui.Text(0, 0, w, h, markup, false);
                    l.IsEllipsis = true;
                    Card(l, dx, dy);
                    return l;
                }
                Card(ui.RoundRect(0, 0, ReviewW, ReviewH, 18, Theme.Card, false), 0, 0);
                Card(ui.Circle(26, 26, 26, Theme.Card2, false), 34, 28);
                var initial = string.IsNullOrEmpty(r.Author) ? "?" : r.Author.Substring(0, 1).ToUpperInvariant();
                Line(34, 37, 52, 34, Theme.T(initial, 22, bold: true, align: "center"));
                Line(102, 26, 380, 32, Theme.T(r.Author ?? "", 21, bold: true));
                Line(102, 56, 380, 28, Theme.T(r.Date ?? "", 17, DimHex));
                if (r.Useful != null) Line(ReviewW - 34 - 360, 32, 360, 28, Theme.T(r.Useful, 17, Theme.MutedHex, align: "right"));
                var tw = ReviewW - 68;
                if (r.Spoilers)
                {
                    const int sw = 120;
                    Card(ui.RoundRect(0, 0, sw, 30, 8, Theme.Accent, false), ReviewW - 34 - sw, 102);
                    Line(ReviewW - 34 - sw, 105, sw, 26, Theme.T(RuText.Spoilers, 16, bold: true, align: "center"));
                    tw -= sw + 16;
                }
                Line(34, 98, tw, 40, Theme.T(r.Title, 26, bold: true));
                var body = ui.Text(0, 0, ReviewW - 68, ReviewLines * 32, Theme.T(Cut(r.Text.Replace("\n\n", " ").Replace('\n', ' '), ReviewLines, ReviewChars), 21, Theme.MutedHex), false);
                body.LineWrapType = WrapType.Word;
                Card(body, 34, 148);
            }
        }

        // Всё на месте с учётом прокрутки; фокус-рамки — по зоне.
        void Place()
        {
            foreach (var p in parts) { p.O.Move(p.X, p.Y - scrollY); p.O.Show(); }
            foreach (var p in btnParts) { p.O.Move(p.X, p.Y - scrollY); p.O.Show(); }
            foreach (var (p, card) in reviewParts)
            {
                p.O.Move(ReviewX(card) + p.X, reviewsY + p.Y - scrollY);
                p.O.Show();
            }
            shots?.Place(scrollY);
            related?.Place(scrollY);
            UpdateFocus();
        }

        int ReviewX(int card) => Theme.Margin + card * (ReviewW + ReviewGap) - reviewOffset;

        void UpdateFocus()
        {
            var z = zones[zone];
            ui.MoveFocus(descFrame, TextX - 12, DescY - 8 - scrollY, TextW + 24, DescLines * DescLineH + 16, 14);
            ui.ShowFocus(descFrame, z == Zone.Desc);
            if (z == Zone.Buttons && buttons.Count > 0)
                ui.MoveFocus(btnFrame, buttons[button].X, BtnY - scrollY, buttons[button].W, BtnH, BtnH / 2);
            ui.ShowFocus(btnFrame, z == Zone.Buttons);
            shots?.SetActive(z == Zone.Shots);
            related?.SetActive(z == Zone.Related);
            if (reviewFrame != null)
            {
                ui.MoveFocus(reviewFrame, ReviewX(reviewFocus), reviewsY - scrollY, ReviewW, ReviewH, 18);
                ui.ShowFocus(reviewFrame, z == Zone.Reviews);
            }
        }

        void GoZone(int i)
        {
            if (i < 0 || i >= zones.Count || i == zone) return;
            zone = i;
            int top, bottom;
            switch (zones[i])
            {
                case Zone.Shots: top = shots.Y; bottom = shots.Y + HeadH + 225; break;
                case Zone.About: top = aboutY; bottom = aboutBottom; break;
                case Zone.Schedule: top = scheduleY; bottom = scheduleBottom; break;
                case Zone.Related: top = related.Y; bottom = related.Y + related.Height; break;
                case Zone.Reviews: top = reviewsY - HeadH; bottom = reviewsY + ReviewH; break;
                default: top = bottom = 0; break;
            }
            if (top == 0) scrollY = 0;
            else
            {
                if (bottom - scrollY > ViewBottom) scrollY = bottom - ViewBottom;
                if (top - scrollY < ViewTop) scrollY = top - ViewTop;
            }
            Place();
        }

        void MoveReview(int dx)
        {
            var i = Math.Max(0, Math.Min(reviewFocus + dx, info.Reviews.Count - 1));
            if (i == reviewFocus) return;
            reviewFocus = i;
            var x = i * (ReviewW + ReviewGap);
            var right = Theme.W - 2 * Theme.Margin;
            if (x - reviewOffset + ReviewW > right) reviewOffset = x + ReviewW - right;
            if (x - reviewOffset < 0) reviewOffset = x;
            Place();
        }

        static int Leading(string s)
        {
            var n = 0;
            foreach (var c in s ?? "") { if (c < '0' || c > '9') break; n = n * 10 + (c - '0'); }
            return n;
        }

        // Текст не длиннее lines строк (по ~chars знаков): обрезка по слову и «…».
        static string Cut(string s, int lines, int chars = DescChars)
        {
            s = (s ?? "").Trim();
            var max = lines * chars;
            if (s.Length <= max) return s;
            var cut = s.LastIndexOf(' ', max - 1);
            return s.Substring(0, cut > max / 2 ? cut : max - 1).TrimEnd(' ', ',', '.', ';', ':') + "…";
        }

        // ---- действия ----

        void Ok(Zone z)
        {
            switch (z)
            {
                case Zone.Desc: OpenReader(null, info.Title, info.Description); break;
                case Zone.Buttons:
                    switch (buttons[button].Kind)
                    {
                        case Btn.Watch: Play(false); break;
                        case Btn.Episodes: OpenEpisodes(); break;
                        case Btn.Reviews: GoZone(zones.IndexOf(Zone.Reviews)); break;
                    }
                    break;
                case Zone.Shots: shots.Ok(); break;
                case Zone.Related: related.Ok(); break;
                case Zone.Reviews:
                    var r = info.Reviews[reviewFocus];
                    var meta = string.Join(" · ", new[] { r.Author, r.Date, r.Useful }.Where(s => !string.IsNullOrEmpty(s)));
                    OpenReader(meta, r.Title, r.Text);
                    break;
            }
        }

        // «Серии» — отдельный экран выбора серии и сезона (плеер не запускаем).
        void OpenEpisodes()
        {
            DevLog.Write("open: серии " + id + (resumeEp > 0 ? " (текущая " + resumeEp + ")" : ""));
            app.Open(new EpisodesScreen(app, id, url, info, resumeEp, siteResume), () => new TitleScreen(app, id, url, siteResume));
        }

        void OpenReader(string meta, string title, string text)
        {
            DevLog.Write("title: читалка «" + title + "»");
            reader = new ReaderView(app.Win, meta, title, text, () => { reader?.Hide(); reader = null; });
        }

        void OpenGallery(int index)
        {
            DevLog.Write("title: кадр " + (index + 1) + " из " + info.Screenshots.Count);
            gallery = new GalleryView(app, info.Screenshots, index, () => { gallery?.Hide(); gallery = null; });
        }

        string WatchLabel(int ep)
        {
            var up = Release.Of(info, ep);
            return up == null ? RuText.WatchEpisode(ep) : string.IsNullOrEmpty(up.Value.When) ? RuText.NotReleased : RuText.EpisodeAirs(ep, up.Value.When);
        }

        // Серия, с которой продолжать, ещё не вышла: сказать когда, плеер не открывать.
        bool Upcoming(int ep)
        {
            var up = Release.Of(info, ep);
            if (up == null) return false;
            Toast.Show(ui, string.IsNullOrEmpty(up.Value.When) ? RuText.NotReleased : RuText.EpisodeAirs(ep, up.Value.When));
            return true;
        }

        // Нет связи — «Нет связи» и остаёмся: наугад серию 1 не включаем.
        void Play(bool episodes)
        {
            if (resumeEp > 0) { if (!Upcoming(resumeEp)) Open(resumeEp, episodes); return; }
            if (busy) return;
            busy = true;
            Task.Run(async () =>
            {
                var ep = await ResolveAsync().ConfigureAwait(false);
                App.Ui(() =>
                {
                    busy = false;
                    if (!alive) return;
                    if (ep == 0) { Toast.Show(ui, RuText.NoConnection); return; }
                    if (!Upcoming(ep)) Open(ep, episodes);
                });
            });
        }

        // Позиция — с сайта (карточка «Продолжить» с главной), если это та же серия; иначе из «виденного» на ТВ;
        // иначе с начала.
        void Open(int ep, bool episodes)
        {
            var seen = app.Prefs.Seen(app.UserKey, id);
            var pos = siteResume != null && siteResume.EpisodeNumber == ep ? siteResume.PositionSec
                : seen != null && seen.Episode == ep ? seen.Seconds : 0;
            DevLog.Write("open: anime " + id + " ep " + ep + " pos " + pos + (episodes ? " episodes" : ""));
            app.Open(new PlayerScreen(app, id, url, ep, pos, episodes), () => new TitleScreen(app, id, url));
        }

        void OpenRelated(RowItem it)
        {
            var r = (RelatedTitle)it.Tag;
            var rUrl = "/anime/" + r.Slug + "-" + r.Id;
            DevLog.Write("open: title " + r.Id + " " + rUrl);
            app.Open(new TitleScreen(app, r.Id, rUrl), () => new TitleScreen(app, id, url));
        }

        void ShowOverlay(MessageView next)
        {
            overlay?.Hide();
            overlay = next;
            overlay?.Show();
        }

        static string Why(Exception e) => e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name + " " + e.Message;
    }
}
