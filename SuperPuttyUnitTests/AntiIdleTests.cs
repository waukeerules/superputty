using System;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The anti-idle string's escapes, as KiTTY's SendKeyboardPlus turns them into keys (AntiIdle.Keys), and when it's
    /// due (AntiIdle.IsDue); ported from SuperXTerminal's antiIdle.test.ts
    /// </summary>
    [TestFixture]
    public class AntiIdleTests
    {
        /// <summary>A character by its hex code; a trailing backslash only ends the string</summary>
        [Test]
        public void HexCodeAndTrailingBackslash()
        {
            // Ctrl+E: the end of the line on a Cisco, which shows nothing
            Assert.AreEqual("\x05", AntiIdle.Keys(@"\x05\"));
            Assert.AreEqual("\x05", AntiIdle.Keys(@"\x05"));
        }

        /// <summary>Keys by their virtual-key code: KiTTY's space and backspace, and Escape</summary>
        [Test]
        public void VirtualKeyCodes()
        {
            Assert.AreEqual(" \b", AntiIdle.Keys(@" \k08\"));
            Assert.AreEqual("\x1b", AntiIdle.Keys(@"\k1B"));
        }

        /// <summary>\r, \n, \t, \h and \\</summary>
        [Test]
        public void NamedEscapes()
        {
            Assert.AreEqual("a\rb\nc\td\be\\f", AntiIdle.Keys(@"a\rb\nc\td\he\\f"));
        }

        /// <summary>KiTTY's pauses and Ctrl+Break are left out; any other backslash, or one without two hex digits, is typed as it is</summary>
        [Test]
        public void PausesLeftOutOthersKept()
        {
            Assert.AreEqual("abcd", AntiIdle.Keys(@"a\pb\s03c\cd"));
            Assert.AreEqual(@"\q \xZZ", AntiIdle.Keys(@"\q \xZZ"));
            Assert.AreEqual(@"\x 5", AntiIdle.Keys(@"\x 5"));
        }

        /// <summary>Plain text as it is; nothing for nothing</summary>
        [Test]
        public void PlainText()
        {
            Assert.AreEqual("", AntiIdle.Keys(""));
            Assert.AreEqual("", AntiIdle.Keys(null));
            Assert.AreEqual(" ", AntiIdle.Keys(" "));
        }

        /// <summary>Due once nothing has been typed for the seconds set (KiTTY's 180 when none), and never with nothing to type</summary>
        [Test]
        public void Due()
        {
            DateTime typed = new DateTime(2026, 11, 14, 12, 0, 0);
            Assert.IsFalse(AntiIdle.IsDue("\x05", 60, typed, typed.AddSeconds(59)));
            Assert.IsTrue(AntiIdle.IsDue("\x05", 60, typed, typed.AddSeconds(60)));
            Assert.IsFalse(AntiIdle.IsDue("\x05", 0, typed, typed.AddSeconds(179)));
            Assert.IsTrue(AntiIdle.IsDue("\x05", 0, typed, typed.AddSeconds(180)));
            Assert.IsFalse(AntiIdle.IsDue("", 60, typed, typed.AddHours(1)));
        }
    }
}
