using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        /// <summary>The first of PuTTY's Special Command IDs (IDM_SPECIAL_MIN); each command is 0x10 after the last</summary>
        private const int IdmSpecialMin = 0x0400;

        /// <summary>The end of PuTTY's Special Command IDs (IDM_SPECIAL_MAX)</summary>
        private const int IdmSpecialMax = 0x0800;

        /// <summary>GetMenuItemInfo: read the item's type (MIIM_FTYPE)</summary>
        private const uint MiimFtype = 0x100;

        /// <summary>A menu item's type: a separator (MFT_SEPARATOR)</summary>
        private const uint MftSeparator = 0x800;

        /// <summary>GetMenuString: the item is given by its position (MF_BYPOSITION)</summary>
        private const uint MfByPosition = 0x400;

        /// <summary>One entry of PuTTY's Special Command menu</summary>
        public class SpecialCommand
        {
            /// <summary>Its text as PuTTY shows it, without the &amp; of an access key, e.g. "Break"; null for a separator</summary>
            public string Text { get; set; }

            /// <summary>The command to send PuTTY (WM_SYSCOMMAND); 0 for a separator or a submenu</summary>
            public int Command { get; set; }

            /// <summary>The commands of a submenu, such as SSH's signals; null for a command</summary>
            public List<SpecialCommand> Items { get; set; }

            /// <summary>Whether this is a separator line</summary>
            public bool IsSeparator
            {
                get { return this.Text == null; }
            }
        }

        /// <summary>The window's system menu</summary>
        /// <param name="window">The window</param>
        /// <param name="revert">false to get the window's own copy, which PuTTY has filled in</param>
        /// <returns>The menu, or IntPtr.Zero</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

        /// <summary>How many items a menu has</summary>
        /// <param name="menu">The menu</param>
        /// <returns>The count, or -1 on error</returns>
        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr menu);

        /// <summary>The submenu an item opens</summary>
        /// <param name="menu">The menu</param>
        /// <param name="position">The item's position</param>
        /// <returns>The submenu, or IntPtr.Zero if the item isn't one</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr menu, int position);

        /// <summary>An item's command ID</summary>
        /// <param name="menu">The menu</param>
        /// <param name="position">The item's position</param>
        /// <returns>The ID; 0xFFFFFFFF for a submenu</returns>
        [DllImport("user32.dll")]
        private static extern uint GetMenuItemID(IntPtr menu, int position);

        /// <summary>An item's text</summary>
        /// <param name="menu">The menu</param>
        /// <param name="item">The item's position (with MF_BYPOSITION)</param>
        /// <param name="text">Receives the text</param>
        /// <param name="max">The buffer's size</param>
        /// <param name="flags">MF_BYPOSITION</param>
        /// <returns>The text's length</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int max, uint flags);

        /// <summary>Part of a menu item's details, for <see cref="GetMenuItemInfo"/></summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MENUITEMINFO
        {
            public uint cbSize, fMask, fType, fState, wID;
            public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        /// <summary>Read a menu item's details</summary>
        /// <param name="menu">The menu</param>
        /// <param name="item">The item's position</param>
        /// <param name="byPosition">true: <paramref name="item"/> is a position</param>
        /// <param name="info">The fields to read (fMask) and their values</param>
        /// <returns>true on success</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);

        /// <summary>
        /// PuTTY's Special Command menu as it is now: the protocol's Break, Telnet's Are You There, SSH's signals and so
        /// on. PuTTY builds it from the connection's protocol (and only once connected), so it's read from PuTTY's
        /// window menu rather than listed here. It's found by its commands' IDs (IDM_SPECIAL_MIN..MAX), not its title,
        /// so KiTTY and translated builds work too. Used by the tab menu's Special Command (ctlPuttyPanel).
        /// </summary>
        /// <param name="puttyWindow">PuTTY's main window</param>
        /// <returns>The entries; empty when PuTTY has none (not connected, or a protocol without any)</returns>
        public static List<SpecialCommand> SpecialCommands(IntPtr puttyWindow)
        {
            IntPtr system = puttyWindow == IntPtr.Zero ? IntPtr.Zero : GetSystemMenu(puttyWindow, false);
            int count = system == IntPtr.Zero ? 0 : GetMenuItemCount(system);
            for (int i = 0; i < count; i++)
            {
                IntPtr sub = GetSubMenu(system, i);
                if (sub != IntPtr.Zero && HasSpecialCommand(sub))
                    return ReadMenu(sub);
            }
            return new List<SpecialCommand>();
        }

        /// <summary>Send one of PuTTY's Special Commands, e.g. a serial or Telnet Break</summary>
        /// <param name="puttyWindow">PuTTY's main window</param>
        /// <param name="command">The entry's <see cref="SpecialCommand.Command"/></param>
        public static void SendSpecialCommand(IntPtr puttyWindow, int command)
        {
            // posted, as for Restart: PuTTY runs it from its own message loop
            NativeMethods.PostMessagePtr(puttyWindow, (uint)NativeMethods.WM.SYSCOMMAND, (IntPtr)command, IntPtr.Zero);
        }

        /// <summary>Whether a menu, or a submenu of it, has a command in PuTTY's Special Command range</summary>
        /// <param name="menu">The menu</param>
        /// <returns>true if it's the Special Command menu</returns>
        private static bool HasSpecialCommand(IntPtr menu)
        {
            int count = GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                IntPtr sub = GetSubMenu(menu, i);
                if (sub != IntPtr.Zero ? HasSpecialCommand(sub) : IsSpecial(GetMenuItemID(menu, i)))
                    return true;
            }
            return false;
        }

        /// <summary>Whether a command ID is one of PuTTY's Special Commands</summary>
        /// <param name="id">The ID</param>
        /// <returns>true if it is</returns>
        private static bool IsSpecial(uint id)
        {
            return id >= IdmSpecialMin && id < IdmSpecialMax;
        }

        /// <summary>Read a menu's items: their text, command and submenus</summary>
        /// <param name="menu">The menu</param>
        /// <returns>The items, in order</returns>
        private static List<SpecialCommand> ReadMenu(IntPtr menu)
        {
            List<SpecialCommand> items = new List<SpecialCommand>();
            int count = GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                MENUITEMINFO info = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO)), fMask = MiimFtype };
                if (GetMenuItemInfo(menu, (uint)i, true, ref info) && (info.fType & MftSeparator) != 0)
                {
                    items.Add(new SpecialCommand());
                    continue;
                }
                StringBuilder text = new StringBuilder(256);
                GetMenuString(menu, (uint)i, text, text.Capacity, MfByPosition);
                // && is a literal ampersand; a single & marks the access key
                string label = text.ToString().Replace("&&", "\0").Replace("&", "").Replace("\0", "&");
                IntPtr sub = GetSubMenu(menu, i);
                if (sub != IntPtr.Zero)
                {
                    items.Add(new SpecialCommand { Text = label, Items = ReadMenu(sub) });
                }
                else
                {
                    uint id = GetMenuItemID(menu, i);
                    if (IsSpecial(id))
                        items.Add(new SpecialCommand { Text = label, Command = (int)id });
                }
            }
            return items;
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
