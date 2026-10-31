/*
 * Copyright (c) 2009-2015 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using DarkModeForms;
using System.Drawing;
using SuperPuTTY.Scripting;

namespace SuperPutty
{
    public partial class dlgScriptEditor : Form
    {
        /// <summary>Raised when a script is ready to execute.</summary>
        public event ExecuteScriptEventHandler ScriptReady;
        public delegate void ExecuteScriptEventHandler(object sender, ExecuteScriptEventArgs e);

        protected virtual void OnScriptReady(ExecuteScriptEventArgs e)
        {
            ExecuteScriptEventHandler handler = ScriptReady;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>The Run as choices, in the order of <see cref="ScriptKind"/></summary>
        private static readonly string[] RunAsChoices = { "Send lines", "SPSL script" };

        /// <summary>
        /// A dialog that allows opening, saving, editing and executing multiline scripts. Saved scripts are on the left
        /// (ScriptLibraryPanel); "Run as" chooses whether the text is typed line by line or run as SPSL, and in SPSL mode
        /// the commands, examples and a script check are on the right (SpslHelpPanel). No #!/bin/spsl line is needed.
        /// </summary>
        public dlgScriptEditor()
        {
            InitializeComponent();
            this.ActiveControl = textBoxScript;
            this.helpPanel.Editor = this.textBoxScript;
            this.libraryPanel.Editor = this.textBoxScript;
            this.libraryPanel.CurrentKind = () => this.Kind;
            this.libraryPanel.InsertedIntoEmpty += kind => this.Kind = kind;
            this.comboRunAs.Items.AddRange(RunAsChoices);
            this.toolTip = new ToolTip();
            this.toolTip.SetToolTip(this.comboRunAs, "Send lines types each line as it is, then Enter. SPSL script runs SPSL commands such as WAITFOR and SENDKEY.");
            this.Kind = ScriptKind.Spsl;

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>
        /// Send the script to the selected sessions. An SPSL script is checked first, and if it has errors you're asked
        /// whether to send it anyway.
        /// </summary>
        /// <param name="sender">The Send button</param>
        /// <param name="e">Not used</param>
        private void buttonRunScript_Click(object sender, EventArgs e)
        {
            ExecuteScriptEventArgs args = new ExecuteScriptEventArgs { Script = textBoxScript.Text, RunAsSpsl = this.Kind == ScriptKind.Spsl };
            if (args.IsSPSL)
            {
                int errors = this.helpPanel.Check().Count(i => i.IsError);
                if (errors > 0 && Messenger.MessageBox(
                        string.Format("The script has {0} error{1}, listed beside it. Send it anyway?", errors, errors == 1 ? "" : "s"),
                        "Script Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
            OnScriptReady(args);
            this.Close();
            
        }

        private void buttonLoadScript_Click(object sender, EventArgs e)
        {
            DialogResult dlgResult = this.openFileDialog1.ShowDialog();
            if(dlgResult == DialogResult.OK)
            {
                string script = File.ReadAllText(this.openFileDialog1.FileName);
                // an .spsl file, or an older script with the #!/bin/spsl line, runs as SPSL
                if (this.openFileDialog1.FileName.EndsWith(".spsl", StringComparison.OrdinalIgnoreCase)
                    || script.TrimStart().StartsWith(SpslReference.Header, StringComparison.OrdinalIgnoreCase))
                {
                    this.Kind = ScriptKind.Spsl;
                }
                textBoxScript.AppendText(script);
            }
        }

        /// <summary>The tooltip for the Run as choice</summary>
        private ToolTip toolTip;

        /// <summary>Whether the script is typed line by line or run as SPSL: the Run as choice</summary>
        private ScriptKind Kind
        {
            get { return this.comboRunAs.SelectedIndex == (int)ScriptKind.Spsl ? ScriptKind.Spsl : ScriptKind.Lines; }
            set { this.comboRunAs.SelectedIndex = (int)value; }
        }

        /// <summary>
        /// Show the SPSL panel (commands and check) only for SPSL scripts; for lines to send, the script box takes its space
        /// </summary>
        /// <param name="sender">The Run as list</param>
        /// <param name="e">Not used</param>
        private void comboRunAs_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool spsl = this.Kind == ScriptKind.Spsl;
            int right = spsl ? this.helpPanel.Left - (this.textBoxScript.Left - this.libraryPanel.Right) : this.helpPanel.Right;
            this.helpPanel.Visible = spsl;
            this.textBoxScript.Width = right - this.textBoxScript.Left;
        }

        private void buttonSaveScript_Click(object sender, EventArgs e)
        {
            DialogResult dlgResult = this.saveFileDialog1.ShowDialog();
            if(dlgResult == DialogResult.OK)
            {
                File.WriteAllText(this.saveFileDialog1.FileName, textBoxScript.Text);
            }
        }
    }

    /// <summary>Handles passing the script data to caller.</summary>
    public class ExecuteScriptEventArgs : EventArgs
    {
        /// <summary>A string containing a list of commands to be sent to open terminal sessions</summary>
        public string Script { get; set; }
        /// <summary>Run the script as SPSL, as chosen in the Script Editor's Run as list</summary>
        public bool RunAsSpsl { get; set; }

        /// <summary>
        /// True if the script should be handled by script parser: chosen with <see cref="RunAsSpsl"/>, or marked by a
        /// first line of #!/bin/spsl (session script files, and older scripts)
        /// </summary>
        public bool IsSPSL {
            get
            {
                if (this.RunAsSpsl || (!string.IsNullOrEmpty(this.Script)
                    && this.Script.StartsWith("#!/bin/spsl")))
                    return true;
                else
                    return false;
            }
        }

        /// <summary>If set to the handle of a window, script will be restricted to the specified session only.</summary>
        public IntPtr Handle { get; set; }
    }
}
