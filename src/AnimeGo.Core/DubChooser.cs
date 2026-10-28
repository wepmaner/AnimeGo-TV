using System.Collections.Generic;

namespace AnimeGo.Core
{
    public sealed class DubDecision
    {
        public string TranslationKey;          // null = спросить пользователя
        public bool UsedFallback;
        public IReadOnlyList<string> Available = new string[0];
    }

    // Основная и запасная озвучка (спека §5.6, §9). Играем только cvh и aniboom: kodik не воспроизводим.
    public static class DubChooser
    {
        public static DubDecision Choose(DubPref pref, IReadOnlyList<VideoSource> sources)
        {
            var available = new List<string>();
            foreach (var s in sources)
                if ((s.Provider == "cvh" || s.Provider == "aniboom") && s.TranslationKey != null && !available.Contains(s.TranslationKey))
                    available.Add(s.TranslationKey);

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
