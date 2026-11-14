using System;
using System.Text;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// When a tab counts as connected rather than still connecting, as SuperXTerminal decides it (its commits adb6fe1,
    /// c343e83, 721d6ce): once the far end has printed some text and the session is still running a second later. A
    /// running plink or PuTTY isn't enough, as it runs while it reaches the host; and on Windows the pseudo console
    /// prints codes of its own the moment plink starts (modes, a cursor position query, a cleared screen, the window
    /// title), and PuTTY's session log starts with a header line, so those don't count. A connection that fails at once
    /// prints its message and ends within the second, so it goes from connecting straight to dropped.
    /// Used by ctlPuttyPanel.CheckConnection (PuTTY tabs, reading the session log) and TerminalControl.OnOutput (the
    /// built-in terminal); RDP tabs are told by the control itself (RdpControl.OnConnected).
    /// </summary>
    public static class ConnectionProgress
    {
        /// <summary>How long a session must keep running after its first text to count as connected</summary>
        public static readonly TimeSpan AnsweredAfter = TimeSpan.FromSeconds(1);

        /// <summary>The start of the line PuTTY writes at the top of a session log (-sessionlog)</summary>
        private const string PuttyLogHeader = "=~=~=~=~=~=~=~=~=~=~=~=";

        /// <summary>
        /// Whether a session's connection is tracked as connecting: a remote session, not a local shell, nor a serial
        /// line, which has no far end to answer and may print nothing until spoken to
        /// </summary>
        /// <param name="proto">The session's protocol</param>
        /// <returns>true for SSH, Telnet, Rlogin, Raw and RDP</returns>
        public static bool IsTracked(ConnectionProtocol proto)
        {
            return proto == ConnectionProtocol.SSH || proto == ConnectionProtocol.Telnet || proto == ConnectionProtocol.Rlogin
                || proto == ConnectionProtocol.Raw || proto == ConnectionProtocol.RDP;
        }

        /// <summary>
        /// Whether output has any text in it: what's left once escape sequences (a window title included), control
        /// characters, spaces and PuTTY's session log header are taken out. A password prompt or a coloured prompt is
        /// text; the pseudo console's start-up codes are not.
        /// </summary>
        /// <param name="output">The output, as bytes from plink or the session log</param>
        /// <param name="length">How many bytes of <paramref name="output"/> count</param>
        /// <returns>true if anything printable is left</returns>
        public static bool HasText(byte[] output, int length)
        {
            if (output == null)
                return false;
            length = Math.Min(length, output.Length);
            int at = 0;
            while (at < length)
            {
                byte b = output[at];
                if (b == 0x1B)
                {
                    at = SkipEscape(output, length, at);
                }
                else if (b == (byte)'=' && StartsLine(output, at) && Matches(output, length, at, PuttyLogHeader))
                {
                    // PuTTY's "=~=~=~... PuTTY log <date> =~=~=~..." line, written when the log opens
                    while (at < length && output[at] != (byte)'\n')
                        at++;
                }
                else if (b <= 0x20 || b == 0x7F)
                {
                    at++;
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Output as plain text, for reading a failure's last words (ConnectFailure.Read): escape sequences and control
        /// characters taken out, line breaks kept, and a cursor move (which the pseudo console uses between lines)
        /// taken as a line break
        /// </summary>
        /// <param name="output">The output, as bytes from plink or the session log</param>
        /// <param name="length">How many bytes of <paramref name="output"/> count</param>
        /// <returns>The text, UTF-8 decoded</returns>
        public static string PlainText(byte[] output, int length)
        {
            if (output == null)
                return string.Empty;
            length = Math.Min(length, output.Length);
            System.IO.MemoryStream text = new System.IO.MemoryStream(length);
            int at = 0;
            while (at < length)
            {
                byte b = output[at];
                if (b == 0x1B)
                {
                    int end = SkipEscape(output, length, at);
                    byte final = output[end - 1];
                    if (end - at > 2 && output[at + 1] == (byte)'[' && (final == (byte)'H' || final == (byte)'f'))
                        text.WriteByte((byte)'\n');
                    at = end;
                    continue;
                }
                if ((b >= 0x20 && b != 0x7F) || b == (byte)'\n' || b == (byte)'\r' || b == (byte)'\t')
                    text.WriteByte(b);
                at++;
            }
            return Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
        }

        /// <summary>Whether a byte is at the start of a line</summary>
        /// <param name="output">The output</param>
        /// <param name="at">The byte's position</param>
        /// <returns>true at the start or after a CR or LF</returns>
        private static bool StartsLine(byte[] output, int at)
        {
            return at == 0 || output[at - 1] == (byte)'\n' || output[at - 1] == (byte)'\r';
        }

        /// <summary>Whether the output has the given ASCII text at a position</summary>
        /// <param name="output">The output</param>
        /// <param name="length">How many bytes count</param>
        /// <param name="at">Where to look</param>
        /// <param name="text">The text</param>
        /// <returns>true if it's there</returns>
        private static bool Matches(byte[] output, int length, int at, string text)
        {
            if (at + text.Length > length)
                return false;
            byte[] expected = Encoding.ASCII.GetBytes(text);
            for (int i = 0; i < expected.Length; i++)
            {
                if (output[at + i] != expected[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Skip one escape sequence: CSI (ESC [ ... final byte), OSC (ESC ] ... BEL or ESC \), or ESC and one more byte.
        /// One cut off at the end of the output is skipped to the end.
        /// </summary>
        /// <param name="output">The output</param>
        /// <param name="length">How many bytes count</param>
        /// <param name="at">The position of the ESC</param>
        /// <returns>The position after the sequence</returns>
        private static int SkipEscape(byte[] output, int length, int at)
        {
            if (at + 1 >= length)
                return length;
            byte kind = output[at + 1];
            if (kind == (byte)'[')
            {
                at += 2;
                while (at < length && (output[at] < 0x40 || output[at] > 0x7E))
                    at++;
                return Math.Min(length, at + 1);
            }
            if (kind == (byte)']' || kind == (byte)'P' || kind == (byte)'_' || kind == (byte)'^')
            {
                // OSC, DCS, APC, PM: to BEL or ST (ESC \)
                at += 2;
                while (at < length)
                {
                    if (output[at] == 0x07)
                        return at + 1;
                    if (output[at] == 0x1B && at + 1 < length && output[at + 1] == (byte)'\\')
                        return at + 2;
                    at++;
                }
                return length;
            }
            if (kind == (byte)'(' || kind == (byte)')' || kind == (byte)'#')
            {
                // character set or line size: one more byte
                return Math.Min(length, at + 3);
            }
            return at + 2;
        }

        /// <summary>Whether a connecting session counts as connected now</summary>
        /// <param name="textSeenAt">When its first text was seen; null if none yet</param>
        /// <param name="now">The time now</param>
        /// <returns>true once <see cref="AnsweredAfter"/> has passed since the text</returns>
        public static bool IsAnswered(DateTime? textSeenAt, DateTime now)
        {
            return textSeenAt.HasValue && now - textSeenAt.Value >= AnsweredAfter;
        }
    }
}
