using System.Collections.Generic;
using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>A PuTTY profile's colours (Window > Colours) as the built-in terminal's theme takes them (TerminalControl.ParseProfileColours)</summary>
    [TestFixture]
    public class ProfileColoursTests
    {
        /// <summary>Text, background, cursor and the ANSI colours, each normal then bright, from PuTTY's "R,G,B" values</summary>
        [Test]
        public void ReadsPuttysColours()
        {
            Dictionary<string, string> saved = new Dictionary<string, string>
            {
                { "Colour0", "187,187,187" }, { "Colour2", "0,0,64" }, { "Colour4", "0,0,0" }, { "Colour5", "0,255,0" },
                { "Colour6", "0,0,0" }, { "Colour7", "85,85,85" }, { "Colour8", "187,0,0" }, { "Colour9", "255,85,85" },
                { "Colour20", "187,187,187" }, { "Colour21", "255,255,255" },
            };
            Dictionary<string, object> colours = TerminalControl.ParseProfileColours(name => saved.TryGetValue(name, out string v) ? v : null);
            Assert.AreEqual("#bbbbbb", colours["foreground"]);
            Assert.AreEqual("#000040", colours["background"]);
            Assert.AreEqual("#00ff00", colours["cursor"]);
            Assert.AreEqual("#000000", colours["cursorAccent"]);
            string[] ansi = (string[])colours["ansi"];
            Assert.AreEqual("#000000", ansi[0]);
            Assert.AreEqual("#bb0000", ansi[1]);
            Assert.AreEqual("#bbbbbb", ansi[7]);
            Assert.AreEqual("#555555", ansi[8]);
            Assert.AreEqual("#ff5555", ansi[9]);
            Assert.AreEqual("#ffffff", ansi[15]);
            Assert.IsNull(ansi[2], "not set: the theme's");
        }

        /// <summary>A profile without colours, or with ones that aren't R,G,B, gives none</summary>
        [Test]
        public void NoneOrUnreadable()
        {
            Assert.IsNull(TerminalControl.ParseProfileColours(name => null));
            Assert.IsNull(TerminalControl.ParseProfileColours(name => name == "Colour0" ? "300,0,0" : name == "Colour2" ? "red" : null));
        }
    }
}
