using System.Linq;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The key events an RDP tab's Send Key sends (RdpKeys.Events): fixed PC scan codes, pressed in order and released
    /// in reverse, extended keys flagged, as the Remote Desktop control's SendKeys takes them
    /// </summary>
    [TestFixture]
    public class RdpKeysTests
    {
        /// <summary>Ctrl+Alt+Delete: Ctrl, Alt, Delete (extended) down, then up in reverse</summary>
        [Test]
        public void CtrlAltDelete()
        {
            int[] codes;
            short[] ups;
            RdpKeys.Events(new[] { Keys.LControlKey, Keys.LMenu, Keys.Delete }, out codes, out ups);
            CollectionAssert.AreEqual(new[] { 0x1D, 0x38, 0x153, 0x153, 0x38, 0x1D }, codes);
            CollectionAssert.AreEqual(new short[] { 0, 0, 0, -1, -1, -1 }, ups);
        }

        /// <summary>The Windows key is an extended key (E0 5B)</summary>
        [Test]
        public void WindowsKeyIsExtended()
        {
            int[] codes;
            short[] ups;
            RdpKeys.Events(new[] { Keys.LWin, Keys.R }, out codes, out ups);
            CollectionAssert.AreEqual(new[] { 0x15B, 0x13, 0x13, 0x15B }, codes);
        }

        /// <summary>Every key in the menu has a scan code, so none of its items throws</summary>
        [Test]
        public void EveryMenuKeyHasScanCodes()
        {
            foreach (var key in RdpKeys.MenuKeys)
            {
                int[] codes;
                short[] ups;
                RdpKeys.Events(key.Value, out codes, out ups);
                Assert.AreEqual(key.Value.Length * 2, codes.Length, key.Key);
                Assert.IsTrue(codes.All(c => c > 0), key.Key);
            }
        }
    
        /// <summary>In SuperXPuTTY's full screen, the Windows keys and Alt+Tab / Alt+Esc go to an RDP tab; other keys don't</summary>
        [Test]
        public void WindowsKeysPassedOnInFullScreen()
        {
            Assert.IsTrue(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.LWin, false));
            Assert.IsTrue(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.RWin, false));
            Assert.IsTrue(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.Tab, true), "Alt+Tab");
            Assert.IsTrue(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.Escape, true), "Alt+Esc");
            Assert.IsFalse(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.Tab, false), "Tab alone");
            Assert.IsFalse(SuperPutty.Gui.RdpWindowsKeys.IsPassedOn(Keys.A, true), "Alt+A");
            Assert.AreEqual(0x15C, RdpKeys.ScanCodeOf(Keys.RWin), "right Windows key, extended");
            Assert.AreEqual(0, RdpKeys.ScanCodeOf(Keys.A), "not in the table");
        }
    }
}
