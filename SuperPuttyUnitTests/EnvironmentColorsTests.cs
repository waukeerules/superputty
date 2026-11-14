using System.Linq;
using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>The colours the Colour menus and the Environment colours window offer (EnvironmentColors.Presets)</summary>
    [TestFixture]
    public class EnvironmentColorsTests
    {
        /// <summary>
        /// The ten colours of IconLibrary.Palette, as SuperXTerminal's palette, red first (a new rule's), with what
        /// red, orange and green are usually for
        /// </summary>
        [Test]
        public void PresetsAreThePalette()
        {
            CollectionAssert.AreEquivalent(IconLibrary.Palette.Select(p => EnvironmentColors.Format(p.Value)), EnvironmentColors.Presets.Select(p => p.Value));
            Assert.AreEqual("#C62828", EnvironmentColors.Presets[0].Value);
            Assert.AreEqual("Red (production)", EnvironmentColors.Presets[0].Key);
            Assert.AreEqual("Orange (staging)", EnvironmentColors.Presets[1].Key);
            Assert.IsTrue(EnvironmentColors.Presets.Any(p => p.Key == "Green (lab)" && p.Value == "#2E7D32"));
        }
    }
}
