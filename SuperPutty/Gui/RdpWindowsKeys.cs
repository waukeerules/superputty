using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using log4net;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The RDP option "Windows keys: only when full screen" (KeyboardHookMode 2) for SuperXPuTTY's own full screen. The
    /// Remote Desktop control only applies that mode to its own full-screen window, which an RDP tab never uses, so the
    /// Windows key and Alt+Tab stayed here even with SuperXPuTTY full screen. As SuperXTerminal does (its commits b954e96,
    /// cf85320), while SuperXPuTTY is full screen (frmSuperPutty.ToggleFullScreen calls <see cref="SetFullScreen"/>) a
    /// low-level keyboard hook passes the Windows keys, and Tab or Escape with Alt held (Alt+Tab, Alt+Esc), to the
    /// focused RDP tab (RdpControl.SendScanCode) instead of letting Windows act on them. Only then, and only when the
    /// focus is in an RDP tab with that option; everything else is left alone.
    /// </summary>
    public static class RdpWindowsKeys
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RdpWindowsKeys));

        /// <summary>The hook while SuperXPuTTY is full screen; IntPtr.Zero otherwise</summary>
        private static IntPtr hook = IntPtr.Zero;

        /// <summary>The hook's callback, kept in a field so it isn't collected while Windows still calls it</summary>
        private static NativeMethods.LowLevelKMProc callback;

        /// <summary>WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN and WM_SYSKEYUP</summary>
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

        /// <summary>KBDLLHOOKSTRUCT.flags: Alt was down</summary>
        private const int LLKHF_ALTDOWN = 0x20;

        /// <summary>
        /// SuperXPuTTY went full screen, or back: install the hook, or remove it. Called by frmSuperPutty.ToggleFullScreen.
        /// </summary>
        /// <param name="fullScreen">Whether it's now full screen</param>
        public static void SetFullScreen(bool fullScreen)
        {
            if (fullScreen && hook == IntPtr.Zero)
            {
                callback = HookProc;
                hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, callback, NativeMethods.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                {
                    Log.Warn("Could not install the keyboard hook for RDP tabs in full screen: error " + Marshal.GetLastWin32Error());
                }
            }
            else if (!fullScreen && hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Whether a key, with Alt held or not, is one Windows takes before an RDP tab sees it, so it should be passed on:
        /// the Windows keys, and Tab or Escape with Alt
        /// </summary>
        /// <param name="key">The virtual key</param>
        /// <param name="altDown">Whether Alt is held</param>
        /// <returns>true to pass it to the remote desktop</returns>
        public static bool IsPassedOn(Keys key, bool altDown)
        {
            return key == Keys.LWin || key == Keys.RWin || (altDown && (key == Keys.Tab || key == Keys.Escape));
        }

        /// <summary>
        /// The hook: a key Windows would take (<see cref="IsPassedOn"/>) is sent to the focused RDP tab, if it has
        /// "only when full screen", and kept from Windows; anything else goes on as usual. The key is sent after the hook
        /// returns, as low-level hooks must be quick.
        /// </summary>
        /// <param name="nCode">Negative: pass on without looking</param>
        /// <param name="wParam">The keyboard message</param>
        /// <param name="lParam">The KBDLLHOOKSTRUCT</param>
        /// <returns>1 to keep the key from Windows, else the next hook's result</returns>
        private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
                Keys key = (Keys)Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                if ((down || up) && IsPassedOn(key, (flags & LLKHF_ALTDOWN) != 0))
                {
                    RdpControl rdp = FocusedRdpTab();
                    if (rdp != null)
                    {
                        rdp.BeginInvoke(new Action(() => rdp.SendScanCode(key, up)));
                        return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        /// <summary>The RDP tab with the keyboard focus, if SuperXPuTTY is in front and it has "only when full screen"</summary>
        /// <returns>The tab's control, or null</returns>
        private static RdpControl FocusedRdpTab()
        {
            Form main = SuperPuTTY.MainForm;
            if (main == null || NativeMethods.GetForegroundWindow() != main.Handle)
                return null;
            for (Control c = Control.FromChildHandle(NativeMethods.GetFocus()); c != null; c = c.Parent)
            {
                RdpControl rdp = c as RdpControl;
                if (rdp != null)
                    return rdp.WindowsKeysOnlyInFullScreen && rdp.IsConnected ? rdp : null;
            }
            return null;
        }
    }
}
