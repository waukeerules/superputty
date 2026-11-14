using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using log4net;
using Newtonsoft.Json;

namespace SuperPutty.Data
{
    /// <summary>
    /// A copy of the shared Sessions.xml and Folders.xml kept beside the user's own settings, so Saved sessions still
    /// show (and open) while the settings folder can't be reached: a laptop off the network, a VPN that dropped, a
    /// file server down. As SuperXTerminal's saved_cache. SuperPuTTY.LoadSessions keeps a copy after every good read
    /// of the files (<see cref="Keep"/>) and reads it when the folder is away (<see cref="TryLoad"/>); FolderSettings
    /// reads the folders' copy then. The copy belongs to one settings folder: another one chosen, and away, has none.
    /// </summary>
    public static class SavedSessionsCache
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SavedSessionsCache));

        /// <summary>The copy of Sessions.xml, in <see cref="SuperPuTTY.UserSettingsFolder"/></summary>
        public const string SessionsFileName = "SavedSessions.cache.xml";

        /// <summary>The copy of Folders.xml</summary>
        public const string FoldersFileName = "SavedFolders.cache.xml";

        /// <summary>Which settings folder the copy is of, and when it was taken</summary>
        public const string InfoFileName = "SavedSessions.cache.json";

        /// <summary>What <see cref="InfoFileName"/> holds</summary>
        private class Info
        {
            /// <summary>The settings folder the files were read from</summary>
            public string Folder { get; set; }

            /// <summary>When the copy was last brought up to date (UTC)</summary>
            public DateTime CachedAt { get; set; }
        }

        /// <summary>The folder the copy is kept in; set it to keep it elsewhere (tests)</summary>
        public static string Folder { get; set; }

        /// <summary>The folder the copy is kept in: <see cref="Folder"/>, or beside the user's settings</summary>
        private static string Dir
        {
            get { return Folder ?? SuperPuTTY.UserSettingsFolder; }
        }

        /// <summary>
        /// Keep a copy of the files just read from a settings folder. Each is copied only when it differs from the copy
        /// already kept, so an unchanged read writes nothing but the time. Never throws: the copy is a convenience.
        /// </summary>
        /// <param name="settingsFolder">The settings folder they were read from</param>
        /// <param name="sessionsFile">Its Sessions.xml, which was just read without errors</param>
        /// <param name="foldersFile">Its Folders.xml; may not exist</param>
        public static void Keep(string settingsFolder, string sessionsFile, string foldersFile)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                CopyIfDifferent(sessionsFile, Path.Combine(Dir, SessionsFileName));
                string foldersCopy = Path.Combine(Dir, FoldersFileName);
                if (File.Exists(foldersFile))
                    CopyIfDifferent(foldersFile, foldersCopy);
                else if (File.Exists(foldersCopy))
                    File.Delete(foldersCopy);
                File.WriteAllText(Path.Combine(Dir, InfoFileName),
                    JsonConvert.SerializeObject(new Info { Folder = settingsFolder, CachedAt = DateTime.UtcNow }, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not keep a copy of the saved sessions in " + Dir, ex);
            }
        }

        /// <summary>
        /// Read the copy kept of a settings folder's Sessions.xml, for while that folder can't be reached
        /// </summary>
        /// <param name="settingsFolder">The settings folder that's away</param>
        /// <param name="sessions">The sessions in the copy; empty when there's none of this folder</param>
        /// <param name="cachedAt">When the copy was taken (local time); null when there's none</param>
        /// <returns>true if there's a copy of this folder</returns>
        public static bool TryLoad(string settingsFolder, out List<SessionData> sessions, out DateTime? cachedAt)
        {
            sessions = new List<SessionData>();
            cachedAt = null;
            Info info = ReadInfo(settingsFolder);
            string copy = Path.Combine(Dir, SessionsFileName);
            if (info == null || !File.Exists(copy))
                return false;
            try
            {
                sessions = SessionData.LoadSessionsFromFile(copy);
                cachedAt = info.CachedAt.ToLocalTime();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the copy of the saved sessions, " + copy, ex);
                sessions = new List<SessionData>();
                return false;
            }
        }

        /// <summary>The copy of a settings folder's Folders.xml, for FolderSettings while the folder is away</summary>
        /// <param name="settingsFolder">The settings folder that's away</param>
        /// <returns>The copy's path; null when there's none of this folder</returns>
        public static string FoldersCopy(string settingsFolder)
        {
            string copy = Path.Combine(Dir, FoldersFileName);
            return ReadInfo(settingsFolder) != null && File.Exists(copy) ? copy : null;
        }

        /// <summary>The copy's info, if it's of this settings folder</summary>
        /// <param name="settingsFolder">The settings folder</param>
        /// <returns>The info, or null when there's no copy or it's of another folder</returns>
        private static Info ReadInfo(string settingsFolder)
        {
            string file = Path.Combine(Dir, InfoFileName);
            try
            {
                if (!File.Exists(file))
                    return null;
                Info info = JsonConvert.DeserializeObject<Info>(File.ReadAllText(file));
                return info != null && string.Equals(Normalize(info.Folder), Normalize(settingsFolder), StringComparison.OrdinalIgnoreCase) ? info : null;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read " + file, ex);
                return null;
            }
        }

        /// <summary>A folder path without a trailing separator, for comparing</summary>
        /// <param name="folder">The path</param>
        /// <returns>The path trimmed</returns>
        private static string Normalize(string folder)
        {
            return (folder ?? string.Empty).TrimEnd('\\', '/');
        }

        /// <summary>Copy a file over another unless they're already the same</summary>
        /// <param name="from">The file read</param>
        /// <param name="to">The copy</param>
        private static void CopyIfDifferent(string from, string to)
        {
            byte[] bytes = File.ReadAllBytes(from);
            if (File.Exists(to) && new FileInfo(to).Length == bytes.Length && File.ReadAllBytes(to).SequenceEqual(bytes))
                return;
            File.WriteAllBytes(to, bytes);
        }
    }
}
