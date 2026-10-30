using System;
using System.Text.RegularExpressions;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    public static partial class Commands
    {
        /// <summary>How long WAITFOR waits when no timeout is given</summary>
        private static readonly TimeSpan DefaultWaitForTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The WAITFOR command: wait until text appears in the session's output, e.g. "WAITFOR Password:", with an
        /// optional timeout in seconds first, e.g. "WAITFOR /t:10 Password:". The waiting itself is done by
        /// ctlPuttyPanel.WaitForOutput, which reads the session log; if the text doesn't appear in time the script stops.
        /// </summary>
        /// <param name="arg">The text to wait for, optionally starting with /t:seconds</param>
        /// <returns>A <seealso cref="CommandData"/> with <see cref="CommandData.WaitFor"/> set</returns>
        internal static CommandData WaitForHandler(string arg)
        {
            TimeSpan timeout = DefaultWaitForTimeout;
            string text = arg ?? string.Empty;

            Match match = Regex.Match(text, @"^/t:(\d+)\s+(.*)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                timeout = TimeSpan.FromSeconds(int.Parse(match.Groups[1].Value));
                text = match.Groups[2].Value;
            }
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("WAITFOR needs the text to wait for, e.g. WAITFOR Password:");
            }

            return new CommandData((string)null) { WaitFor = text, WaitTimeout = timeout };
        }
    }
}
