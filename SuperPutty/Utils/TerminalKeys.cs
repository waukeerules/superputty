using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Turns SPSL's SENDKEY keys into what a terminal sends, and characters into keys. SENDKEY (Scripting/SendKeyCommand)
    /// asks <see cref="FromCharacter"/> which key types a character such as "]" or "."; it used to take the character's
    /// code as a key code, so "." pressed Delete and "^]" pressed Ctrl+Menu. The built-in terminal
    /// (TerminalControl.KeySequence) and PuTTY windows (CommandData.SendToTerminal, for keys with Ctrl, Alt or Shift,
    /// whose state PuTTY can't see in a posted key press) type <see cref="Sequence"/>: xterm's control characters and
    /// escape sequences, as PuTTY itself would send them.
    /// </summary>
    public static class TerminalKeys
    {
        /// <summary>
        /// The keys offered by Send Key, in the tab menu (ctlPuttyPanel) and the built-in terminal's menu (TerminalControl
        /// passes them to the page): each is the key as SENDKEY writes it, its label, and what it's for
        /// </summary>
        public static readonly string[][] MenuKeys =
        {
            new[] { "^C", "Ctrl+C", "interrupt" },
            new[] { "^Z", "Ctrl+Z", "end, leave config" },
            new[] { "^6", "Ctrl+Shift+6", "Cisco escape" },
            new[] { "^]", "Ctrl+]", "Telnet escape" },
            new[] { "^D", "Ctrl+D", "end of input" },
            new[] { "^\\", "Ctrl+\\", "quit" },
            new[] { "ESC", "Esc", "" }
        };

        /// <summary>Which key types a character on the current keyboard layout, and the Shift, Ctrl or Alt it needs</summary>
        /// <param name="ch">The character</param>
        /// <returns>The key in the low byte; in the high byte 1 for Shift, 2 for Ctrl, 4 for Alt; -1 if no key types it</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "VkKeyScanW")]
        private static extern short VkKeyScan(char ch);

        /// <summary>The character a key types with the given keyboard state, on the current keyboard layout</summary>
        /// <param name="virtualKey">The key</param>
        /// <param name="scanCode">Its scan code</param>
        /// <param name="keyState">256 bytes, one per key; 0x80 means pressed</param>
        /// <param name="buffer">Receives the characters</param>
        /// <param name="bufferSize">The buffer's size</param>
        /// <param name="flags">4: don't change the keyboard's dead-key state</param>
        /// <returns>The number of characters; 0 for none, negative for a dead key</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicode(uint virtualKey, uint scanCode, byte[] keyState, StringBuilder buffer, int bufferSize, uint flags);

        /// <summary>Translate a key code and scan code (MapVirtualKey's MAPVK_VK_TO_VSC)</summary>
        /// <param name="code">The key code</param>
        /// <param name="mapType">0: key code to scan code</param>
        /// <returns>The scan code, or 0</returns>
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        /// <summary>
        /// The key, with Shift, Ctrl and Alt as needed, that types a character on this keyboard layout: "]" is the
        /// close-bracket key, "!" is Shift+1. Used by SENDKEY for single characters other than letters and digits.
        /// </summary>
        /// <param name="ch">The character</param>
        /// <returns>The key and modifiers, or -1 if no key on this layout types it</returns>
        public static int FromCharacter(char ch)
        {
            short scan = VkKeyScan(ch);
            if (scan == -1)
                return -1;
            int key = scan & 0xFF;
            int state = (scan >> 8) & 0xFF;
            if ((state & 1) != 0) key |= (int)Keys.Shift;
            if ((state & 2) != 0) key |= (int)Keys.Control;
            if ((state & 4) != 0) key |= (int)Keys.Alt;
            return key;
        }

        /// <summary>
        /// What a terminal sends for a key, as xterm (and PuTTY's default settings) do: Enter is CR, arrows are ESC [ A..D,
        /// Ctrl+letter and Ctrl+[ \ ] @ ^ _ are their control characters (Ctrl+] is 0x1D, Telnet's escape), a character
        /// key is its character, Alt puts ESC in front, and Shift, Alt or Ctrl with an arrow, Home, End or a function key
        /// is xterm's modified sequence (e.g. Ctrl+Up is ESC [ 1 ; 5 A)
        /// </summary>
        /// <param name="key">The key and modifiers</param>
        /// <returns>The characters to send; empty for keys with none (Caps Lock, Print Screen...)</returns>
        public static string Sequence(KeyEventArgs key)
        {
            Keys code = key.KeyCode;
            int modifier = 1 + (key.Shift ? 1 : 0) + (key.Alt ? 2 : 0) + (key.Control ? 4 : 0);
            string named = NamedKey(code, key.Shift, modifier);
            if (named != null)
            {
                if (key.Control && code == Keys.Space)
                    return (key.Alt ? "\x1b" : "") + "\0";
                if (key.Control && code == Keys.Back)
                    return (key.Alt ? "\x1b" : "") + "\b";
                // keys whose sequence has no modifier form take Alt as an ESC in front
                return key.Alt && named.Length <= 1 ? "\x1b" + named : named;
            }

            // Ctrl+Alt is AltGr on many layouts (e.g. @ on a German keyboard): if it types a character, that's the key
            if (key.Control && key.Alt)
            {
                string altGr = Character(code, key.Shift, true);
                if (altGr.Length > 0 && !char.IsControl(altGr[0]))
                    return altGr;
            }
            string text = Character(code, key.Shift, false);
            if (text.Length == 0)
                return string.Empty;
            if (key.Control)
            {
                char control = ControlCharacter(text[0]);
                if (control != '￿')
                    text = control.ToString();
            }
            return key.Alt ? "\x1b" + text : text;
        }

        /// <summary>The sequence for a key that isn't a character key, with xterm's modifier number where it has one</summary>
        /// <param name="code">The key, without modifiers</param>
        /// <param name="shift">Whether Shift is held, for Shift+Tab</param>
        /// <param name="modifier">xterm's modifier number: 1 plus 1 for Shift, 2 for Alt, 4 for Ctrl</param>
        /// <returns>The sequence, or null if it's a character key</returns>
        private static string NamedKey(Keys code, bool shift, int modifier)
        {
            switch (code)
            {
                case Keys.Enter: return "\r";
                case Keys.Tab: return shift ? "\x1b[Z" : "\t";
                case Keys.Escape: return "\x1b";
                case Keys.Back: return "\x7f";
                case Keys.Space: return " ";
                case Keys.Up: return Cursor('A', modifier);
                case Keys.Down: return Cursor('B', modifier);
                case Keys.Right: return Cursor('C', modifier);
                case Keys.Left: return Cursor('D', modifier);
                case Keys.Home: return Cursor('H', modifier);
                case Keys.End: return Cursor('F', modifier);
                case Keys.Insert: return Tilde(2, modifier);
                case Keys.Delete: return Tilde(3, modifier);
                case Keys.PageUp: return Tilde(5, modifier);
                case Keys.PageDown: return Tilde(6, modifier);
                case Keys.F1: return modifier > 1 ? "\x1b[1;" + modifier + "P" : "\x1bOP";
                case Keys.F2: return modifier > 1 ? "\x1b[1;" + modifier + "Q" : "\x1bOQ";
                case Keys.F3: return modifier > 1 ? "\x1b[1;" + modifier + "R" : "\x1bOR";
                case Keys.F4: return modifier > 1 ? "\x1b[1;" + modifier + "S" : "\x1bOS";
                case Keys.F5: return Tilde(15, modifier);
                case Keys.F6: return Tilde(17, modifier);
                case Keys.F7: return Tilde(18, modifier);
                case Keys.F8: return Tilde(19, modifier);
                case Keys.F9: return Tilde(20, modifier);
                case Keys.F10: return Tilde(21, modifier);
                case Keys.F11: return Tilde(23, modifier);
                case Keys.F12: return Tilde(24, modifier);
                // xterm sends F13-F16 as Shift+F1-F4
                case Keys.F13: return "\x1b[1;2P";
                case Keys.F14: return "\x1b[1;2Q";
                case Keys.F15: return "\x1b[1;2R";
                case Keys.F16: return "\x1b[1;2S";
                case Keys.ShiftKey:
                case Keys.ControlKey:
                case Keys.Menu:
                case Keys.Capital:
                case Keys.NumLock:
                case Keys.Scroll:
                case Keys.PrintScreen:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Apps:
                case Keys.Help:
                case Keys.Clear:
                    return string.Empty;
                case Keys.Cancel:
                    // Break (Ctrl+Break): PuTTY sends it as a Telnet or serial break, which a typed character can't be
                    return string.Empty;
                default:
                    return null;
            }
        }

        /// <summary>An arrow, Home or End: ESC [ letter, or ESC [ 1 ; modifier letter with Shift, Alt or Ctrl</summary>
        /// <param name="letter">A, B, C, D, H or F</param>
        /// <param name="modifier">xterm's modifier number; 1 for none</param>
        /// <returns>The sequence</returns>
        private static string Cursor(char letter, int modifier)
        {
            return modifier > 1 ? "\x1b[1;" + modifier + letter : "\x1b[" + letter;
        }

        /// <summary>An editing or function key: ESC [ number ~, or ESC [ number ; modifier ~</summary>
        /// <param name="number">The key's number, e.g. 3 for Delete</param>
        /// <param name="modifier">xterm's modifier number; 1 for none</param>
        /// <returns>The sequence</returns>
        private static string Tilde(int number, int modifier)
        {
            return "\x1b[" + number + (modifier > 1 ? ";" + modifier : "") + "~";
        }

        /// <summary>The character a key types on this keyboard layout</summary>
        /// <param name="code">The key, without modifiers</param>
        /// <param name="shift">Whether Shift is held</param>
        /// <param name="altGr">Whether Ctrl and Alt are held, as AltGr</param>
        /// <returns>The character, or empty if the key types none</returns>
        private static string Character(Keys code, bool shift, bool altGr)
        {
            byte[] state = new byte[256];
            if (shift) state[(int)Keys.ShiftKey] = 0x80;
            if (altGr)
            {
                state[(int)Keys.ControlKey] = 0x80;
                state[(int)Keys.Menu] = 0x80;
            }
            StringBuilder buffer = new StringBuilder(8);
            int count = ToUnicode((uint)code, MapVirtualKey((uint)code, 0), state, buffer, buffer.Capacity, 4);
            return count > 0 ? buffer.ToString(0, count) : string.Empty;
        }

        /// <summary>The control character Ctrl gives with a character, as in xterm: Ctrl+C is 0x03, Ctrl+] is 0x1D</summary>
        /// <param name="ch">The character the key types without Ctrl</param>
        /// <returns>The control character, or U+FFFF if Ctrl doesn't make one with it</returns>
        public static char ControlCharacter(char ch)
        {
            if (ch >= 'a' && ch <= 'z') return (char)(ch - 'a' + 1);
            if (ch >= 'A' && ch <= 'Z') return (char)(ch - 'A' + 1);
            switch (ch)
            {
                case '@': case '2': case ' ': return '\0';
                case '[': case '3': return '\x1b';
                case '\\': case '4': return '\x1c';
                case ']': case '5': return '\x1d';
                case '^': case '6': return '\x1e';
                case '_': case '-': case '/': case '7': return '\x1f';
                case '?': case '8': return '\x7f';
                default: return '￿';
            }
        }
    }
}
