using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>The built-in terminal's preferences as Options keeps them (TerminalPreferences)</summary>
    [TestFixture]
    public class TerminalPreferencesTests
    {
        /// <summary>Word delimiters default to xterm.js's own less the space, as SuperXTerminal's</summary>
        [Test]
        public void WordSeparatorsDefault()
        {
            Assert.AreEqual("()[]{}',\"`", TerminalPreferences.Defaults["wordSeparators"]);
        }

        /// <summary>Spaces and repeats typed are dropped; the rest stays in the order typed; nothing typed is none</summary>
        [Test]
        public void WordSeparatorsAsTyped()
        {
            Assert.AreEqual("#()[]", TerminalPreferences.NormalizeWordSeparators("# ( ) [ ] ##"));
            Assert.AreEqual("", TerminalPreferences.NormalizeWordSeparators("   "));
            Assert.AreEqual("", TerminalPreferences.NormalizeWordSeparators(null));
        }

        /// <summary>Colours are kept as "#rrggbb"; anything else counts as the theme's</summary>
        [Test]
        public void ColoursAsKept()
        {
            Assert.IsTrue(TerminalPreferences.IsColour("#1E1e1E"));
            Assert.IsFalse(TerminalPreferences.IsColour(""));
            Assert.IsFalse(TerminalPreferences.IsColour("red"));
            Assert.IsFalse(TerminalPreferences.IsColour("#fff"));
            Assert.AreEqual("#0a64c8", TerminalPreferences.ColourText(System.Drawing.Color.FromArgb(0x0A, 0x64, 0xC8)));
            foreach (string name in new[] { "colorBackground", "colorForeground", "colorSelection", "colorCursor" })
                Assert.AreEqual("", TerminalPreferences.Defaults[name], name);
        }
    }
}
