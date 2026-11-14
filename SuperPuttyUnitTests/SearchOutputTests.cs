using System;
using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>Search Output's line test (dlgSearchOutput.MakeMatcher), whole word included</summary>
    [TestFixture]
    public class SearchOutputTests
    {
        /// <summary>A whole word isn't part of a longer word on either side</summary>
        [Test]
        public void WholeWord()
        {
            Func<string, bool> show = dlgSearchOutput.MakeMatcher("show", false, false, true);
            Assert.IsTrue(show("sw1#show run"));
            Assert.IsTrue(show("SHOW version"));
            Assert.IsFalse(show("showing the output"));
            Assert.IsFalse(show("reshow"));
        }

        /// <summary>Text ending in punctuation still counts as a word; regex characters in plain text are literal</summary>
        [Test]
        public void PunctuationAndLiteralText()
        {
            Assert.IsTrue(dlgSearchOutput.MakeMatcher("sw1#", false, false, true)("sw1# show run"));
            Assert.IsTrue(dlgSearchOutput.MakeMatcher("10.0.0.1", false, false, true)("ping 10.0.0.1 ok"));
            Assert.IsFalse(dlgSearchOutput.MakeMatcher("10.0.0.1", false, false, true)("ping 10.0.0.11"));
        }

        /// <summary>A whole word with a regex and with match case</summary>
        [Test]
        public void RegexAndCase()
        {
            Func<string, bool> gi = dlgSearchOutput.MakeMatcher(@"Gi\d+", true, true, true);
            Assert.IsTrue(gi("interface Gi12 up"));
            Assert.IsFalse(gi("interface gi12 up"));
            Assert.IsFalse(gi("interface Gi12x up"));
        }

        /// <summary>Without whole word, as before: anywhere in the line</summary>
        [Test]
        public void NotWholeWordAsBefore()
        {
            Assert.IsTrue(dlgSearchOutput.MakeMatcher("show", false, false)("showing"));
        }
    }
}
