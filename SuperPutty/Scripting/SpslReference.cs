using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperPuTTY.Scripting
{
    /// <summary>
    /// What each SPSL command does, how to write it and an example, for the script editors' reference panel
    /// (SpslHelpPanel) and the script checker (SpslChecker). Kept beside the command handlers in this folder; when a
    /// command is added to SPSL.keywords, add it here too.
    /// </summary>
    public static class SpslReference
    {
        /// <summary>One entry in the reference</summary>
        public class Entry
        {
            /// <summary>The command word, e.g. "SENDLINE"; or a heading for syntax that isn't a command</summary>
            public string Name { get; set; }

            /// <summary>How to write it, e.g. "SENDLINE text"</summary>
            public string Syntax { get; set; }

            /// <summary>What it does</summary>
            public string Description { get; set; }

            /// <summary>An example line, inserted by the reference panel's Insert button</summary>
            public string Example { get; set; }

            /// <summary>Whether this is a command (false for comments and placeholders)</summary>
            public bool IsCommand { get; set; }

            /// <summary>Whether the command works; false for ones SPSL lists but doesn't implement</summary>
            public bool Available { get; set; } = true;

            /// <summary>The name shown in the list</summary>
            /// <returns>The name</returns>
            public override string ToString()
            {
                return this.Available ? this.Name : this.Name + " (not available)";
            }
        }

        /// <summary>
        /// The first line that used to mark a Script Editor script as SPSL; still recognised in session script files and
        /// older scripts, and left out when a saved script is inserted
        /// </summary>
        public const string Header = "#!/bin/spsl";

        /// <summary>The entries, in the order the reference panel lists them</summary>
        public static readonly IList<Entry> Entries = new List<Entry>
        {
            new Entry
            {
                Name = "SENDLINE", IsCommand = true, Syntax = "SENDLINE text",
                Description = "Types the text, then presses Enter. On its own, it just presses Enter.",
                Example = "SENDLINE show version"
            },
            new Entry
            {
                Name = "SENDCHAR", IsCommand = true, Syntax = "SENDCHAR text",
                Description = "Types the text without pressing Enter.",
                Example = "SENDCHAR y"
            },
            new Entry
            {
                Name = "SENDKEY", IsCommand = true, Syntax = "SENDKEY key",
                Description = "Presses one key: a name such as ENTER, TAB, ESC, UP, DOWN, PGUP, PGDN, HOME, END, DEL, "
                    + "BACKSPACE or F1-F16 (with or without braces), or a single character. Put ^ in front for Ctrl, "
                    + "% for Alt and + for Shift. {KEY n} presses it n times.",
                Example = "SENDKEY ^C"
            },
            new Entry
            {
                Name = "WAITFOR", IsCommand = true, Syntax = "WAITFOR [/t:seconds] text",
                Description = "Waits until the text appears in the session's output, for up to 30 seconds (or the "
                    + "/t: time). If it doesn't appear, the script stops. Needs a PuTTY session, since it reads the "
                    + "session log.",
                Example = "WAITFOR /t:10 Password:"
            },
            new Entry
            {
                Name = "SLEEP", IsCommand = true, Syntax = "SLEEP milliseconds",
                Description = "Waits this long before the next line, e.g. 1000 for one second.",
                Example = "SLEEP 500"
            },
            new Entry
            {
                Name = "PROMPT", IsCommand = true, Syntax = "PROMPT message",
                Description = "Asks you for some text, showing the message, then types it and presses Enter.",
                Example = "PROMPT Which VLAN?"
            },
            new Entry
            {
                Name = "PWDPROMPT", IsCommand = true, Syntax = "PWDPROMPT",
                Description = "Asks you for a password, hidden as you type, then types it and presses Enter. Cancel stops the script.",
                Example = "PWDPROMPT"
            },
            new Entry
            {
                Name = "OPENSESSION", IsCommand = true, Syntax = "OPENSESSION session-id",
                Description = "Opens a saved session by its ID: its folder path and name, as in the Sessions tree.",
                Example = "OPENSESSION Lab/core-sw1"
            },
            new Entry
            {
                Name = "CLOSESESSION", IsCommand = true, Available = false, Syntax = "CLOSESESSION",
                Description = "Listed by SPSL but not implemented: using it stops the script with an error.",
                Example = "# CLOSESESSION is not available"
            },
            new Entry
            {
                Name = "# comment", Syntax = "# text",
                Description = "A comment: ignored when the script runs. Whether text runs as SPSL is chosen with Run as "
                    + "(Script Editor) or Action (button bar); an older " + Header + " first line is just a comment.",
                Example = "# log in, then show the interfaces"
            },
            new Entry
            {
                Name = "{placeholders}", Syntax = "{key}",
                Description = "Replaced with your value from Tools > Environment Variables before the script runs, "
                    + "e.g. SENDLINE {user}. Secret values are typed but never shown or logged.",
                Example = "SENDLINE {user}"
            }
        };

        /// <summary>The entry for a command word</summary>
        /// <param name="command">The command, in any case</param>
        /// <returns>The entry, or null if it isn't an SPSL command</returns>
        public static Entry Find(string command)
        {
            return Entries.FirstOrDefault(e => e.IsCommand && string.Equals(e.Name, command, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The key names SENDKEY understands</summary>
        public static IEnumerable<string> KeyNames
        {
            get { return Commands.SendKeyNames; }
        }

        /// <summary>Whether SENDKEY understands a key name</summary>
        /// <param name="name">The name, e.g. "PGDN"</param>
        /// <returns>true if it's a key name</returns>
        public static bool IsKeyName(string name)
        {
            return Commands.IsKeyName(name);
        }
    }
}
