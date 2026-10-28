using System;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Однострочное поле ввода поверх ElmSharp.Entry. Экранная клавиатура ТВ появляется, когда Entry получает фокус.
    // Содержимое поля (особенно пароль) нигде не логируется.
    sealed class Keyboard
    {
        readonly Entry entry;

        // Return на экранной клавиатуре.
        public event Action Done;
        // Текст изменил пользователь.
        public event Action Changed;
        // Entry получил фокус (по нашему SetFocus или сам EFL).
        public event Action Opened;
        // Entry потерял фокус (клавиатуру закрыли).
        public event Action Closed;
        // Entry сейчас в фокусе (клавиатура открыта).
        public bool IsOpen { get; private set; }

        public Keyboard(Window win, Widgets ui, int x, int y, int w, int h, bool password)
        {
            entry = ui.Place(new Entry(win)
            {
                IsSingleLine = true,
                IsPassword = password,
                Scrollable = false,
                PredictionAllowed = false,
                AutoCapital = AutoCapital.None,
            }, x, y, w, h, true);
            entry.Focused += (s, e) => { IsOpen = true; DevLog.Write("entry: focused"); Opened?.Invoke(); };
            entry.Unfocused += (s, e) => { IsOpen = false; DevLog.Write("entry: unfocused"); Closed?.Invoke(); };
            entry.SetInputPanelEnabled(true);
            entry.SetInputPanelReturnKeyType(InputPanelReturnKeyType.Done);
            entry.Activated += (s, e) => Done?.Invoke();
            entry.ChangedByUser += (s, e) => Changed?.Invoke();
        }

        // Текст в чистом виде (Entry хранит разметку: &amp; и т.п.).
        public string Text => Entry.ConvertMarkupToUtf8(entry.Text ?? "") ?? "";

        public bool IsEmpty => string.IsNullOrEmpty(entry.Text);

        // Цвет и размер текста; зовётся при смене подсветки поля.
        public void Style(string colorHex, int px) =>
            entry.TextStyle = "DEFAULT='font_size=" + px + " color=" + colorHex + " wrap=none align=left'";

        public void Focus()
        {
            entry.SetInputPanelEnabled(true);
            entry.SetFocus(true);
            entry.ShowInputPanel();
        }

        public void Blur()
        {
            entry.HideInputPanel();
            entry.SetFocus(false);
        }
    }
}
