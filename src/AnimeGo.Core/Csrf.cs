using System;
using System.Security.Cryptography;

namespace AnimeGo.Core
{
    // Токен формы входа: сайт проверяет пару «кука __Host-csrf-token_<токен> + поле _csrf_token».
    public static class Csrf
    {
        public static string NewToken()
        {
            var b = new byte[18];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b); // 18 байт -> 24 символа без «=»
        }

        public static string CookieName(string token) => "__Host-csrf-token_" + token;
    }
}
