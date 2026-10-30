using System;
using System.Collections.Generic;

namespace AnimeGo.Core
{
    // Разобранные ответы сайта в памяти процесса: экран, на который вернулись по «Назад», рисуется из кеша сразу,
    // а на сайт идёт только если запись устарела (политику свежести решает экран). Ключи — строки вида «title:{url}».
    // Записей не больше max: при переполнении уходит самая старая по времени Put. Потокобезопасен.
    public sealed class DataCache
    {
        sealed class Entry { public object Value; public DateTime At; }

        readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>();
        readonly Func<DateTime> clock;
        readonly int max;

        public DataCache(Func<DateTime> clock = null, int max = 120)
        {
            this.clock = clock ?? (() => DateTime.UtcNow);
            this.max = Math.Max(1, max);
        }

        public int Count { get { lock (map) return map.Count; } }

        // true — запись есть и её тип T; ageSec — сколько секунд прошло с Put.
        public bool TryGet<T>(string key, out T value, out double ageSec)
        {
            lock (map)
            {
                if (map.TryGetValue(key, out var e) && e.Value is T t)
                {
                    value = t;
                    ageSec = Math.Max(0, (clock() - e.At).TotalSeconds);
                    return true;
                }
            }
            value = default(T);
            ageSec = 0;
            return false;
        }

        // null не хранится (нечего показывать): такая запись удаляется.
        public void Put(string key, object value)
        {
            lock (map)
            {
                if (value == null) { map.Remove(key); return; }
                map[key] = new Entry { Value = value, At = clock() };
                while (map.Count > max) map.Remove(Oldest());
            }
        }

        public void Remove(string key)
        {
            lock (map) map.Remove(key);
        }

        public void Clear()
        {
            lock (map) map.Clear();
        }

        string Oldest()
        {
            string key = null;
            var at = DateTime.MaxValue;
            foreach (var kv in map) if (kv.Value.At < at) { at = kv.Value.At; key = kv.Key; }
            return key;
        }
    }
}
