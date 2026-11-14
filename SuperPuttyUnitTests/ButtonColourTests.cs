using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The colour a button's command numbers its lines in (ButtonBarStrip.LineNumberColor): the colour the button shows,
    /// as SuperXTerminal's buttonColour tests
    /// </summary>
    [TestFixture]
    public class ButtonColourTests
    {
        /// <summary>A button</summary>
        /// <param name="icon">Its icon key; "" for none</param>
        /// <param name="color">Its circle's colour; "" for none</param>
        /// <returns>The button</returns>
        private static CommandButton Button(string icon, string color = "")
        {
            return new CommandButton { Label = "show run", ImageKey = icon, Color = color };
        }

        /// <summary>An icon's chosen colour</summary>
        [Test]
        public void IconsChosenColour()
        {
            Assert.AreEqual("#a371f7", ButtonBarStrip.LineNumberColor(Button("switch#A371F7")));
        }

        /// <summary>An icon's own colour when none is chosen (the switch is green, the document blue)</summary>
        [Test]
        public void IconsOwnColour()
        {
            Assert.AreEqual("#2e7d32", ButtonBarStrip.LineNumberColor(Button("switch")));
            Assert.AreEqual("#1e63b4", ButtonBarStrip.LineNumberColor(Button("document")));
        }

        /// <summary>An icon wins over the circle's colour, which the button doesn't show then</summary>
        [Test]
        public void IconOverCircle()
        {
            Assert.AreEqual("#1e63b4", ButtonBarStrip.LineNumberColor(Button("document", "#FF0000")));
        }

        /// <summary>Without an icon, the circle's colour; with neither, none</summary>
        [Test]
        public void CircleOrNone()
        {
            Assert.AreEqual("#3fb950", ButtonBarStrip.LineNumberColor(Button("", "#3FB950")));
            Assert.AreEqual("", ButtonBarStrip.LineNumberColor(Button("")));
        }
    }
}
