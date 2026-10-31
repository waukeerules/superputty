using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using log4net;
using Newtonsoft.Json;

namespace SuperPutty.Data
{
    /// <summary>
    /// The current user's favourite sessions and recently opened sessions, shown as the Favorites and Recent groups at
    /// the top of the Sessions tree (SessionTreeview). Both are lists of session IDs, kept in SessionShortcuts.json
    /// beside the user's SuperXPuTTY.settings rather than in Sessions.xml, so starring a session in a shared settings
    /// folder doesn't change it for the whole team. SuperPuTTY.OpenProtoSession records recent sessions; the tree
    /// adds and removes favourites and follows renames.
    /// </summary>
    public static class SessionShortcuts
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionShortcuts));

        /// <summary>How many recently opened sessions are kept</summary>
        public const int RecentCount = 10;

        /// <summary>The file's name, in <see cref="SuperPuTTY.UserSettingsFolder"/></summary>
        private const string FileName = "SessionShortcuts.json";

        /// <summary>What's stored in the file</summary>
        private class Store
        {
            /// <summary>Favourite session IDs, in the order they were added</summary>
            public List<string> Favorites { get; set; } = new List<string>();

            /// <summary>Recently opened session IDs, most recent first</summary>
            public List<string> Recent { get; set; } = new List<string>();
        }

        /// <summary>The loaded lists; null until first used</summary>
        private static Store store;

        /// <summary>Raised after either list changes, so the Sessions tree can redraw its groups</summary>
        public static event EventHandler Changed;

        /// <summary>The file's full path; set it to read and write elsewhere (tests)</summary>
        public static string FilePath { get; set; }

        /// <summary>Favourite session IDs, in the order they were added</summary>
        public static IList<string> Favorites
        {
            get { return Data.Favorites.AsReadOnly(); }
        }

        /// <summary>Recently opened session IDs, most recent first</summary>
        public static IList<string> Recent
        {
            get { return Data.Recent.AsReadOnly(); }
        }

        /// <summary>Whether a session is a favourite</summary>
        /// <param name="sessionId">The session's ID</param>
        /// <returns>true if it's in Favorites</returns>
        public static bool IsFavorite(string sessionId)
        {
            return sessionId != null && Data.Favorites.Contains(sessionId);
        }

        /// <summary>Add a session to Favorites, if it isn't there already</summary>
        /// <param name="sessionId">The session's ID</param>
        public static void AddFavorite(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && !Data.Favorites.Contains(sessionId))
            {
                Data.Favorites.Add(sessionId);
                SaveAndNotify();
            }
        }

        /// <summary>Remove a session from Favorites</summary>
        /// <param name="sessionId">The session's ID</param>
        public static void RemoveFavorite(string sessionId)
        {
            if (Data.Favorites.Remove(sessionId))
            {
                SaveAndNotify();
            }
        }

        /// <summary>Record that a session was opened, putting it first in Recent</summary>
        /// <param name="sessionId">The session's ID</param>
        public static void AddRecent(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;
            List<string> recent = PushRecent(Data.Recent, sessionId, RecentCount);
            if (!recent.SequenceEqual(Data.Recent))
            {
                Data.Recent = recent;
                SaveAndNotify();
            }
        }

        /// <summary>Remove one session from Recent</summary>
        /// <param name="sessionId">The session's ID</param>
        public static void RemoveRecent(string sessionId)
        {
            if (Data.Recent.Remove(sessionId))
            {
                SaveAndNotify();
            }
        }

        /// <summary>Empty Recent</summary>
        public static void ClearRecent()
        {
            if (Data.Recent.Count > 0)
            {
                Data.Recent.Clear();
                SaveAndNotify();
            }
        }

        /// <summary>Forget a deleted session in both lists</summary>
        /// <param name="sessionId">The deleted session's ID</param>
        public static void Forget(string sessionId)
        {
            bool removed = Data.Favorites.Remove(sessionId);
            removed |= Data.Recent.Remove(sessionId);
            if (removed)
            {
                SaveAndNotify();
            }
        }

        /// <summary>Follow a session that was renamed or moved to another folder, so it stays a favourite</summary>
        /// <param name="oldId">Its ID before</param>
        /// <param name="newId">Its ID now</param>
        public static void Rename(string oldId, string newId)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || oldId == newId)
                return;
            bool changed = ReplaceId(Data.Favorites, oldId, newId);
            changed |= ReplaceId(Data.Recent, oldId, newId);
            if (changed)
            {
                SaveAndNotify();
            }
        }

        /// <summary>Put an ID first in a recent list, without duplicates, keeping at most <paramref name="max"/></summary>
        /// <param name="recent">The list, most recent first</param>
        /// <param name="sessionId">The ID just opened</param>
        /// <param name="max">The most to keep</param>
        /// <returns>The new list; the input isn't changed</returns>
        public static List<string> PushRecent(IEnumerable<string> recent, string sessionId, int max)
        {
            return new[] { sessionId }.Concat(recent.Where(id => id != sessionId)).Take(max).ToList();
        }

        /// <summary>Replace an ID in a list, in place; if the new ID is already there, just drop the old one</summary>
        /// <param name="ids">The list</param>
        /// <param name="oldId">The ID to replace</param>
        /// <param name="newId">Its replacement</param>
        /// <returns>true if the list changed</returns>
        public static bool ReplaceId(List<string> ids, string oldId, string newId)
        {
            int index = ids.IndexOf(oldId);
            if (index < 0)
                return false;
            if (ids.Contains(newId))
                ids.RemoveAt(index);
            else
                ids[index] = newId;
            return true;
        }

        /// <summary>Read the file again, e.g. after the settings folder changed; raises <see cref="Changed"/></summary>
        public static void Reload()
        {
            store = null;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The lists, read from the file on first use; empty if it's missing or unreadable</summary>
        private static Store Data
        {
            get
            {
                if (store == null)
                {
                    store = new Store();
                    string path = Path;
                    try
                    {
                        if (File.Exists(path))
                        {
                            store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(path)) ?? new Store();
                            store.Favorites = store.Favorites ?? new List<string>();
                            store.Recent = store.Recent ?? new List<string>();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Could not read " + path, ex);
                    }
                }
                return store;
            }
        }

        /// <summary>Where the lists are kept: <see cref="FilePath"/>, or SessionShortcuts.json beside the user's settings</summary>
        private static string Path
        {
            get { return FilePath ?? System.IO.Path.Combine(SuperPuTTY.UserSettingsFolder, FileName); }
        }

        /// <summary>Write the lists and tell the Sessions tree</summary>
        private static void SaveAndNotify()
        {
            string path = Path;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(Data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save " + path, ex);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
