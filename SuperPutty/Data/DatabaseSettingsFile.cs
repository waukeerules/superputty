using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SuperPutty.Data
{
    /// <summary>
    /// The Database sessions settings as a JSON file, so a team can pass one connection around: export it from the
    /// Database settings dialog (Gui.dlgDatabaseSettings), and a co-worker imports it there. It holds the server,
    /// port, database, schema, table, username, SSL mode, read only and how often to check for changes. A password is
    /// written only when it's a {key} placeholder (each person sets its value in Environment Variables); a saved one is
    /// encrypted for its Windows user and stays out. The nickname is the person's own and stays out too.
    /// </summary>
    public static class DatabaseSettingsFile
    {
        /// <summary>The value of the file's "format" field, so another JSON file isn't taken for one</summary>
        public const string Format = "superxputty-database-settings";

        /// <summary>A password that's a {key} placeholder, the only kind written to the file</summary>
        private static readonly Regex Placeholder = new Regex(@"^\{\w+\}$");

        /// <summary>The SSL modes accepted, as Npgsql names them</summary>
        private static readonly string[] SslModes = { "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull" };

        /// <summary>What the file holds</summary>
        public class Settings
        {
            /// <summary>Server name or address</summary>
            public string Host { get; set; }

            /// <summary>Server port</summary>
            public int Port { get; set; } = 5432;

            /// <summary>The database holding the table</summary>
            public string Database { get; set; }

            /// <summary>The schema holding the table</summary>
            public string Schema { get; set; }

            /// <summary>The table's name</summary>
            public string Table { get; set; }

            /// <summary>Login name</summary>
            public string User { get; set; }

            /// <summary>A {key} placeholder for the password, or null: a real password is never in the file</summary>
            public string Password { get; set; }

            /// <summary>Npgsql SSL mode name</summary>
            public string SslMode { get; set; } = "Prefer";

            /// <summary>Never create or change anything in the database</summary>
            public bool ReadOnly { get; set; }

            /// <summary>How often to check the table for changes, in seconds; 0 never</summary>
            public int CheckSeconds { get; set; } = 30;
        }

        /// <summary>The file's text for some settings</summary>
        /// <param name="settings">The settings; a password that isn't a {key} placeholder is left out</param>
        /// <returns>Indented JSON, starting with the format marker</returns>
        public static string ToJson(Settings settings)
        {
            JObject json = new JObject
            {
                ["format"] = Format,
                ["version"] = 1,
                ["host"] = settings.Host ?? string.Empty,
                ["port"] = settings.Port,
                ["database"] = settings.Database ?? string.Empty,
                ["schema"] = settings.Schema ?? string.Empty,
                ["table"] = settings.Table ?? string.Empty,
                ["user"] = settings.User ?? string.Empty,
                ["sslMode"] = settings.SslMode ?? "Prefer",
                ["readOnly"] = settings.ReadOnly,
                ["checkSeconds"] = settings.CheckSeconds
            };
            if (IsPlaceholder(settings.Password))
            {
                json["password"] = settings.Password;
            }
            return json.ToString(Formatting.Indented);
        }

        /// <summary>Read a file's text, checking each field as the dialog would</summary>
        /// <param name="text">The file's text</param>
        /// <param name="error">Why it can't be used, for the user; null when it can</param>
        /// <returns>The settings, or null</returns>
        public static Settings FromJson(string text, out string error)
        {
            JObject json;
            try
            {
                json = JObject.Parse(text ?? string.Empty);
            }
            catch (JsonException)
            {
                error = "This isn't a JSON file.";
                return null;
            }
            if ((string)json["format"] != Format)
            {
                error = "This isn't a database settings file exported from SuperXPuTTY.";
                return null;
            }
            Settings settings = new Settings
            {
                Host = ((string)json["host"] ?? string.Empty).Trim(),
                Port = (int?)json["port"] ?? 5432,
                Database = ((string)json["database"] ?? string.Empty).Trim(),
                Schema = ((string)json["schema"] ?? string.Empty).Trim(),
                Table = ((string)json["table"] ?? string.Empty).Trim(),
                User = ((string)json["user"] ?? string.Empty).Trim(),
                Password = IsPlaceholder((string)json["password"]) ? (string)json["password"] : null,
                SslMode = SslModes.FirstOrDefault(m => string.Equals(m, (string)json["sslMode"], StringComparison.OrdinalIgnoreCase)) ?? "Prefer",
                ReadOnly = (bool?)json["readOnly"] ?? false,
                CheckSeconds = Math.Max(0, Math.Min(3600, (int?)json["checkSeconds"] ?? 30))
            };
            if (string.IsNullOrEmpty(settings.Host))
                error = "The file has no host.";
            else if (settings.Port < 1 || settings.Port > 65535)
                error = "The file's port isn't between 1 and 65535.";
            else if (!DatabaseSessions.ValidateIdentifier(settings.Database, "Database", out error)
                || !DatabaseSessions.ValidateIdentifier(settings.Schema, "Schema", out error)
                || !DatabaseSessions.ValidateIdentifier(settings.Table, "Table", out error))
                error = "In the file: " + error + ".";
            return error == null ? settings : null;
        }

        /// <summary>Whether a password is a {key} placeholder, which can go in the file</summary>
        /// <param name="password">The password as typed or stored</param>
        /// <returns>true for e.g. "{pg_password}"</returns>
        public static bool IsPlaceholder(string password)
        {
            return !string.IsNullOrEmpty(password) && Placeholder.IsMatch(password);
        }
    }
}
