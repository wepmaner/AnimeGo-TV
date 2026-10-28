using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Страница тайтла (спека §5.5): слева постер 250×350, справа название, оригинал, «тип · год · жанры», описание
    // в 3 строки (OK — целиком, до 10 строк), кнопки «Смотреть» и «Серии», ниже ряд «Связанное».
    // url нужен целиком: по одному id сайт страницу не отдаёт (/anime/{id} — 404).
    sealed class TitleScreen : IScreen
    {
        const int PosterW = 250, PosterH = 350, Top = 80, TextX = Theme.Margin + PosterW + 56, TextW = Theme.W - Theme.Margin - TextX;
        const int DescY = 236, LineH = 33, ShortLines = 3, LongLines = 10, CharsPerLine = 120;
        const int BtnH = 64, BtnGap = 24;
        enum Focus { Desc, Watch, Episodes, Related }

        readonly App app;
        readonly int id;
        readonly string url;
        readonly Widgets ui;
        readonly List<EvasObject> content = new List<EvasObject>();
        MessageView overlay;
        AnimeInfo info;
        Label desc;
        EvasObject descFrame, btnFrame;
        Polygon[] btnBack = new Polygon[2];
        Label[] btnText = new Label[2];
        int[] btnX = new int[2], btnW = new int[2];
        Row related;
        Focus focus = Focus.Watch;
        bool alive, expanded, longDesc, busy;
        int btnY;

        readonly ContinueCard siteResume;   // карточка «Продолжить» этого тайтла с главной: где остановились по сайту

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
            overlay?.Hide();
            overlay = null;
            ui.Dispose();
        }

        public bool OnKey(string key)
        {
            if (overlay != null) return overlay.OnKey(key);
            if (info == null) return false;
            switch (key)
            {
                case Keys.Back: app.Back(); return true;
                case Keys.Up: Go(focus == Focus.Related ? Focus.Watch : longDesc ? Focus.Desc : focus); return true;
                case Keys.Down: Go(focus == Focus.Desc ? Focus.Watch : related != null && !related.IsEmpty ? Focus.Related : focus); return true;
                case Keys.Left:
                    if (focus == Focus.Episodes) Go(Focus.Watch); else if (focus == Focus.Related) related.Move(-1);
                    return true;
                case Keys.Right:
                    if (focus == Focus.Watch) Go(Focus.Episodes); else if (focus == Focus.Related) related.Move(1);
                    return true;
                case Keys.Ok: Ok(); return true;
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
                catch (Exception e) { why = e is ApiException x ? x.Kind + " " + x.Message : e.GetType().Name + " " + e.Message; }
                App.Ui(() =>
                {
                    if (!alive) return;
                    if (a == null)
                    {
                        DevLog.Write("title: " + id + " ошибка " + why);
                        ShowOverlay(new MessageView(app, RuText.NoConnection, Load, app.Back));
                        return;
                    }
                    DevLog.Write("title: " + id + " ok, related " + a.Related.Count);
                    ShowOverlay(null);
                    info = a;
                    Build();
                });
            });
        }

        void Build()
        {
            ui.RoundRect(Theme.Margin, Top, PosterW, PosterH, 14, Theme.Card);
            app.Images.Set(ui.Picture(Theme.Margin, Top, PosterW, PosterH), info.PosterUrl);

            var title = ui.Text(TextX, Top, TextW, 56, Theme.T(info.Title, 40, bold: true));
            title.IsEllipsis = true;
            if (!string.IsNullOrEmpty(info.TitleOriginal))
            {
                var orig = ui.Text(TextX, Top + 62, TextW, 32, Theme.T(info.TitleOriginal, 22, Theme.MutedHex));
                orig.IsEllipsis = true;
            }
            var meta = new[] { info.Kind, info.Year }.Concat(info.Genres.Take(4)).Where(s => !string.IsNullOrEmpty(s));
            var line = ui.Text(TextX, Top + 104, TextW, 30, Theme.T(string.Join(" · ", meta), 20, Theme.MutedHex));
            line.IsEllipsis = true;

            longDesc = Cut(info.Description, ShortLines) != (info.Description ?? "");
            descFrame = ui.RoundFocus(TextX - 12, DescY - 8, TextW + 24, ShortLines * LineH + 16, 14);
            desc = ui.Text(TextX, DescY, TextW, ShortLines * LineH, "");
            desc.LineWrapType = WrapType.Word;

            btnFrame = ui.RoundFocus(0, 0, 1, BtnH, BtnH / 2);
            var x = TextX;
            var labels = new[] { RuText.Watch, RuText.Episodes };
            for (var i = 0; i < 2; i++)
            {
                btnW[i] = Math.Max(220, 64 + 14 * labels[i].Length);
                btnX[i] = x;
                btnBack[i] = ui.RoundRect(0, 0, btnW[i], BtnH, BtnH / 2, i == 0 ? Theme.Accent : Theme.Card2);
                btnText[i] = ui.Text(0, 0, btnW[i], 36, Theme.T(labels[i], 24, bold: true, align: "center"));
                x += btnW[i] + BtnGap;
            }

            var rel = info.Related.Where(r => r.Id != id).Select(r => new RowItem
            {
                Title = r.Title,
                Subtitle = string.Join(" · ", new[] { r.Kind, r.Year }.Where(s => !string.IsNullOrEmpty(s))),
                ImageUrl = r.PosterUrl,
                Tag = r,
            }).ToList();
            if (rel.Count > 0)
            {
                related = new Row(ui, app.Images, 0, 140, 196, 32, RuText.Related) { OnOk = OpenRelated };
                related.SetItems(rel);
            }
            Layout();
            Toast.Raise();
        }

        // Описание (свёрнутое или целиком) сдвигает кнопки и ряд «Связанное».
        void Layout()
        {
            var lines = expanded ? LongLines : ShortLines;
            var text = Cut(info.Description, lines);
            var shown = Math.Max(1, Math.Min(lines, (text.Length + CharsPerLine - 1) / CharsPerLine));
            desc.Resize(TextW, shown * LineH);
            desc.Text = Theme.T(text, 22);
            ui.MoveFocus(descFrame, TextX - 12, DescY - 8, TextW + 24, shown * LineH + 16);

            btnY = Math.Max(DescY + shown * LineH + 36, Top + PosterH - BtnH);
            for (var i = 0; i < 2; i++)
            {
                btnBack[i].Move(btnX[i], btnY);
                btnText[i].Move(btnX[i], btnY + 14);
            }
            if (related != null)
            {
                related.Y = Math.Max(btnY + BtnH, Top + PosterH) + 56;
                related.Place(0);
            }
            UpdateFocus();
        }

        void UpdateFocus()
        {
            ui.ShowFocus(descFrame, focus == Focus.Desc);
            var b = focus == Focus.Watch ? 0 : focus == Focus.Episodes ? 1 : -1;
            if (b >= 0) ui.MoveFocus(btnFrame, btnX[b], btnY, btnW[b], BtnH, BtnH / 2);
            ui.ShowFocus(btnFrame, b >= 0);
            related?.SetActive(focus == Focus.Related);
        }

        void Go(Focus f)
        {
            if (f == focus) return;
            focus = f;
            UpdateFocus();
        }

        // Текст не длиннее lines строк (по ~CharsPerLine знаков): обрезка по слову и «…».
        static string Cut(string s, int lines)
        {
            s = (s ?? "").Trim();
            var max = lines * CharsPerLine;
            if (s.Length <= max) return s;
            var cut = s.LastIndexOf(' ', max - 1);
            return s.Substring(0, cut > max / 2 ? cut : max - 1).TrimEnd(' ', ',', '.', ';', ':') + "…";
        }

        // ---- действия ----

        void Ok()
        {
            switch (focus)
            {
                case Focus.Desc: expanded = !expanded; Layout(); break;
                case Focus.Watch: Play(false); break;
                case Focus.Episodes: Play(true); break;
                case Focus.Related: related.Ok(); break;
            }
        }

        // Серия, с которой продолжать: по отметкам «просмотрено» из /player и ответу continuation сайта
        // (PlayerInfo.ResumeEpisode). Нет связи — «Нет связи» и остаёмся: наугад серию 1 не включаем.
        void Play(bool episodes)
        {
            if (busy) return;
            busy = true;
            Task.Run(async () =>
            {
                var ep = 0;
                try
                {
                    var player = await app.Api.PlayerAsync(id).ConfigureAwait(false);
                    int? site = null;
                    if (!string.IsNullOrEmpty(player.ContinuationUrl))
                        site = PlayerParser.ParseContinuation(await app.Api.GetAjaxAsync(player.ContinuationUrl).ConfigureAwait(false));
                    ep = player.ResumeEpisode(site);
                    DevLog.Write("title: continuation " + (site?.ToString() ?? "нет") + ", просмотрено " + player.Episodes.Count(e => e.Watched) +
                        " из " + player.Episodes.Count + " → серия " + ep);
                }
                catch (Exception e) { DevLog.Write("title: player " + id + " ошибка " + (e is ApiException a ? a.Kind + " " + a.Message : e.GetType().Name)); }
                App.Ui(() =>
                {
                    busy = false;
                    if (!alive) return;
                    if (ep == 0) { Toast.Show(ui, RuText.NoConnection); return; }
                    // Позиция — с сайта (карточка «Продолжить» с главной), если это та же серия; иначе из «виденного»
                    // на ТВ; иначе с начала.
                    var seen = app.Prefs.Seen(app.UserKey, id);
                    var pos = siteResume != null && siteResume.EpisodeNumber == ep ? siteResume.PositionSec
                        : seen != null && seen.Episode == ep ? seen.Seconds : 0;
                    DevLog.Write("open: anime " + id + " ep " + ep + " pos " + pos + (episodes ? " episodes" : ""));
                    app.Open(new PlayerScreen(app, id, url, ep, pos, episodes), () => new TitleScreen(app, id, url));
                });
            });
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
    }
}
