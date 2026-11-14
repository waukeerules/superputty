using System;
using System.Drawing;
using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>A tab with new output pulses in its colour (TabIcons.PulseLevel, PulseColour), as SuperXTerminal's</summary>
    [TestFixture]
    public class TabPulseTests
    {
        /// <summary>From the usual look to the full pulse and back, once per period; held still without animations</summary>
        [Test]
        public void Level()
        {
            Assert.AreEqual(0f, TabIcons.PulseLevel(TimeSpan.Zero, true), 0.001f);
            Assert.AreEqual(1f, TabIcons.PulseLevel(TimeSpan.FromMilliseconds(800), true), 0.001f);
            Assert.AreEqual(0f, TabIcons.PulseLevel(TimeSpan.FromMilliseconds(1600), true), 0.001f);
            Assert.AreEqual(0.6f, TabIcons.PulseLevel(TimeSpan.FromMilliseconds(800), false), 0.001f);
        }

        /// <summary>An unselected tab goes from near its background to nearly its colour; a filled one towards white</summary>
        [Test]
        public void Colour()
        {
            Color red = Color.FromArgb(0xC6, 0x28, 0x28), grey = Color.FromArgb(0x2D, 0x2D, 0x30);
            Color low = TabIcons.PulseColour(red, grey, false, 0), high = TabIcons.PulseColour(red, grey, false, 1);
            Assert.Less(Math.Abs(low.R - grey.R), Math.Abs(low.R - red.R), "at rest, near the background");
            Assert.Less(Math.Abs(high.R - red.R), Math.Abs(high.R - grey.R), "at full, near the colour");
            Color filled = TabIcons.PulseColour(red, red, true, 1);
            Assert.Greater(filled.G, red.G, "a filled tab pulses lighter");
            Assert.AreEqual(red, TabIcons.PulseColour(red, red, true, 0));
        }
    }
}
