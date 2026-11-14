using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Why a session's connection failed, read from plink's or PuTTY's last words, and whether reconnecting by itself
    /// can help, as SuperXTerminal's sshFailure.ts (its commit 50be4ea) does for OpenSSH. A refused login isn't retried
    /// on its own (a wrong password tried again and again can lock the account), nor an unknown host, a host key that
    /// wasn't accepted, no algorithm in common, or too many login attempts; nothing answering, no route or a timeout
    /// are, with a hint. Reconnect attempts wait twice as long each time, up to two minutes (<see cref="ReconnectDelay"/>),
    /// so a switch's reload is ridden out. Used by TerminalControl.OnExited (the built-in terminal, from plink's output),
    /// ctlPuttyPanel.CheckConnection (PuTTY's error box and session log) and RdpControl (the delay only).
    /// </summary>
    public sealed class ConnectFailure
    {
        /// <summary>The kinds of failure told apart</summary>
        public enum FailureKind
        {
            /// <summary>Nothing recognised: an ordinary drop</summary>
            None,
            /// <summary>The username, password or key was refused</summary>
            Login,
            /// <summary>The server gave up after too many login attempts (often an agent offering many keys)</summary>
            TooManyAttempts,
            /// <summary>The host name doesn't resolve</summary>
            UnknownHost,
            /// <summary>The host key wasn't accepted (cancelled at the prompt, or changed)</summary>
            HostKey,
            /// <summary>No algorithm of one kind in common (an old device)</summary>
            Negotiate,
            /// <summary>Nothing listening on the port</summary>
            Refused,
            /// <summary>No route to the host</summary>
            Unreachable,
            /// <summary>The host didn't answer in time</summary>
            TimedOut
        }

        /// <summary>How many of the last lines are read: earlier ones are the session's own (a remote "Access denied")</summary>
        private const int LastLines = 8;

        /// <summary>The longest automatic delay, unless a longer one was chosen in Options</summary>
        private const int MaxDelaySeconds = 120;

        /// <summary>What went wrong</summary>
        public FailureKind Kind { get; private set; }

        /// <summary>The host that didn't resolve, the port that refused, or the kind of algorithm; null if none</summary>
        public string Detail { get; private set; }

        /// <summary>What the device offers, for <see cref="FailureKind.Negotiate"/>; null otherwise</summary>
        public string Offer { get; private set; }

        /// <summary>Whether reconnecting by itself can help: an ordinary drop, nothing answering, no route, a timeout</summary>
        public bool Reconnects
        {
            get
            {
                return this.Kind == FailureKind.None || this.Kind == FailureKind.Refused
                    || this.Kind == FailureKind.Unreachable || this.Kind == FailureKind.TimedOut;
            }
        }

        /// <summary>
        /// Read why a connection failed from the end of its output: plink's last lines in the built-in terminal, or
        /// PuTTY's error box text and the end of its session log
        /// </summary>
        /// <param name="output">The output as plain text (ConnectionProgress.PlainText); null is no output</param>
        /// <returns>The failure; <see cref="FailureKind.None"/> if nothing was recognised</returns>
        public static ConnectFailure Read(string output)
        {
            string[] lines = (output ?? string.Empty).Replace("\r", "\n").Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            string tail = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - LastLines)));

            // checked before a refused login, which a server may also report as it gives up
            if (Regex.IsMatch(tail, @"Too many authentication failures", RegexOptions.IgnoreCase))
                return new ConnectFailure { Kind = FailureKind.TooManyAttempts };
            Match negotiate = Regex.Match(tail, @"Couldn't agree a ([\w -]+?) algorithm \(available: ([^)]+)\)", RegexOptions.IgnoreCase);
            if (!negotiate.Success)
                negotiate = Regex.Match(tail, @"Couldn't agree a ([\w -]+?)(?: algorithm)?(?:\s*\(available: ([^)]+)\))?$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (negotiate.Success)
                return new ConnectFailure { Kind = FailureKind.Negotiate, Detail = negotiate.Groups[1].Value.Trim(), Offer = negotiate.Groups[2].Success ? negotiate.Groups[2].Value.Trim() : null };
            // plink's own words, each on a line of its own: a remote "% Access denied" isn't one
            if (Regex.IsMatch(tail, @"^(Access denied|.*No supported authentication methods available.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return new ConnectFailure { Kind = FailureKind.Login };
            if (Regex.IsMatch(tail, @"Host does not exist", RegexOptions.IgnoreCase))
                return new ConnectFailure { Kind = FailureKind.UnknownHost };
            if (Regex.IsMatch(tail, @"^Connection abandoned\.?$|Cannot confirm a host key in batch mode|POTENTIAL SECURITY BREACH", RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return new ConnectFailure { Kind = FailureKind.HostKey };
            if (Regex.IsMatch(tail, @"Network error: Connection refused", RegexOptions.IgnoreCase))
                return new ConnectFailure { Kind = FailureKind.Refused };
            if (Regex.IsMatch(tail, @"Network error: (Network is unreachable|No route to host)", RegexOptions.IgnoreCase))
                return new ConnectFailure { Kind = FailureKind.Unreachable };
            if (Regex.IsMatch(tail, @"Network error: Connection timed out", RegexOptions.IgnoreCase))
                return new ConnectFailure { Kind = FailureKind.TimedOut };
            return new ConnectFailure { Kind = FailureKind.None };
        }

        /// <summary>
        /// What went wrong and what to do, for the terminal's notice or the status bar; null for an ordinary drop. A
        /// failure that isn't retried says so.
        /// </summary>
        /// <param name="host">The session's host and port, e.g. "10.0.0.9 port 22"</param>
        /// <returns>The message, without a trailing instruction to reconnect</returns>
        public string Describe(string host)
        {
            string notRetried = " Not reconnecting by itself.";
            switch (this.Kind)
            {
                case FailureKind.Login:
                    return "Login failed: the username, password or key was refused. Check them in the session or its PuTTY profile."
                        + " Not reconnecting by itself, so a wrong password can't lock the account.";
                case FailureKind.TooManyAttempts:
                    return "The server stopped after too many login attempts: Pageant or the profile may offer many keys before the right one."
                        + notRetried;
                case FailureKind.UnknownHost:
                    return "The host name of " + host + " wasn't found: check the session's host." + notRetried;
                case FailureKind.HostKey:
                    return "The host key wasn't accepted, so the connection was abandoned." + notRetried;
                case FailureKind.Negotiate:
                    return "No " + this.Detail + " algorithm in common with the device" + (this.Offer != null ? " (it offers " + this.Offer + ")" : "")
                        + ": allow older algorithms in its PuTTY profile (Connection > SSH > Kex, Host keys or Cipher)." + notRetried;
                case FailureKind.Refused:
                    return "Nothing answered on " + host + ": it may be rebooting, have SSH turned off, or listen on another port.";
                case FailureKind.Unreachable:
                    return "No route to " + host + ": check the network or VPN.";
                case FailureKind.TimedOut:
                    return host + " didn't answer in time.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Seconds before an automatic reconnect: the delay chosen in Options at first, doubling each time up to two
        /// minutes, so a switch's reload is ridden out; a delay chosen above two minutes stays as chosen
        /// </summary>
        /// <param name="delaySeconds">Options -> Advanced -> Dropped sessions' delay</param>
        /// <param name="attempt">Attempts already made since the session last ran steadily (0 for the first)</param>
        /// <returns>The delay in seconds</returns>
        public static int ReconnectDelay(int delaySeconds, int attempt)
        {
            int delay = Math.Max(1, delaySeconds);
            if (delay >= MaxDelaySeconds)
                return delay;
            for (int i = 0; i < attempt && delay < MaxDelaySeconds; i++)
                delay *= 2;
            return Math.Min(delay, MaxDelaySeconds);
        }
    }
}
