using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using log4net;

namespace SuperPuTTY.Scripting
{
    /// <summary>The two kinds of script SuperPuTTY sends</summary>
    public enum ScriptKind
    {
        /// <summary>Lines typed as they are, each followed by Enter</summary>
        Lines,

        /// <summary>An SPSL script, run command by command</summary>
        Spsl
    }

    /// <summary>
    /// Saved scripts, kept in a "scripts" folder in the settings folder, next to Sessions.xml, so a team sharing that
    /// folder shares its scripts too. A script's kind is its extension: .spsl for SPSL scripts and .txt for lines to
    /// type, so no marker line is needed in the script itself. Listed, inserted, saved and deleted by
    /// ScriptLibraryPanel, without going through a file dialog.
    /// </summary>
    public static class ScriptLibrary
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ScriptLibrary));

        /// <summary>One saved script: its name and kind</summary>
        public class Entry
        {
            /// <summary>The name, without the extension</summary>
            public string Name { get; set; }

            /// <summary>Whether it's SPSL or lines to type</summary>
            public ScriptKind Kind { get; set; }

            /// <summary>How the list shows it: the name, then its kind</summary>
            /// <returns>e.g. "Cisco login  ·  SPSL"</returns>
            public override string ToString()
            {
                return this.Name + "  ·  " + (this.Kind == ScriptKind.Spsl ? "SPSL" : "lines");
            }
        }

        /// <summary>Names allowed for saved scripts: letters, digits, spaces, dots, dashes and underscores</summary>
        private static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9 ._-]{1,60}$", RegexOptions.Compiled);

        /// <summary>The folder scripts are kept in: "scripts" in the settings folder</summary>
        public static string Folder
        {
            get { return Path.Combine(SuperPutty.SuperPuTTY.Settings.SettingsFolder ?? string.Empty, "scripts"); }
        }

        /// <summary>A kind's file extension</summary>
        /// <param name="kind">The kind</param>
        /// <returns>".spsl" or ".txt"</returns>
        private static string ExtensionOf(ScriptKind kind)
        {
            return kind == ScriptKind.Spsl ? ".spsl" : ".txt";
        }

        /// <summary>The saved scripts, alphabetically</summary>
        /// <returns>The scripts</returns>
        public static IList<Entry> List()
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return new List<Entry>();
                return Directory.GetFiles(Folder)
                    .Where(f => f.EndsWith(".spsl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    .Select(f => new Entry
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Kind = f.EndsWith(".spsl", StringComparison.OrdinalIgnoreCase) ? ScriptKind.Spsl : ScriptKind.Lines
                    })
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list saved scripts in " + Folder, ex);
                return new List<Entry>();
            }
        }

        /// <summary>Whether a script with this name is saved, of either kind</summary>
        /// <param name="name">The name</param>
        /// <returns>true if it is</returns>
        public static bool Exists(string name)
        {
            return File.Exists(PathOf(name, ScriptKind.Spsl)) || File.Exists(PathOf(name, ScriptKind.Lines));
        }

        /// <summary>A saved script's text</summary>
        /// <param name="entry">The script</param>
        /// <returns>The text, with \n line endings</returns>
        /// <exception cref="IOException">The file can't be read</exception>
        public static string Load(Entry entry)
        {
            return File.ReadAllText(PathOf(entry.Name, entry.Kind)).Replace("\r\n", "\n");
        }

        /// <summary>Save a script, replacing any saved under the same name (of either kind)</summary>
        /// <param name="name">The name</param>
        /// <param name="kind">Whether it's SPSL or lines to type</param>
        /// <param name="text">The script</param>
        /// <exception cref="IOException">The file can't be written</exception>
        public static void Save(string name, ScriptKind kind, string text)
        {
            Directory.CreateDirectory(Folder);
            // CRLF, so the files open cleanly in Notepad too
            File.WriteAllText(PathOf(name, kind), (text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n"));
            ScriptKind other = kind == ScriptKind.Spsl ? ScriptKind.Lines : ScriptKind.Spsl;
            if (File.Exists(PathOf(name, other)))
            {
                File.Delete(PathOf(name, other));
            }
            Log.InfoFormat("Saved script {0} ({1})", name, kind);
        }

        /// <summary>Delete a saved script</summary>
        /// <param name="entry">The script</param>
        /// <exception cref="IOException">The file can't be deleted</exception>
        public static void Delete(Entry entry)
        {
            File.Delete(PathOf(entry.Name, entry.Kind));
            Log.InfoFormat("Deleted script {0}", entry.Name);
        }

        /// <summary>Check that a name can be used for a saved script</summary>
        /// <param name="name">The name</param>
        /// <param name="error">Why it can't, if it can't</param>
        /// <returns>true if it can be used</returns>
        public static bool ValidateName(string name, out string error)
        {
            error = null;
            if (name == null || !NamePattern.IsMatch(name.Trim()))
            {
                error = "Use 1 to 60 letters, digits, spaces, dots, dashes or underscores.";
                return false;
            }
            return true;
        }

        /// <summary>A saved script's file</summary>
        /// <param name="name">The name</param>
        /// <param name="kind">The kind</param>
        /// <returns>The full path</returns>
        private static string PathOf(string name, ScriptKind kind)
        {
            return Path.Combine(Folder, name.Trim() + ExtensionOf(kind));
        }
    }
}
