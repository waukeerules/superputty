using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using log4net;

namespace SuperPutty.Data
{
    /// <summary>One folder's own settings, inherited by the sessions and folders under it</summary>
    public class FolderSetting
    {
        /// <summary>The folder's path in its root, e.g. "Lab/Core"</summary>
        [XmlAttribute]
        public string Path { get; set; }

        /// <summary>The folder's environment colour, "#RRGGBB", e.g. red for production; empty for none</summary>
        [XmlAttribute]
        public string Color { get; set; }

        /// <summary>Whether it holds nothing, and can be dropped</summary>
        [XmlIgnore]
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(this.Color); }
        }

        /// <summary>A copy, so callers can change one without touching the stored one</summary>
        /// <returns>The copy</returns>
        public FolderSetting Clone()
        {
            return (FolderSetting)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// Settings kept per folder rather than per session (so far the environment colour), for each root of the Sessions
    /// tree. Saved sessions' folders are in Folders.xml beside Sessions.xml, so a team sharing the settings folder
    /// shares them; Private sessions' in PrivateFolders.xml beside PrivateSessions.xml; Database sessions' in the
    /// table's &lt;table&gt;_folders, given here by DatabaseSessions after each read. Sessions.xml itself is left as
    /// upstream reads it. SuperPuTTY.LoadSessions calls <see cref="Load"/>; SessionTreeview changes folders through
    /// <see cref="Set"/>, <see cref="Move"/> and <see cref="Remove"/>; EnvironmentColors reads <see cref="Along"/>.
    /// </summary>
    public static class FolderSettings
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FolderSettings));

        /// <summary>Which root a folder belongs to</summary>
        public enum Store
        {
            /// <summary>Saved sessions (Folders.xml)</summary>
            Shared,
            /// <summary>Private sessions (PrivateFolders.xml)</summary>
            Private,
            /// <summary>Database sessions (the table's _folders table)</summary>
            Database
        }

        /// <summary>The settings, by store and then by path (case-insensitive, as the tree's folder names are)</summary>
        private static readonly Dictionary<Store, Dictionary<string, FolderSetting>> stores = new Dictionary<Store, Dictionary<string, FolderSetting>>
        {
            { Store.Shared, new Dictionary<string, FolderSetting>(StringComparer.OrdinalIgnoreCase) },
            { Store.Private, new Dictionary<string, FolderSetting>(StringComparer.OrdinalIgnoreCase) },
            { Store.Database, new Dictionary<string, FolderSetting>(StringComparer.OrdinalIgnoreCase) }
        };

        /// <summary>Raised after any folder's settings change, so tabs and the tree can show the new colours</summary>
        public static event EventHandler Changed;

        /// <summary>Folders.xml's path; set it to use another (tests)</summary>
        public static string SharedFileName { get; set; }

        /// <summary>PrivateFolders.xml's path; set it to use another (tests)</summary>
        public static string PrivateFileName { get; set; }

        /// <summary>The file for a store</summary>
        /// <param name="store">Shared or Private</param>
        /// <returns>The path</returns>
        private static string FileFor(Store store)
        {
            if (store == Store.Shared)
                return SharedFileName ?? System.IO.Path.Combine(SuperPuTTY.Settings.SettingsFolder ?? string.Empty, "Folders.xml");
            return PrivateFileName ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SuperPuTTY.PrivateSessionsFileName) ?? string.Empty, "PrivateFolders.xml");
        }

        /// <summary>The store a session's folders are in</summary>
        /// <param name="session">The session</param>
        /// <returns>Its store</returns>
        public static Store StoreOf(SessionData session)
        {
            return session.IsDatabase ? Store.Database : session.IsPrivate ? Store.Private : Store.Shared;
        }

        /// <summary>Read Folders.xml and PrivateFolders.xml; a missing or unreadable file means no settings</summary>
        public static void Load()
        {
            foreach (Store store in new[] { Store.Shared, Store.Private })
            {
                stores[store].Clear();
                foreach (FolderSetting setting in ReadFile(FileFor(store)))
                {
                    if (!string.IsNullOrEmpty(setting.Path) && !setting.IsEmpty)
                        stores[store][Clean(setting.Path)] = setting;
                }
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Read a folders file</summary>
        /// <param name="file">The file</param>
        /// <returns>Its settings; empty if it's missing or can't be read</returns>
        private static List<FolderSetting> ReadFile(string file)
        {
            try
            {
                if (!File.Exists(file))
                    return new List<FolderSetting>();
                using (StreamReader reader = new StreamReader(file))
                {
                    return (List<FolderSetting>)new XmlSerializer(typeof(List<FolderSetting>), new XmlRootAttribute("Folders")).Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read folder settings from " + file, ex);
                return new List<FolderSetting>();
            }
        }

        /// <summary>Write a store's file; a store with no settings writes an empty list, so a teammate's copy empties too</summary>
        /// <param name="store">Shared or Private</param>
        private static void Save(Store store)
        {
            string file = FileFor(store);
            List<FolderSetting> list = stores[store].Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0 && !File.Exists(file))
                return;
            try
            {
                using (StreamWriter writer = new StreamWriter(file))
                {
                    new XmlSerializer(typeof(List<FolderSetting>), new XmlRootAttribute("Folders")).Serialize(writer, list);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not save folder settings to " + file, ex);
                SuperPuTTY.ReportStatus("Could not save folder settings: {0}", ex.Message);
            }
        }

        /// <summary>A path as stored: without leading or trailing /</summary>
        /// <param name="path">The path</param>
        /// <returns>The cleaned path</returns>
        private static string Clean(string path)
        {
            return (path ?? string.Empty).Trim('/');
        }

        /// <summary>A folder's own settings</summary>
        /// <param name="store">Its root</param>
        /// <param name="path">Its path, e.g. "Lab/Core"</param>
        /// <returns>A copy of its settings, or null if it has none</returns>
        public static FolderSetting Get(Store store, string path)
        {
            FolderSetting setting;
            return stores[store].TryGetValue(Clean(path), out setting) ? setting.Clone() : null;
        }

        /// <summary>The settings of a folder and the folders above it, nearest first, for inheriting</summary>
        /// <param name="store">The root</param>
        /// <param name="path">The folder's path, e.g. "Lab/Core/Access"; "" for the top</param>
        /// <returns>Those that have settings</returns>
        public static IEnumerable<FolderSetting> Along(Store store, string path)
        {
            string current = Clean(path);
            while (current.Length > 0)
            {
                FolderSetting setting;
                if (stores[store].TryGetValue(current, out setting))
                    yield return setting;
                int slash = current.LastIndexOf('/');
                current = slash < 0 ? string.Empty : current.Substring(0, slash);
            }
        }

        /// <summary>
        /// Set a Saved or Private folder's settings and save its file. Database folders are written to the table
        /// (DatabaseSessions.SetFolder), which then calls <see cref="ReplaceDatabase"/>.
        /// </summary>
        /// <param name="store">Shared or Private</param>
        /// <param name="setting">The folder and its settings; empty settings remove it</param>
        public static void Set(Store store, FolderSetting setting)
        {
            if (store == Store.Database)
                throw new InvalidOperationException("Database folders are set through DatabaseSessions.SetFolder");
            string path = Clean(setting.Path);
            if (setting.IsEmpty)
                stores[store].Remove(path);
            else
                stores[store][path] = new FolderSetting { Path = path, Color = setting.Color };
            Save(store);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// A folder moved or was renamed: its settings, and those of the folders under it, go with it. Between Saved and
        /// Private sessions they move from one file to the other. Database folders are moved by the table's own update.
        /// </summary>
        /// <param name="from">The store it was in</param>
        /// <param name="fromPath">Its old path</param>
        /// <param name="to">The store it's in now</param>
        /// <param name="toPath">Its new path</param>
        public static void Move(Store from, string fromPath, Store to, string toPath)
        {
            if (from == Store.Database || to == Store.Database)
                return;
            fromPath = Clean(fromPath);
            toPath = Clean(toPath);
            List<FolderSetting> moving = stores[from].Values.Where(f => Within(f.Path, fromPath)).ToList();
            if (moving.Count == 0)
                return;
            foreach (FolderSetting setting in moving)
            {
                stores[from].Remove(setting.Path);
                string newPath = toPath + setting.Path.Substring(fromPath.Length);
                stores[to][newPath] = new FolderSetting { Path = newPath, Color = setting.Color };
            }
            Save(from);
            if (to != from)
                Save(to);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>A folder was removed: drop its settings and those of the folders under it</summary>
        /// <param name="store">Shared or Private</param>
        /// <param name="path">Its path</param>
        public static void Remove(Store store, string path)
        {
            if (store == Store.Database)
                return;
            path = Clean(path);
            List<string> gone = stores[store].Keys.Where(p => Within(p, path)).ToList();
            if (gone.Count == 0)
                return;
            gone.ForEach(p => stores[store].Remove(p));
            Save(store);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Whether a path is a folder or one of the folders under it</summary>
        /// <param name="path">The path to test</param>
        /// <param name="folder">The folder</param>
        /// <returns>true for the folder itself and anything below it</returns>
        public static bool Within(string path, string folder)
        {
            return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
                || (folder.Length > 0 && path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Use the Database folders' settings as last read from the table (or its cache)</summary>
        /// <param name="settings">The settings</param>
        public static void ReplaceDatabase(IEnumerable<FolderSetting> settings)
        {
            stores[Store.Database].Clear();
            foreach (FolderSetting setting in settings ?? Enumerable.Empty<FolderSetting>())
            {
                if (!string.IsNullOrEmpty(setting.Path) && !setting.IsEmpty)
                    stores[Store.Database][Clean(setting.Path)] = setting;
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
