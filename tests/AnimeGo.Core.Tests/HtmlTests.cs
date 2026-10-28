using System.Linq;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class HtmlTests
    {
        [Fact]
        public void Attr_SingleQuotes() => Assert.Equal("5", Html.Attr("<a href=\"/x\" data-ep='5'>", "data-ep"));

        [Fact]
        public void Attr_DoubleQuotesAndEntities() => Assert.Equal("/x?a=1&b=2", Html.Attr("<a href=\"/x?a=1&amp;b=2\">", "href"));

        [Fact]
        public void Attr_Missing_Null() => Assert.Null(Html.Attr("<a href=\"/x\">", "data-ep"));

        [Fact]
        public void Attr_DoesNotMatchSuffix() => Assert.Null(Html.Attr("<a data-href=\"/x\">", "href"));

        [Fact]
        public void Attr_MultilineTag() => Assert.Equal("7", Html.Attr("<a\n  href=\"/x\"\n  data-ep=\"7\"\n>", "data-ep"));

        [Fact]
        public void Decode_Entities() => Assert.Equal("[\"cvh\"]", Html.Decode("&#x5B;&quot;cvh&quot;&#x5D;"));

        [Fact]
        public void Decode_NamedAndNumeric() => Assert.Equal("a & <b> 'c' d", Html.Decode("a &amp; &lt;b&gt; &#39;c&#39; d"));

        [Fact]
        public void Decode_Nbsp() => Assert.Equal("a b", Html.Decode("a&nbsp;b"));

        [Fact]
        public void Blocks_Nested() =>
            Assert.Equal(2, Html.Blocks("<div class=\"a\"><div class=\"b\">1</div></div><div class=\"a\">2</div>", "div", "a").Count());

        [Fact]
        public void Blocks_NestedSameTagKeptWhole() =>
            Assert.Equal("<div class=\"a\"><div>1</div>x</div>",
                Html.Blocks("<div class=\"a\"><div>1</div>x</div><div>tail</div>", "div", "a").Single());

        [Fact]
        public void Tags_ByClassToken()
        {
            var tags = Html.Tags("<a class=\"btn big\" href=\"1\"></a><a class=\"bigger\" href=\"2\"></a><a class='x big' href='3'>", "a", "big").ToList();
            Assert.Equal(2, tags.Count);
            Assert.Equal("3", Html.Attr(tags[1], "href"));
        }

        [Fact]
        public void Text_StripsTags() => Assert.Equal("Серия 22", Html.Text("<b>Серия</b>  22"));

        [Fact]
        public void Text_TagBecomesSpace() => Assert.Equal("a b", Html.Text("a<br>b"));

        [Fact]
        public void Text_QuotedGtInAttribute() => Assert.Equal("x", Html.Text("<a title=\"a>b\">x</a>"));

        [Fact]
        public void Tags_PlayerFixture()
        {
            var content = Json.Parse<AjaxDto>(Fixture.Read("player_3547.json")).Data.Content;
            var tags = Html.Tags(content, "button", "list-group-item").ToList();
            Assert.True(tags.Count >= 25);
            // первые кнопки — озвучки (без data-player), плееры идут дальше
            Assert.NotNull(tags.Select(t => Html.Attr(t, "data-player")).FirstOrDefault(v => v != null));
        }

        [Fact]
        public void Text_DropsScriptAndStyle() =>
            Assert.Equal("a b", Html.Text("a <script>var x = '<b>';</script><style>p{}</style> b"));

        [Fact]
        public void Text_DecodesAndCollapses() => Assert.Equal("Tom & Jerry", Html.Text("<p>Tom\n&amp;\n  Jerry</p>"));

        [Fact]
        public void TagsWithAttr_AnyValue_MatchesPresentOnly()
        {
            var html = "<div data-x=\"1\"></div><span data-x='2'></span><div data-y=\"3\"></div>";
            Assert.Equal(2, Html.TagsWithAttr(html, null, "data-x").Count());
            Assert.Single(Html.TagsWithAttr(html, "div", "data-x"));
        }

        [Fact]
        public void TagsWithAttr_Value_FiltersByValue()
        {
            var html = "<button data-t=\"provider\">a</button><button data-t=\"translation\">b</button>";
            var tags = Html.TagsWithAttr(html, "button", "data-t", "translation").ToList();
            Assert.Single(tags);
            Assert.Contains("translation", tags[0]);
        }

        [Fact]
        public void TagsWithAttr_ArrowInsideOtherAttribute_StillFound()
        {
            var html = "<div data-action=\"a->b c->d\" data-id=\"7\">x</div>";
            Assert.Equal("7", Html.Attr(Html.TagsWithAttr(html, "div", "data-id").Single(), "data-id"));
        }

        [Fact]
        public void TagsWithAttr_NoMatch_Empty()
        {
            Assert.Empty(Html.TagsWithAttr("<div data-x=\"1\"></div>", "div", "data-x", "2"));
            Assert.Empty(Html.TagsWithAttr("<div data-xy=\"1\"></div>", null, "data-x"));
            Assert.Empty(Html.TagsWithAttr(null, null, "data-x"));
        }

        [Fact]
        public void BlocksWithAttr_ReturnsWholeElement()
        {
            var html = "<button data-t=\"translation\"><span>A</span></button><button data-t=\"other\">B</button>";
            Assert.Equal("<button data-t=\"translation\"><span>A</span></button>", Html.BlocksWithAttr(html, "button", "data-t", "translation").Single());
        }
    }
}
