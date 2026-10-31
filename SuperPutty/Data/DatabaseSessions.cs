using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using log4net;
using Npgsql;
using SuperPutty.Utils;

namespace SuperPutty.Data
{
    /// <summary>
    /// Sessions shared through a Postgres table, shown under "Database sessions" in the Sessions tree. The table is
    /// read in pages in the background, kept in a local cache beside the user's settings so the sessions still open
    /// when Postgres can't be reached, and retried while it's offline. The connection is set in
    /// Gui.dlgDatabaseSettings; frmSuperPutty calls <see cref="Start"/>, SessionTreeview shows <see cref="Status"/>
    /// and calls <see cref="BeginSync"/> for Refresh, and SuperPuTTY.ReplaceDatabaseSessions puts the rows in the
    /// session list. Database sessions get IDs under <see cref="IdPrefix"/> so they never clash with Sessions.xml.
    /// The password is never logged.
    /// </summary>
    public static class DatabaseSessions
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(DatabaseSessions));

        /// <summary>The first part of every database session's ID, e.g. "@db/Lab/sw1", so it can't clash with Sessions.xml</summary>
        public const string IdPrefix = "@db";

        /// <summary>Rows read per query</summary>
        public static int PageSize { get; set; } = 500;

        /// <summary>How long to wait before trying again while Postgres can't be reached</summary>
        public static TimeSpan RetryInterval { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>The oldest Postgres the schema works with (gen_random_uuid() is built in from 13)</summary>
        public const int MinServerVersion = 130000;

        /// <summary>The columns the table needs, with their definitions, in order; "Create / upgrade" adds any that are missing</summary>
        private static readonly string[][] Columns =
        {
            new[] { "id", "uuid PRIMARY KEY DEFAULT gen_random_uuid()" },
            new[] { "folder", "text NOT NULL DEFAULT ''" },
            new[] { "name", "text NOT NULL" },
            new[] { "protocol", "text NOT NULL DEFAULT 'SSH'" },
            new[] { "host", "text NOT NULL DEFAULT ''" },
            new[] { "port", "integer NOT NULL DEFAULT 0" },
            new[] { "username", "text NOT NULL DEFAULT ''" },
            new[] { "putty_profile", "text NOT NULL DEFAULT 'Default Settings'" },
            new[] { "extra_args", "text NOT NULL DEFAULT ''" },
            new[] { "icon", "text" },
            new[] { "notes", "text" },
            new[] { "spsl_script", "text" },
            new[] { "rdp_options", "text" },
            new[] { "local_path", "text" },
            new[] { "remote_path", "text" },
            new[] { "updated_at", "timestamptz NOT NULL DEFAULT now()" },
            new[] { "updated_by", "text NOT NULL DEFAULT current_user" }
        };

        /// <summary>The Postgres notification channel the table's trigger signals on every change</summary>
        public const string NotifyChannel = "superputty_sessions";

        #region Connection settings

        /// <summary>The connection, as set in Options (Settings.Database*)</summary>
        public class Connection
        {
            /// <summary>Server name or address</summary>
            public string Host { get; set; }

            /// <summary>Server port, usually 5432</summary>
            public int Port { get; set; } = 5432;

            /// <summary>The database holding the table</summary>
            public string Database { get; set; }

            /// <summary>The schema holding the table</summary>
            public string Schema { get; set; }

            /// <summary>The table's name</summary>
            public string Table { get; set; }

            /// <summary>Login name</summary>
            public string User { get; set; }

            /// <summary>The password as stored: DPAPI-encrypted ("dpapi:..."), or a {key} placeholder, or empty</summary>
            public string StoredPassword { get; set; }

            /// <summary>Npgsql SSL mode name: Disable, Allow, Prefer, Require, VerifyCA or VerifyFull</summary>
            public string SslMode { get; set; } = "Prefer";

            /// <summary>Never create or change anything in the database</summary>
            public bool ReadOnly { get; set; }

            /// <summary>The table's quoted, schema-qualified name for SQL</summary>
            public string QualifiedTable
            {
                get { return Quote(this.Schema) + "." + Quote(this.Table); }
            }

            /// <summary>Where the sessions come from, for status text and to tell whether the cache matches the settings</summary>
            /// <returns>e.g. "postgres@192.168.3.2:5432/superputty/superputty.sessions"</returns>
            public override string ToString()
            {
                return string.Format("{0}@{1}:{2}/{3}/{4}.{5}", this.User, this.Host, this.Port, this.Database, this.Schema, this.Table);
            }
        }

        /// <summary>Whether database sessions are turned on in Options</summary>
        public static bool Enabled
        {
            get { return SuperPuTTY.Settings.DatabaseEnabled && !string.IsNullOrWhiteSpace(SuperPuTTY.Settings.DatabaseHost); }
        }

        /// <summary>The connection from the saved settings</summary>
        /// <returns>The connection</returns>
        public static Connection FromSettings()
        {
            return new Connection
            {
                Host = SuperPuTTY.Settings.DatabaseHost,
                Port = SuperPuTTY.Settings.DatabasePort,
                Database = SuperPuTTY.Settings.DatabaseName,
                Schema = SuperPuTTY.Settings.DatabaseSchema,
                Table = SuperPuTTY.Settings.DatabaseTable,
                User = SuperPuTTY.Settings.DatabaseUser,
                StoredPassword = SuperPuTTY.Settings.DatabasePassword,
                SslMode = SuperPuTTY.Settings.DatabaseSslMode,
                ReadOnly = SuperPuTTY.Settings.DatabaseReadOnly
            };
        }

        /// <summary>A database, schema or table name the settings accept: a plain Postgres identifier</summary>
        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,62}$");

        /// <summary>Check a database, schema or table name</summary>
        /// <param name="name">The name</param>
        /// <param name="what">"Database", "Schema" or "Table", for the message</param>
        /// <param name="error">Why it's not accepted</param>
        /// <returns>true if it's a plain identifier: letters, digits and _, not starting with a digit, at most 63 long</returns>
        public static bool ValidateIdentifier(string name, string what, out string error)
        {
            error = Identifier.IsMatch(name ?? string.Empty) ? null
                : what + " must be letters, digits and _, not starting with a digit (at most 63)";
            return error == null;
        }

        /// <summary>Quote an identifier for SQL</summary>
        /// <param name="name">A checked identifier</param>
        /// <returns>"name", with any quote doubled</returns>
        private static string Quote(string name)
        {
            return "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Extra input to DPAPI, so other programs using DPAPI for this user can't decrypt the password by accident</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SuperPuTTY.DatabasePassword");

        /// <summary>A whole-field {key} placeholder, stored as it is rather than encrypted</summary>
        private static readonly Regex Placeholder = new Regex(@"^\{\w+\}$");

        /// <summary>
        /// The password as it's saved in the settings: a {key} placeholder as it is (its value lives in Environment
        /// Variables, where it can be secret), anything else encrypted with DPAPI for this Windows user
        /// </summary>
        /// <param name="password">The password as typed</param>
        /// <returns>What to store in Settings.DatabasePassword</returns>
        public static string ProtectPassword(string password)
        {
            if (string.IsNullOrEmpty(password) || Placeholder.IsMatch(password))
                return password ?? string.Empty;
            byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(encrypted);
        }

        /// <summary>The password to log in with: decrypted, or the placeholder's value</summary>
        /// <param name="stored">The stored password</param>
        /// <returns>The password; empty if it can't be decrypted (saved by another Windows user or computer)</returns>
        public static string ResolvePassword(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return string.Empty;
            if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                try
                {
                    byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(6)), Entropy,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("Could not decrypt the database password (saved by another Windows user or computer?); enter it again: {0}", ex.Message);
                    return string.Empty;
                }
            }
            return SessionVariables.Substitute(stored, SessionVariables.Load());
        }

        /// <summary>The Npgsql connection string; never logged, since it holds the password</summary>
        /// <param name="c">The connection</param>
        /// <param name="database">A database to use instead of the configured one, e.g. "postgres" to create it</param>
        /// <returns>The connection string</returns>
        private static string ConnectionString(Connection c, string database = null)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = c.Host,
                Port = c.Port,
                Database = database ?? c.Database,
                Username = c.User,
                Password = ResolvePassword(c.StoredPassword),
                Timeout = 8,
                CommandTimeout = 30,
                ApplicationName = "SuperPuTTY",
                Pooling = false
            };
            SslMode mode;
            if (Enum.TryParse(c.SslMode ?? string.Empty, true, out mode))
            {
                builder.SslMode = mode;
            }
            return builder.ConnectionString;
        }

        /// <summary>Open a connection</summary>
        /// <param name="c">The connection settings</param>
        /// <param name="database">A database to use instead of the configured one</param>
        /// <returns>The open connection; the caller disposes it</returns>
        private static NpgsqlConnection Open(Connection c, string database = null)
        {
            NpgsqlConnection connection = new NpgsqlConnection(ConnectionString(c, database));
            connection.Open();
            return connection;
        }

        #endregion

        #region Checking and creating the schema

        /// <summary>What <see cref="Probe"/> found</summary>
        public class ProbeResult
        {
            /// <summary>Logged in to the server</summary>
            public bool Connected { get; set; }

            /// <summary>The server's version, e.g. "16.4"</summary>
            public string ServerVersion { get; set; }

            /// <summary>The server's version as a number, e.g. 160004</summary>
            public int ServerVersionNumber { get; set; }

            /// <summary>The configured database exists</summary>
            public bool DatabaseExists { get; set; }

            /// <summary>The table exists</summary>
            public bool TableExists { get; set; }

            /// <summary>Columns the table lacks</summary>
            public List<string> MissingColumns { get; set; } = new List<string>();

            /// <summary>The change trigger exists</summary>
            public bool TriggerExists { get; set; }

            /// <summary>The user may add and change rows</summary>
            public bool CanWrite { get; set; }

            /// <summary>Rows in the table</summary>
            public long Rows { get; set; }

            /// <summary>Why the connection failed, when it did</summary>
            public string Error { get; set; }

            /// <summary>Whether "Create / upgrade schema" has something to do</summary>
            public bool NeedsSchema
            {
                get { return this.Connected && (!this.DatabaseExists || !this.TableExists || this.MissingColumns.Count > 0 || !this.TriggerExists); }
            }

            /// <summary>Whether the table can be read as it is</summary>
            public bool Ready
            {
                get { return this.Connected && this.DatabaseExists && this.TableExists && !this.MissingColumns.Contains("name"); }
            }

            /// <summary>A summary for the settings dialog</summary>
            /// <returns>A few lines</returns>
            public override string ToString()
            {
                if (!this.Connected)
                    return "Couldn't connect: " + this.Error;
                StringBuilder text = new StringBuilder("Connected to PostgreSQL " + this.ServerVersion + ".");
                if (this.Error != null)
                    return text.Append(" But looking at the table failed: " + this.Error).ToString();
                if (this.ServerVersionNumber > 0 && this.ServerVersionNumber < MinServerVersion)
                    text.Append(" PostgreSQL 13 or later is needed to create the table.");
                if (!this.DatabaseExists)
                    text.Append(" The database doesn't exist yet: Create schema makes it and the table.");
                else if (!this.TableExists)
                    text.Append(" The table doesn't exist yet: Create schema makes it.");
                else
                {
                    text.AppendFormat(" The table has {0} session{1}{2}.", this.Rows, this.Rows == 1 ? "" : "s", this.CanWrite ? "" : " (you can only read it)");
                    if (this.MissingColumns.Count > 0)
                        text.Append(" Missing columns: " + string.Join(", ", this.MissingColumns) + "; Create / upgrade schema adds them.");
                    else if (!this.TriggerExists)
                        text.Append(" Its change trigger is missing; Create / upgrade schema adds it.");
                }
                return text.ToString();
            }
        }

        /// <summary>
        /// Connect and look at the database and table without changing anything: for Test connection, and to decide
        /// whether "Create / upgrade schema" is needed
        /// </summary>
        /// <param name="c">The connection</param>
        /// <returns>What was found; <see cref="ProbeResult.Error"/> says why if it couldn't connect</returns>
        public static ProbeResult Probe(Connection c)
        {
            ProbeResult result = new ProbeResult();
            try
            {
                NpgsqlConnection connection;
                try
                {
                    connection = Open(c);
                    result.DatabaseExists = true;
                }
                catch (PostgresException ex) when (ex.SqlState == "3D000")
                {
                    // the database is missing; log in to the default one to tell that from a bad login
                    connection = Open(c, "postgres");
                }
                using (connection)
                {
                    result.Connected = true;
                    result.ServerVersion = connection.ServerVersion;
                    result.ServerVersionNumber = Convert.ToInt32(Scalar(connection, "SELECT current_setting('server_version_num')::int"));
                    if (!result.DatabaseExists)
                        return result;
                    string qualified = c.QualifiedTable;
                    result.TableExists = Scalar(connection, "SELECT to_regclass(@t) IS NOT NULL", "t", qualified) as bool? == true;
                    if (!result.TableExists)
                        return result;
                    HashSet<string> present = new HashSet<string>();
                    using (NpgsqlCommand command = new NpgsqlCommand(
                        "SELECT column_name FROM information_schema.columns WHERE table_schema = @s AND table_name = @t", connection))
                    {
                        command.Parameters.AddWithValue("s", c.Schema);
                        command.Parameters.AddWithValue("t", c.Table);
                        using (NpgsqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                                present.Add(reader.GetString(0));
                        }
                    }
                    result.MissingColumns = Columns.Select(col => col[0]).Where(name => !present.Contains(name)).ToList();
                    result.TriggerExists = Convert.ToInt64(Scalar(connection,
                        "SELECT count(*) FROM pg_trigger WHERE tgrelid = to_regclass(@t) AND tgname = @n", "t", qualified, "n", TriggerName(c))) > 0;
                    result.CanWrite = Scalar(connection, "SELECT has_table_privilege(@t, 'INSERT') AND has_table_privilege(@t, 'UPDATE')", "t", qualified) as bool? == true;
                    result.Rows = Convert.ToInt64(Scalar(connection, "SELECT count(*) FROM " + qualified));
                }
            }
            catch (Exception ex)
            {
                result.Error = Describe(ex);
            }
            return result;
        }

        /// <summary>The name of the table's change trigger</summary>
        /// <param name="c">The connection</param>
        /// <returns>e.g. "sessions_changed"</returns>
        private static string TriggerName(Connection c)
        {
            return c.Table + "_changed";
        }

        /// <summary>
        /// Create the database, schema and table if they're missing, add any missing columns, and (re)create the
        /// triggers that keep updated_at and updated_by current and announce changes. Safe to run again.
        /// </summary>
        /// <param name="c">The connection</param>
        /// <returns>What was done, for the settings dialog</returns>
        /// <exception cref="InvalidOperationException">The connection is read-only</exception>
        public static string CreateOrUpgradeSchema(Connection c)
        {
            if (c.ReadOnly)
                throw new InvalidOperationException("The database connection is read-only, so nothing is created or changed.");
            List<string> done = new List<string>();
            ProbeResult before = Probe(c);
            if (!before.Connected)
                throw new InvalidOperationException("Couldn't connect: " + before.Error);
            if (before.ServerVersionNumber < MinServerVersion)
                throw new InvalidOperationException("PostgreSQL 13 or later is needed; the server is " + before.ServerVersion + ".");
            if (!before.DatabaseExists)
            {
                using (NpgsqlConnection server = Open(c, "postgres"))
                {
                    Execute(server, "CREATE DATABASE " + Quote(c.Database));
                }
                done.Add("created database " + c.Database);
            }
            using (NpgsqlConnection connection = Open(c))
            using (NpgsqlTransaction transaction = connection.BeginTransaction())
            {
                string table = c.QualifiedTable;
                Execute(connection, "CREATE SCHEMA IF NOT EXISTS " + Quote(c.Schema));
                if (!before.TableExists)
                {
                    Execute(connection, "CREATE TABLE IF NOT EXISTS " + table + " ("
                        + string.Join(", ", Columns.Select(col => Quote(col[0]) + " " + col[1]))
                        + ", UNIQUE (folder, name)"
                        + ", CHECK (name <> '' AND strpos(name, '/') = 0)"
                        + ", CHECK (folder = '' OR (left(folder, 1) <> '/' AND right(folder, 1) <> '/')))");
                    Execute(connection, "COMMENT ON TABLE " + table + " IS 'SuperPuTTY sessions: folder is the tree path (e.g. Lab/Core), '''' at the top'");
                    done.Add("created table " + c.Schema + "." + c.Table);
                }
                else
                {
                    foreach (string[] column in Columns.Where(col => before.MissingColumns.Contains(col[0]) && col[0] != "id"))
                    {
                        Execute(connection, "ALTER TABLE " + table + " ADD COLUMN IF NOT EXISTS " + Quote(column[0]) + " " + column[1]);
                        done.Add("added column " + column[0]);
                    }
                }
                string function = Quote(c.Schema) + "." + Quote(c.Table + "_touch");
                Execute(connection, "CREATE OR REPLACE FUNCTION " + function + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN "
                    + "IF TG_OP = 'INSERT' OR TG_OP = 'UPDATE' THEN NEW.updated_at := now(); NEW.updated_by := current_user; RETURN NEW; END IF; "
                    + "RETURN OLD; END $$");
                string notify = Quote(c.Schema) + "." + Quote(c.Table + "_notify");
                Execute(connection, "CREATE OR REPLACE FUNCTION " + notify + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN "
                    + "PERFORM pg_notify('" + NotifyChannel + "', TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME); RETURN NULL; END $$");
                Execute(connection, "DROP TRIGGER IF EXISTS " + Quote(c.Table + "_touch") + " ON " + table);
                Execute(connection, "CREATE TRIGGER " + Quote(c.Table + "_touch") + " BEFORE INSERT OR UPDATE ON " + table
                    + " FOR EACH ROW EXECUTE FUNCTION " + function + "()");
                Execute(connection, "DROP TRIGGER IF EXISTS " + Quote(TriggerName(c)) + " ON " + table);
                Execute(connection, "CREATE TRIGGER " + Quote(TriggerName(c)) + " AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON " + table
                    + " FOR EACH STATEMENT EXECUTE FUNCTION " + notify + "()");
                if (!before.TriggerExists)
                    done.Add("added the change triggers");
                transaction.Commit();
            }
            Log.InfoFormat("Database schema ready at {0}: {1}", c, done.Count == 0 ? "already up to date" : string.Join(", ", done));
            return done.Count == 0 ? "The schema is already up to date." : "Done: " + string.Join(", ", done) + ".";
        }

        /// <summary>Run a statement</summary>
        /// <param name="connection">An open connection</param>
        /// <param name="sql">The statement</param>
        private static void Execute(NpgsqlConnection connection, string sql)
        {
            using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Run a query for one value</summary>
        /// <param name="connection">An open connection</param>
        /// <param name="sql">The query</param>
        /// <param name="parameters">Parameter names and values, alternating</param>
        /// <returns>The value</returns>
        private static object Scalar(NpgsqlConnection connection, string sql, params object[] parameters)
        {
            using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
            {
                for (int i = 0; i + 1 < parameters.Length; i += 2)
                {
                    command.Parameters.AddWithValue((string)parameters[i], parameters[i + 1]);
                }
                return command.ExecuteScalar();
            }
        }

        /// <summary>A short, readable reason for a failure</summary>
        /// <param name="ex">The exception</param>
        /// <returns>e.g. "password authentication failed for user "postgres""</returns>
        private static string Describe(Exception ex)
        {
            PostgresException postgres = ex as PostgresException ?? ex.InnerException as PostgresException;
            if (postgres != null)
                return postgres.MessageText;
            Exception inner = ex;
            while (inner.InnerException != null && (inner is NpgsqlException || inner is AggregateException))
                inner = inner.InnerException;
            return inner.Message;
        }

        #endregion

        #region Reading the table

        /// <summary>
        /// Read every row, a page at a time in (folder, name, id) order, as sessions with IDs under <see cref="IdPrefix"/>
        /// </summary>
        /// <param name="c">The connection</param>
        /// <param name="progress">Told the number read so far after each page; may be null</param>
        /// <returns>The sessions</returns>
        public static List<SessionData> FetchAll(Connection c, Action<int> progress = null)
        {
            bool writable;
            return FetchAll(c, progress, out writable);
        }

        /// <summary>Read every row (see <see cref="FetchAll(Connection, Action{int})"/>), and whether the login may change them</summary>
        /// <param name="c">The connection</param>
        /// <param name="progress">Told the number read so far after each page; may be null</param>
        /// <param name="writable">Whether the connection isn't read-only and the login may add, change and remove rows</param>
        /// <returns>The sessions</returns>
        public static List<SessionData> FetchAll(Connection c, Action<int> progress, out bool writable)
        {
            List<SessionData> sessions = new List<SessionData>();
            using (NpgsqlConnection connection = Open(c))
            {
                writable = !c.ReadOnly && HasWritePrivilege(connection, c);
                string lastFolder = null, lastName = null;
                Guid lastId = Guid.Empty;
                while (true)
                {
                    int read = 0;
                    using (NpgsqlCommand command = new NpgsqlCommand(
                        "SELECT id, folder, name, protocol, host, port, username, putty_profile, extra_args, icon, notes, spsl_script, "
                        + "rdp_options, local_path, remote_path, updated_at FROM " + c.QualifiedTable
                        + (lastName == null ? "" : " WHERE (folder, name, id) > (@f, @n, @i)")
                        + " ORDER BY folder, name, id LIMIT @limit", connection))
                    {
                        if (lastName != null)
                        {
                            command.Parameters.AddWithValue("f", lastFolder);
                            command.Parameters.AddWithValue("n", lastName);
                            command.Parameters.AddWithValue("i", lastId);
                        }
                        command.Parameters.AddWithValue("limit", PageSize);
                        using (NpgsqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                read++;
                                lastId = reader.GetGuid(0);
                                lastFolder = reader.GetString(1);
                                lastName = reader.GetString(2);
                                sessions.Add(ToSession(reader));
                            }
                        }
                    }
                    progress?.Invoke(sessions.Count);
                    if (read < PageSize)
                        break;
                }
            }
            return sessions;
        }

        /// <summary>A row as a session</summary>
        /// <param name="reader">The reader, on a row of <see cref="FetchAll"/>'s query</param>
        /// <returns>The session</returns>
        private static SessionData ToSession(NpgsqlDataReader reader)
        {
            string folder = reader.GetString(1).Trim('/');
            string name = reader.GetString(2);
            ConnectionProtocol protocol;
            if (!Enum.TryParse(reader.GetString(3), true, out protocol))
            {
                protocol = ConnectionProtocol.SSH;
            }
            SessionData session = new SessionData
            {
                SessionName = name,
                Proto = protocol,
                Host = reader.GetString(4),
                Port = reader.GetInt32(5),
                Username = reader.GetString(6),
                PuttySession = reader.GetString(7),
                ExtraArgs = reader.GetString(8),
                ImageKey = Text(reader, 9),
                Note = Text(reader, 10),
                SPSLFileName = Text(reader, 11),
                RdpOptions = Text(reader, 12) ?? string.Empty,
                LocalPath = Text(reader, 13),
                RemotePath = Text(reader, 14),
                DatabaseId = reader.GetGuid(0).ToString(),
                DatabaseVersion = reader.GetDateTime(15).Ticks.ToString()
            };
            session.SessionId = MakeSessionId(folder, name);
            return session;
        }

        /// <summary>A nullable text column</summary>
        /// <param name="reader">The reader</param>
        /// <param name="column">The column</param>
        /// <returns>Its value, or null</returns>
        private static string Text(NpgsqlDataReader reader, int column)
        {
            return reader.IsDBNull(column) ? null : reader.GetString(column);
        }

        /// <summary>A database session's ID from its folder and name</summary>
        /// <param name="folder">The folder path, e.g. "Lab/Core", or empty at the top</param>
        /// <param name="name">The session name</param>
        /// <returns>e.g. "@db/Lab/Core/sw1"</returns>
        public static string MakeSessionId(string folder, string name)
        {
            return IdPrefix + "/" + (string.IsNullOrEmpty(folder) ? name : folder + "/" + name);
        }

        #endregion

        #region Writing the table

        /// <summary>The columns a session is written to, in the order <see cref="AddSessionParameters"/> fills them</summary>
        private const string WriteColumns = "folder, name, protocol, host, port, username, putty_profile, extra_args, icon, notes, "
            + "spsl_script, rdp_options, local_path, remote_path";

        /// <summary>The parameter names for <see cref="WriteColumns"/></summary>
        private const string WriteParameters = "@folder, @name, @protocol, @host, @port, @username, @putty_profile, @extra_args, @icon, "
            + "@notes, @spsl_script, @rdp_options, @local_path, @remote_path";

        /// <summary>Fill a command's parameters from a session</summary>
        /// <param name="command">The command</param>
        /// <param name="session">The session</param>
        /// <param name="folder">Its folder path, e.g. "Lab/Core", or empty at the top</param>
        private static void AddSessionParameters(NpgsqlCommand command, SessionData session, string folder)
        {
            command.Parameters.AddWithValue("folder", (folder ?? string.Empty).Trim('/'));
            command.Parameters.AddWithValue("name", session.SessionName ?? string.Empty);
            command.Parameters.AddWithValue("protocol", session.Proto.ToString());
            command.Parameters.AddWithValue("host", session.Host ?? string.Empty);
            command.Parameters.AddWithValue("port", session.Port);
            command.Parameters.AddWithValue("username", session.Username ?? string.Empty);
            command.Parameters.AddWithValue("putty_profile", session.PuttySession ?? string.Empty);
            command.Parameters.AddWithValue("extra_args", session.ExtraArgs ?? string.Empty);
            command.Parameters.AddWithValue("icon", (object)NullIfEmpty(session.ImageKey) ?? DBNull.Value);
            command.Parameters.AddWithValue("notes", (object)NullIfEmpty(session.Note) ?? DBNull.Value);
            command.Parameters.AddWithValue("spsl_script", (object)NullIfEmpty(session.SPSLFileName) ?? DBNull.Value);
            command.Parameters.AddWithValue("rdp_options", (object)NullIfEmpty(session.RdpOptions) ?? DBNull.Value);
            command.Parameters.AddWithValue("local_path", (object)NullIfEmpty(session.LocalPath) ?? DBNull.Value);
            command.Parameters.AddWithValue("remote_path", (object)NullIfEmpty(session.RemotePath) ?? DBNull.Value);
        }

        /// <summary>Null for empty text, so optional columns stay NULL</summary>
        /// <param name="text">The text</param>
        /// <returns>The text, or null</returns>
        private static string NullIfEmpty(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>Refuse to write when the connection is read-only</summary>
        /// <param name="c">The connection</param>
        /// <exception cref="InvalidOperationException">It's read-only</exception>
        private static void EnsureWritable(Connection c)
        {
            if (c.ReadOnly)
                throw new InvalidOperationException("The database connection is read-only, so nothing is created or changed.");
        }

        /// <summary>Run a write, turning Postgres' errors for duplicate and invalid names into readable ones</summary>
        /// <param name="c">The connection</param>
        /// <param name="write">The write, given an open connection</param>
        /// <returns>What the write returned</returns>
        /// <exception cref="InvalidOperationException">The write failed; the message says why</exception>
        private static T Write<T>(Connection c, Func<NpgsqlConnection, T> write)
        {
            EnsureWritable(c);
            try
            {
                using (NpgsqlConnection connection = Open(c))
                {
                    return write(connection);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                throw new InvalidOperationException("A session with that name is already in that folder in the database.", ex);
            }
            catch (PostgresException ex) when (ex.SqlState == "23514")
            {
                throw new InvalidOperationException("The database refused the name or folder: names can't be empty or contain /.", ex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException(Describe(ex), ex);
            }
        }

        /// <summary>Add a session to the table</summary>
        /// <param name="c">The connection</param>
        /// <param name="session">The session; its name is used, not its ID</param>
        /// <param name="folder">The folder to put it in, e.g. "Lab/Core", or empty at the top</param>
        /// <returns>The new row's id</returns>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed</exception>
        public static string Insert(Connection c, SessionData session, string folder)
        {
            return Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("INSERT INTO " + c.QualifiedTable + " (" + WriteColumns + ") VALUES ("
                    + WriteParameters + ") RETURNING id", connection))
                {
                    AddSessionParameters(command, session, folder);
                    return command.ExecuteScalar().ToString();
                }
            });
        }

        /// <summary>
        /// Save changes to a session's row, only if the row hasn't changed since it was read (its updated_at still
        /// matches <see cref="SessionData.DatabaseVersion"/>)
        /// </summary>
        /// <param name="c">The connection</param>
        /// <param name="id">The row's id</param>
        /// <param name="version">Its updated_at when read, as ticks</param>
        /// <param name="session">The changed session</param>
        /// <param name="folder">Its folder</param>
        /// <exception cref="InvalidOperationException">It's read-only, someone else changed or deleted it, or the write failed</exception>
        public static void Update(Connection c, string id, string version, SessionData session, string folder)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
                throw new InvalidOperationException("This session was read by an older version; Refresh, then make your change again.");
            int rows = Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("UPDATE " + c.QualifiedTable + " SET (" + WriteColumns + ") = ("
                    + WriteParameters + ") WHERE id = @id AND updated_at = @version", connection))
                {
                    AddSessionParameters(command, session, folder);
                    command.Parameters.AddWithValue("id", Guid.Parse(id));
                    command.Parameters.AddWithValue("version", new DateTime(long.Parse(version), DateTimeKind.Utc));
                    return command.ExecuteNonQuery();
                }
            });
            if (rows == 0)
                throw new InvalidOperationException("Someone else changed or deleted this session since it was read. Refresh, then make your change again.");
        }

        /// <summary>Remove a session's row</summary>
        /// <param name="c">The connection</param>
        /// <param name="id">The row's id</param>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed</exception>
        public static void Delete(Connection c, string id)
        {
            Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("DELETE FROM " + c.QualifiedTable + " WHERE id = @id", connection))
                {
                    command.Parameters.AddWithValue("id", Guid.Parse(id));
                    return command.ExecuteNonQuery();
                }
            });
        }

        /// <summary>The SQL condition for a folder and everything under it</summary>
        private const string InFolder = "(folder = @from OR left(folder, length(@from) + 1) = @from || '/')";

        /// <summary>Rename a folder: every session in it, and in folders under it, moves to the new path</summary>
        /// <param name="c">The connection</param>
        /// <param name="from">The folder's path, e.g. "Lab/Core"</param>
        /// <param name="to">Its new path, e.g. "Lab/Backbone"</param>
        /// <returns>How many sessions moved</returns>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed (e.g. a name clash)</exception>
        public static int RenameFolder(Connection c, string from, string to)
        {
            return Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("UPDATE " + c.QualifiedTable
                    + " SET folder = @to || substr(folder, length(@from) + 1) WHERE " + InFolder, connection))
                {
                    command.Parameters.AddWithValue("from", from.Trim('/'));
                    command.Parameters.AddWithValue("to", to.Trim('/'));
                    return command.ExecuteNonQuery();
                }
            });
        }

        /// <summary>Remove a folder: every session in it and in folders under it</summary>
        /// <param name="c">The connection</param>
        /// <param name="folder">The folder's path</param>
        /// <returns>How many sessions were removed</returns>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed</exception>
        public static int DeleteFolder(Connection c, string folder)
        {
            return Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("DELETE FROM " + c.QualifiedTable + " WHERE " + InFolder, connection))
                {
                    command.Parameters.AddWithValue("from", folder.Trim('/'));
                    return command.ExecuteNonQuery();
                }
            });
        }

        /// <summary>Whether this login may add, change and remove rows</summary>
        /// <param name="connection">An open connection</param>
        /// <param name="c">The connection settings</param>
        /// <returns>true if it has INSERT, UPDATE and DELETE on the table</returns>
        private static bool HasWritePrivilege(NpgsqlConnection connection, Connection c)
        {
            return Scalar(connection, "SELECT has_table_privilege(@t, 'INSERT') AND has_table_privilege(@t, 'UPDATE') AND has_table_privilege(@t, 'DELETE')",
                "t", c.QualifiedTable) as bool? == true;
        }

        #endregion

        #region Cache

        /// <summary>The local copy of the table, so the sessions open when Postgres can't be reached</summary>
        public class CacheFile
        {
            /// <summary>Where the rows came from (<see cref="Connection.ToString"/>); a cache from another source isn't used</summary>
            public string Source { get; set; }

            /// <summary>When the rows were read</summary>
            public DateTime SyncedAt { get; set; }

            /// <summary>The sessions</summary>
            public List<SessionData> Sessions { get; set; } = new List<SessionData>();
        }

        /// <summary>The cache file's path; set it to use another (tests)</summary>
        public static string CachePath { get; set; }

        /// <summary>The cache file: DatabaseSessions.cache.xml beside the user's settings</summary>
        private static string CacheFileName
        {
            get { return CachePath ?? Path.Combine(SuperPuTTY.UserSettingsFolder, "DatabaseSessions.cache.xml"); }
        }

        /// <summary>Write the cache</summary>
        /// <param name="cache">The rows and where they came from</param>
        public static void SaveCache(CacheFile cache)
        {
            string file = CacheFileName;
            string temp = file + ".tmp";
            XmlSerializer serializer = new XmlSerializer(typeof(CacheFile));
            using (StreamWriter writer = new StreamWriter(temp))
            {
                serializer.Serialize(writer, cache);
            }
            // replace in one step, so a crash mid-write never leaves half a cache
            if (File.Exists(file))
                File.Replace(temp, file, null);
            else
                File.Move(temp, file);
        }

        /// <summary>Read the cache</summary>
        /// <returns>The cache, or null if there's none or it can't be read</returns>
        public static CacheFile LoadCache()
        {
            string file = CacheFileName;
            try
            {
                if (!File.Exists(file))
                    return null;
                XmlSerializer serializer = new XmlSerializer(typeof(CacheFile));
                using (StreamReader reader = new StreamReader(file))
                {
                    return (CacheFile)serializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the database sessions cache " + file, ex);
                return null;
            }
        }

        #endregion

        #region Status and syncing

        /// <summary>How the database sessions are doing</summary>
        public enum SyncState
        {
            /// <summary>Turned off in Options</summary>
            Off,
            /// <summary>Reading the table</summary>
            Loading,
            /// <summary>Read; the sessions are current</summary>
            Online,
            /// <summary>Postgres can't be reached; the cached sessions are shown and it's retried</summary>
            Offline
        }

        /// <summary>The current state</summary>
        public static SyncState State { get; private set; } = SyncState.Off;

        /// <summary>The last failure, while <see cref="State"/> is Offline</summary>
        public static string LastError { get; private set; }

        /// <summary>When the sessions shown were read (from the table, or the cache's time when offline)</summary>
        public static DateTime? SyncedAt { get; private set; }

        /// <summary>How many database sessions there are</summary>
        public static int Count { get; private set; }

        /// <summary>
        /// Whether sessions can be added and changed from the tree: the table was read, the connection isn't read-only,
        /// and the login may write it. False while offline or before the first read.
        /// </summary>
        public static bool Writable { get; private set; }

        /// <summary>Rows read so far while <see cref="State"/> is Loading</summary>
        public static int LoadedSoFar { get; private set; }

        /// <summary>Raised on the UI thread whenever the state, count or error changes</summary>
        public static event EventHandler StatusChanged;

        /// <summary>The UI thread's context, so background reads report back on it</summary>
        private static SynchronizationContext ui;

        /// <summary>Retries while offline</summary>
        private static System.Windows.Forms.Timer retryTimer;

        /// <summary>Whether a read is running</summary>
        private static bool syncing;

        /// <summary>
        /// Show the cached sessions at once, then read the table in the background. Called on the UI thread at start-up
        /// and after the database settings change; turns everything off when the feature is.
        /// </summary>
        public static void Start()
        {
            CaptureUiThread();
            StopRetrying();
            Writable = false;
            if (!Enabled)
            {
                SuperPuTTY.ReplaceDatabaseSessions(new List<SessionData>());
                SetStatus(SyncState.Off, null, null, 0);
                return;
            }
            Connection c = FromSettings();
            CacheFile cache = LoadCache();
            if (cache != null && cache.Source == c.ToString())
            {
                SuperPuTTY.ReplaceDatabaseSessions(cache.Sessions);
                SetStatus(SyncState.Loading, null, cache.SyncedAt, cache.Sessions.Count);
            }
            else
            {
                SuperPuTTY.ReplaceDatabaseSessions(new List<SessionData>());
                SetStatus(SyncState.Loading, null, null, 0);
            }
            BeginSync();
        }

        /// <summary>Read the table in the background (Refresh); does nothing if a read is already running or it's turned off</summary>
        public static void BeginSync()
        {
            CaptureUiThread();
            if (syncing || !Enabled)
                return;
            syncing = true;
            StopRetrying();
            Connection c = FromSettings();
            LoadedSoFar = 0;
            SetStatus(SyncState.Loading, null, SyncedAt, Count);
            Task.Run(() =>
            {
                try
                {
                    bool writable;
                    List<SessionData> sessions = FetchAll(c, n => Post(() => { LoadedSoFar = n; StatusChanged?.Invoke(null, EventArgs.Empty); }), out writable);
                    CacheFile cache = new CacheFile { Source = c.ToString(), SyncedAt = DateTime.Now, Sessions = sessions };
                    try
                    {
                        SaveCache(cache);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Could not write the database sessions cache", ex);
                    }
                    Post(() =>
                    {
                        syncing = false;
                        Writable = writable;
                        SuperPuTTY.ReplaceDatabaseSessions(sessions);
                        SetStatus(SyncState.Online, null, cache.SyncedAt, sessions.Count);
                        Log.InfoFormat("Read {0} database sessions from {1}", sessions.Count, c);
                    });
                }
                catch (Exception ex)
                {
                    string error = Describe(ex);
                    Log.WarnFormat("Could not read database sessions from {0}: {1}", c, error);
                    Post(() =>
                    {
                        syncing = false;
                        Writable = false;
                        SetStatus(SyncState.Offline, error, SyncedAt, Count);
                        StartRetrying();
                    });
                }
            });
        }

        /// <summary>
        /// Remember the UI thread's context, if this is the UI thread. Only a WinForms context will do: a plain one
        /// (e.g. on a worker thread) would run the results on the thread pool, where touching the tree throws.
        /// </summary>
        private static void CaptureUiThread()
        {
            if (SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext)
            {
                ui = SynchronizationContext.Current;
            }
        }

        /// <summary>Run an action on the UI thread: through its context, or the main window, or at once if there's no UI (tests)</summary>
        /// <param name="action">The action</param>
        private static void Post(Action action)
        {
            System.Windows.Forms.Form main = SuperPuTTY.MainForm;
            if (ui != null)
                ui.Post(_ => action(), null);
            else if (main != null && main.IsHandleCreated)
                main.BeginInvoke(action);
            else
                action();
        }

        /// <summary>Set the status and tell the Sessions tree</summary>
        /// <param name="state">The state</param>
        /// <param name="error">The failure, when offline</param>
        /// <param name="syncedAt">When the sessions shown were read</param>
        /// <param name="count">How many there are</param>
        private static void SetStatus(SyncState state, string error, DateTime? syncedAt, int count)
        {
            State = state;
            LastError = error;
            SyncedAt = syncedAt;
            Count = count;
            StatusChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Try again every <see cref="RetryInterval"/> while offline</summary>
        private static void StartRetrying()
        {
            if (ui == null)
                return;
            if (retryTimer == null)
            {
                retryTimer = new System.Windows.Forms.Timer();
                retryTimer.Tick += (s, e) => BeginSync();
            }
            retryTimer.Interval = (int)RetryInterval.TotalMilliseconds;
            retryTimer.Start();
        }

        /// <summary>Stop retrying</summary>
        private static void StopRetrying()
        {
            retryTimer?.Stop();
        }

        /// <summary>A one-line status for the tree's root, e.g. "3,214" or "offline, cached 09:41"</summary>
        /// <returns>The text to show in brackets after "Database sessions"</returns>
        public static string StatusText()
        {
            string readOnly = SuperPuTTY.Settings.DatabaseReadOnly || (State == SyncState.Online && !Writable) ? ", read-only" : "";
            switch (State)
            {
                case SyncState.Loading:
                    return LoadedSoFar > 0 ? string.Format("loading {0:N0}...", LoadedSoFar) : "loading...";
                case SyncState.Online:
                    return string.Format("{0:N0}{1}", Count, readOnly);
                case SyncState.Offline:
                    return SyncedAt.HasValue
                        ? string.Format("offline, cached {0:t}{1}", SyncedAt.Value, readOnly)
                        : "offline";
                default:
                    return "off";
            }
        }

        #endregion
    }
}
