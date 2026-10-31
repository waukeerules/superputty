using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using log4net;

namespace SuperPutty.Data
{
    /// <summary>
    /// Changes to database sessions that haven't reached the Postgres table yet: made while it couldn't be reached, or
    /// refused when sent (someone else's change, a lock, a name clash). They're kept in DatabaseSessions.outbox.xml
    /// beside the user's settings, one list per database, sent in order by DatabaseSessions when the table can be
    /// reached, and shown in the tree meanwhile (<see cref="Apply"/>). DatabaseSessions.Submit adds to it;
    /// Gui.dlgPendingChanges retries, overwrites or discards. Safe to use from the UI and the background sync.
    /// </summary>
    public static class DatabaseOutbox
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(DatabaseOutbox));

        /// <summary>What a change does</summary>
        public enum ChangeKind
        {
            /// <summary>Add a session</summary>
            Insert,
            /// <summary>Change a session</summary>
            Update,
            /// <summary>Remove a session</summary>
            Delete,
            /// <summary>Move a folder and everything under it</summary>
            RenameFolder,
            /// <summary>Remove a folder and everything under it</summary>
            DeleteFolder
        }

        /// <summary>One change waiting to reach the table</summary>
        public class PendingChange
        {
            /// <summary>Identifies the change; a queued new session shows with the id "pending:" + this</summary>
            public string Id { get; set; } = Guid.NewGuid().ToString();

            /// <summary>What it does</summary>
            public ChangeKind Kind { get; set; }

            /// <summary>For Insert and Update, the session's values; for Update and Delete, its row id and version</summary>
            public SessionData Session { get; set; }

            /// <summary>For Insert and Update, the folder it goes in</summary>
            public string Folder { get; set; }

            /// <summary>For RenameFolder and DeleteFolder, the folder's path</summary>
            public string From { get; set; }

            /// <summary>For RenameFolder, the new path</summary>
            public string To { get; set; }

            /// <summary>When it was queued</summary>
            public DateTime QueuedAt { get; set; } = DateTime.Now;

            /// <summary>Why the table refused it; null while it's waiting to be sent</summary>
            public string Error { get; set; }

            /// <summary>For Update: save over someone else's change (chosen after a conflict)</summary>
            public bool Force { get; set; }

            /// <summary>A short description for the pending changes list</summary>
            /// <returns>e.g. "Change Lab/sw1"</returns>
            public override string ToString()
            {
                switch (this.Kind)
                {
                    case ChangeKind.Insert:
                        return "Add " + Path(this.Folder, this.Session?.SessionName);
                    case ChangeKind.Update:
                        return "Change " + Path(this.Folder, this.Session?.SessionName);
                    case ChangeKind.Delete:
                        return "Delete " + DatabaseSessions.TreePath(this.Session?.SessionId);
                    case ChangeKind.RenameFolder:
                        return "Rename folder " + this.From + " to " + this.To;
                    default:
                        return "Remove folder " + this.From;
                }
            }

            /// <summary>A folder and name as one path</summary>
            /// <param name="folder">The folder, or empty</param>
            /// <param name="name">The name</param>
            /// <returns>e.g. "Lab/sw1"</returns>
            private static string Path(string folder, string name)
            {
                return string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
            }
        }

        /// <summary>The changes for one database</summary>
        public class Outbox
        {
            /// <summary>Which database: DatabaseSessions.Connection.ToString()</summary>
            public string Source { get; set; }

            /// <summary>The changes, oldest first</summary>
            public List<PendingChange> Changes { get; set; } = new List<PendingChange>();
        }

        /// <summary>The file's contents: an outbox per database, so switching databases in the settings loses nothing</summary>
        public class OutboxFile
        {
            /// <summary>The outboxes</summary>
            public List<Outbox> Outboxes { get; set; } = new List<Outbox>();
        }

        /// <summary>Guards the file and its contents between the UI and the background sync</summary>
        private static readonly object gate = new object();

        /// <summary>The loaded file; null until first used</summary>
        private static OutboxFile loaded;

        /// <summary>The file's path; set it to use another (tests)</summary>
        public static string FilePath { get; set; }

        /// <summary>The file: DatabaseSessions.outbox.xml beside the user's settings</summary>
        private static string FileName
        {
            get { return FilePath ?? System.IO.Path.Combine(SuperPuTTY.UserSettingsFolder, "DatabaseSessions.outbox.xml"); }
        }

        /// <summary>Forget the loaded file, so the next use reads it again (tests)</summary>
        public static void Reset()
        {
            lock (gate)
            {
                loaded = null;
            }
        }

        /// <summary>A copy of a database's changes, oldest first</summary>
        /// <param name="source">The database</param>
        /// <returns>The changes</returns>
        public static List<PendingChange> Changes(string source)
        {
            lock (gate)
            {
                return OutboxFor(source, false)?.Changes.ToList() ?? new List<PendingChange>();
            }
        }

        /// <summary>How many changes wait to be sent, and how many were refused</summary>
        /// <param name="source">The database</param>
        /// <param name="failed">How many were refused</param>
        /// <returns>How many wait</returns>
        public static int Count(string source, out int failed)
        {
            List<PendingChange> changes = Changes(source);
            failed = changes.Count(c => c.Error != null);
            return changes.Count - failed;
        }

        /// <summary>
        /// Queue a change. A change to a session that's itself still queued is folded into it: changing a queued new
        /// session updates the insert, deleting it drops the insert, and a second change to the same row replaces
        /// the first (keeping the version it was read at).
        /// </summary>
        /// <param name="source">The database</param>
        /// <param name="change">The change</param>
        public static void Add(string source, PendingChange change)
        {
            lock (gate)
            {
                List<PendingChange> changes = OutboxFor(source, true).Changes;
                string rowId = change.Session?.DatabaseId;
                if (rowId != null && rowId.StartsWith("pending:", StringComparison.Ordinal)
                    && (change.Kind == ChangeKind.Update || change.Kind == ChangeKind.Delete))
                {
                    PendingChange insert = changes.FirstOrDefault(c => c.Kind == ChangeKind.Insert && c.Id == rowId.Substring(8));
                    if (insert != null)
                    {
                        if (change.Kind == ChangeKind.Delete)
                        {
                            changes.Remove(insert);
                        }
                        else
                        {
                            insert.Session = change.Session;
                            insert.Folder = change.Folder;
                            insert.Error = null;
                        }
                        Save();
                        return;
                    }
                }
                if (rowId != null && change.Kind == ChangeKind.Update)
                {
                    PendingChange earlier = changes.FirstOrDefault(c => c.Kind == ChangeKind.Update && c.Session?.DatabaseId == rowId);
                    if (earlier != null)
                    {
                        // keep the version it was first read at, so someone else's change in between is still noticed
                        change.Session.DatabaseVersion = earlier.Session.DatabaseVersion;
                        earlier.Session = change.Session;
                        earlier.Folder = change.Folder;
                        earlier.Error = null;
                        Save();
                        return;
                    }
                }
                if (rowId != null && change.Kind == ChangeKind.Delete)
                {
                    changes.RemoveAll(c => c.Kind == ChangeKind.Update && c.Session?.DatabaseId == rowId);
                }
                changes.Add(change);
                Save();
            }
        }

        /// <summary>Remove a change: sent, or discarded</summary>
        /// <param name="source">The database</param>
        /// <param name="id">The change's <see cref="PendingChange.Id"/></param>
        public static void Remove(string source, string id)
        {
            lock (gate)
            {
                Outbox outbox = OutboxFor(source, false);
                if (outbox != null && outbox.Changes.RemoveAll(c => c.Id == id) > 0)
                {
                    Save();
                }
            }
        }

        /// <summary>Record that the table refused a change, or clear that so it's sent again</summary>
        /// <param name="source">The database</param>
        /// <param name="id">The change</param>
        /// <param name="error">Why it was refused; null to send it again</param>
        /// <param name="force">For an Update, save over someone else's change next time</param>
        public static void SetError(string source, string id, string error, bool force = false)
        {
            lock (gate)
            {
                PendingChange change = OutboxFor(source, false)?.Changes.FirstOrDefault(c => c.Id == id);
                if (change != null)
                {
                    change.Error = error;
                    change.Force |= force;
                    Save();
                }
            }
        }

        /// <summary>
        /// The sessions as the tree should show them: the table's (or the cache's) with every queued or refused change
        /// applied on top, the changed ones marked <see cref="SessionData.DatabasePending"/>. The given sessions aren't
        /// changed; changed ones are copies.
        /// </summary>
        /// <param name="server">The sessions as read from the table</param>
        /// <param name="source">The database</param>
        /// <returns>The sessions to show</returns>
        public static List<SessionData> Apply(IList<SessionData> server, string source)
        {
            List<SessionData> sessions = server.ToList();
            foreach (PendingChange change in Changes(source))
            {
                string note = change.Error == null
                    ? "Waiting to be saved to the database (queued " + change.QueuedAt.ToString("t") + ")"
                    : "Not saved to the database: " + change.Error + " (Database sessions > Pending changes)";
                switch (change.Kind)
                {
                    case ChangeKind.Insert:
                        SessionData added = Copy(change.Session, change.Folder, note);
                        added.DatabaseId = "pending:" + change.Id;
                        added.DatabaseVersion = null;
                        sessions.RemoveAll(s => s.SessionId == added.SessionId);
                        sessions.Add(added);
                        break;
                    case ChangeKind.Update:
                        int index = sessions.FindIndex(s => s.DatabaseId == change.Session.DatabaseId);
                        if (index >= 0)
                        {
                            SessionData updated = Copy(change.Session, change.Folder, note);
                            updated.DatabaseLocked = sessions[index].DatabaseLocked;
                            updated.DatabaseLockedFields = sessions[index].DatabaseLockedFields;
                            sessions[index] = updated;
                        }
                        break;
                    case ChangeKind.Delete:
                        sessions.RemoveAll(s => s.DatabaseId == change.Session.DatabaseId);
                        break;
                    case ChangeKind.RenameFolder:
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            string folder = DatabaseSessions.FolderOf(sessions[i].SessionId);
                            if (InFolder(folder, change.From))
                            {
                                sessions[i] = Copy(sessions[i], change.To + folder.Substring(change.From.Length), note);
                            }
                        }
                        break;
                    case ChangeKind.DeleteFolder:
                        sessions.RemoveAll(s => InFolder(DatabaseSessions.FolderOf(s.SessionId), change.From));
                        break;
                }
            }
            return sessions;
        }

        /// <summary>Whether a folder is another or under it</summary>
        /// <param name="folder">The folder</param>
        /// <param name="root">The other</param>
        /// <returns>true if it's the same or below</returns>
        private static bool InFolder(string folder, string root)
        {
            return folder == root || folder.StartsWith(root + "/", StringComparison.Ordinal);
        }

        /// <summary>A copy of a session, placed in a folder and marked pending</summary>
        /// <param name="session">The session</param>
        /// <param name="folder">Its folder</param>
        /// <param name="note">What's pending</param>
        /// <returns>The copy</returns>
        private static SessionData Copy(SessionData session, string folder, string note)
        {
            SessionData copy = (SessionData)session.Clone();
            copy.SessionId = DatabaseSessions.MakeSessionId(folder, session.SessionName);
            copy.DatabasePending = true;
            copy.DatabasePendingNote = note;
            return copy;
        }

        /// <summary>The outbox for a database, read from the file on first use</summary>
        /// <param name="source">The database</param>
        /// <param name="create">Make one if there's none</param>
        /// <returns>The outbox, or null if there's none and <paramref name="create"/> is false</returns>
        private static Outbox OutboxFor(string source, bool create)
        {
            if (loaded == null)
            {
                loaded = Load();
            }
            Outbox outbox = loaded.Outboxes.FirstOrDefault(o => o.Source == source);
            if (outbox == null && create)
            {
                outbox = new Outbox { Source = source };
                loaded.Outboxes.Add(outbox);
            }
            return outbox;
        }

        /// <summary>Read the file</summary>
        /// <returns>Its contents; empty if there's none or it can't be read</returns>
        private static OutboxFile Load()
        {
            string file = FileName;
            try
            {
                if (File.Exists(file))
                {
                    using (StreamReader reader = new StreamReader(file))
                    {
                        return (OutboxFile)new XmlSerializer(typeof(OutboxFile)).Deserialize(reader) ?? new OutboxFile();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the database outbox " + file + "; queued changes in it are not shown", ex);
            }
            return new OutboxFile();
        }

        /// <summary>Write the file (called under <see cref="gate"/>), leaving out empty outboxes; it's deleted when nothing's queued</summary>
        private static void Save()
        {
            string file = FileName;
            try
            {
                loaded.Outboxes.RemoveAll(o => o.Changes.Count == 0);
                if (loaded.Outboxes.Count == 0)
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    return;
                }
                string temp = file + ".tmp";
                using (StreamWriter writer = new StreamWriter(temp))
                {
                    new XmlSerializer(typeof(OutboxFile)).Serialize(writer, loaded);
                }
                if (File.Exists(file))
                    File.Replace(temp, file, null);
                else
                    File.Move(temp, file);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save the database outbox " + file, ex);
            }
        }
    }
}
