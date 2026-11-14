using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SuperPutty.Utils;

namespace SuperPutty.Data
{
    /// <summary>
    /// Remote Desktop's .rdp files, as SuperXTerminal reads and writes them: <see cref="Write"/> makes one for a session
    /// with the options it connects with, for Connect external (PuttyStartInfo.StartStandalone), so mstsc gets the same
    /// display, redirections and gateway as the built-in client; <see cref="Read"/> makes a session of one, for
    /// File > Import > From .rdp files (frmSuperPutty).
    /// </summary>
    public static class RdpFile
    {
        /// <summary>Remote Desktop's own port, left out of an address</summary>
        private const int DefaultPort = 3389;

        /// <summary>
        /// A .rdp file's text for a session: its address and username, then its options (Tools > Options > RDP with
        /// the session's own) as the matching .rdp settings. No password: mstsc asks, or uses Windows' saved credentials.
        /// </summary>
        /// <param name="session">The session, with {key} placeholders already replaced</param>
        /// <param name="options">The options it connects with (RdpOptions.EffectiveFor)</param>
        /// <returns>The file's text, CRLF line ends</returns>
        public static string Write(SessionData session, RdpOptions options)
        {
            List<string> lines = new List<string>();
            string address = session.Port != 0 && session.Port != DefaultPort ? session.Host + ":" + session.Port : session.Host;
            lines.Add("full address:s:" + address);
            if (!string.IsNullOrEmpty(session.Username))
                lines.Add("username:s:" + session.Username);
            if (options.DisplayMode == "Fixed")
            {
                lines.Add("screen mode id:i:1");
                if (options.FixedWidth.HasValue && options.FixedHeight.HasValue)
                {
                    lines.Add("desktopwidth:i:" + options.FixedWidth.Value);
                    lines.Add("desktopheight:i:" + options.FixedHeight.Value);
                }
            }
            else
            {
                lines.Add("dynamic resolution:i:1");
            }
            AddFlag(lines, "smart sizing", options.SmartSizing);
            AddFlag(lines, "redirectclipboard", options.RedirectClipboard);
            AddFlag(lines, "redirectprinters", options.RedirectPrinters);
            AddFlag(lines, "redirectsmartcards", options.RedirectSmartCards);
            AddFlag(lines, "audiocapturemode", options.RedirectMicrophone);
            AddFlag(lines, "administrative session", options.ConnectToConsole);
            if (options.RedirectDrives.HasValue)
                lines.Add("drivestoredirect:s:" + (options.RedirectDrives.Value ? "*" : ""));
            AddNumber(lines, "session bpp", options.ColorDepth);
            AddNumber(lines, "audiomode", options.AudioMode);
            AddNumber(lines, "keyboardhook", options.KeyboardHookMode);
            AddNumber(lines, "authentication level", options.AuthenticationLevel);
            if (!string.IsNullOrWhiteSpace(options.GatewayHost))
            {
                lines.Add("gatewayhostname:s:" + options.GatewayHost.Trim());
                lines.Add("gatewayusagemethod:i:1");
            }
            return string.Join("\r\n", lines) + "\r\n";
        }

        /// <summary>Add an on/off .rdp setting when the option is set</summary>
        /// <param name="lines">The file's lines</param>
        /// <param name="setting">The .rdp setting's name</param>
        /// <param name="value">The option; null leaves the setting out</param>
        private static void AddFlag(List<string> lines, string setting, bool? value)
        {
            if (value.HasValue)
                lines.Add(setting + ":i:" + (value.Value ? 1 : 0));
        }

        /// <summary>Add a number .rdp setting when the option is set</summary>
        /// <param name="lines">The file's lines</param>
        /// <param name="setting">The .rdp setting's name</param>
        /// <param name="value">The option; null leaves the setting out</param>
        private static void AddNumber(List<string> lines, string setting, int? value)
        {
            if (value.HasValue)
                lines.Add(setting + ":i:" + value.Value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A session from a .rdp file: named as the file, its host and port from <c>full address:s:</c> (host, host:port
        /// or [IPv6]:port), its username from <c>username:s:</c>; Remote Desktop's port when the address has none
        /// </summary>
        /// <param name="fileName">The file's path, for the name and the note</param>
        /// <param name="text">The file's text</param>
        /// <returns>The session, its ID its name (ImportSessions puts it in a folder); null when it has no address</returns>
        public static SessionData Read(string fileName, string text)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            SessionData session = new SessionData
            {
                SessionName = string.IsNullOrWhiteSpace(name) ? "rdp" : name.Trim(),
                Proto = ConnectionProtocol.RDP,
                Port = DefaultPort,
                ImageKey = "workstation",
                Note = "From " + Path.GetFileName(fileName)
            };
            session.SessionId = session.SessionName;
            foreach (string raw in (text ?? string.Empty).Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                int at = line.IndexOf(":s:", StringComparison.Ordinal);
                if (at < 0)
                    continue;
                string key = line.Substring(0, at).Trim().ToLowerInvariant();
                string value = line.Substring(at + 3).Trim();
                if (key == "full address")
                {
                    string host = value;
                    int port = DefaultPort;
                    if (value.StartsWith("[", StringComparison.Ordinal) && value.IndexOf(']') > 0)
                    {
                        int close = value.IndexOf(']');
                        host = value.Substring(1, close - 1);
                        int parsed;
                        if (value.Length > close + 2 && value[close + 1] == ':' && int.TryParse(value.Substring(close + 2), out parsed))
                            port = parsed;
                    }
                    else
                    {
                        int colon = value.LastIndexOf(':');
                        int parsed;
                        // one colon only: host:port; more is a bare IPv6 address
                        if (colon > 0 && value.IndexOf(':') == colon && int.TryParse(value.Substring(colon + 1), out parsed))
                        {
                            host = value.Substring(0, colon);
                            port = parsed;
                        }
                    }
                    session.Host = host;
                    session.Port = port;
                }
                else if (key == "username")
                {
                    session.Username = value;
                }
            }
            return string.IsNullOrEmpty(session.Host) ? null : session;
        }
    }
}
