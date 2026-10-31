using System;
using System.Collections.Generic;
using System.Linq;
using log4net;

namespace SuperPutty.Data
{
    /// <summary>
    /// Changing the same fields on many sessions at once, e.g. a new username for every device at a site. Gui.dlgBulkEdit
    /// (a folder's "Edit sessions..." in the Sessions tree) fills a <see cref="Change"/> and calls <see cref="Apply"/>.
    /// Saved and Private sessions are changed in place and saved once; Database sessions go to the table in one batch
    /// (DatabaseSessions.SubmitBatch), and fields locked there are left alone.
    /// </summary>
    public static class SessionBulkEdit
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionBulkEdit));

        /// <summary>The new values; null leaves a field as each session has it</summary>
        public class Change
        {
            /// <summary>Login name, e.g. "{user}"</summary>
            public string Username { get; set; }

            /// <summary>PuTTY saved-session profile</summary>
            public string PuttyProfile { get; set; }

            /// <summary>Port; 0 means the protocol's usual one</summary>
            public int? Port { get; set; }

            /// <summary>Connection type</summary>
            public ConnectionProtocol? Protocol { get; set; }

            /// <summary>Icon key, e.g. "switch#1E63B4"</summary>
            public string Icon { get; set; }

            /// <summary>Environment colour, "#RRGGBB", or "" for none</summary>
            public string Color { get; set; }

            /// <summary>Extra PuTTY arguments</summary>
            public string ExtraArgs { get; set; }

            /// <summary>Jump host: a session's path or "user@host:port"; "" for none</summary>
            public string JumpHost { get; set; }

            /// <summary>The table's column names of the fields being changed, to check against locked_fields</summary>
            /// <returns>e.g. "username", "port"</returns>
            public List<string> Columns()
            {
                List<string> columns = new List<string>();
                if (this.Username != null) columns.Add("username");
                if (this.PuttyProfile != null) columns.Add("putty_profile");
                if (this.Port != null) columns.Add("port");
                if (this.Protocol != null) columns.Add("protocol");
                if (this.Icon != null) columns.Add("icon");
                if (this.Color != null) columns.Add("color");
                if (this.ExtraArgs != null) columns.Add("extra_args");
                if (this.JumpHost != null) columns.Add("jump_host");
                return columns;
            }

            /// <summary>Set the changed fields on a session</summary>
            /// <param name="session">The session (a copy, for database sessions)</param>
            public void ApplyTo(SessionData session)
            {
                if (this.Username != null) session.Username = this.Username;
                if (this.PuttyProfile != null) session.PuttySession = this.PuttyProfile;
                if (this.Port != null) session.Port = this.Port.Value;
                if (this.Protocol != null) session.Proto = this.Protocol.Value;
                if (this.Icon != null) session.ImageKey = this.Icon;
                if (this.Color != null) session.Color = this.Color;
                if (this.ExtraArgs != null) session.ExtraArgs = this.ExtraArgs;
                if (this.JumpHost != null) session.JumpHost = this.JumpHost;
            }
        }

        /// <summary>What <see cref="Apply"/> did</summary>
        public class Result
        {
            /// <summary>Sessions changed (or, for the database while offline, queued)</summary>
            public int Changed { get; set; }

            /// <summary>Why sessions were left alone: locked in the database, or refused by it</summary>
            public List<string> Skipped { get; } = new List<string>();
        }

        /// <summary>The first field of a change that's locked for a session in the database, if any</summary>
        /// <param name="session">The session</param>
        /// <param name="change">The change</param>
        /// <returns>The locked column, or null</returns>
        public static string LockedField(SessionData session, Change change)
        {
            return change.Columns().FirstOrDefault(session.IsDatabaseFieldLocked);
        }

        /// <summary>
        /// Change the sessions: Saved and Private ones in place, saved once; Database ones sent to the table as one
        /// batch, skipping those with a changed field locked
        /// </summary>
        /// <param name="sessions">The sessions</param>
        /// <param name="change">The new values</param>
        /// <returns>What was done</returns>
        public static Result Apply(IEnumerable<SessionData> sessions, Change change)
        {
            Result result = new Result();
            List<DatabaseOutbox.PendingChange> database = new List<DatabaseOutbox.PendingChange>();
            bool savedOrPrivate = false;
            foreach (SessionData session in sessions)
            {
                string locked = LockedField(session, change);
                if (locked != null)
                {
                    result.Skipped.Add(session.SessionName + ": " + (session.DatabaseLocked ? "locked in the database" : locked + " is locked in the database"));
                    continue;
                }
                if (session.IsDatabase)
                {
                    // the table is changed, then read back; the session shown isn't touched until then
                    SessionData copy = (SessionData)session.Clone();
                    change.ApplyTo(copy);
                    database.Add(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Update, Session = copy, Folder = DatabaseSessions.FolderOf(session.SessionId) });
                    continue;
                }
                change.ApplyTo(session);
                savedOrPrivate = true;
                result.Changed++;
            }
            if (savedOrPrivate)
            {
                SuperPuTTY.SaveSessions();
            }
            if (database.Count > 0)
            {
                List<string> errors;
                result.Changed += DatabaseSessions.SubmitBatch(database, out errors);
                result.Skipped.AddRange(errors);
            }
            Log.InfoFormat("Edited {0} sessions at once ({1}); {2} skipped", result.Changed, string.Join(", ", change.Columns()), result.Skipped.Count);
            return result;
        }
    }
}
