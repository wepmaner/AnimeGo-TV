using System;

namespace AnimeGo.Core
{
    public enum ApiErrorKind { Network, Server, Unauthorized, Parse }

    public sealed class ApiException : Exception
    {
        public ApiException(ApiErrorKind kind, string message, Exception inner = null) : base(message, inner)
        {
            Kind = kind;
        }

        public ApiErrorKind Kind { get; }
    }
}
