using System;
using System.Collections.Generic;

namespace AnimeGo.Core
{
    public sealed class ContinueRows
    {
        public IReadOnlyList<ContinueCard> Active;
        public IReadOnlyList<ContinueCard> Stale;
    }

    // Делит карточки «Продолжить просмотр» на актуальные и «давно не смотрели» (спека §8).
    // Меняет prefs в памяти; Save вызывает экран.
    public static class ContinueModel
    {
        public const int StaleDays = 14;

        public static ContinueRows Split(IReadOnlyList<ContinueCard> cards, Prefs p, string userKey, DateTime nowUtc)
        {
            var active = new List<ContinueCard>();
            var stale = new List<ContinueCard>();
            var present = new HashSet<int>();

            foreach (var c in cards)
            {
                present.Add(c.AnimeId);
                var seen = p.Seen(userKey, c.AnimeId);
                if (seen == null || seen.Episode != c.EpisodeNumber || seen.Seconds != c.PositionSec)
                {
                    p.SetSeen(userKey, c.AnimeId, c.EpisodeNumber, c.PositionSec, nowUtc);
                    active.Add(c);
                }
                else if (nowUtc - seen.AtUtc >= TimeSpan.FromDays(StaleDays)) stale.Add(c);
                else active.Add(c);
            }

            foreach (var id in p.SeenIds(userKey))
                if (!present.Contains(id)) p.RemoveSeen(userKey, id);

            return new ContinueRows { Active = active, Stale = stale };
        }
    }
}
