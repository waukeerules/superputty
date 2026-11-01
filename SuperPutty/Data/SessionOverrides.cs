using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using log4net;
using Newtonsoft.Json;

namespace SuperPutty.Data
{
    /// <summary>
    /// The current user's local overrides: their own host, port, username, PuTTY profile, extra arguments or jump
    /// host for a session, used instead of the stored value whenever it opens. For a Database session whose field is
    /// locked, or a shared Sessions.xml session, where an edit isn't possible or would change it for everyone. Kept in
    /// SessionOverrides.json beside the user's SuperXPuTTY.settings, by session ID, and never written into the session
    /// itself. Utils.SessionVariables.Resolve applies them (<see cref="Apply"/>), after the folders' defaults, so every
    /// launch gets them; Gui.dlgSessionOverrides edits them, from the Sessions tree's "Local overrides...".
    /// SessionShortcuts.Rename and Forget pass on renames and deletions.
    /// </summary>
    public static class SessionOverrides
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionOverrides));

        /// <summary>The file's name, in <see cref="SuperPuTTY.UserSettingsFolder"/></summary>
        private const string FileName = "SessionOverrides.json";

        /// <summary>One session's overrides; a null field (or a port of 0) isn't overridden</summary>
        public class Override
        {
            /// <summary>The host to connect to instead</summary>
            public string Host { get; set; }

            /// <summary>The port instead; 0 for the session's own</summary>
            public int Port { get; set; }

            /// <summary>The username instead</summary>
            public string Username { get; set; }

            /// <summary>The PuTTY profile instead</summary>
            public string PuttyProfile { get; set; }

            /// <summary>The extra arguments instead</summary>
            public string ExtraArgs { get; set; }

            /// <summary>The jump host instead ("" for none, overriding the session's or folder's)</summary>
            public string JumpHost { get; set; }

            /// <summary>Whether nothing is overridden, so the entry can be dropped</summary>
            [JsonIgnore]
            public bool IsEmpty
            {
                get { return this.Host == null && this.Port <= 0 && this.Username == null && this.PuttyProfile == null && this.ExtraArgs == null && this.JumpHost == null; }
            }

            /// <summary>The names of the fields overridden, for the tree's tooltip</summary>
            /// <returns>e.g. ["Host", "Username"]</returns>
            public List<string> Fields()
            {
                List<string> fields = new List<string>();
                if (this.Host != null) fields.Add("Host");
                if (this.Port > 0) fields.Add("Port");
                if (this.Username != null) fields.Add("Username");
                if (this.PuttyProfile != null) fields.Add("PuTTY profile");
                if (this.ExtraArgs != null) fields.Add("Extra args");
                if (this.JumpHost != null) fields.Add("Jump host");
                return fields;
            }

            /// <summary>A copy, for the dialog to edit</summary>
            /// <returns>The copy</returns>
            public Override Clone()
            {
                return (Override)this.MemberwiseClone();
            }
        }

        /// <summary>The overrides by session ID; null until first used</summary>
        private static Dictionary<string, Override> store;

        /// <summary>Raised after an override is set or removed, so the Sessions tree can update tooltips</summary>
        public static event EventHandler Changed;

        /// <summary>The file's full path; set it to read and write elsewhere (tests)</summary>
        public static string FilePath { get; set; }

        /// <summary>A session's overrides</summary>
        /// <param name="sessionId">The session's ID</param>
        /// <returns>A copy of them, or null when it has none</returns>
        public static Override Get(string sessionId)
        {
            Override found;
            return sessionId != null && Data.TryGetValue(sessionId, out found) ? found.Clone() : null;
        }

        /// <summary>Set a session's overrides, or remove them when there are none; saves the file</summary>
        /// <param name="sessionId">The session's ID</param>
        /// <param name="value">The overrides; null or empty removes them</param>
        public static void Set(string sessionId, Override value)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;
            if (value == null || value.IsEmpty)
                Data.Remove(sessionId);
            else
                Data[sessionId] = value.Clone();
            SaveAndNotify();
        }

        /// <summary>
        /// Put a session's overrides into a copy that's about to be launched. Called by SessionVariables.Resolve after
        /// the folders' defaults, so an override beats both the session's value and its folder's.
        /// </summary>
        /// <param name="copy">The launch copy (never the stored session)</param>
        /// <returns>The same copy</returns>
        public static SessionData Apply(SessionData copy)
        {
            Override value;
            if (copy?.SessionId == null || !Data.TryGetValue(copy.SessionId, out value))
                return copy;
            if (value.Host != null) copy.Host = value.Host;
            if (value.Port > 0) copy.Port = value.Port;
            if (value.Username != null) copy.Username = value.Username;
            if (value.PuttyProfile != null) copy.PuttySession = value.PuttyProfile;
            if (value.ExtraArgs != null) copy.ExtraArgs = value.ExtraArgs;
            if (value.JumpHost != null) copy.JumpHost = value.JumpHost;
            return copy;
        }

        /// <summary>Follow a session that was renamed or moved to another folder, so its overrides stay with it</summary>
        /// <param name="oldId">Its ID before</param>
        /// <param name="newId">Its ID now</param>
        public static void Rename(string oldId, string newId)
        {
            Override value;
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || oldId == newId || !Data.TryGetValue(oldId, out value))
                return;
            Data.Remove(oldId);
            Data[newId] = value;
            SaveAndNotify();
        }

        /// <summary>Drop a deleted session's overrides</summary>
        /// <param name="sessionId">The deleted session's ID</param>
        public static void Forget(string sessionId)
        {
            if (sessionId != null && Data.Remove(sessionId))
            {
                SaveAndNotify();
            }
        }

        /// <summary>Read the file again, e.g. after the settings folder changed; raises <see cref="Changed"/></summary>
        public static void Reload()
        {
            store = null;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The overrides, read from the file on first use; empty if it's missing or unreadable</summary>
        private static Dictionary<string, Override> Data
        {
            get
            {
                if (store == null)
                {
                    store = new Dictionary<string, Override>();
                    string path = Path;
                    try
                    {
                        if (File.Exists(path))
                        {
                            Dictionary<string, Override> read = JsonConvert.DeserializeObject<Dictionary<string, Override>>(File.ReadAllText(path));
                            if (read != null)
                            {
                                store = read.Where(kv => kv.Value != null && !kv.Value.IsEmpty).ToDictionary(kv => kv.Key, kv => kv.Value);
                            }
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

        /// <summary>Where the overrides are kept: <see cref="FilePath"/>, or SessionOverrides.json beside the user's settings</summary>
        private static string Path
        {
            get { return FilePath ?? System.IO.Path.Combine(SuperPuTTY.UserSettingsFolder, FileName); }
        }

        /// <summary>Write the overrides and tell the Sessions tree</summary>
        private static void SaveAndNotify()
        {
            string path = Path;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(Data, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save " + path, ex);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
