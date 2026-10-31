using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using log4net;
using SuperPutty.Utils;

namespace SuperPutty.Data
{
    /// <summary>
    /// Sessions as CSV, for audits and for teams that keep their device lists in spreadsheets. frmSuperPutty's
    /// File -> Export sessions to CSV and SessionTreeview's folder menu call <see cref="Export"/>; File -> Import
    /// sessions -> From CSV file calls <see cref="ReadPlan"/>, shows its summary and then <see cref="Apply"/>, which
    /// adds and updates Saved and Private sessions through SuperPuTTY.AddSession and saves once. The columns match
    /// the Database sessions table (folder, name, host, ...), so a table exported from Postgres imports too.
    /// Passwords given with -pw are never written out.
    /// </summary>
    public static class SessionCsv
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionCsv));

        /// <summary>What a -pw password is exported as</summary>
        public const string MaskedPassword = "********";

        /// <summary>The Source column's values</summary>
        public const string SourceSaved = "Saved", SourcePrivate = "Private", SourceDatabase = "Database";

        /// <summary>A masked -pw and its value, with the space before it, to drop it from a new session's arguments</summary>
        private static readonly Regex MaskedPasswordOption = new Regex(@"\s*-pw[=:\s]" + Regex.Escape(MaskedPassword), RegexOptions.Compiled);

        /// <summary>One column: its header, the other headers accepted for it, and how it's read from and written to a session</summary>
        private class Column
        {
            /// <summary>The header written on export</summary>
            public string Header;

            /// <summary>Headers accepted on import, normalized (<see cref="Normalize"/>)</summary>
            public string[] Names;

            /// <summary>The session's value as text</summary>
            public Func<SessionData, string> Get;

            /// <summary>Set the session's value from text; returns an error, or null</summary>
            public Func<SessionData, string, string> Set;
        }

        /// <summary>Every column, in the order exported. Source, Folder and Name are handled apart: they say which session a row is.</summary>
        private static readonly Column[] Columns =
        {
            new Column { Header = "Protocol", Names = new[] { "protocol", "proto", "type", "connectiontype" },
                Get = s => s.Proto.ToString(),
                Set = (s, v) =>
                {
                    ConnectionProtocol p;
                    if (string.IsNullOrWhiteSpace(v)) { s.Proto = ConnectionProtocol.SSH; return null; }
                    if (!Enum.TryParse(v.Trim(), true, out p) || !Enum.IsDefined(typeof(ConnectionProtocol), p)) return "unknown protocol \"" + v + "\"";
                    s.Proto = p; return null;
                } },
            new Column { Header = "Host", Names = new[] { "host", "hostname", "address", "ip" }, Get = s => s.Host, Set = (s, v) => { s.Host = v.Trim(); return null; } },
            new Column { Header = "Port", Names = new[] { "port" },
                Get = s => s.Port.ToString(),
                Set = (s, v) =>
                {
                    int port;
                    if (string.IsNullOrWhiteSpace(v)) { s.Port = 0; return null; }
                    if (!int.TryParse(v.Trim(), out port) || port < 0 || port > 65535) return "port \"" + v + "\" isn't 0 to 65535";
                    s.Port = port; return null;
                } },
            new Column { Header = "Username", Names = new[] { "username", "user", "login" }, Get = s => s.Username, Set = (s, v) => { s.Username = v.Trim(); return null; } },
            new Column { Header = "PuTTY profile", Names = new[] { "puttyprofile", "puttysession", "profile" }, Get = s => s.PuttySession, Set = (s, v) => { s.PuttySession = v; return null; } },
            new Column { Header = "Extra args", Names = new[] { "extraargs", "extraarguments", "arguments" },
                Get = s => MaskPassword(s.ExtraArgs), Set = (s, v) => { s.ExtraArgs = KeepPassword(v, s.ExtraArgs); return null; } },
            new Column { Header = "Icon", Names = new[] { "icon", "imagekey" }, Get = s => s.ImageKey, Set = (s, v) => { s.ImageKey = v.Trim(); return null; } },
            new Column { Header = "Notes", Names = new[] { "notes", "note" }, Get = s => s.Note, Set = (s, v) => { s.Note = v; return null; } },
            new Column { Header = "SPSL script", Names = new[] { "spslscript", "spslfilename", "spsl", "script" }, Get = s => s.SPSLFileName, Set = (s, v) => { s.SPSLFileName = v; return null; } },
            new Column { Header = "RDP options", Names = new[] { "rdpoptions" }, Get = s => s.RdpOptions, Set = (s, v) => { s.RdpOptions = v; return null; } },
            new Column { Header = "Local path", Names = new[] { "localpath" }, Get = s => s.LocalPath, Set = (s, v) => { s.LocalPath = v; return null; } },
            new Column { Header = "Remote path", Names = new[] { "remotepath" }, Get = s => s.RemotePath, Set = (s, v) => { s.RemotePath = v; return null; } },
            new Column { Header = "Jump host", Names = new[] { "jumphost", "bastion", "proxyjump" }, Get = s => s.JumpHost, Set = (s, v) => { s.JumpHost = v.Trim(); return null; } },
            new Column { Header = "Colour", Names = new[] { "colour", "color", "environmentcolour", "environmentcolor" },
                Get = s => s.Color,
                Set = (s, v) =>
                {
                    if (string.IsNullOrWhiteSpace(v)) { s.Color = string.Empty; return null; }
                    string color = Gui.EnvironmentColors.Normalize(v);
                    if (color == null) return "colour \"" + v + "\" isn't #RRGGBB";
                    s.Color = color; return null;
                } }
        };

        /// <summary>A header for matching: lower case, without spaces, _ and -</summary>
        /// <param name="header">The header as written, e.g. "PuTTY profile" or "putty_profile"</param>
        /// <returns>e.g. "puttyprofile"</returns>
        private static string Normalize(string header)
        {
            return new string((header ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-').ToArray()).ToLowerInvariant();
        }

        #region Export

        /// <summary>Which root a session is under, for the Source column</summary>
        /// <param name="session">The session</param>
        /// <returns>Saved, Private or Database</returns>
        public static string SourceOf(SessionData session)
        {
            return session.IsDatabase ? SourceDatabase : session.IsPrivate ? SourcePrivate : SourceSaved;
        }

        /// <summary>A session's folder path, e.g. "Lab/Core", or "" at the top</summary>
        /// <param name="session">The session</param>
        /// <returns>The folder path</returns>
        public static string FolderOf(SessionData session)
        {
            return session.IsDatabase ? DatabaseSessions.FolderOf(session.SessionId) : SessionData.GetSessionParentId(session.SessionId) ?? string.Empty;
        }

        /// <summary>Extra arguments with any -pw password replaced by <see cref="MaskedPassword"/></summary>
        /// <param name="args">The arguments</param>
        /// <returns>The arguments, safe to write out</returns>
        public static string MaskPassword(string args)
        {
            return string.IsNullOrEmpty(CommandLineOptions.getcommand(args, "-pw")) ? args : CommandLineOptions.replacePassword(args, MaskedPassword);
        }

        /// <summary>
        /// Imported extra arguments, with a masked -pw (as exported) given back the session's real password, or
        /// dropped for a new session, so re-importing an export never sets the password to asterisks
        /// </summary>
        /// <param name="imported">The arguments from the file</param>
        /// <param name="current">The session's arguments before the import; null or empty for a new session</param>
        /// <returns>The arguments to keep</returns>
        public static string KeepPassword(string imported, string current)
        {
            if (CommandLineOptions.getcommand(imported, "-pw") != MaskedPassword)
                return imported;
            string password = CommandLineOptions.getcommand(current, "-pw");
            return string.IsNullOrEmpty(password) ? MaskedPasswordOption.Replace(imported, string.Empty).Trim() : CommandLineOptions.replacePassword(imported, password);
        }

        /// <summary>The sessions as CSV text: a header, then a row per session ordered by source, folder and name</summary>
        /// <param name="sessions">The sessions</param>
        /// <returns>The CSV, with CRLF line ends</returns>
        public static string ToCsv(IEnumerable<SessionData> sessions)
        {
            StringBuilder csv = new StringBuilder();
            WriteRow(csv, new[] { "Source", "Folder", "Name" }.Concat(Columns.Select(c => c.Header)));
            foreach (SessionData session in sessions.OrderBy(SourceOf).ThenBy(FolderOf, StringComparer.OrdinalIgnoreCase).ThenBy(NameOf, StringComparer.OrdinalIgnoreCase))
            {
                WriteRow(csv, new[] { SourceOf(session), FolderOf(session), NameOf(session) }.Concat(Columns.Select(c => c.Get(session))));
            }
            return csv.ToString();
        }

        /// <summary>
        /// A session's name as its ID has it (the last part), which is what places it in the tree; a hand-edited
        /// Sessions.xml can have a SessionName that differs, and exporting that broke the match on re-import
        /// </summary>
        /// <param name="session">The session</param>
        /// <returns>e.g. "core-sw1" for "Lab/Core/core-sw1"</returns>
        private static string NameOf(SessionData session)
        {
            return SessionData.GetSessionNameFromId(session.IsDatabase ? DatabaseSessions.TreePath(session.SessionId) : session.SessionId);
        }

        /// <summary>Write the sessions to a CSV file, as UTF-8 with a byte order mark so Excel reads accents and symbols right</summary>
        /// <param name="sessions">The sessions</param>
        /// <param name="path">The file</param>
        /// <returns>How many sessions were written</returns>
        public static int Export(IEnumerable<SessionData> sessions, string path)
        {
            List<SessionData> list = sessions.ToList();
            File.WriteAllText(path, ToCsv(list), new UTF8Encoding(true));
            Log.InfoFormat("Exported {0} sessions to {1}", list.Count, path);
            return list.Count;
        }

        /// <summary>Add one CSV row</summary>
        /// <param name="csv">The text so far</param>
        /// <param name="values">The row's values</param>
        private static void WriteRow(StringBuilder csv, IEnumerable<string> values)
        {
            csv.Append(string.Join(",", values.Select(Quote))).Append("\r\n");
        }

        /// <summary>
        /// One value for CSV: quoted when it holds a comma, quote or line break. A value starting with = + - or @
        /// gets a leading ' so a spreadsheet shows it as text rather than running it as a formula (e.g. "-L 8080:..."
        /// in Extra args); <see cref="Unquote"/> removes it again on import.
        /// </summary>
        /// <param name="value">The value; null is written as empty</param>
        /// <returns>The value as written</returns>
        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0)
                value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n', ';', '\t' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>A value as read: without the ' <see cref="Quote"/> put before a formula-like value</summary>
        /// <param name="value">The value from the file</param>
        /// <returns>The value</returns>
        private static string Unquote(string value)
        {
            return value.Length > 1 && value[0] == '\'' && "=+-@".IndexOf(value[1]) >= 0 ? value.Substring(1) : value;
        }

        #endregion

        #region Reading

        /// <summary>
        /// Split CSV text into rows of values. Handles quoted values with commas, quotes and line breaks in them,
        /// and takes the separator from the header line: a comma, or the ; or tab some spreadsheets save with.
        /// </summary>
        /// <param name="text">The file's text</param>
        /// <returns>The rows; blank lines are left out</returns>
        public static List<string[]> ParseRows(string text)
        {
            text = text ?? string.Empty;
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);
            string firstLine = text.Split('\n')[0];
            char separator = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();

            List<string[]> rows = new List<string[]>();
            List<string> row = new List<string>();
            StringBuilder value = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else value.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == separator) { row.Add(value.ToString()); value.Clear(); }
                else if (ch == '\r') { }
                else if (ch == '\n') { row.Add(value.ToString()); value.Clear(); AddRow(rows, row); row = new List<string>(); }
                else value.Append(ch);
            }
            row.Add(value.ToString());
            AddRow(rows, row);
            return rows;
        }

        /// <summary>Keep a row unless it's blank</summary>
        /// <param name="rows">The rows so far</param>
        /// <param name="row">The row</param>
        private static void AddRow(List<string[]> rows, List<string> row)
        {
            if (row.Any(v => v.Length > 0))
                rows.Add(row.ToArray());
        }

        #endregion

        #region Importing

        /// <summary>What importing one row will do</summary>
        public enum RowAction
        {
            /// <summary>Add a new session</summary>
            Add,
            /// <summary>Change the session already at that folder and name</summary>
            Update,
            /// <summary>Leave it out; <see cref="PlannedRow.Reason"/> says why</summary>
            Skip
        }

        /// <summary>One row of the file and what importing it will do</summary>
        public class PlannedRow
        {
            /// <summary>The row's line number in the file, counting the header as 1</summary>
            public int Line { get; set; }

            /// <summary>What will happen</summary>
            public RowAction Action { get; set; }

            /// <summary>Why it's skipped</summary>
            public string Reason { get; set; }

            /// <summary>The session's ID, e.g. "Lab/Core/sw1"</summary>
            public string SessionId { get; set; }

            /// <summary>Whether it goes in Private sessions</summary>
            public bool IsPrivate { get; set; }

            /// <summary>The session as it will be: a new one, or a copy of the existing one with the row's values</summary>
            public SessionData Session { get; set; }
        }

        /// <summary>What importing a file will do, worked out before anything changes</summary>
        public class ImportPlan
        {
            /// <summary>Every data row</summary>
            public List<PlannedRow> Rows { get; } = new List<PlannedRow>();

            /// <summary>Why the whole file can't be imported (no Name column, nothing in it); null if it can</summary>
            public string Error { get; set; }

            /// <summary>Columns in the file that aren't used</summary>
            public List<string> IgnoredColumns { get; } = new List<string>();

            /// <summary>Rows that add a session</summary>
            public int Adds { get { return this.Rows.Count(r => r.Action == RowAction.Add); } }

            /// <summary>Rows that change one</summary>
            public int Updates { get { return this.Rows.Count(r => r.Action == RowAction.Update); } }

            /// <summary>Rows left out</summary>
            public int Skips { get { return this.Rows.Count(r => r.Action == RowAction.Skip); } }

            /// <summary>A summary for the confirmation box: the counts, and the first few skipped rows with why</summary>
            /// <returns>A few lines</returns>
            public string Summary()
            {
                if (this.Error != null)
                    return this.Error;
                StringBuilder text = new StringBuilder();
                text.AppendFormat("{0} new session{1}, {2} updated, {3} skipped.", this.Adds, this.Adds == 1 ? "" : "s", this.Updates, this.Skips);
                foreach (PlannedRow skip in this.Rows.Where(r => r.Action == RowAction.Skip).Take(8))
                {
                    text.AppendFormat("\nLine {0}: {1}", skip.Line, skip.Reason);
                }
                if (this.Skips > 8)
                    text.AppendFormat("\n... and {0} more skipped", this.Skips - 8);
                if (this.IgnoredColumns.Count > 0)
                    text.Append("\nColumns not used: " + string.Join(", ", this.IgnoredColumns));
                return text.ToString();
            }
        }

        /// <summary>Read a CSV file and work out what importing it would do, without changing anything</summary>
        /// <param name="path">The file</param>
        /// <param name="existing">Looks up a session by ID; normally SuperPuTTY.GetSessionById</param>
        /// <returns>The plan</returns>
        public static ImportPlan ReadPlan(string path, Func<string, SessionData> existing)
        {
            // File.ReadAllText honours a byte order mark and otherwise reads UTF-8
            return Plan(File.ReadAllText(path), existing);
        }

        /// <summary>
        /// Work out what importing CSV text would do. A row is matched to a session by its Source, Folder and Name:
        /// an existing Saved or Private session there is updated, otherwise one is added. Columns a file doesn't
        /// have are left as they are (or their defaults, for new sessions). Database rows are skipped: the table is
        /// changed in SuperXPuTTY one session at a time, or with SQL.
        /// </summary>
        /// <param name="text">The CSV</param>
        /// <param name="existing">Looks up a session by ID</param>
        /// <returns>The plan</returns>
        public static ImportPlan Plan(string text, Func<string, SessionData> existing)
        {
            ImportPlan plan = new ImportPlan();
            List<string[]> rows = ParseRows(text);
            if (rows.Count == 0)
            {
                plan.Error = "The file is empty.";
                return plan;
            }
            string[] headers = rows[0].Select(Normalize).ToArray();
            int source = Array.IndexOf(headers, "source"), folder = Array.FindIndex(headers, h => h == "folder" || h == "path"),
                name = Array.FindIndex(headers, h => h == "name" || h == "sessionname");
            if (name < 0)
            {
                plan.Error = "The file has no Name column. The first line must name the columns, e.g. Folder,Name,Host,Username.";
                return plan;
            }
            Dictionary<int, Column> used = new Dictionary<int, Column>();
            for (int i = 0; i < headers.Length; i++)
            {
                Column column = Columns.FirstOrDefault(c => c.Names.Contains(headers[i]));
                if (column != null && !used.ContainsValue(column))
                    used[i] = column;
                else if (i != source && i != folder && i != name && headers[i].Length > 0)
                    plan.IgnoredColumns.Add(rows[0][i]);
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int r = 1; r < rows.Count; r++)
            {
                string[] row = rows[r];
                Func<int, string> cell = i => i >= 0 && i < row.Length ? Unquote(row[i]) : string.Empty;
                PlannedRow planned = new PlannedRow { Line = r + 1 };
                plan.Rows.Add(planned);

                string kind = cell(source).Trim();
                string sessionName = cell(name).Trim();
                string path = cell(folder).Trim().Trim('/');
                if (kind.Equals(SourceDatabase, StringComparison.OrdinalIgnoreCase))
                {
                    Skip(planned, "a Database session; change the table with SQL, or in the tree");
                    continue;
                }
                if (kind.Length > 0 && !kind.Equals(SourceSaved, StringComparison.OrdinalIgnoreCase) && !kind.Equals(SourcePrivate, StringComparison.OrdinalIgnoreCase))
                {
                    Skip(planned, "Source \"" + kind + "\" isn't Saved, Private or Database");
                    continue;
                }
                if (sessionName.Length == 0 || sessionName.Contains("/"))
                {
                    Skip(planned, sessionName.Length == 0 ? "no name" : "the name \"" + sessionName + "\" has a /");
                    continue;
                }
                // with Saved sessions turned off in Options, Saved rows go to Private sessions
                planned.IsPrivate = kind.Equals(SourcePrivate, StringComparison.OrdinalIgnoreCase) || !SuperPuTTY.Settings.UseSessionsXml;
                planned.SessionId = path.Length == 0 ? sessionName : path + "/" + sessionName;
                if (!seen.Add(planned.SessionId))
                {
                    Skip(planned, "\"" + planned.SessionId + "\" is in the file twice; the first one is used");
                    continue;
                }

                SessionData current = existing(planned.SessionId);
                if (current != null && (current.IsDatabase || current.IsPrivate != planned.IsPrivate))
                {
                    Skip(planned, "\"" + planned.SessionId + "\" is already a " + SourceOf(current).ToLowerInvariant() + " session");
                    continue;
                }
                planned.Action = current == null ? RowAction.Add : RowAction.Update;
                // a copy: nothing changes until Apply, and only then if the user agrees
                SessionData session = current == null
                    ? new SessionData(sessionName, string.Empty, 0, ConnectionProtocol.SSH, "Default Settings") { SessionId = planned.SessionId }
                    : (SessionData)current.Clone();
                foreach (KeyValuePair<int, Column> column in used)
                {
                    string error = column.Value.Set(session, cell(column.Key));
                    if (error != null)
                    {
                        Skip(planned, error);
                        break;
                    }
                }
                if (planned.Action != RowAction.Skip)
                {
                    planned.Session = session;
                }
            }
            if (plan.Rows.Count == 0)
                plan.Error = "The file has column names but no sessions.";
            return plan;
        }

        /// <summary>Mark a row skipped</summary>
        /// <param name="row">The row</param>
        /// <param name="reason">Why</param>
        private static void Skip(PlannedRow row, string reason)
        {
            row.Action = RowAction.Skip;
            row.Reason = reason;
            row.Session = null;
        }

        /// <summary>
        /// Carry out a plan: new sessions are added, existing ones get the row's values, and Sessions.xml (and
        /// PrivateSessions.xml) are saved once at the end
        /// </summary>
        /// <param name="plan">The plan from <see cref="Plan"/></param>
        /// <returns>How many sessions were added and updated</returns>
        public static int Apply(ImportPlan plan)
        {
            int done = 0;
            foreach (PlannedRow row in plan.Rows.Where(r => r.Action != RowAction.Skip))
            {
                if (row.Action == RowAction.Add)
                {
                    row.Session.IsPrivate = row.IsPrivate;
                    if (SuperPuTTY.AddSession(row.Session))
                        done++;
                    continue;
                }
                SessionData current = SuperPuTTY.GetSessionById(row.SessionId);
                if (current == null)
                    continue;
                SessionData from = row.Session;
                current.Proto = from.Proto;
                current.Host = from.Host;
                current.Port = from.Port;
                current.Username = from.Username;
                current.PuttySession = from.PuttySession;
                current.ExtraArgs = from.ExtraArgs;
                current.ImageKey = from.ImageKey;
                current.Note = from.Note;
                current.SPSLFileName = from.SPSLFileName;
                current.RdpOptions = from.RdpOptions;
                current.LocalPath = from.LocalPath;
                current.RemotePath = from.RemotePath;
                current.Color = from.Color;
                current.JumpHost = from.JumpHost;
                done++;
            }
            SuperPuTTY.SaveSessions();
            Log.InfoFormat("Imported from CSV: {0} added, {1} updated, {2} skipped", plan.Adds, plan.Updates, plan.Skips);
            return done;
        }

        #endregion

        #region With dialogs

        /// <summary>Ask where to save, export the sessions there, and say how it went in the status bar</summary>
        /// <param name="owner">The window the dialogs belong to</param>
        /// <param name="sessions">The sessions to export</param>
        /// <param name="fileName">The file name suggested, e.g. "Sessions.csv" or "Lab.csv"</param>
        public static void ExportWithDialog(System.Windows.Forms.IWin32Window owner, List<SessionData> sessions, string fileName)
        {
            using (System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files|*.*",
                FileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()))
            })
            {
                if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                    return;
                try
                {
                    int count = Export(sessions, dialog.FileName);
                    int masked = sessions.Count(s => !string.IsNullOrEmpty(CommandLineOptions.getcommand(s.ExtraArgs, "-pw")));
                    SuperPuTTY.ReportStatus("Exported {0} sessions to {1}{2}", count, Path.GetFileName(dialog.FileName),
                        masked > 0 ? string.Format(" ({0} -pw password{1} written as {2})", masked, masked == 1 ? "" : "s", MaskedPassword) : "");
                }
                catch (Exception ex)
                {
                    Log.Error("Could not export sessions to " + dialog.FileName, ex);
                    DarkModeForms.Messenger.MessageBox("Could not export the sessions: " + ex.Message, "Export to CSV",
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Ask for a CSV file, show what importing it would do, and import it if the user agrees</summary>
        /// <param name="owner">The window the dialogs belong to</param>
        public static void ImportWithDialog(System.Windows.Forms.IWin32Window owner)
        {
            using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "CSV files (*.csv;*.txt)|*.csv;*.txt|All files|*.*",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                    return;
                ImportPlan plan;
                try
                {
                    plan = ReadPlan(dialog.FileName, SuperPuTTY.GetSessionById);
                }
                catch (IOException ex)
                {
                    DarkModeForms.Messenger.MessageBox("Could not read the file: " + ex.Message, "Import from CSV",
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
                if (plan.Error != null || plan.Adds + plan.Updates == 0)
                {
                    DarkModeForms.Messenger.MessageBox("Nothing to import from " + Path.GetFileName(dialog.FileName) + ".\n\n" + plan.Summary(), "Import from CSV",
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                    return;
                }
                if (DarkModeForms.Messenger.MessageBox("Import sessions from " + Path.GetFileName(dialog.FileName) + "?\n\n" + plan.Summary()
                    + "\n\nSessions are matched by folder and name; existing ones get the file's values.", "Import from CSV",
                    System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes)
                    return;
                Apply(plan);
                SuperPuTTY.ReportStatus("Imported {0} new and {1} updated sessions from {2}", plan.Adds, plan.Updates, Path.GetFileName(dialog.FileName));
            }
        }

        #endregion
    }
}
