using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using log4net;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Jump hosts (bastions): a session's JumpHost, or its folder's, makes PuTTY reach the host through another SSH
    /// server. PuTTY's command line has no jump-host option, so PuttyStartInfo adds <see cref="ProxyCommandArgs"/>:
    /// "-proxycmd" running plink (beside putty.exe) with "-nc %host:%port", which PuTTY fills in. plink runs with
    /// -batch, as there's no window to type a password into, so the bastion must accept a key: Pageant, or a key in
    /// its PuTTY profile. The jump host is a saved session's path (its host, port, username and profile are used,
    /// with its folders' defaults and placeholders resolved) or "user@host:port". Chains aren't followed.
    /// </summary>
    public static class JumpHosts
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(JumpHosts));

        /// <summary>"user@host:port", each part but the host optional</summary>
        private static readonly Regex HostSpec = new Regex(@"^(?:(?<user>[^@\s]+)@)?(?<host>[^@:\s]+)(?::(?<port>\d{1,5}))?$", RegexOptions.Compiled);

        /// <summary>Where a jump goes: the bastion's address, login and profile</summary>
        public class Target
        {
            /// <summary>Host name or address</summary>
            public string Host { get; set; }

            /// <summary>SSH port</summary>
            public int Port { get; set; } = 22;

            /// <summary>Login name; null for the profile's or PuTTY's own</summary>
            public string User { get; set; }

            /// <summary>PuTTY profile for plink (its key, agent settings); null for none</summary>
            public string Profile { get; set; }
        }

        /// <summary>Whether a protocol is a TCP connection PuTTY can make through a proxy command</summary>
        /// <param name="protocol">The session's protocol</param>
        /// <returns>true for SSH, Telnet, Rlogin and Raw</returns>
        public static bool Applies(ConnectionProtocol protocol)
        {
            return protocol == ConnectionProtocol.SSH || protocol == ConnectionProtocol.Telnet
                || protocol == ConnectionProtocol.Rlogin || protocol == ConnectionProtocol.Raw;
        }

        /// <summary>
        /// Work out a jump host: a saved session (by its ID, e.g. "Bastions/bastion1", or a Database session's path)
        /// or "user@host:port"
        /// </summary>
        /// <param name="spec">The JumpHost value</param>
        /// <param name="lookup">Finds a session by ID; normally SuperPuTTY.GetSessionById</param>
        /// <param name="from">The session being opened, so it can't jump through itself</param>
        /// <returns>The target, or null if it names nothing usable</returns>
        public static Target Resolve(string spec, Func<string, SessionData> lookup, SessionData from)
        {
            spec = (spec ?? string.Empty).Trim();
            if (spec.Length == 0)
                return null;
            SessionData session = lookup(spec) ?? lookup(DatabaseSessions.IdPrefix + "/" + spec.TrimStart('/'));
            if (session != null)
            {
                if (from != null && session.SessionId == from.SessionId)
                    return null;
                // its own folder defaults and placeholders, as when it's opened itself
                SessionData resolved = SessionVariables.Resolve(session);
                return new Target
                {
                    Host = resolved.Host,
                    Port = resolved.Port > 0 ? resolved.Port : 22,
                    User = string.IsNullOrEmpty(resolved.Username) ? null : resolved.Username,
                    Profile = FolderSettings.IsDefaultProfile(resolved.PuttySession) ? null : resolved.PuttySession
                };
            }
            Match match = HostSpec.Match(spec);
            if (!match.Success)
                return null;
            int port;
            return new Target
            {
                Host = match.Groups["host"].Value,
                Port = match.Groups["port"].Success && int.TryParse(match.Groups["port"].Value, out port) && port > 0 && port <= 65535 ? port : 22,
                User = match.Groups["user"].Success ? match.Groups["user"].Value : null
            };
        }

        /// <summary>
        /// The plink to run: the one chosen in Options (Paths, "plink / klink") if it exists, else plink.exe beside
        /// the PuTTY that's set there, or KiTTY's klink.exe beside KiTTY. klink takes the same options and reads
        /// KiTTY's saved sessions (in the registry, or the Sessions folder of a portable KiTTY), so with KiTTY it's
        /// preferred; with PuTTY, plink is.
        /// </summary>
        /// <returns>Its path, or null if none is there</returns>
        public static string PlinkPath()
        {
            try
            {
                string chosen = SuperPuTTY.Settings.PlinkExe;
                if (!string.IsNullOrEmpty(chosen))
                {
                    chosen = Environment.ExpandEnvironmentVariables(chosen);
                    if (File.Exists(chosen))
                        return chosen;
                    Log.WarnFormat("plink set in Options isn't there ({0}); looking beside PuTTY", chosen);
                }
                string putty = SuperPuTTY.Settings.PuttyExe;
                if (string.IsNullOrEmpty(putty))
                    return null;
                string folder = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(putty)) ?? string.Empty;
                string[] names = SuperPuTTY.IsKiTTY ? new[] { "klink.exe", "plink.exe" } : new[] { "plink.exe", "klink.exe" };
                return names.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// PuTTY's arguments to connect through a jump host: -proxycmd with plink reaching %host:%port. Two layers of
        /// quoting: PuTTY reads backslashes in a proxy command as escapes (so they're doubled), and the whole command
        /// is one quoted argument (so its quotes are escaped).
        /// </summary>
        /// <param name="target">The jump host</param>
        /// <param name="plink">plink.exe's path</param>
        /// <returns>e.g. -proxycmd "\"C:\\Program Files\\PuTTY\\plink.exe\" -batch -ssh -P 22 -l admin bastion -nc %host:%port"</returns>
        public static string ProxyCommandArgs(Target target, string plink)
        {
            string command = Quote(plink) + " -batch -ssh -P " + target.Port
                + (target.Profile == null ? "" : " -load " + Quote(target.Profile))
                + (target.User == null ? "" : " -l " + Quote(target.User))
                + " " + target.Host + " -nc %host:%port";
            command = command.Replace("\\", "\\\\");
            return "-proxycmd \"" + command.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Quote a value for plink's command line</summary>
        /// <param name="value">The value</param>
        /// <returns>The value in double quotes</returns>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", string.Empty) + "\"";
        }

        /// <summary>
        /// The arguments to add for a session's jump host, or "" when it has none, its protocol can't use one, or it
        /// can't be used (plink missing, the jump host unknown), which is logged and shown in the status bar
        /// </summary>
        /// <param name="session">The resolved session being opened</param>
        /// <returns>The arguments, with a trailing space, or ""</returns>
        public static string ArgsFor(SessionData session)
        {
            if (string.IsNullOrWhiteSpace(session.JumpHost) || !Applies(session.Proto))
                return string.Empty;
            Target target = Resolve(session.JumpHost, SuperPuTTY.GetSessionById, session);
            if (target == null || string.IsNullOrEmpty(target.Host))
            {
                Log.WarnFormat("Jump host \"{0}\" for {1} isn't a saved session or user@host:port; connecting directly", session.JumpHost, session.SessionId);
                SuperPuTTY.ReportStatus("Jump host \"{0}\" not found; connecting {1} directly", session.JumpHost, session.SessionName);
                return string.Empty;
            }
            string plink = PlinkPath();
            if (plink == null)
            {
                Log.WarnFormat("No plink.exe or klink.exe (Options, or beside PuTTY), so {0} can't use its jump host; connecting directly", session.SessionId);
                SuperPuTTY.ReportStatus("plink.exe or klink.exe wasn't found (Options > Paths, or beside PuTTY), so {0} connects directly, not through {1}", session.SessionName, session.JumpHost);
                return string.Empty;
            }
            return ProxyCommandArgs(target, plink) + " ";
        }
    }
}
