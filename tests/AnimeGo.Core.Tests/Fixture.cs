using System;
using System.IO;

namespace AnimeGo.Core.Tests
{
    static class Fixture
    {
        public static string Read(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

        // null, если приватной фикстуры (снятой под аккаунтом) нет.
        public static string ReadPrivate(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "private", name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
