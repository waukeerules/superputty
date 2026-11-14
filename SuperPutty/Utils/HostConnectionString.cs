using System;
using SuperPutty.Data;
using log4net;
using DarkModeForms;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Helper class to parse host connection strings (e.g. ssh://localhost:2222)
    /// </summary>
    public class HostConnectionString
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(HostConnectionString));

        public HostConnectionString(string hostString)
        {
            int idx = hostString.IndexOf("://");
            string hostPort = hostString;
            if (idx != -1)
            {
                // ssh://localhost:2020, or ssh2://localhost:2020
                if (Enum.TryParse(hostString.Substring(0, idx), true, out ConnectionProtocol protocol))
                {
                    Protocol = protocol;
                }
                else
                {
                    Messenger.MessageBox("Unknown protocol: " + hostString.Substring(0, idx) + ". Will default to ssh.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Protocol = null;
                }
                hostPort = hostString.Substring(idx + 3);
            }

            // Firefox addes a '/'...
            hostPort = hostPort.TrimEnd('/');
            // "user@host": its own login, as quick connect's list of hosts takes it (QuickHosts)
            int at = hostPort.LastIndexOf('@');
            if (at > 0)
            {
                User = hostPort.Substring(0, at);
                hostPort = hostPort.Substring(at + 1);
            }
            int idxPort = hostPort.IndexOf(":");
            if (hostPort.StartsWith("[") && hostPort.IndexOf(']') > 0)
            {
                // "[fe80::1]:22": an IPv6 address in brackets, whose colons aren't a port's
                int close = hostPort.IndexOf(']');
                Host = hostPort.Substring(1, close - 1);
                if (hostPort.Length > close + 2 && hostPort[close + 1] == ':' && int.TryParse(hostPort.Substring(close + 2), out int v6Port))
                {
                    Port = v6Port;
                }
            }
            else if (idxPort != -1)
            {
                // localhost:2020
                if (int.TryParse(hostPort.Substring(idxPort + 1), out int port))
                {
                    Host = hostPort.Substring(0, idxPort);
                    Port = port;
                }
                else
                {
                    Host = hostPort;
                }

            }
            else
            {
                // Try port separated by space
                idxPort = hostPort.IndexOf(" ");
                if (idxPort != -1)
                {
                    // localhost 2020
                    if (int.TryParse(hostPort.Substring(idxPort + 1), out int port))
                    {
                        Host = hostPort.Substring(0, idxPort);
                        Port = port;
                    }
                    else
                    {
                        Host = hostPort;
                    }

                }
                else
                {
                    // localhost
                    Host = hostPort;
                }
            }


            log.InfoFormat(
                "Parsed[{0}]: proto={1}, host={2}, port={3}", 
                hostString, 
                this.Protocol.HasValue ? this.Protocol.ToString() : "", 
                this.Host, 
                this.Port.HasValue ? this.Port.ToString() : "");
        }

        public ConnectionProtocol? Protocol { get; set; }
        public string Host { get; set; }

        /// <summary>The login given before the host ("admin@10.0.0.7"); null when none was</summary>
        public string User { get; set; }
        public int? Port { get; set; }
    }
}
