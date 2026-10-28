using System.Text.RegularExpressions;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class CsrfTests
    {
        [Fact]
        public void NewToken_HasExpectedShapeAndDiffers()
        {
            var a = Csrf.NewToken();
            Assert.Matches(new Regex(@"^[-_/+a-zA-Z0-9]{24,}$"), a);
            Assert.NotEqual(a, Csrf.NewToken());
        }

        [Fact]
        public void CookieName_HasHostPrefix() =>
            Assert.Equal("__Host-csrf-token_abc", Csrf.CookieName("abc"));
    }
}
