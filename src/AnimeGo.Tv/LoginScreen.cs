using System;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Экран входа по макету Login.dc.html. Фокус ↑↓: логин → пароль → «Войти» → «Продолжить без входа».
    // OK на поле открывает нашу экранную клавиатуру QWERTY (OnScreenKeyboard). Системная клавиатура ТВ (Entry + IME)
    // осталась запасной и включается UseNativeKeyboard. Пока клавиатура открыта, пульт управляет ею, а не экраном.
    sealed class LoginScreen : IScreen
    {
        // true — системная клавиатура ТВ (Entry + IME). На UE50TU7140 приспособить не удалось (CLAUDE.md), поэтому своя.
        public static readonly bool UseNativeKeyboard = false;
        const int Fields = 2, Items = 4;
        const int CardX = 1040, CardY = 156, CardW = 720, CardH = 768, Pad = 56, FieldH = 80, BtnH = 80;
        const string BorderHex = "#2e323c", DarkHex = "#0e0f13";
        static readonly Color CardColor = Color.FromHex("#16181e");

        readonly App app;
        readonly Widgets ui;
        readonly Rectangle[] frames = new Rectangle[Items];
        readonly Rectangle[] fieldBack = new Rectangle[Fields];
        readonly Rectangle[] fieldBorder = new Rectangle[Fields];
        readonly Label[] fieldText = new Label[Fields];
        readonly string[] values = { "", "" };   // логин и пароль (пароль не логируется)
        readonly Label placeholder, error, loginLabel;
        readonly Keyboard[] native = new Keyboard[Fields];   // поля Entry (системная клавиатура)
        readonly Button sink;   // невидимая «заглушка» фокуса: пока ввода нет, фокус EFL лежит на ней, а не на полях
        OnScreenKeyboard kbd;   // открытая своя клавиатура или null
        readonly StashLoginServer stash;     // вход с ПК через Stash: порт открыт, пока виден этот экран
        readonly LoginProgressPanel progress;   // поверх карточки, пока идёт вход
        readonly UiTimer after = new UiTimer();   // пауза после «Готово» / ошибки на панели
        TaskCompletionSource<(User, string)> remote;   // ответ для Stash, пока идёт вход с его данными
        int index;              // подсвеченный пункт 0..3
        int requested = -1;     // поле, которому по OK попросили фокус (ждём Focused)
        bool typing, busy, alive;   // typing — системная клавиатура открыта (ставится только в Focused)
        long closedAt = -1_000_000;   // когда закрыли системную клавиатуру: тот же Return не должен сработать как OK

        public LoginScreen(App app)
        {
            this.app = app;
            ui = new Widgets(app.Win);
            ui.Rect(0, 0, Theme.W, Theme.H, Theme.Bg, false);
            if (UseNativeKeyboard)
            {
                sink = ui.Place(new Button(app.Win), -100, -100, 1, 1, true);
                sink.AllowFocus(true);
            }

            // Левая колонка
            const int lx = 160, lw = 700;
            ui.Text(lx, 270, lw, 60, "<font_size=40 color=" + Theme.TextHex + " font_weight=Bold>Anime</font_size><font_size=40 color=" +
                Theme.AccentHex + " font_weight=Bold>Go</font_size>", false);
            var head = ui.Text(lx, 360, lw, 150, Theme.T("Войдите в аккаунт animego.me", 60, bold: true), false);
            head.LineWrapType = WrapType.Word;
            string[] benefits = { "«Продолжить просмотр» с того же места", "Отметки просмотренных серий", "То же самое на телефоне и компьютере" };
            for (var i = 0; i < benefits.Length; i++)
            {
                var y = 560 + i * 62;
                ui.Rect(lx, y + 14, 14, 14, Theme.Accent, false);
                ui.Text(lx + 40, y, lw - 40, 44, Theme.T(benefits[i], 24, "#c9cdd4"), false);
            }

            // Вход с компьютера через Stash: пока этот экран открыт, данные придут сами (StashLoginServer).
            const int sy = 790, icon = 72;
            ui.RoundRect(lx, sy, lw, 150, 20, Theme.Card, false);
            var stashIcon = ui.Picture(lx + 28, sy + 39, icon, icon, false);
            stashIcon.Load(System.IO.Path.Combine(Tizen.Applications.Application.Current.DirectoryInfo.Resource, "stash.png"));
            ui.Text(lx + 28 + icon + 24, sy + 26, lw - icon - 80, 40, Theme.T(RuText.StashLoginTitle, 26, bold: true), false);
            ui.Text(lx + 28 + icon + 24, sy + 68, lw - icon - 80, 64, Theme.T(RuText.StashLoginHint, 20, "#a3a9b4"), false)
                .LineWrapType = WrapType.Word;

            // Карточка
            ui.Rect(CardX, CardY, CardW, CardH, CardColor, false);
            const int cx = CardX + Pad, cw = CardW - 2 * Pad;
            var fy = new int[Fields];
            string[] labels = { "Логин или e-mail", "Пароль" };
            var yy = CardY + Pad;
            for (var i = 0; i < Fields; i++)
            {
                ui.Text(cx, yy, cw, 30, Theme.T(labels[i], 20, "#c9cdd4", bold: true), false);
                fy[i] = yy + 40;
                yy += 40 + FieldH + 28;
            }
            // yy — ниже полей с зазором 28: строка ошибки, подсказка, кнопки
            var errY = yy - 28 + 18;
            var hintY = errY + 40;
            var btnY = hintY + 56 + 36;
            var by = new[] { fy[0], fy[1], btnY, btnY + BtnH + 16 };

            // Рамки фокуса — до самих виджетов (EFL кладёт новые объекты выше старых)
            for (var i = 0; i < Items; i++)
                frames[i] = ui.Focus(cx, by[i], cw, i < Fields ? FieldH : BtnH, CardColor);

            for (var i = 0; i < Fields; i++)
            {
                fieldBorder[i] = ui.Rect(cx, fy[i], cw, FieldH, Color.FromHex(BorderHex), false);
                fieldBack[i] = ui.Rect(cx + 2, fy[i] + 2, cw - 4, FieldH - 4, Theme.Bg, false);
            }
            placeholder = ui.Text(cx + 28, fy[1] + 18, cw - 56, FieldH - 30, Theme.T("••••••••", 26, "#7a808c"), false);
            for (var i = 0; i < Fields; i++)
            {
                var n = i;
                if (!UseNativeKeyboard) { fieldText[i] = ui.Text(cx + 28, fy[i] + 18, cw - 56, FieldH - 30, "", false); continue; }
                var k = new Keyboard(app.Win, ui, cx + 28, fy[i] + 8, cw - 56, FieldH - 16, password: i == 1);
                native[i] = k;
                k.Opened += () => OnEntryFocused(n);
                k.Done += () => { if (typing) DevLog.Write("kbd: done (entry Return/Activated)"); CloseNative(true); };
                k.Closed += () => { if (typing) { DevLog.Write("kbd: closed by Unfocused"); CloseNative(false); } };
                k.Changed += () => { if (n == 1) Refresh(); };
            }

            error = ui.Text(cx, errY, cw, 30, "", false);
            ui.Text(cx, hintY, cw, 56,
                Theme.T("Наберите с экранной клавиатуры. Вход запомнится, вводить второй раз не придётся.", 19, "#a3a9b4"), false).LineWrapType = WrapType.Word;

            ui.Rect(cx, by[2], cw, BtnH, Theme.Accent, false);
            loginLabel = ui.Text(cx, by[2] + 20, cw, 50, "", false);
            ui.Rect(cx, by[3], cw, BtnH, Theme.Card2, false);
            ui.Text(cx, by[3] + 22, cw, 50, Theme.T(RuText.LoginWithout, 24, Theme.TextHex, align: "center"), false);
            ui.Text(cx, by[3] + BtnH + 28, cw, 56,
                Theme.T("Без входа работают каталог, поиск и просмотр. Войти можно позже: кнопка «Войти» вверху главной.", 18, "#a3a9b4"), false).LineWrapType = WrapType.Word;

            progress = new LoginProgressPanel(ui, CardX, CardY, CardW, CardH, CardColor);   // последним — поверх карточки
            stash = new StashLoginServer(FromStash, _ => App.Ui(() => after.Start(1.2, () => { if (alive) app.ShowHome(); })));
        }

        public void Show()
        {
            alive = true;
            ui.ShowAll();
            progress.Hide();   // ShowAll показал и её
            Refresh();
            sink?.SetFocus(true);
            stash.Start();
        }

        public void Hide()
        {
            alive = false;
            kbd?.Close();
            kbd = null;
            foreach (var f in native) f?.Blur();
            stash.Stop();
            after.Stop();
            progress.Hide();
            remote?.TrySetResult((null, RuText.StashLoginClosed));
            ui.Dispose();
        }

        public bool OnKey(string key)
        {
            if (busy) return key != Keys.Exit;   // во время запроса клавиши глотаем
            if (kbd != null) return kbd.OnKey(key);
            if (typing)
            {
                // «Назад» пульта при открытой клавиатуре ТВ приходит как Cancel (иногда как XF86Back): только закрыть.
                if (key == Keys.Cancel || key == Keys.Back) { DevLog.Write("kbd: closed by Back (" + key + ")"); CloseNative(false); return true; }
                // OK и стрелки — системной клавиатуре. Глобальный Return — это и буква, и смена языка, и «Готово»,
                // поэтому «Готово» ловим по Return в самом Entry (Keyboard.Done).
                return key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right || key == Keys.Ok;
            }
            switch (key)
            {
                case Keys.Up: Move(-1); return true;
                case Keys.Down: Move(1); return true;
                case Keys.Ok:
                    if (Clock.Ms - closedAt < 400) return true;   // хвост Return, закрывшего клавиатуру
                    Press(); return true;
                case Keys.Back: Skip(); return true;
            }
            return false;
        }

        void Move(int d)
        {
            index = Math.Max(0, Math.Min(Items - 1, index + d));
            requested = -1;
            sink?.SetFocus(true);
            Refresh();
        }

        void Press()
        {
            if (index < Fields && UseNativeKeyboard) OpenNative(index);
            else if (index < Fields) OpenKeyboard(index);
            else if (index == Fields) Submit();
            else Skip();
        }

        // Системная клавиатура: просим фокус, а режим ввода включаем только в Focused —
        // если SetFocus молча не сработал, экран не застрянет, следующий OK попробует снова.
        void OpenNative(int n)
        {
            requested = n;
            DevLog.Write("kbd: request focus " + (n == 1 ? "password" : "login"));
            sink.SetFocus(false);
            native[n].Focus();
        }

        void OnEntryFocused(int n)
        {
            if (!alive) return;
            if (requested != n)
            {
                // EFL сам отдал фокус полю (навигация) — вернуть его заглушке.
                DevLog.Write("kbd: unrequested focus, back to sink");
                native[n].Blur();
                sink.SetFocus(true);
                return;
            }
            requested = -1;
            typing = true;
        }

        // next — перейти к следующему пункту («Готово»), иначе остаться на поле («Назад»).
        void CloseNative(bool next)
        {
            if (!alive || !typing) return;
            typing = false;
            closedAt = Clock.Ms;
            foreach (var f in native) f.Blur();
            sink.SetFocus(true);
            if (next) index = Math.Min(Items - 1, index + 1);   // дальше: пароль, потом «Войти»
            Refresh();
        }

        void OpenKeyboard(int n)
        {
            kbd = new OnScreenKeyboard(ui, app.Win, password: n == 1, initial: values[n]);
            kbd.Changed += _ => Refresh();
            kbd.Done += text =>
            {
                kbd = null;
                values[n] = text;
                index = n + 1;   // дальше: пароль, потом «Войти»
                Refresh();
            };
            kbd.Cancelled += () => { kbd = null; Refresh(); };
            Refresh();
        }

        // Текст поля: пароль — точками, длинный — хвостом; при вводе — красная каретка в конце.
        void ShowField(int i)
        {
            if (UseNativeKeyboard)
            {
                native[i].Style(i == index ? DarkHex : Theme.TextHex, 26);
                if (i == 1) { if (native[1].IsEmpty) placeholder.Show(); else placeholder.Hide(); }
                return;
            }
            var editing = kbd != null && index == i;
            var s = editing ? kbd.Text : values[i];
            if (i == 1) s = new string('•', s.Length);
            if (s.Length > 34) s = "…" + s.Substring(s.Length - 33);
            var color = i == index ? DarkHex : Theme.TextHex;
            fieldText[i].Text = "<align=left><font_size=26 color=" + color + ">" + Theme.Escape(s) + "</font_size>" +
                (editing ? "<font_size=26 color=" + Theme.AccentHex + ">|</font_size>" : "") + "</align>";
            if (i == 1) { if (s.Length == 0 && !editing) placeholder.Show(); else placeholder.Hide(); }
        }

        void Refresh()
        {
            for (var i = 0; i < Items; i++) ui.ShowFocus(frames[i], i == index);
            for (var i = 0; i < Fields; i++)
            {
                var on = i == index;
                // Подсвеченное поле — белое с тёмным текстом, остальные — тёмные с рамкой.
                fieldBorder[i].Color = on ? Color.White : Color.FromHex(BorderHex);
                fieldBack[i].Color = on ? Color.White : Theme.Bg;
                ShowField(i);
            }
            loginLabel.Text = Theme.T(busy ? RuText.Loading : RuText.Login, 26, "#ffffff", bold: true, align: "center");
        }

        void ShowError(string text) => error.Text = Theme.T(text, 22, Theme.AccentHex);

        // Данные для входа из Stash (поток сервера): входим так же, как с пульта, но поля не показываем — панель.
        Task<(User, string)> FromStash(string login, string password)
        {
            var tcs = new TaskCompletionSource<(User, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            App.Ui(() =>
            {
                if (!alive) { tcs.TrySetResult((null, RuText.StashLoginClosed)); return; }
                if (busy || remote != null) { tcs.TrySetResult((null, RuText.StashLoginBusy)); return; }
                kbd?.Close();
                kbd = null;
                remote = tcs;
                Submit(login, password);
            });
            return tcs.Task;
        }

        void Submit()
        {
            var login = (UseNativeKeyboard ? native[0].Text : values[0]).Trim();
            var password = UseNativeKeyboard ? native[1].Text : values[1];
            if (login.Length == 0 || password.Length == 0) { ShowError(RuText.BadLogin); return; }
            Submit(login, password);
        }

        void Submit(string login, string password)
        {
            busy = true;
            error.Text = "";
            Refresh();
            progress.Show(remote != null);
            Task.Run(async () =>
            {
                User user = null;
                ApiException fail = null;
                try
                {
                    user = await app.Api.LoginAsync(login, password).ConfigureAwait(false);
                    DevLog.Write("login: ok " + user.Id);
                }
                catch (ApiException e)
                {
                    fail = e;
                    DevLog.Write(e.Kind == ApiErrorKind.Unauthorized ? "login: unauthorized" : "login: " + e.Kind);
                }
                catch (Exception e)
                {
                    fail = new ApiException(ApiErrorKind.Server, e.GetType().Name);
                    DevLog.Write("login: " + e.GetType().Name);
                }
                App.Ui(() => Finished(user, fail));
            });
        }

        // Удачно — «Готово» на панели и через 1,2 с главная (для Stash — после того как ответ ему ушёл).
        // Нет — ошибка на панели 2,5 с, затем снова форма с той же ошибкой.
        void Finished(User user, ApiException fail)
        {
            var r = remote;
            remote = null;
            if (!alive) { r?.TrySetResult((null, RuText.StashLoginClosed)); return; }
            if (user != null)
            {
                app.ForgetUserData();   // главная и «Мой список» в кеше — гостя или прежнего пользователя
                app.User = user;
                app.Prefs.Cookies = app.Jar.Export();
                app.Prefs.SkippedLogin = false;
                app.SavePrefs();
                progress.Success(user.Name);
                if (r != null) r.TrySetResult((user, null));   // главную откроет сервер, когда ответит Stash
                else after.Start(1.2, () => { if (alive) app.ShowHome(); });
                return;   // busy остаётся: до перехода клавиши не нужны
            }
            var text = fail != null && fail.Kind == ApiErrorKind.Unauthorized ? RuText.BadLogin : RuText.NoConnection;
            progress.Fail(text);
            r?.TrySetResult((null, text));
            after.Start(2.5, () =>
            {
                if (!alive) return;
                busy = false;
                progress.Hide();
                ShowError(text);
                Refresh();
            });
        }

        void Skip()
        {
            app.Prefs.SkippedLogin = true;
            app.SavePrefs();
            app.ShowHome();
        }
    }
}
