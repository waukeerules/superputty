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
    }
}
