namespace SuperPutty
{
    partial class dlgScriptEditor
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.buttonSendScript = new System.Windows.Forms.Button();
            this.textBoxScript = new System.Windows.Forms.TextBox();
            this.buttonLoadScript = new System.Windows.Forms.Button();
            this.buttonSaveScript = new System.Windows.Forms.Button();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.libraryPanel = new SuperPutty.Gui.ScriptLibraryPanel();
            this.helpPanel = new SuperPutty.Gui.SpslHelpPanel();
            this.labelRunAs = new System.Windows.Forms.Label();
            this.comboRunAs = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            //
            // buttonSendScript
            //
            this.buttonSendScript.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSendScript.Location = new System.Drawing.Point(791, 386);
            this.buttonSendScript.Name = "buttonSendScript";
            this.buttonSendScript.Size = new System.Drawing.Size(75, 23);
            this.buttonSendScript.TabIndex = 7;
            this.buttonSendScript.Text = "Send";
            this.buttonSendScript.UseVisualStyleBackColor = true;
            this.buttonSendScript.Click += new System.EventHandler(this.buttonRunScript_Click);
            //
            // textBoxScript
            //
            this.textBoxScript.AcceptsTab = true;
            this.textBoxScript.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxScript.Font = new System.Drawing.Font("Consolas", 9.75F);
            this.textBoxScript.Location = new System.Drawing.Point(194, 12);
            this.textBoxScript.Multiline = true;
            this.textBoxScript.Name = "textBoxScript";
            this.textBoxScript.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBoxScript.Size = new System.Drawing.Size(376, 364);
            this.textBoxScript.TabIndex = 1;
            this.textBoxScript.WordWrap = false;
            //
            // buttonLoadScript
            //
            this.buttonLoadScript.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.buttonLoadScript.Location = new System.Drawing.Point(194, 386);
            this.buttonLoadScript.Name = "buttonLoadScript";
            this.buttonLoadScript.Size = new System.Drawing.Size(80, 23);
            this.buttonLoadScript.TabIndex = 3;
            this.buttonLoadScript.Text = "Open file...";
            this.buttonLoadScript.UseVisualStyleBackColor = true;
            this.buttonLoadScript.Click += new System.EventHandler(this.buttonLoadScript_Click);
            //
            // buttonSaveScript
            //
            this.buttonSaveScript.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.buttonSaveScript.Location = new System.Drawing.Point(280, 386);
            this.buttonSaveScript.Name = "buttonSaveScript";
            this.buttonSaveScript.Size = new System.Drawing.Size(90, 23);
            this.buttonSaveScript.TabIndex = 4;
            this.buttonSaveScript.Text = "Save to file...";
            this.buttonSaveScript.UseVisualStyleBackColor = true;
            this.buttonSaveScript.Click += new System.EventHandler(this.buttonSaveScript_Click);
            //
            // openFileDialog1
            //
            this.openFileDialog1.DefaultExt = "scr";
            this.openFileDialog1.Filter = "script files (*.spsl)|*.spsl|txt files (*.txt)|*.txt|All files (*.*)|*.*";
            //
            // saveFileDialog1
            //
            this.saveFileDialog1.DefaultExt = "spsl";
            this.saveFileDialog1.Filter = "script files (*.spsl)|*.spsl|txt files (*.txt)|*.txt|All files (*.*)|*.*";
            //
            // libraryPanel
            //
            this.libraryPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)));
            this.libraryPanel.Location = new System.Drawing.Point(12, 12);
            this.libraryPanel.Name = "libraryPanel";
            this.libraryPanel.Size = new System.Drawing.Size(170, 397);
            this.libraryPanel.TabIndex = 0;
            //
            // helpPanel
            //
            this.helpPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.helpPanel.Location = new System.Drawing.Point(582, 12);
            this.helpPanel.Name = "helpPanel";
            this.helpPanel.Size = new System.Drawing.Size(284, 364);
            this.helpPanel.TabIndex = 2;
            //
            // labelRunAs
            //
            this.labelRunAs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.labelRunAs.AutoSize = true;
            this.labelRunAs.Location = new System.Drawing.Point(582, 391);
            this.labelRunAs.Name = "labelRunAs";
            this.labelRunAs.TabIndex = 5;
            this.labelRunAs.Text = "Run as:";
            //
            // comboRunAs
            //
            this.comboRunAs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.comboRunAs.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboRunAs.Location = new System.Drawing.Point(630, 387);
            this.comboRunAs.Name = "comboRunAs";
            this.comboRunAs.Size = new System.Drawing.Size(150, 21);
            this.comboRunAs.TabIndex = 6;
            this.comboRunAs.SelectedIndexChanged += new System.EventHandler(this.comboRunAs_SelectedIndexChanged);
            //
            // dlgScriptEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.ClientSize = new System.Drawing.Size(878, 421);
            this.Controls.Add(this.libraryPanel);
            this.Controls.Add(this.textBoxScript);
            this.Controls.Add(this.helpPanel);
            this.Controls.Add(this.buttonLoadScript);
            this.Controls.Add(this.buttonSaveScript);
            this.Controls.Add(this.labelRunAs);
            this.Controls.Add(this.comboRunAs);
            this.Controls.Add(this.buttonSendScript);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.MinimumSize = new System.Drawing.Size(720, 300);
            this.Name = "dlgScriptEditor";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Script Editor";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button buttonSendScript;
        private System.Windows.Forms.TextBox textBoxScript;
        private System.Windows.Forms.Button buttonLoadScript;
        private System.Windows.Forms.Button buttonSaveScript;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private SuperPutty.Gui.ScriptLibraryPanel libraryPanel;
        private SuperPutty.Gui.SpslHelpPanel helpPanel;
        private System.Windows.Forms.Label labelRunAs;
        private System.Windows.Forms.ComboBox comboRunAs;
    }
}