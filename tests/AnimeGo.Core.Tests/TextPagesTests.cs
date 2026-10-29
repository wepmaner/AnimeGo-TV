using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class TextPagesTests
    {
        [Fact]
        public void WrapsByWords()
        {
            var p = TextPages.Split("один два три четыре пять", 10, 10);
            Assert.Equal(new[] { "один два", "три четыре", "пять" }, p.Single());
        }

        [Fact]
        public void KeepsParagraphs_OneBlankLine()
        {
            var p = TextPages.Split("абзац один\n\n\n\nабзац два", 20, 10);
            Assert.Equal(new[] { "абзац один", "", "абзац два" }, p.Single());
        }

        [Fact]
        public void Pages_NoLeadingBlank()
        {
            var p = TextPages.Split("a\nb\n\nc\nd", 5, 2);
            Assert.Equal(new[] { "a", "b" }, p[0]);
            Assert.Equal(new[] { "c", "d" }, p[1]);   // пустая строка на стыке страниц выпала
            Assert.Equal(2, p.Count);
        }

        [Fact]
        public void LongWord_Cut()
        {
            var p = TextPages.Split("абвгдежзик", 4, 10);
            Assert.Equal(new[] { "абвг", "дежз", "ик" }, p.Single());
        }

        [Fact]
        public void Empty_OnePage() => Assert.Empty(TextPages.Split(null, 10, 5).Single());

        [Fact]
        public void RealReview_AllWordsKept()
        {
            var text = AnimeParser.Parse(Fixture.Read("anime_280.html"), 280).Reviews[0].Text;
            var pages = TextPages.Split(text, 66, 14);
            var words = string.Join(" ", pages.SelectMany(x => x)).Split(' ').Where(w => w.Length > 0);
            Assert.Equal(text.Split(' ', '\n').Where(w => w.Length > 0), words);
            Assert.All(pages.SelectMany(x => x), l => Assert.True(l.Length <= 66));
        }
    }
}
