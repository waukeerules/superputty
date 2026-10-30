using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Web;
using log4net;
using System.Xml;
using System.IO;
using System.Linq;

namespace SuperPutty.Data
{
    /// <summary>Helper methods used mostly for importing settings and session data from other applications</summary>
    public class PuttyDataHelper
    {
        public static readonly ILog Log = LogManager.GetLogger(typeof(PuttyDataHelper));

        public const string SessionDefaultSettings = "Default Settings";
        public const string SessionEmptySettings = "";

        public static RegistryKey RootAppKey
        {
            get
            {
                RegistryKey key = SuperPuTTY.IsKiTTY
                    ? Registry.CurrentUser.OpenSubKey(@"Software\9bis.com\KiTTY\Sessions")
                    : Registry.CurrentUser.OpenSubKey(@"Software\SimonTatham\PuTTY\Sessions");
                return key;
            }
        }
        /// <summary>
        /// The folder KiTTY saves its sessions in, one file per session, when it doesn't use the registry: always for
        /// kitty_portable.exe, and for kitty.exe with "savemode=dir" in the kitty.ini next to it. Null for PuTTY, or
        /// for KiTTY saving to the registry.
        /// </summary>
        public static string KittySessionFolder
        {
            get
            {
                string exe = SuperPuTTY.Settings.PuttyExe;
                if (!SuperPuTTY.IsKiTTY || string.IsNullOrEmpty(exe))
                    return null;
                string exeFolder = Path.GetDirectoryName(exe);
                Dictionary<string, string> ini = ReadKittyIni(exeFolder);

                string saveMode;
                bool portable = Path.GetFileName(exe).IndexOf("portable", StringComparison.OrdinalIgnoreCase) >= 0;
                bool inFiles = ini.TryGetValue("savemode", out saveMode)
                    ? saveMode.Equals("dir", StringComparison.OrdinalIgnoreCase)
                    : portable;
                if (!inFiles)
                    return null;

                string configDir;
                if (!ini.TryGetValue("configdir", out configDir) || string.IsNullOrWhiteSpace(configDir))
                {
                    configDir = exeFolder;
                }
                return Path.Combine(Path.Combine(exeFolder, Environment.ExpandEnvironmentVariables(configDir.Trim())), "Sessions");
            }
        }

        /// <summary>
        /// The settings in the [KiTTY] section of the kitty.ini next to KiTTY's exe (savemode, configdir,
        /// fileextension, ...), with lower-case keys
        /// </summary>
        /// <param name="exeFolder">The folder kitty.exe or kitty_portable.exe is in</param>
        /// <returns>The settings; empty if there's no kitty.ini or it can't be read</returns>
        private static Dictionary<string, string> ReadKittyIni(string exeFolder)
        {
            Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(exeFolder ?? string.Empty, "kitty.ini");
            if (!File.Exists(path))
                return settings;
            try
            {
                string section = null;
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                    }
                    else if (section != null && section.Equals("KiTTY", StringComparison.OrdinalIgnoreCase)
                        && !line.StartsWith(";") && line.Contains("="))
                    {
                        int equals = line.IndexOf('=');
                        settings[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read " + path, ex);
            }
            return settings;
        }

        /// <summary>
        /// The files of KiTTY's saved sessions (see <see cref="KittySessionFolder"/>), with each session's name. File
        /// names are URL-encoded like registry key names ("Default%20Settings"), plus kitty.ini's fileextension if set.
        /// </summary>
        /// <returns>(name, file) pairs; empty if KiTTY isn't saving to files or the folder doesn't exist yet</returns>
        private static IEnumerable<KeyValuePair<string, string>> GetKittySessionFiles()
        {
            string folder = KittySessionFolder;
            if (folder == null || !Directory.Exists(folder))
                yield break;

            string extension;
            ReadKittyIni(Path.GetDirectoryName(SuperPuTTY.Settings.PuttyExe)).TryGetValue("fileextension", out extension);
            foreach (string file in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(extension))
                {
                    if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        continue;
                    name = name.Substring(0, name.Length - extension.Length);
                }
                yield return new KeyValuePair<string, string>(HttpUtility.UrlDecode(name), file);
            }
        }

        /// <summary>Where saved sessions are read from, for messages: KiTTY's sessions folder, or the registry key</summary>
        public static string SessionStoreDescription
        {
            get
            {
                return KittySessionFolder
                    ?? (SuperPuTTY.IsKiTTY ? @"HKEY_CURRENT_USER\Software\9bis.com\KiTTY\Sessions" : @"HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions");
            }
        }

