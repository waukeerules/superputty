using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Turns a button bar "send string" into the <see cref="CommandData"/> steps that
    /// <see cref="CommandData.SendToTerminal"/> types into a terminal. Supports these escape codes:
    /// <list type="table">
    /// <item><term>\r</term><description>Enter (a key press, followed by a short pause like SPSL's SENDLINE)</description></item>
    /// <item><term>\n</term><description>line feed character (Ctrl+J)</description></item>
    /// <item><term>\t</term><description>Tab</description></item>
    /// <item><term>\b</term><description>Backspace</description></item>
    /// <item><term>\e</term><description>Escape</description></item>
    /// <item><term>\p</term><description>pause for one second</description></item>
    /// <item><term>\v</term><description>paste the clipboard text</description></item>
    /// <item><term>\\</term><description>a literal backslash</description></item>
    /// </list>
    /// A line break in the text also means Enter. Any other backslash sequence, and a trailing backslash, are sent as typed.
    /// </summary>
    public static class SendStringParser
    {
        /// <summary>How long to wait after Enter so the posted key is processed before more text arrives</summary>
        public static readonly TimeSpan EnterDelay = TimeSpan.FromMilliseconds(50);

        /// <summary>The pause inserted for \p</summary>
        public static readonly TimeSpan PauseDelay = TimeSpan.FromSeconds(1);

        /// <summary>The escape codes and what they do, for showing in the button editor</summary>
        public const string HelpText = @"\r Enter   \n line feed   \t Tab   \b Backspace   \e Escape   \p 1-second pause   \v paste clipboard   \\ backslash";

        /// <summary>Parse a send string into steps to send, in order</summary>
        /// <param name="text">The string to send, with escape codes</param>
        /// <param name="getClipboardText">Returns the clipboard text for \v. Only called if \v is used; must be called on the UI thread</param>
        /// <returns>The steps: runs of text, Enter key presses, and pauses. Empty for empty input</returns>
        public static List<CommandData> Parse(string text, Func<string> getClipboardText)
        {
            List<CommandData> steps = new List<CommandData>();
            if (string.IsNullOrEmpty(text))
                return steps;

            StringBuilder pending = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    continue;
                }
                if (c == '\n')
                {
                    // a line break typed in the editor means Enter, like \r
                    steps.Add(new CommandData(TakeText(pending), new KeyEventArgs(Keys.Enter), EnterDelay));
                    continue;
                }
                if (c != '\\' || i == text.Length - 1)
                {
                    pending.Append(c);
                    continue;
                }

                char code = text[++i];
                switch (code)
                {
                    case 'r':
                        // Enter goes as a key press, the way the Send Commands toolbar and SENDLINE send it
                        steps.Add(new CommandData(TakeText(pending), new KeyEventArgs(Keys.Enter), EnterDelay));
                        break;
                    case 'p':
                        FlushText(pending, steps);
                        steps.Add(new CommandData(null, null, PauseDelay));
                        break;
                    case 'v':
                        pending.Append(getClipboardText() ?? string.Empty);
                        break;
                    case 'n': pending.Append('\n'); break;
                    case 't': pending.Append('\t'); break;
                    case 'b': pending.Append('\b'); break;
                    case 'e': pending.Append('\x1b'); break;
                    case '\\': pending.Append('\\'); break;
                    default:
                        pending.Append('\\').Append(code);
                        break;
                }
            }
            FlushText(pending, steps);
            return steps;
        }

        /// <summary>Add any text collected so far as a step of its own</summary>
        /// <param name="pending">Text collected since the last step; cleared</param>
        /// <param name="steps">The step list to add to</param>
        private static void FlushText(StringBuilder pending, List<CommandData> steps)
        {
            string text = TakeText(pending);
            if (text != null)
            {
                steps.Add(new CommandData(text));
            }
        }

        /// <summary>Return the text collected so far and clear it</summary>
        /// <param name="pending">Text collected since the last step; cleared</param>
        /// <returns>The text, or null if there was none</returns>
        private static string TakeText(StringBuilder pending)
        {
            if (pending.Length == 0)
                return null;

            string text = pending.ToString();
            pending.Clear();
            return text;
        }
    }
}
