using System.IO;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Поверх карточки входа, пока идёт вход (поля логина и пароля скрыты):
    // из Stash — значок Stash → бегущие точки → значок AnimeGo, «Данные для входа получены из Stash»;
    // с пульта — значок AnimeGo, под ним бегущие точки, «Соединяемся с animego.me».
    // Ниже состояние: «Входим в аккаунт…» → «Готово, вы вошли как …» или ошибка.
    sealed class LoginProgressPanel
    {
        const int Icon = 140, Dots = 5, DotR = 7, DotStep = 32, IconY = 150;
        const string LimeHex = "#c8f135";
        static readonly Color Lime = Color.FromHex(LimeHex), LimeDim = Color.FromHex("#5f7320"), DotOff = Color.FromHex("#3a3e48");

        readonly Widgets ui;
        readonly EvasObject bg;
        readonly Image stash, anime;
        readonly Polygon[] dots = new Polygon[Dots];
        readonly Label title, state;
        readonly UiTimer anim = new UiTimer();
        readonly int x, y, w;
        bool fromStash;
        int phase;

        public bool Visible { get; private set; }

        public LoginProgressPanel(Widgets ui, int x, int y, int w, int h, Color back)
        {
            this.ui = ui;
            this.x = x;
            this.y = y;
            this.w = w;
            bg = ui.Rect(x, y, w, h, back, false);
            stash = ui.Picture(x + 110, y + IconY, Icon, Icon, false);
            anime = ui.Picture(0, 0, Icon, Icon, false);
            var res = Tizen.Applications.Application.Current.DirectoryInfo.Resource;
            stash.Load(Path.Combine(res, "stash.png"));
            anime.Load(Path.Combine(res, "animego.png"));
            for (var i = 0; i < Dots; i++) dots[i] = ui.Circle(0, 0, DotR, DotOff, false);
            title = ui.Text(x + 60, y + IconY + Icon + 90, w - 120, 96, "", false);
            title.LineWrapType = WrapType.Word;
            state = ui.Text(x + 60, y + IconY + Icon + 210, w - 120, 80, "", false);
            state.LineWrapType = WrapType.Word;
        }

        public void Show(bool fromStash)
        {
            this.fromStash = fromStash;
            Visible = true;
            int dotsY;
            if (fromStash)
            {
                anime.Move(x + w - 110 - Icon, y + IconY);   // Stash слева, AnimeGo справа, точки между ними
                dotsY = y + IconY + Icon / 2;
            }
            else
            {
                anime.Move(x + (w - Icon) / 2, y + IconY);   // AnimeGo по центру, точки под ним
                dotsY = y + IconY + Icon + 40;
            }
            var dx = x + w / 2 - (Dots - 1) * DotStep / 2;
            for (var i = 0; i < Dots; i++) dots[i].Move(dx + i * DotStep - DotR, dotsY - DotR);
            title.Text = Theme.T(fromStash ? RuText.StashGotLogin : RuText.ConnectingAnimego, 32, bold: true, align: "center");
            state.Text = Theme.T(RuText.SigningIn, 26, Theme.MutedHex, align: "center");
            foreach (var o in new EvasObject[] { bg, anime, title, state }) { o.Show(); ui.Raise(o); }
            if (fromStash) { stash.Show(); ui.Raise(stash); } else stash.Hide();
            foreach (var d in dots) { d.Show(); ui.Raise(d); }
            phase = 0;
            anim.Start(0.12, Step, true);
            Step();
        }

        // Точка-«пакет» бежит слева направо, за ней тускнеющий след.
        void Step()
        {
            for (var i = 0; i < Dots; i++)
                dots[i].Color = i == phase ? Lime : i == phase - 1 ? LimeDim : DotOff;
            phase = (phase + 1) % (Dots + 2);
        }

        public void Success(string name)
        {
            anim.Stop();
            foreach (var d in dots) d.Color = Lime;
            state.Text = Theme.T(RuText.SignedInAs(name), 26, LimeHex, true, "center");
        }

        public void Fail(string error)
        {
            anim.Stop();
            foreach (var d in dots) d.Color = Theme.Accent;
            state.Text = Theme.T(error, 26, Theme.AccentHex, true, "center");
        }

        public void Hide()
        {
            anim.Stop();
            Visible = false;
            foreach (var o in new EvasObject[] { bg, stash, anime, title, state }) o.Hide();
            foreach (var d in dots) d.Hide();
        }
    }
}