        /// <summary>
        /// The names of PuTTY's or KiTTY's saved sessions, for the session profile lists: from KiTTY's sessions folder
        /// when it saves to files (portable KiTTY), otherwise from the registry. Read fresh on every call, so sessions
        /// saved in PuTTY while SuperPuTTY runs show up. Starts with the empty profile and always has "Default Settings".
        /// </summary>
        /// <returns>The names, decoded</returns>
        public static List<string> GetSessionNames()
        {
            List<string> names = new List<string> {SessionEmptySettings};
            if (KittySessionFolder != null)
            {
                names.AddRange(GetKittySessionFiles().Select(file => file.Key));
            }
            else
            {
                RegistryKey key = RootAppKey;
                if (key != null)
                {
                    string[] savedSessionNames = key.GetSubKeyNames();
                    names.AddRange(savedSessionNames.Select(HttpUtility.UrlDecode));
                }
            }

            if (!names.Contains(SessionDefaultSettings))
            {
                names.Insert(1, SessionDefaultSettings);
            }

            return names;
        }

        public static List<SessionData> GetAllSessionsFromPuTTY()
        {
            List<SessionData> sessions = new List<SessionData>();

            if (KittySessionFolder != null)
            {
                foreach (KeyValuePair<string, string> file in GetKittySessionFiles())
                {
                    SessionData session = ReadKittySessionFile(file.Key, file.Value);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                return sessions;
            }

            RegistryKey key = RootAppKey;
            if (key != null)
            {
                string[] savedSessionNames = key.GetSubKeyNames();
                foreach (string keyName in savedSessionNames)
                {
                    RegistryKey sessionKey = key.OpenSubKey(keyName);
                    if (sessionKey != null)
                    {
                        SessionData session = new SessionData
                        {
                            Host = (string) sessionKey.GetValue("HostName", ""),
                            Port = (int) sessionKey.GetValue("PortNumber", 22),
                            Proto =
                                (ConnectionProtocol)
                                    Enum.Parse(typeof (ConnectionProtocol),
                                        (string) sessionKey.GetValue("Protocol", "SSH"), true),
                            PuttySession = (string) sessionKey.GetValue("PuttySession", HttpUtility.UrlDecode(keyName)),
                            SessionName = HttpUtility.UrlDecode(keyName),
                            Username = (string) sessionKey.GetValue("UserName", "")
                        };
                        sessions.Add(session);
                    }
                }
            }

            return sessions;
        }

        /// <summary>
        /// Read a session saved by portable KiTTY. Each line of the file is "Name\Value\", like a registry value.
        /// </summary>
        /// <param name="name">The session's name</param>
        /// <param name="path">Its file</param>
        /// <returns>The session, or null if the file can't be read</returns>
        private static SessionData ReadKittySessionFile(string name, string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('\\');
                    if (split > 0)
                    {
                        values[line.Substring(0, split)] = line.Substring(split + 1).TrimEnd('\\');
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read KiTTY session " + path, ex);
                return null;
            }

            string host, port, protocol, user;
            values.TryGetValue("HostName", out host);
            values.TryGetValue("UserName", out user);
            int portNumber;
            ConnectionProtocol proto;
            return new SessionData
            {
                Host = host ?? string.Empty,
                Port = values.TryGetValue("PortNumber", out port) && int.TryParse(port, out portNumber) ? portNumber : 22,
                Proto = values.TryGetValue("Protocol", out protocol) && Enum.TryParse(protocol, true, out proto) ? proto : ConnectionProtocol.SSH,
                PuttySession = name,
                SessionName = name,
                Username = user ?? string.Empty
            };
        }

        public static List<SessionData> GetAllSessionsFromPuTTYCM(string fileExport)
        {
            List<SessionData> sessions = new List<SessionData>();

            if (fileExport == null || !File.Exists(fileExport))
            {
                return sessions;
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(fileExport);


            XmlNodeList connections = doc.DocumentElement.SelectNodes("//connection[@type='PuTTY']");
            foreach (XmlElement connection in connections)
            {
                List<string> folders = new List<string>();
                XmlElement node = connection.ParentNode as XmlElement;
                while (node != null && node.Name != "root")
                {
                    if (node.Name == "container" && node.GetAttribute("type") == "folder")
                    {
                        folders.Add(node.GetAttribute("name"));
                    }
                    node = node.ParentNode as XmlElement;
                }
                folders.Reverse();
                string parentPath = string.Join("/", folders.ToArray());

                XmlElement info = (XmlElement)connection.SelectSingleNode("connection_info");
                XmlElement login = (XmlElement)connection.SelectSingleNode("login");

                SessionData session = new SessionData
                {
                    SessionName = info.SelectSingleNode("name").InnerText,
                    Host = info.SelectSingleNode("host").InnerText,
                    Port = Convert.ToInt32(info.SelectSingleNode("port").InnerText),
                    Proto =
                        (ConnectionProtocol)
                            Enum.Parse(typeof (ConnectionProtocol), info.SelectSingleNode("protocol").InnerText),
                    PuttySession = info.SelectSingleNode("session").InnerText
                };
                session.SessionId = string.IsNullOrEmpty(parentPath) 
                    ? session.SessionName 
                    : SessionData.CombineSessionIds(parentPath, session.SessionName);
                session.Username = login.SelectSingleNode("login").InnerText;

                sessions.Add(session);
            }

            return sessions;
        }
    }
}
