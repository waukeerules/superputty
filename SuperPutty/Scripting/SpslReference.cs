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
                Description = "Types the text, then presses Enter. On its own, it just presses Enter. The text is typed "
                    + "as it is: braces only mean something as {placeholders} and {prompt:...}.",
                Example = "SENDLINE show version"
            },
            new Entry
            {
                Name = "SENDCHAR", IsCommand = true, Syntax = "SENDCHAR text",
                Description = "Types the text without pressing Enter, e.g. to answer a y/n question or start a line "
                    + "that a later SENDKEY finishes.",
                Example = "SENDCHAR y"
            },
            new Entry
            {
                Name = "SENDKEY", IsCommand = true, Syntax = "SENDKEY [^%+]key  or  SENDKEY {key count}",
                Description = "Presses one key: a key name such as ENTER, TAB, ESC, UP, PGDN or F5 (with or without "
                    + "braces), or a single character such as y, ] or ?. Put ^ in front for Ctrl, % for Alt and + for "
                    + "Shift, in any combination (^%DEL). {TAB 3} presses it 3 times. Ctrl with a letter or [ \\ ] @ _ "
                    + "sends that control character, e.g. ^C to interrupt, ^Z to suspend, ^] for Telnet's escape. "
                    + "See \"Keys for SENDKEY\" for every key name.",
                Example = "SENDKEY ^C"
            },
            new Entry
            {
                Name = "WAITFOR", IsCommand = true, Syntax = "WAITFOR [/t:seconds] text",
                Description = "Waits until the text appears in the session's output, then goes on; the text is matched "
                    + "exactly, including case, anywhere in a line. It waits up to 30 seconds, or the /t: time; if the "
                    + "text doesn't appear, the script stops (the status bar says why), so nothing is typed into the "
                    + "wrong prompt. It looks at output since the last WAITFOR, starting from the line the cursor is on "
                    + "when the script starts (from the start of the session for a session's own script). Works in "
                    + "PuTTY tabs and the built-in terminal; RDP, VNC and console windows have no output to read.",
                Example = "WAITFOR /t:10 Password:"
            },
            new Entry
            {
                Name = "SLEEP", IsCommand = true, Syntax = "SLEEP milliseconds",
                Description = "Waits this long before the next line, e.g. 1000 for one second. Stopping the script ends "
                    + "the wait at once. Prefer WAITFOR when there's a prompt to wait for.",
                Example = "SLEEP 500"
            },
            new Entry
            {
                Name = "PROMPT", IsCommand = true, Syntax = "PROMPT message",
                Description = "Asks you for some text, showing the message, then types it and presses Enter. Cancel "
                    + "types an empty line and the script goes on. To ask before the script starts, and use the answer "
                    + "anywhere in it, use {prompt:Name} instead.",
                Example = "PROMPT Which VLAN?"
            },
            new Entry
            {
                Name = "PWDPROMPT", IsCommand = true, Syntax = "PWDPROMPT",
                Description = "Asks you for a password, hidden as you type, then types it and presses Enter. Cancel stops "
                    + "the script. The password is never shown or logged.",
                Example = "PWDPROMPT"
            },
            new Entry
            {
                Name = "OPENSESSION", IsCommand = true, Syntax = "OPENSESSION session-id",
                Description = "Opens a saved session in a new tab, by its ID: its folder path and name, as in the "
                    + "Sessions tree. The rest of the script still goes to the tab it was running in.",
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
                Name = "SENDBREAK", IsCommand = true, Available = false, Syntax = "SENDBREAK",
                Description = "Not available: a Break (to stop a router's boot, for instance) is a signal on the serial "
                    + "or Telnet line, not a key, so a script can't type one. Send it from the tab's right-click menu, "
                    + "Special Command > Break, in a PuTTY tab; a built-in terminal tab can be opened in PuTTY from "
                    + "there. Check script reports SENDBREAK as an error; run anyway, the line does nothing.",
                Example = "# SENDBREAK is not available: use Special Command > Break"
            },
            new Entry
            {
                Name = "Keys for SENDKEY", Syntax = "SENDKEY name  |  SENDKEY ^x  |  SENDKEY {name count}",
                Description = "Key names (any case, with or without braces): " + KeyNameList() + "."
                    + Environment.NewLine + Environment.NewLine
                    + "Modifiers: ^ Ctrl, % Alt, + Shift, before the key, e.g. ^C, %F, +TAB (Shift+Tab), ^UP, ^%DEL. "
                    + "To press the ^, % or + key itself, put it in braces: {^}, {%}, {+}."
                    + Environment.NewLine + Environment.NewLine
                    + "Control characters: ^A-^Z, ^[ (the same as ESC), ^\\, ^] (Telnet's escape), ^@ (NUL), ^_ , and "
                    + "^6 or ^+6 (Ctrl+Shift+6, 0x1E: Cisco's escape, e.g. to stop a traceroute)."
                    + Environment.NewLine + Environment.NewLine
                    + "Single characters: any letter, digit or symbol your keyboard has, e.g. SENDKEY ? or SENDKEY ]. "
                    + "Shift is added for you where the character needs it (!, ?, :)."
                    + Environment.NewLine + Environment.NewLine
                    + "Repeats: {DOWN 5}, {TAB 3}, {] 2}."
                    + Environment.NewLine + Environment.NewLine
                    + "In PuTTY tabs, keys with Ctrl, Alt or Shift are sent as the characters a terminal sends for them, "
                    + "as in the built-in terminal; plain named keys follow PuTTY's own keyboard settings. BREAK, the "
                    + "lock keys, PRTSC, HELP and CLEAR have no characters, so the built-in terminal sends nothing for them.",
                Example = "SENDKEY {DOWN 3}"
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
                    + "anywhere in a line, e.g. SENDLINE {user} or WAITFOR {hostname}#. Secret values are typed but "
                    + "never shown or logged. Check script warns about keys that have no value. Replaced in button bar "
                    + "scripts and the Script Editor; a session's own script file is typed as it is.",
                Example = "SENDLINE {user}"
            },
            new Entry
            {
                Name = "{prompt:Name}", Syntax = "{prompt:Name}  {prompt:Name=default}  {secret:Name}",
                Description = "Asked for once, before the script starts, in one box with a field per name; every use of "
                    + "the same name gets the same answer. {prompt:Name=default} fills the field in; {secret:Name} is "
                    + "hidden as you type, and never shown or logged. Cancel doesn't run the script. Works in button "
                    + "bar scripts and the Script Editor; not in a session's own script, which runs as it opens.",
                Example = "SENDLINE interface {prompt:Interface=Gi1/0/1}"
            },
            new Entry
            {
                Name = "How scripts run", Syntax = "one command per line",
                Description = "Each line starts with a command, in any case (sendline works too), and may be indented; "
                    + "blank lines and # comments are skipped. Lines run one after another in the background; the "
                    + "status bar shows the script's progress and a Stop button. The script stops when a WAITFOR "
                    + "times out, a line fails or PWDPROMPT is cancelled, and the status bar says why."
                    + Environment.NewLine + Environment.NewLine
                    + "Scripts run from the button bar (in the active tab, or the selected ones), the Script Editor (in "
                    + "the selected tabs) and a session's own script file (Edit Session, when it opens). They type into "
                    + "PuTTY tabs, the built-in terminal and RDP tabs (keys only; WAITFOR needs output to read)."
                    + Environment.NewLine + Environment.NewLine
                    + "Check script finds unknown commands, missing arguments, keys SENDKEY doesn't know, unknown "
                    + "session IDs and placeholders without a value.",
                Example = "# answer a prompt, then go on" + Environment.NewLine + "SENDLINE copy run start"
                    + Environment.NewLine + "WAITFOR /t:10 [startup-config]?" + Environment.NewLine + "SENDKEY ENTER"
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

        /// <summary>
        /// Whether SENDKEY can press a single character: a letter or digit, or a character a key types on this keyboard
        /// layout; not the braces, which are SENDKEY's own syntax
        /// </summary>
        /// <param name="ch">The character, e.g. ']'</param>
        /// <returns>true if SENDKEY can press it</returns>
        public static bool CanTypeKey(char ch)
        {
            if (ch == '{' || ch == '}')
                return false;
            return char.IsLetterOrDigit(ch) && ch < 128 || SuperPutty.Utils.TerminalKeys.FromCharacter(ch) >= 0;
        }

        /// <summary>The key names for the "Keys for SENDKEY" entry, each once, e.g. "ENTER, TAB, ESC (or ESCAPE)"</summary>
        /// <returns>The names, comma separated, with aliases in brackets</returns>
        private static string KeyNameList()
        {
            // aliases share a key code; list each key once, with its other names after it
            List<string> keys = new List<string>();
            foreach (IGrouping<int, string> key in Commands.SendKeyCodes().Where(k => k.Key.Length > 1)
                .GroupBy(k => k.Value, k => k.Key))
            {
                string[] names = key.ToArray();
                keys.Add(names.Length == 1 ? names[0] : names[0] + " (or " + string.Join(", ", names.Skip(1)) + ")");
            }
            return string.Join(", ", keys);
        }
    }
}
