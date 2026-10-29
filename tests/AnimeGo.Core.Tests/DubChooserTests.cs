using System.Collections.Generic;
using Xunit;

namespace AnimeGo.Core.Tests
{
    public class DubChooserTests
    {
        private static VideoSource S(string provider, string key) =>
            new VideoSource { Provider = provider, TranslationKey = key, PlayerUrl = "https://x/" + key };

        private static readonly VideoSource[] Sources =
        {
            S("kodik", "k"), S("cvh", "59"), S("aniboom", "59"), S("aniboom", "12"), S("kodik", "7"), S("cvh", "3"), S("other", "x"),
        };

        [Fact]
        public void Main_Available_IsChosen()
        {
            var d = DubChooser.Choose(new DubPref { Main = "12", Fallback = "3" }, Sources);
            Assert.Equal("12", d.TranslationKey);
            Assert.False(d.UsedFallback);
        }

        [Fact]
        public void Main_OnlyKodik_IsChosen()
        {
            var d = DubChooser.Choose(new DubPref { Main = "7", Fallback = "3" }, Sources);
            Assert.Equal("7", d.TranslationKey);
            Assert.False(d.UsedFallback);
        }

        [Fact]
        public void Main_Missing_UsesFallback()
        {
            var d = DubChooser.Choose(new DubPref { Main = "zzz", Fallback = "12" }, Sources);
            Assert.Equal("12", d.TranslationKey);
            Assert.True(d.UsedFallback);
        }

        [Fact]
        public void EmptyPrefs_FirstAvailableInSiteOrder()
        {
            var d = DubChooser.Choose(new DubPref(), Sources);
            Assert.Equal("59", d.TranslationKey);
            Assert.False(d.UsedFallback);
        }

        // Без настроек первой идёт озвучка с CVH/AniBoom: у них быстрее старт; Kodik-only — в конце, неизвестные — нет.
        [Fact]
        public void PrefsSet_ButUnavailable_AsksUser_KodikOnlyLast()
        {
            var d = DubChooser.Choose(new DubPref { Main = "zzz", Fallback = "yyy" }, Sources);
            Assert.Null(d.TranslationKey);
            Assert.False(d.UsedFallback);
            Assert.Equal(new[] { "59", "12", "3", "k", "7" }, new List<string>(d.Available));
        }

        [Fact]
        public void OnlyMainSet_Unavailable_AsksUser()
        {
            Assert.Null(DubChooser.Choose(new DubPref { Main = "zzz" }, Sources).TranslationKey);
        }

        [Fact]
        public void NoSources_AsksUser()
        {
            var d = DubChooser.Choose(new DubPref(), new VideoSource[0]);
            Assert.Null(d.TranslationKey);
            Assert.Empty(d.Available);
        }
    }
}
