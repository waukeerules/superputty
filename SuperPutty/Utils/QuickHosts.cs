using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The quick connect bar's Host box as a list, as SuperXTerminal's quickHosts.ts (its 43e4ba1, asked for in
    /// SilverGreen93/superputty#21): several hosts typed or pasted there open a tab each with the same Login, Password
    /// and Profile, for a handful of servers to run a command on and then forget (nothing is saved). The hosts are
    /// separated by commas, semicolons, spaces or new lines; a number standing alone after a host is still that host's
    /// port ("10.0.0.1 2222"), as the box has always taken it. Used by frmSuperPutty.TryConnectFromToolbar and its
    /// list of hosts (Gui.HostListPopup); each host is then read by HostConnectionString.
    /// </summary>
    public static class QuickHosts
    {
        /// <summary>More hosts than this are asked about before they're opened, as the Sessions tree's Connect all is</summary>
        public const int ManyHosts = 10;

        /// <summary>A port on its own: up to five digits</summary>
        private static readonly Regex PortOnly = new Regex(@"^\d{1,5}$");

        /// <summary>
        /// The hosts in what's typed in the Host box, in the order typed, each once (a host typed twice, in either case,
        /// opens one tab)
        /// </summary>
        /// <param name="text">The Host box's text; null for none</param>
        /// <returns>The hosts, each as "host", "host:port", "host port", "user@host" or "telnet://host"; none when empty</returns>
        public static List<string> Split(string text)
        {
            List<string> hosts = new List<string>();
            foreach (string part in Regex.Split(text ?? string.Empty, @"[\s,;]+"))
            {
                if (part.Length == 0)
                    continue;
                // "10.0.0.1 2222": the old way of giving a port, kept
                if (PortOnly.IsMatch(part) && hosts.Count > 0 && !hosts[hosts.Count - 1].Contains(" ") && !HasPort(hosts[hosts.Count - 1]))
                {
                    hosts[hosts.Count - 1] += " " + part;
                    continue;
                }
                hosts.Add(part);
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return hosts.Where(seen.Add).ToList();
        }

        /// <summary>Whether a host already names its port ("host:22", "[fe80::1]:22")</summary>
        /// <param name="host">A host as typed</param>
        /// <returns>true if it ends in a port after a colon</returns>
        private static bool HasPort(string host)
        {
            string address = host.Contains("://") ? host.Substring(host.IndexOf("://", StringComparison.Ordinal) + 3) : host;
            return Regex.IsMatch(address, @"^(\[[^\]]*\]|[^:\[\]]*):\d+$");
        }

        /// <summary>
        /// Pasted text as it goes into the Host box. The one-line box drops the line breaks of what's pasted, which
        /// would run a column of hosts together ("10.0.0.110.0.0.2"), so a paste of several lines goes in with commas
        /// between.
        /// </summary>
        /// <param name="text">The text pasted</param>
        /// <returns>The hosts with commas between; null for text of one line, which the box takes as it is</returns>
        public static string Pasted(string text)
        {
            return text != null && (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) ? string.Join(", ", Split(text)) : null;
        }

        /// <summary>The hosts typed, one on each line, for the list of hosts</summary>
        /// <param name="text">The Host box's text</param>
        /// <returns>The lines</returns>
        public static string AsLines(string text)
        {
            return string.Join(Environment.NewLine, Split(text));
        }
    }
}
