using System;
using System.Globalization;
using System.Text;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The anti-idle string, as KiTTY's and SuperXTerminal's (its 17961a5, b92caa2): typed into a remote session after a
    /// quiet spell, so a device's or shell's own idle timeout (Cisco's exec-timeout, a shell's TMOUT), which ssh's
    /// keep-alives don't reach, doesn't log it out. Kept as KiTTY writes it in the antiIdle terminal preference
    /// (Options -> Terminal); <see cref="Keys"/> turns its escapes into the keys typed, and Gui.TerminalControl types
    /// them once nothing has been typed for antiIdleSeconds (<see cref="IsDue"/>). Not for local shells, which have no
    /// idle timeout to keep awake.
    /// </summary>
    public static class AntiIdle
    {
        /// <summary>Seconds without typing before the string is typed when none are set, as KiTTY's default</summary>
        public const int DefaultSeconds = 180;

        /// <summary>
        /// The keys an anti-idle string types, after KiTTY's SendKeyboardPlus: <c>\xHH</c> the character HH (hex),
        /// <c>\kHH</c> the key HH (a Windows virtual-key code; the ones that are characters, as \k08 backspace or \k1B
        /// Escape), <c>\r</c>, <c>\n</c>, <c>\t</c>, <c>\h</c> (backspace) and <c>\\</c>. A <c>\</c> at the end only ends
        /// the string (KiTTY's own examples end so, as <c>\x05\</c>), and any other is typed as it is. KiTTY's pauses
        /// (\p, \sNN) and Ctrl+Break (\c) have no place in a string sent while idle, so they're left out.
        /// </summary>
        /// <param name="text">The string as kept; null for none</param>
        /// <returns>What to type; "" for nothing</returns>
        public static string Keys(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            StringBuilder keys = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\')
                {
                    keys.Append(c);
                    continue;
                }
                if (i + 1 >= text.Length)
                    break;
                char next = text[i + 1];
                // two hex digits after \x or \k; HexNumber alone would also take spaces round them
                int hex = i + 3 < text.Length && Uri.IsHexDigit(text[i + 2]) && Uri.IsHexDigit(text[i + 3])
                    ? int.Parse(text.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : -1;
                if ((next == 'x' || next == 'k') && hex >= 0)
                {
                    keys.Append((char)hex);
                    i += 3;
                }
                else if (next == 'r' || next == 'n' || next == 't' || next == 'h' || next == '\\')
                {
                    keys.Append(next == 'r' ? '\r' : next == 'n' ? '\n' : next == 't' ? '\t' : next == 'h' ? '\b' : '\\');
                    i += 1;
                }
                else if (next == 'p' || next == 'c')
                {
                    i += 1;
                }
                else if (next == 's')
                {
                    i += i + 3 < text.Length && char.IsDigit(text[i + 2]) && char.IsDigit(text[i + 3]) ? 3 : 1;
                }
                else
                {
                    keys.Append(c);
                }
            }
            return keys.ToString();
        }

        /// <summary>Whether the string is due: something to type, and nothing typed for the seconds set</summary>
        /// <param name="keys">What <see cref="Keys"/> gave; "" never is</param>
        /// <param name="seconds">Seconds without typing; 0 or less for <see cref="DefaultSeconds"/></param>
        /// <param name="typedAt">When something was last typed into the session (or it started)</param>
        /// <param name="now">The time now</param>
        /// <returns>true to type it now</returns>
        public static bool IsDue(string keys, int seconds, DateTime typedAt, DateTime now)
        {
            return !string.IsNullOrEmpty(keys) && (now - typedAt).TotalSeconds >= (seconds > 0 ? seconds : DefaultSeconds);
        }
    }
}
