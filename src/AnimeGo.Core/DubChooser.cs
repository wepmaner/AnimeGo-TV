using System.Collections.Generic;

namespace AnimeGo.Core
{
    public sealed class DubDecision
    {
        public string TranslationKey;          // null = спросить пользователя
        public bool UsedFallback;
        public IReadOnlyList<string> Available = new string[0];
    }

    // Основная и запасная озвучка (спека §5.6, §9). Доступны озвучки с cvh, aniboom или kodik; без настроек берём
    // первую с cvh/aniboom (у Kodik старт дольше), озвучки только с Kodik — в конце списка.
    public static class DubChooser
    {
        public static DubDecision Choose(DubPref pref, IReadOnlyList<VideoSource> sources)
        {
            var fast = new List<string>();
            var kodikOnly = new List<string>();
            foreach (var s in sources)
            {
                if (!s.Playable || s.TranslationKey == null) continue;
                if (VideoSource.Rank(s.Provider) < 2)
                {
                    kodikOnly.Remove(s.TranslationKey);
                    if (!fast.Contains(s.TranslationKey)) fast.Add(s.TranslationKey);
                }
                else if (!fast.Contains(s.TranslationKey) && !kodikOnly.Contains(s.TranslationKey)) kodikOnly.Add(s.TranslationKey);
            }
            var available = new List<string>(fast);
            available.AddRange(kodikOnly);

            var d = new DubDecision { Available = available };
            if (pref.Main != null && available.Contains(pref.Main)) d.TranslationKey = pref.Main;
            else if (pref.Fallback != null && available.Contains(pref.Fallback))
            {
                d.TranslationKey = pref.Fallback;
                d.UsedFallback = true;
            }
            else if (pref.Main == null && pref.Fallback == null && available.Count > 0)
                d.TranslationKey = available[0];
            return d;
        }
    }
}
