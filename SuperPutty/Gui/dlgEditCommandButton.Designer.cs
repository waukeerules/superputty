namespace SuperPutty.Gui
{
    partial class dlgEditCommandButton
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
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
            this.labelLabel = new System.Windows.Forms.Label();
            this.textBoxLabel = new System.Windows.Forms.TextBox();
            this.labelTooltip = new System.Windows.Forms.Label();
            this.textBoxTooltip = new System.Windows.Forms.TextBox();
            this.labelAction = new System.Windows.Forms.Label();
            this.comboBoxAction = new System.Windows.Forms.ComboBox();
            this.labelIcon = new System.Windows.Forms.Label();
            this.comboBoxIcon = new System.Windows.Forms.ComboBox();
            this.labelText = new System.Windows.Forms.Label();
            this.textBoxText = new System.Windows.Forms.TextBox();
            this.labelHelp = new System.Windows.Forms.Label();
            this.helpPanel = new SuperPutty.Gui.SpslHelpPanel();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // labelLabel
            //
            this.labelLabel.AutoSize = true;
            this.labelLabel.Location = new System.Drawing.Point(12, 15);
            this.labelLabel.Name = "labelLabel";
            this.labelLabel.Size = new System.Drawing.Size(36, 13);
            this.labelLabel.TabIndex = 0;
            this.labelLabel.Text = "&Label:";
            //
            // textBoxLabel
            //
            this.textBoxLabel.Location = new System.Drawing.Point(80, 12);
            this.textBoxLabel.Name = "textBoxLabel";
            this.textBoxLabel.Size = new System.Drawing.Size(388, 20);
            this.textBoxLabel.TabIndex = 1;
            //
            // labelTooltip
            //
            this.labelTooltip.AutoSize = true;
            this.labelTooltip.Location = new System.Drawing.Point(12, 41);
            this.labelTooltip.Name = "labelTooltip";
            this.labelTooltip.Size = new System.Drawing.Size(42, 13);
            this.labelTooltip.TabIndex = 2;
            this.labelTooltip.Text = "T&ooltip:";
            //
            // textBoxTooltip
            //
            this.textBoxTooltip.Location = new System.Drawing.Point(80, 38);
            this.textBoxTooltip.Name = "textBoxTooltip";
            this.textBoxTooltip.Size = new System.Drawing.Size(388, 20);
            this.textBoxTooltip.TabIndex = 3;
            //
            // labelAction
            //
            this.labelAction.AutoSize = true;
            this.labelAction.Location = new System.Drawing.Point(12, 67);
            this.labelAction.Name = "labelAction";
            this.labelAction.Size = new System.Drawing.Size(40, 13);
            this.labelAction.TabIndex = 4;
            this.labelAction.Text = "&Action:";
            //
            // comboBoxAction
            //
            this.comboBoxAction.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxAction.FormattingEnabled = true;
            this.comboBoxAction.Items.AddRange(new object[] {
            "Send string",
            "Run SPSL script"});
            this.comboBoxAction.Location = new System.Drawing.Point(80, 64);
            this.comboBoxAction.Name = "comboBoxAction";
            this.comboBoxAction.Size = new System.Drawing.Size(160, 21);
            this.comboBoxAction.TabIndex = 5;
            this.comboBoxAction.SelectedIndexChanged += new System.EventHandler(this.comboBoxAction_SelectedIndexChanged);
            //
            // labelIcon
            //
            this.labelIcon.AutoSize = true;
            this.labelIcon.Location = new System.Drawing.Point(12, 94);
            this.labelIcon.Name = "labelIcon";
            this.labelIcon.Size = new System.Drawing.Size(31, 13);
            this.labelIcon.TabIndex = 6;
            this.labelIcon.Text = "&Icon:";
            //
            // comboBoxIcon
            //
            this.comboBoxIcon.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.comboBoxIcon.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxIcon.FormattingEnabled = true;
            this.comboBoxIcon.ItemHeight = 18;
            this.comboBoxIcon.Location = new System.Drawing.Point(80, 90);
            this.comboBoxIcon.MaxDropDownItems = 16;
            this.comboBoxIcon.Name = "comboBoxIcon";
            this.comboBoxIcon.Size = new System.Drawing.Size(200, 24);
            this.comboBoxIcon.TabIndex = 7;
            this.comboBoxIcon.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.comboBoxIcon_DrawItem);
            this.comboBoxIcon.SelectionChangeCommitted += new System.EventHandler(this.comboBoxIcon_SelectionChangeCommitted);
            //
            // labelText
            //
            this.labelText.AutoSize = true;
            this.labelText.Location = new System.Drawing.Point(12, 123);
            this.labelText.Name = "labelText";
            this.labelText.Size = new System.Drawing.Size(35, 13);
            this.labelText.TabIndex = 8;
            this.labelText.Text = "&Send:";
            //
            // textBoxText
            //
            this.textBoxText.AcceptsReturn = true;
            this.textBoxText.Font = new System.Drawing.Font("Consolas", 9F);
            this.textBoxText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left)));
            this.textBoxText.Location = new System.Drawing.Point(80, 120);
            this.textBoxText.Multiline = true;
            this.textBoxText.Name = "textBoxText";
            this.textBoxText.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxText.Size = new System.Drawing.Size(388, 131);
            this.textBoxText.TabIndex = 9;
            //
            // helpPanel
            //
            this.helpPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left)));
            this.helpPanel.Location = new System.Drawing.Point(480, 12);
            this.helpPanel.Name = "helpPanel";
            this.helpPanel.Size = new System.Drawing.Size(290, 306);
            this.helpPanel.TabIndex = 21;
            this.helpPanel.Visible = false;
            //
            // labelHelp
            //
            this.labelHelp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelHelp.Location = new System.Drawing.Point(77, 256);
            this.labelHelp.Name = "labelHelp";
            this.labelHelp.Size = new System.Drawing.Size(391, 32);
            this.labelHelp.TabIndex = 10;
            //
            // btnOK
            //
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnOK.Location = new System.Drawing.Point(312, 295);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 11;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnCancel.Location = new System.Drawing.Point(393, 295);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 12;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // dlgEditCommandButton
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(480, 330);
            this.Controls.Add(this.labelLabel);
            this.Controls.Add(this.textBoxLabel);
            this.Controls.Add(this.labelTooltip);
            this.Controls.Add(this.textBoxTooltip);
            this.Controls.Add(this.labelAction);
            this.Controls.Add(this.comboBoxAction);
            this.Controls.Add(this.labelIcon);
            this.Controls.Add(this.comboBoxIcon);
            this.Controls.Add(this.labelText);
            this.Controls.Add(this.textBoxText);
            this.Controls.Add(this.labelHelp);
            this.Controls.Add(this.helpPanel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "dlgEditCommandButton";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Button";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labelLabel;
        private System.Windows.Forms.TextBox textBoxLabel;
        private System.Windows.Forms.Label labelTooltip;
        private System.Windows.Forms.TextBox textBoxTooltip;
        private System.Windows.Forms.Label labelAction;
        private System.Windows.Forms.ComboBox comboBoxAction;
        private System.Windows.Forms.Label labelIcon;
        private System.Windows.Forms.ComboBox comboBoxIcon;
        private System.Windows.Forms.Label labelText;
        private System.Windows.Forms.TextBox textBoxText;
        private System.Windows.Forms.Label labelHelp;
        private SuperPutty.Gui.SpslHelpPanel helpPanel;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
