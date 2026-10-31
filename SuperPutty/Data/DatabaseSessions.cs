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
            new[] { "locked", "boolean NOT NULL DEFAULT false" },
            new[] { "locked_fields", "text[] NOT NULL DEFAULT '{}'" },
            new[] { "managed_by", "text" },
            new[] { "external_id", "text" },
            new[] { "created_at", "timestamptz NOT NULL DEFAULT now()" },
            new[] { "updated_at", "timestamptz NOT NULL DEFAULT now()" },
            new[] { "updated_by", "text NOT NULL DEFAULT current_user" },
            new[] { "deleted_at", "timestamptz" }
        };

        /// <summary>
        /// Who is making a change, for updated_by: the Nickname SuperPuTTY sets on each connection (several people
        /// often share one database login), or the database login for other programs
        /// </summary>
        private const string ChangedBy = "coalesce(nullif(current_setting('superputty.nickname', true), ''), current_user)";

        /// <summary>
        /// The columns locked_fields may name, which Edit Session greys out (dlgEditSession.LockedFields). The guard
        /// trigger compares any column named there, so other names work in the database too.
        /// </summary>
        public static readonly string[] LockableFields =
        {
            "folder", "name", "protocol", "host", "port", "username", "putty_profile", "extra_args", "icon", "notes",
            "spsl_script", "rdp_options", "local_path", "remote_path"
        };

        /// <summary>The application name SuperPuTTY connects with; the guard trigger enforces locks only for it</summary>
        public const string ApplicationName = "SuperPuTTY";

        /// <summary>The prefix of the guard trigger's error messages, so they're shown as locks rather than failures</summary>
        private const string LockedPrefix = "locked: ";

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

            /// <summary>Who this user is in updated_by, e.g. "Tim"; told to Postgres on every connection</summary>
            public string Nickname { get; set; }

            /// <summary>The table's quoted, schema-qualified name for SQL</summary>
            public string QualifiedTable
            {
                get { return Quote(this.Schema) + "." + Quote(this.Table); }
            }

            /// <summary>
            /// The quoted name of the one-row table whose version the change trigger raises on every change, e.g.
            /// "superputty"."sessions_version", so a change can be noticed without reading the sessions
            /// </summary>
            public string QualifiedVersionTable
            {
                get { return Quote(this.Schema) + "." + Quote(this.Table + "_version"); }
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
                ReadOnly = SuperPuTTY.Settings.DatabaseReadOnly,
                // set up before Nickname was required: the Windows user still tells people apart
                Nickname = string.IsNullOrWhiteSpace(SuperPuTTY.Settings.DatabaseNickname) ? Environment.UserName : SuperPuTTY.Settings.DatabaseNickname.Trim()
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
                ApplicationName = ApplicationName,
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
            if (!string.IsNullOrWhiteSpace(c.Nickname))
            {
                try
                {
                    // read by the touch trigger for updated_by; a plain setting, so any login may set it
                    Scalar(connection, "SELECT set_config('superputty.nickname', @n, false)", "n", c.Nickname.Trim());
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
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

            /// <summary>The version table, which the change trigger raises so SuperPuTTY notices changes, exists</summary>
            public bool ChangeCounterExists { get; set; }

            /// <summary>The user may add and change rows</summary>
            public bool CanWrite { get; set; }

            /// <summary>Rows in the table</summary>
            public long Rows { get; set; }

            /// <summary>Why the connection failed, when it did</summary>
            public string Error { get; set; }

            /// <summary>Whether "Create / upgrade schema" has something to do</summary>
            public bool NeedsSchema
            {
                get { return this.Connected && (!this.DatabaseExists || !this.TableExists || this.MissingColumns.Count > 0 || !this.TriggerExists || !this.ChangeCounterExists); }
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
                    else if (!this.TriggerExists || !this.ChangeCounterExists)
                        text.Append(" Its triggers are missing or out of date; Create / upgrade schema adds them.");
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
                        "SELECT count(*) FROM pg_trigger WHERE tgrelid = to_regclass(@t) AND tgname = ANY(@n)", "t", qualified,
                        "n", new[] { TriggerName(c), c.Table + "_touch", c.Table + "_guard" })) == 3;
                    // the trigger function must raise it too: an older SuperPuTTY's Create / upgrade puts back one that doesn't
                    result.ChangeCounterExists = Scalar(connection, "SELECT to_regclass(@v) IS NOT NULL AND EXISTS (SELECT 1 FROM pg_proc p "
                        + "JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = @s AND p.proname = @f AND strpos(p.prosrc, @t) > 0)",
                        "v", c.QualifiedVersionTable, "s", c.Schema, "f", c.Table + "_notify", "t", c.Table + "_version") as bool? == true;
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
                // an inventory system's own key: upserts match on it, so a device renamed or moved there updates its session
                Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS " + Quote(c.Table + "_external_id") + " ON " + table
                    + " (managed_by, external_id) WHERE external_id IS NOT NULL");
                string function = Quote(c.Schema) + "." + Quote(c.Table + "_touch");
                Execute(connection, "CREATE OR REPLACE FUNCTION " + function + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN "
                    + "IF TG_OP = 'INSERT' OR TG_OP = 'UPDATE' THEN NEW.updated_at := now(); NEW.updated_by := " + ChangedBy + "; RETURN NEW; END IF; "
                    + "RETURN OLD; END $$");
                // one row whose version goes up with every change, so each SuperPuTTY can check for changes every few
                // seconds without reading the sessions; anyone may read it
                string versions = c.QualifiedVersionTable;
                bool newCounter = !before.ChangeCounterExists;
                Execute(connection, "CREATE TABLE IF NOT EXISTS " + versions + " (version bigint NOT NULL, changed_at timestamptz NOT NULL DEFAULT now())");
                Execute(connection, "INSERT INTO " + versions + " (version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM " + versions + ")");
                Execute(connection, "GRANT SELECT ON " + versions + " TO PUBLIC");
                Execute(connection, "COMMENT ON TABLE " + versions + " IS 'Raised on every change to " + c.Table + "; SuperPuTTY reads it to notice changes'");
                // SECURITY DEFINER: logins that may change sessions needn't be granted the version table too
                string notify = Quote(c.Schema) + "." + Quote(c.Table + "_notify");
                Execute(connection, "CREATE OR REPLACE FUNCTION " + notify + "() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER "
                    + "SET search_path = pg_catalog, pg_temp AS $$ BEGIN "
                    + "UPDATE " + versions + " SET version = version + 1, changed_at = now(); "
                    + "PERFORM pg_notify('" + NotifyChannel + "', TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME); RETURN NULL; END $$");
                if (newCounter)
                    done.Add("added the change counter " + c.Table + "_version");
                Execute(connection, "DROP TRIGGER IF EXISTS " + Quote(c.Table + "_touch") + " ON " + table);
                Execute(connection, "CREATE TRIGGER " + Quote(c.Table + "_touch") + " BEFORE INSERT OR UPDATE ON " + table
                    + " FOR EACH ROW EXECUTE FUNCTION " + function + "()");
                // locks: SuperPuTTY may not change a locked row, the fields locked_fields names, or the locks themselves;
                // other programs (the scripts that manage the table) connect under another name and aren't stopped
                string guard = Quote(c.Schema) + "." + Quote(c.Table + "_guard");
                Execute(connection, "CREATE OR REPLACE FUNCTION " + guard + "() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE f text; BEGIN "
                    + "IF coalesce(current_setting('application_name', true), '') <> '" + ApplicationName + "' THEN "
                    + "IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW; END IF; "
                    + "IF TG_OP = 'DELETE' THEN "
                    + "IF OLD.locked THEN RAISE EXCEPTION '" + LockedPrefix + "\"%\" is locked in the database', OLD.name; END IF; RETURN OLD; END IF; "
                    + "IF NEW.locked IS DISTINCT FROM OLD.locked OR NEW.locked_fields IS DISTINCT FROM OLD.locked_fields THEN "
                    + "RAISE EXCEPTION '" + LockedPrefix + "the locks on \"%\" can only be changed in the database', OLD.name; END IF; "
                    + "IF NEW.managed_by IS DISTINCT FROM OLD.managed_by OR NEW.external_id IS DISTINCT FROM OLD.external_id "
                    + "OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN "
                    + "RAISE EXCEPTION '" + LockedPrefix + "who manages \"%\", and when it was created, can only be changed in the database', OLD.name; END IF; "
                    + "IF OLD.locked AND (to_jsonb(NEW) - 'updated_at' - 'updated_by') IS DISTINCT FROM (to_jsonb(OLD) - 'updated_at' - 'updated_by') THEN "
                    + "RAISE EXCEPTION '" + LockedPrefix + "\"%\" is locked in the database', OLD.name; END IF; "
                    + "FOREACH f IN ARRAY OLD.locked_fields LOOP "
                    + "IF (to_jsonb(NEW) -> f) IS DISTINCT FROM (to_jsonb(OLD) -> f) THEN "
                    + "RAISE EXCEPTION '" + LockedPrefix + "% of \"%\" is locked in the database', f, OLD.name; END IF; END LOOP; "
                    + "RETURN NEW; END $$");
                Execute(connection, "DROP TRIGGER IF EXISTS " + Quote(c.Table + "_guard") + " ON " + table);
                Execute(connection, "CREATE TRIGGER " + Quote(c.Table + "_guard") + " BEFORE UPDATE OR DELETE ON " + table
                    + " FOR EACH ROW EXECUTE FUNCTION " + guard + "()");
                Execute(connection, "DROP TRIGGER IF EXISTS " + Quote(TriggerName(c)) + " ON " + table);
                Execute(connection, "CREATE TRIGGER " + Quote(TriggerName(c)) + " AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON " + table
                    + " FOR EACH STATEMENT EXECUTE FUNCTION " + notify + "()");
                if (!before.TriggerExists)
                    done.Add("added the change and lock triggers");
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
            bool writable, hasLocks;
            return FetchAll(c, progress, out writable, out hasLocks);
        }

        /// <summary>Read every row (see <see cref="FetchAll(Connection, Action{int})"/>), and whether the login may change them</summary>
        /// <param name="c">The connection</param>
        /// <param name="progress">Told the number read so far after each page; may be null</param>
        /// <param name="writable">Whether the connection isn't read-only and the login may add, change and remove rows</param>
        /// <param name="hasLocks">Whether the table has the lock and inventory columns; a table made before them reads as unlocked until upgraded</param>
        /// <returns>The sessions</returns>
        public static List<SessionData> FetchAll(Connection c, Action<int> progress, out bool writable, out bool hasLocks)
        {
            string changeToken;
            return FetchAll(c, progress, out writable, out hasLocks, out changeToken);
        }

        /// <summary>
        /// Read every row that isn't hidden by deleted_at (see <see cref="FetchAll(Connection, Action{int})"/>), whether
        /// the login may change them, and the table's change token as it was before reading, for <see cref="CheckForChanges"/>
        /// </summary>
        /// <param name="c">The connection</param>
        /// <param name="progress">Told the number read so far after each page; may be null</param>
        /// <param name="writable">Whether the connection isn't read-only and the login may add, change and remove rows</param>
        /// <param name="hasLocks">Whether the table has the lock and inventory columns; a table made before them reads as unlocked until upgraded</param>
        /// <param name="changeToken">What <see cref="ReadChangeToken"/> returned just before the rows were read</param>
        /// <returns>The sessions</returns>
        public static List<SessionData> FetchAll(Connection c, Action<int> progress, out bool writable, out bool hasLocks, out string changeToken)
        {
            List<SessionData> sessions = new List<SessionData>();
            using (NpgsqlConnection connection = Open(c))
            {
                // taken first: a change made while the pages are read then shows up at the next check
                changeToken = ReadChangeToken(connection, c);
                writable = !c.ReadOnly && HasWritePrivilege(connection, c);
                HashSet<string> present = new HashSet<string>();
                using (NpgsqlCommand columns = new NpgsqlCommand(
                    "SELECT column_name FROM information_schema.columns WHERE table_schema = @s AND table_name = @t", connection))
                {
                    columns.Parameters.AddWithValue("s", c.Schema);
                    columns.Parameters.AddWithValue("t", c.Table);
                    using (NpgsqlDataReader reader = columns.ExecuteReader())
                    {
                        while (reader.Read())
                            present.Add(reader.GetString(0));
                    }
                }
                // columns added after the first schema read as their defaults until Create / upgrade schema adds them
                hasLocks = present.Contains("locked") && present.Contains("locked_fields") && present.Contains("managed_by");
                string locks = (present.Contains("locked") ? "locked" : "false")
                    + ", " + (present.Contains("locked_fields") ? "locked_fields" : "'{}'::text[]")
                    + ", " + (present.Contains("managed_by") ? "managed_by" : "NULL::text")
                    + ", " + (present.Contains("updated_by") ? "updated_by" : "NULL::text");
                // rows given a deleted_at (by the scripts that manage the table) are hidden, not removed
                string visible = present.Contains("deleted_at") ? "deleted_at IS NULL" : null;
                string lastFolder = null, lastName = null;
                Guid lastId = Guid.Empty;
                while (true)
                {
                    int read = 0;
                    using (NpgsqlCommand command = new NpgsqlCommand(
                        "SELECT id, folder, name, protocol, host, port, username, putty_profile, extra_args, icon, notes, spsl_script, "
                        + "rdp_options, local_path, remote_path, updated_at, " + locks + " FROM " + c.QualifiedTable
                        + Where(lastName == null ? null : "(folder, name, id) > (@f, @n, @i)", visible)
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
                DatabaseVersion = reader.GetDateTime(15).Ticks.ToString(),
                DatabaseLocked = reader.GetBoolean(16),
                DatabaseLockedFields = string.Join(",", reader.GetFieldValue<string[]>(17)),
                DatabaseManagedBy = Text(reader, 18),
                DatabaseUpdatedBy = Text(reader, 19)
            };
            session.SessionId = MakeSessionId(folder, name);
            return session;
        }

        /// <summary>A WHERE clause from the conditions given</summary>
        /// <param name="conditions">SQL conditions; null ones are left out</param>
        /// <returns>" WHERE a AND b", or "" if there are none</returns>
        private static string Where(params string[] conditions)
        {
            string[] used = conditions.Where(x => x != null).ToArray();
            return used.Length == 0 ? string.Empty : " WHERE " + string.Join(" AND ", used);
        }

        /// <summary>
        /// Something that changes whenever the table does, cheap enough to ask for every few seconds: the version
        /// the change trigger raises, or, for a table not yet upgraded (or a login that can't read the version
        /// table), the row count and latest updated_at, which catch nearly every change
        /// </summary>
        /// <param name="connection">An open connection</param>
        /// <param name="c">The connection settings</param>
        /// <returns>e.g. "v:1234", or "c:3214/638660000000000000"</returns>
        public static string ReadChangeToken(NpgsqlConnection connection, Connection c)
        {
            try
            {
                return "v:" + Scalar(connection, "SELECT version FROM " + c.QualifiedVersionTable + " LIMIT 1");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "42501")
            {
                // no version table yet, or no right to read it
            }
            using (NpgsqlCommand command = new NpgsqlCommand("SELECT count(*), max(updated_at) FROM " + c.QualifiedTable, connection))
            using (NpgsqlDataReader reader = command.ExecuteReader())
            {
                reader.Read();
                return "c:" + reader.GetInt64(0) + "/" + (reader.IsDBNull(1) ? 0 : reader.GetDateTime(1).Ticks);
            }
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

        /// <summary>A database session's path in the tree, without the "@db/" prefix</summary>
        /// <param name="sessionId">e.g. "@db/Lab/Core/sw1"</param>
        /// <returns>e.g. "Lab/Core/sw1"</returns>
        public static string TreePath(string sessionId)
        {
            string prefix = IdPrefix + "/";
            return sessionId != null && sessionId.StartsWith(prefix, StringComparison.Ordinal) ? sessionId.Substring(prefix.Length) : sessionId;
        }

        /// <summary>A database session's folder, as in the folder column</summary>
        /// <param name="sessionId">e.g. "@db/Lab/Core/sw1"</param>
        /// <returns>e.g. "Lab/Core", or "" at the top</returns>
        public static string FolderOf(string sessionId)
        {
            return SessionData.GetSessionParentId(TreePath(sessionId)) ?? string.Empty;
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
            catch (PostgresException ex) when (ex.SqlState == "P0001" && ex.MessageText.StartsWith(LockedPrefix, StringComparison.Ordinal))
            {
                string reason = ex.MessageText.Substring(LockedPrefix.Length);
                throw new InvalidOperationException(char.ToUpper(reason[0]) + reason.Substring(1) + ".", ex);
            }
            catch (PostgresException ex) when (ex.SqlState.StartsWith("08", StringComparison.Ordinal) || ex.SqlState.StartsWith("57P", StringComparison.Ordinal))
            {
                // the connection failed or the server is shutting down
                throw new DatabaseUnavailableException(ex.MessageText, ex);
            }
            catch (NpgsqlException ex) when (!(ex is PostgresException))
            {
                // no answer from the server: a refused or timed-out connection, or one dropped mid-way
                throw new DatabaseUnavailableException(Describe(ex), ex);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                throw new InvalidOperationException("A session with that name is already in that folder in the database"
                    + " (perhaps hidden there by its deleted_at).", ex);
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

        /// <summary>A write that failed because the database couldn't be reached, rather than because it refused; it can be queued</summary>
        public class DatabaseUnavailableException : InvalidOperationException
        {
            /// <summary>Create it</summary>
            /// <param name="message">Why</param>
            /// <param name="inner">The underlying failure</param>
            public DatabaseUnavailableException(string message, Exception inner) : base(message, inner)
            {
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
        /// <param name="force">Save even if the row changed since (overwriting that change); it still fails if the row is gone</param>
        /// <exception cref="InvalidOperationException">It's read-only, someone else changed or deleted it, or the write failed</exception>
        public static void Update(Connection c, string id, string version, SessionData session, string folder, bool force = false)
        {
            if (string.IsNullOrEmpty(id) || (!force && string.IsNullOrEmpty(version)))
                throw new InvalidOperationException("This session was read by an older version; Refresh, then make your change again.");
            int rows = Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("UPDATE " + c.QualifiedTable + " SET (" + WriteColumns + ") = ("
                    + WriteParameters + ") WHERE id = @id" + (force ? "" : " AND updated_at = @version"), connection))
                {
                    AddSessionParameters(command, session, folder);
                    command.Parameters.AddWithValue("id", Guid.Parse(id));
                    if (!force)
                        command.Parameters.AddWithValue("version", new DateTime(long.Parse(version), DateTimeKind.Utc));
                    return command.ExecuteNonQuery();
                }
            });
            if (rows == 0)
                throw new InvalidOperationException(force
                    ? "The session isn't in the database any more."
                    : "Someone else changed or deleted this session since it was read. Refresh, then make your change again.");
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
        /// <returns>The ids of the sessions that moved</returns>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed (e.g. a name clash, or a locked session)</exception>
        public static List<string> RenameFolder(Connection c, string from, string to)
        {
            return Write(c, connection =>
            {
                using (NpgsqlCommand command = new NpgsqlCommand("UPDATE " + c.QualifiedTable
                    + " SET folder = @to || substr(folder, length(@from) + 1) WHERE " + InFolder + " RETURNING id", connection))
                {
                    command.Parameters.AddWithValue("from", from.Trim('/'));
                    command.Parameters.AddWithValue("to", to.Trim('/'));
                    List<string> ids = new List<string>();
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            ids.Add(reader.GetGuid(0).ToString());
                    }
                    return ids;
                }
            });
        }

        /// <summary>
        /// Remove a folder: every session in it and in folders under it that's shown. Rows hidden by deleted_at are
        /// left for the scripts that hid them; they weren't on screen to be chosen.
        /// </summary>
        /// <param name="c">The connection</param>
        /// <param name="folder">The folder's path</param>
        /// <returns>How many sessions were removed</returns>
        /// <exception cref="InvalidOperationException">It's read-only, or the write failed</exception>
        public static int DeleteFolder(Connection c, string folder)
        {
            return Write(c, connection =>
            {
                // to_jsonb, so this works before Create / upgrade schema has added deleted_at
                using (NpgsqlCommand command = new NpgsqlCommand("DELETE FROM " + c.QualifiedTable + " t WHERE " + InFolder
                    + " AND (to_jsonb(t) ->> 'deleted_at') IS NULL", connection))
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

            /// <summary>Whether the login could write the table at the last read, so changes made offline can be queued</summary>
            public bool Writable { get; set; }
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
        /// Whether sessions can be added and changed from the tree: the connection isn't read-only and the login could
        /// write the table at the last read. While offline, changes are queued (<see cref="Submit"/>).
        /// </summary>
        public static bool Writable { get; private set; }

        /// <summary>The sessions as last read from the table (or its cache), before queued changes are applied</summary>
        private static List<SessionData> serverSessions = new List<SessionData>();

        /// <summary>Changes waiting to be sent</summary>
        public static int PendingCount { get; private set; }

        /// <summary>Changes the table refused, waiting for Retry, Overwrite or Discard</summary>
        public static int FailedCount { get; private set; }

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
            StopChecking();
            changeToken = null;
            Writable = false;
            NeedsUpgrade = false;
            if (!Enabled)
            {
                serverSessions = new List<SessionData>();
                SuperPuTTY.ReplaceDatabaseSessions(new List<SessionData>());
                SetStatus(SyncState.Off, null, null, 0);
                return;
            }
            Connection c = FromSettings();
            CacheFile cache = LoadCache();
            if (cache != null && cache.Source == c.ToString())
            {
                Writable = cache.Writable && !c.ReadOnly;
                serverSessions = cache.Sessions;
                ShowSessions(c);
                SetStatus(SyncState.Loading, null, cache.SyncedAt, cache.Sessions.Count);
            }
            else
            {
                serverSessions = new List<SessionData>();
                ShowSessions(c);
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
                    if (!c.ReadOnly)
                    {
                        Flush(c);
                    }
                    bool writable, hasLocks;
                    string token;
                    List<SessionData> sessions = FetchAll(c, n => Post(() => { LoadedSoFar = n; StatusChanged?.Invoke(null, EventArgs.Empty); }), out writable, out hasLocks, out token);
                    CacheFile cache = new CacheFile { Source = c.ToString(), SyncedAt = DateTime.Now, Sessions = sessions, Writable = writable };
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
                        NeedsUpgrade = !hasLocks;
                        changeToken = token;
                        StartChecking();
                        serverSessions = sessions;
                        ShowSessions(c);
                        SetStatus(SyncState.Online, null, cache.SyncedAt, sessions.Count);
                        if (PendingCount > 0)
                        {
                            // queued while this read was running
                            BeginSync();
                        }
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
                        // changes made meanwhile are queued, if the table could be written before
                        ShowSessions(c);
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

        /// <summary>Whether the table lacks the lock columns: it's read as unlocked until Create / upgrade schema is run</summary>
        public static bool NeedsUpgrade { get; private set; }

        /// <summary>Show the table's sessions with the queued changes on top, and count what's pending</summary>
        /// <param name="c">The connection</param>
        private static void ShowSessions(Connection c)
        {
            string source = c.ToString();
            int failed;
            PendingCount = DatabaseOutbox.Count(source, out failed);
            FailedCount = failed;
            SuperPuTTY.ReplaceDatabaseSessions(DatabaseOutbox.Apply(serverSessions, source));
        }

        /// <summary>Show the sessions again after the outbox changed (e.g. a discard), and update the status</summary>
        public static void RefreshPending()
        {
            ShowSessions(FromSettings());
            StatusChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>How a change from the tree went</summary>
        public enum SubmitResult
        {
            /// <summary>Saved to the table</summary>
            Saved,
            /// <summary>The table can't be reached: it's queued, shown already, and sent when it can be</summary>
            Queued,
            /// <summary>The table refused it; nothing changed</summary>
            Failed
        }

        /// <summary>
        /// Make a change from the tree. Online with nothing queued, it's written at once and the table read again;
        /// offline (or behind queued changes, to keep their order) it's queued in the outbox and shown at once.
        /// </summary>
        /// <param name="change">The change</param>
        /// <param name="error">Why it was refused, for <see cref="SubmitResult.Failed"/></param>
        /// <returns>How it went</returns>
        public static SubmitResult Submit(DatabaseOutbox.PendingChange change, out string error)
        {
            error = null;
            Connection c = FromSettings();
            if (c.ReadOnly)
            {
                error = "The database connection is read-only.";
                return SubmitResult.Failed;
            }
            string source = c.ToString();
            int failed;
            bool queueEmpty = DatabaseOutbox.Count(source, out failed) == 0 && failed == 0;
            bool pendingRow = change.Session?.DatabaseId != null && change.Session.DatabaseId.StartsWith("pending:", StringComparison.Ordinal);
            if (State == SyncState.Online && queueEmpty && !pendingRow)
            {
                try
                {
                    ApplyChange(c, change, new HashSet<string>());
                    BeginSync();
                    return SubmitResult.Saved;
                }
                catch (DatabaseUnavailableException ex)
                {
                    Log.WarnFormat("The database dropped while saving; queued: {0}", ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    error = ex.Message;
                    return SubmitResult.Failed;
                }
            }
            change.QueuedAt = DateTime.Now;
            DatabaseOutbox.Add(source, change);
            RefreshPending();
            if (State != SyncState.Offline)
            {
                BeginSync();
            }
            return SubmitResult.Queued;
        }

        /// <summary>
        /// Send one change to the table. Rows this sync already changed (e.g. moved by a queued folder rename) are
        /// updated without the version check: the version they were read at is older than our own change.
        /// </summary>
        /// <param name="c">The connection</param>
        /// <param name="change">The change</param>
        /// <param name="touched">Ids of rows changed earlier in this sync; this change's rows are added</param>
        /// <exception cref="InvalidOperationException">Refused, or <see cref="DatabaseUnavailableException"/> when it can't be reached</exception>
        private static void ApplyChange(Connection c, DatabaseOutbox.PendingChange change, HashSet<string> touched)
        {
            switch (change.Kind)
            {
                case DatabaseOutbox.ChangeKind.Insert:
                    touched.Add(Insert(c, change.Session, change.Folder));
                    break;
                case DatabaseOutbox.ChangeKind.Update:
                    string id = change.Session.DatabaseId;
                    Update(c, id, change.Session.DatabaseVersion, change.Session, change.Folder, change.Force || touched.Contains(id));
                    touched.Add(id);
                    break;
                case DatabaseOutbox.ChangeKind.Delete:
                    Delete(c, change.Session.DatabaseId);
                    break;
                case DatabaseOutbox.ChangeKind.RenameFolder:
                    touched.UnionWith(RenameFolder(c, change.From, change.To));
                    break;
                case DatabaseOutbox.ChangeKind.DeleteFolder:
                    DeleteFolder(c, change.From);
                    break;
            }
        }

        /// <summary>
        /// Send the queued changes in order (on the sync's background thread). A refused one is marked with why and
        /// left for the user; the rest still go. Stops, and the sync reports offline, if the table can't be reached.
        /// </summary>
        /// <param name="c">The connection</param>
        /// <exception cref="DatabaseUnavailableException">The table can't be reached</exception>
        private static void Flush(Connection c)
        {
            string source = c.ToString();
            HashSet<string> touched = new HashSet<string>();
            foreach (DatabaseOutbox.PendingChange change in DatabaseOutbox.Changes(source).Where(x => x.Error == null))
            {
                try
                {
                    ApplyChange(c, change, touched);
                    DatabaseOutbox.Remove(source, change.Id);
                    Log.InfoFormat("Sent a queued change: {0}", change);
                }
                catch (DatabaseUnavailableException)
                {
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    DatabaseOutbox.SetError(source, change.Id, ex.Message);
                    Log.WarnFormat("The database refused a queued change ({0}): {1}", change, ex.Message);
                }
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

        /// <summary>Checks the table for changes every Settings.DatabaseCheckSeconds while online</summary>
        private static System.Windows.Forms.Timer checkTimer;

        /// <summary>The table's change token when it was last read (<see cref="ReadChangeToken"/>); null before the first read</summary>
        private static string changeToken;

        /// <summary>Whether a check is running</summary>
        private static bool checking;

        /// <summary>
        /// Start (or restart) checking for changes made by others: people on other computers, or the scripts that
        /// maintain the table. Called after each successful read; does nothing when the interval is 0.
        /// </summary>
        private static void StartChecking()
        {
            int seconds = SuperPuTTY.Settings.DatabaseCheckSeconds;
            if (ui == null || seconds <= 0)
            {
                StopChecking();
                return;
            }
            if (checkTimer == null)
            {
                checkTimer = new System.Windows.Forms.Timer();
                checkTimer.Tick += (s, e) => CheckForChanges();
            }
            checkTimer.Interval = seconds * 1000;
            checkTimer.Start();
        }

        /// <summary>Stop checking for changes</summary>
        private static void StopChecking()
        {
            checkTimer?.Stop();
        }

        /// <summary>
        /// Ask the table, in the background, whether it changed since it was read, and read it again if it did. Only
        /// one small query: the sessions are read only after a change. Skipped while reading, offline (the retry
        /// takes over) and while a dialog is open, so a session being edited isn't replaced under it.
        /// </summary>
        public static void CheckForChanges()
        {
            if (checking || syncing || State != SyncState.Online || !Enabled || changeToken == null)
                return;
            if (System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Any(f => f.Modal))
                return;
            checking = true;
            Connection c = FromSettings();
            string known = changeToken;
            Task.Run(() =>
            {
                string token = null;
                Exception failure = null;
                try
                {
                    using (NpgsqlConnection connection = Open(c))
                    {
                        token = ReadChangeToken(connection, c);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                Post(() =>
                {
                    checking = false;
                    if (failure != null)
                    {
                        // a full read reports offline and starts retrying
                        Log.WarnFormat("Could not check the database sessions for changes: {0}", Describe(failure));
                        BeginSync();
                    }
                    else if (token != known && known == changeToken)
                    {
                        Log.InfoFormat("The database sessions in {0} changed; reading them again", c);
                        SuperPuTTY.ReportStatus("Database sessions changed; reading them again");
                        BeginSync();
                    }
                });
            });
        }

        /// <summary>A one-line status for the tree's root, e.g. "3,214" or "offline, cached 09:41"</summary>
        /// <returns>The text to show in brackets after "Database sessions"</returns>
        public static string StatusText()
        {
            string readOnly = (SuperPuTTY.Settings.DatabaseReadOnly || (State == SyncState.Online && !Writable) ? ", read-only" : "")
                + (PendingCount > 0 ? string.Format(", {0} pending", PendingCount) : "")
                + (FailedCount > 0 ? string.Format(", {0} not saved", FailedCount) : "");
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
