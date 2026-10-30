using System;
using System.Text;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Reads and drives the connection state of an embedded PuTTY window, for auto-reconnect
    /// (ctlPuttyPanel.CheckConnection). When a connection drops, PuTTY shows a modal "PuTTY Fatal Error" box with the
    /// reason; once that box is closed, PuTTY keeps its window open with " (inactive)" at the end of its title, and its
    /// Restart Session command reconnects in the same window, appending to the same session log. A clean exit (e.g.
    /// typing exit) closes PuTTY instead, which closes the tab as before. This relies on PuTTY's "Close window on
    /// exit" being "Only on clean exit" (its default); with "Always" a drop closes the tab too.
    /// </summary>
    public static class PuttyWindow
    {
        /// <summary>PuTTY's Restart Session system-menu command (IDM_RESTART); ignored unless the session is inactive</summary>
        private const int IdmRestart = 0x0040;

        /// <summary>A message box's text control</summary>
        private const int MessageBoxTextId = 0xFFFF;

        /// <summary>The ID of a message box's only OK button (an MB_OK box gives it IDCANCEL, so Esc works)</summary>
        private const int MessageBoxOkId = 2;

        /// <summary>Whether PuTTY's session has ended: its title ends with "(inactive)"</summary>
        /// <param name="puttyWindow">PuTTY's main window</param>
        /// <returns>true when the session is inactive</returns>
        public static bool IsInactive(IntPtr puttyWindow)
        {
            return TextOf(puttyWindow).EndsWith("(inactive)", StringComparison.Ordinal);
        }

        /// <summary>Find the "PuTTY Fatal Error" box PuTTY shows when its connection fails or drops</summary>
        /// <param name="puttyWindow">PuTTY's main window</param>
        /// <param name="message">The error, e.g. "Network error: Software caused connection abort"; null if no box</param>
        /// <returns>The box's window, or IntPtr.Zero if PuTTY isn't showing one</returns>
        public static IntPtr FindFatalErrorBox(IntPtr puttyWindow, out string message)
        {
            message = null;
            uint puttyProcess;
            NativeMethods.GetWindowThreadProcessId(puttyWindow, out puttyProcess);
            if (puttyProcess == 0)
                return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows((window, lParam) =>
            {
                uint process;
                NativeMethods.GetWindowThreadProcessId(window, out process);
                if (process == puttyProcess && ClassOf(window) == "#32770"
                    && TextOf(window).EndsWith("Fatal Error", StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
            {
                message = TextOf(NativeMethods.GetDlgItem(found, MessageBoxTextId)).Trim();
            }
            return found;
        }

        /// <summary>Close a fatal error box as if OK was clicked; PuTTY then marks its session inactive</summary>
        /// <param name="box">The box from <see cref="FindFatalErrorBox"/></param>
        public static void DismissFatalError(IntPtr box)
        {
            NativeMethods.PostMessagePtr(box, (uint)NativeMethods.WM.COMMAND, (IntPtr)MessageBoxOkId, IntPtr.Zero);
        }

        /// <summary>Send PuTTY's Restart Session command, which reconnects an inactive session in the same window</summary>
        /// <param name="puttyWindow">PuTTY's main window</param>
        public static void Restart(IntPtr puttyWindow)
        {
            // posted, not sent, and without focusing PuTTY: this runs for background tabs too
            NativeMethods.PostMessagePtr(puttyWindow, (uint)NativeMethods.WM.SYSCOMMAND, (IntPtr)IdmRestart, IntPtr.Zero);
        }

        /// <summary>A window's title or text; empty if it has none</summary>
        /// <param name="window">The window</param>
        /// <returns>The text</returns>
        private static string TextOf(IntPtr window)
        {
            if (window == IntPtr.Zero)
                return string.Empty;
            StringBuilder text = new StringBuilder(1024);
            NativeMethods.GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        /// <summary>A window's class name</summary>
        /// <param name="window">The window</param>
        /// <returns>The class name</returns>
        private static string ClassOf(IntPtr window)
        {
            StringBuilder name = new StringBuilder(256);
            NativeMethods.GetClassName(window, name, name.Capacity);
            return name.ToString();
        }
    }
}
