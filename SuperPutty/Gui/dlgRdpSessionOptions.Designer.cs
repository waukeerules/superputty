namespace SuperPutty.Gui
{
    partial class dlgRdpSessionOptions
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
            this.labelHelp = new System.Windows.Forms.Label();
            this.groupBoxDisplay = new System.Windows.Forms.GroupBox();
            this.labelSize = new System.Windows.Forms.Label();
            this.comboDisplayMode = new System.Windows.Forms.ComboBox();
            this.numericWidth = new System.Windows.Forms.NumericUpDown();
            this.labelBy = new System.Windows.Forms.Label();
            this.numericHeight = new System.Windows.Forms.NumericUpDown();
            this.labelColor = new System.Windows.Forms.Label();
            this.comboColorDepth = new System.Windows.Forms.ComboBox();
            this.checkSmartSizing = new System.Windows.Forms.CheckBox();
            this.groupBoxResources = new System.Windows.Forms.GroupBox();
            this.labelRedirect = new System.Windows.Forms.Label();
            this.checkClipboard = new System.Windows.Forms.CheckBox();
            this.checkDrives = new System.Windows.Forms.CheckBox();
            this.checkPrinters = new System.Windows.Forms.CheckBox();
            this.checkSmartCards = new System.Windows.Forms.CheckBox();
            this.checkMicrophone = new System.Windows.Forms.CheckBox();
            this.labelAudio = new System.Windows.Forms.Label();
            this.comboAudio = new System.Windows.Forms.ComboBox();
            this.labelKeyboard = new System.Windows.Forms.Label();
            this.comboKeyboard = new System.Windows.Forms.ComboBox();
            this.groupBoxConnection = new System.Windows.Forms.GroupBox();
            this.labelAuth = new System.Windows.Forms.Label();
            this.comboAuth = new System.Windows.Forms.ComboBox();
            this.labelGateway = new System.Windows.Forms.Label();
            this.textBoxGateway = new System.Windows.Forms.TextBox();
            this.checkNoGateway = new System.Windows.Forms.CheckBox();
            this.checkConsole = new System.Windows.Forms.CheckBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.groupBoxDisplay.SuspendLayout();
            this.groupBoxResources.SuspendLayout();
            this.groupBoxConnection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericHeight)).BeginInit();
            this.SuspendLayout();
            // 
            // labelHelp
            // 
            this.labelHelp.AutoSize = true;
            this.labelHelp.Location = new System.Drawing.Point(12, 12);
            this.labelHelp.Name = "labelHelp";
            this.labelHelp.TabIndex = 0;
            this.labelHelp.Text = "Options left at Default follow Tools -> Options -> RDP. A filled checkbox means Default.";
            // 
            // groupBoxDisplay
            // 
            this.groupBoxDisplay.Controls.Add(this.labelSize);
            this.groupBoxDisplay.Controls.Add(this.comboDisplayMode);
            this.groupBoxDisplay.Controls.Add(this.numericWidth);
            this.groupBoxDisplay.Controls.Add(this.labelBy);
            this.groupBoxDisplay.Controls.Add(this.numericHeight);
            this.groupBoxDisplay.Controls.Add(this.labelColor);
            this.groupBoxDisplay.Controls.Add(this.comboColorDepth);
            this.groupBoxDisplay.Controls.Add(this.checkSmartSizing);
            this.groupBoxDisplay.Location = new System.Drawing.Point(8, 44);
            this.groupBoxDisplay.Name = "groupBoxDisplay";
            this.groupBoxDisplay.Size = new System.Drawing.Size(764, 140);
            this.groupBoxDisplay.TabIndex = 1;
            this.groupBoxDisplay.TabStop = false;
            this.groupBoxDisplay.Text = "Display";
            // 
            // labelSize
            // 
            this.labelSize.AutoSize = true;
            this.labelSize.Location = new System.Drawing.Point(16, 32);
            this.labelSize.Name = "labelSize";
            this.labelSize.TabIndex = 0;
            this.labelSize.Text = "Desktop size:";
            // 
            // comboDisplayMode
            // 
            this.comboDisplayMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboDisplayMode.FormattingEnabled = true;
            this.comboDisplayMode.Location = new System.Drawing.Point(190, 28);
            this.comboDisplayMode.Name = "comboDisplayMode";
            this.comboDisplayMode.Size = new System.Drawing.Size(200, 28);
            this.comboDisplayMode.TabIndex = 1;
            this.comboDisplayMode.SelectedIndexChanged += new System.EventHandler(this.comboDisplayMode_SelectedIndexChanged);
            // 
            // numericWidth
            // 
            this.numericWidth.Location = new System.Drawing.Point(400, 29);
            this.numericWidth.Maximum = new decimal(new int[] {
            8192,
            0,
            0,
            0});
            this.numericWidth.Minimum = new decimal(new int[] {
            200,
            0,
            0,
            0});
            this.numericWidth.Name = "numericWidth";
            this.numericWidth.Size = new System.Drawing.Size(80, 27);
            this.numericWidth.TabIndex = 2;
            this.numericWidth.Value = new decimal(new int[] {
            1920,
            0,
            0,
            0});
            // 
            // labelBy
            // 
            this.labelBy.AutoSize = true;
            this.labelBy.Location = new System.Drawing.Point(484, 32);
            this.labelBy.Name = "labelBy";
            this.labelBy.TabIndex = 3;
            this.labelBy.Text = "x";
            // 
            // numericHeight
            // 
            this.numericHeight.Location = new System.Drawing.Point(502, 29);
            this.numericHeight.Maximum = new decimal(new int[] {
            8192,
            0,
            0,
            0});
            this.numericHeight.Minimum = new decimal(new int[] {
            200,
            0,
            0,
            0});
            this.numericHeight.Name = "numericHeight";
            this.numericHeight.Size = new System.Drawing.Size(80, 27);
            this.numericHeight.TabIndex = 4;
            this.numericHeight.Value = new decimal(new int[] {
            1920,
            0,
            0,
            0});
            // 
            // labelColor
            // 
            this.labelColor.AutoSize = true;
            this.labelColor.Location = new System.Drawing.Point(16, 68);
            this.labelColor.Name = "labelColor";
            this.labelColor.TabIndex = 5;
            this.labelColor.Text = "Colour depth:";
            // 
            // comboColorDepth
            // 
            this.comboColorDepth.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboColorDepth.FormattingEnabled = true;
            this.comboColorDepth.Location = new System.Drawing.Point(190, 64);
            this.comboColorDepth.Name = "comboColorDepth";
            this.comboColorDepth.Size = new System.Drawing.Size(200, 28);
            this.comboColorDepth.TabIndex = 6;
            // 
            // checkSmartSizing
            // 
            this.checkSmartSizing.AutoSize = true;
            this.checkSmartSizing.Location = new System.Drawing.Point(190, 100);
            this.checkSmartSizing.Name = "checkSmartSizing";
            this.checkSmartSizing.TabIndex = 7;
            this.checkSmartSizing.Text = "Scale the remote desktop to fit (smart sizing)";
            this.checkSmartSizing.ThreeState = true;
            this.checkSmartSizing.UseVisualStyleBackColor = true;
            // 
            // groupBoxResources
            // 
            this.groupBoxResources.Controls.Add(this.labelRedirect);
            this.groupBoxResources.Controls.Add(this.checkClipboard);
            this.groupBoxResources.Controls.Add(this.checkDrives);
            this.groupBoxResources.Controls.Add(this.checkPrinters);
            this.groupBoxResources.Controls.Add(this.checkSmartCards);
            this.groupBoxResources.Controls.Add(this.checkMicrophone);
            this.groupBoxResources.Controls.Add(this.labelAudio);
            this.groupBoxResources.Controls.Add(this.comboAudio);
            this.groupBoxResources.Controls.Add(this.labelKeyboard);
            this.groupBoxResources.Controls.Add(this.comboKeyboard);
            this.groupBoxResources.Location = new System.Drawing.Point(8, 190);
            this.groupBoxResources.Name = "groupBoxResources";
            this.groupBoxResources.Size = new System.Drawing.Size(764, 140);
            this.groupBoxResources.TabIndex = 2;
            this.groupBoxResources.TabStop = false;
            this.groupBoxResources.Text = "Local resources";
            // 
            // labelRedirect
            // 
            this.labelRedirect.AutoSize = true;
            this.labelRedirect.Location = new System.Drawing.Point(16, 32);
            this.labelRedirect.Name = "labelRedirect";
            this.labelRedirect.TabIndex = 0;
            this.labelRedirect.Text = "Use on remote:";
            // 
            // checkClipboard
            // 
            this.checkClipboard.AutoSize = true;
            this.checkClipboard.Location = new System.Drawing.Point(190, 30);
            this.checkClipboard.Name = "checkClipboard";
            this.checkClipboard.TabIndex = 1;
            this.checkClipboard.Text = "Clipboard";
            this.checkClipboard.ThreeState = true;
            this.checkClipboard.UseVisualStyleBackColor = true;
            // 
            // checkDrives
            // 
            this.checkDrives.AutoSize = true;
            this.checkDrives.Location = new System.Drawing.Point(300, 30);
            this.checkDrives.Name = "checkDrives";
            this.checkDrives.TabIndex = 2;
            this.checkDrives.Text = "Drives";
            this.checkDrives.ThreeState = true;
            this.checkDrives.UseVisualStyleBackColor = true;
            // 
            // checkPrinters
            // 
            this.checkPrinters.AutoSize = true;
            this.checkPrinters.Location = new System.Drawing.Point(390, 30);
            this.checkPrinters.Name = "checkPrinters";
            this.checkPrinters.TabIndex = 3;
            this.checkPrinters.Text = "Printers";
            this.checkPrinters.ThreeState = true;
            this.checkPrinters.UseVisualStyleBackColor = true;
            // 
            // checkSmartCards
            // 
            this.checkSmartCards.AutoSize = true;
            this.checkSmartCards.Location = new System.Drawing.Point(490, 30);
            this.checkSmartCards.Name = "checkSmartCards";
            this.checkSmartCards.TabIndex = 4;
            this.checkSmartCards.Text = "Smart cards";
            this.checkSmartCards.ThreeState = true;
            this.checkSmartCards.UseVisualStyleBackColor = true;
            // 
            // checkMicrophone
            // 
            this.checkMicrophone.AutoSize = true;
            this.checkMicrophone.Location = new System.Drawing.Point(610, 30);
            this.checkMicrophone.Name = "checkMicrophone";
            this.checkMicrophone.TabIndex = 5;
            this.checkMicrophone.Text = "Microphone";
            this.checkMicrophone.ThreeState = true;
            this.checkMicrophone.UseVisualStyleBackColor = true;
            // 
            // labelAudio
            // 
            this.labelAudio.AutoSize = true;
            this.labelAudio.Location = new System.Drawing.Point(16, 68);
            this.labelAudio.Name = "labelAudio";
            this.labelAudio.TabIndex = 6;
            this.labelAudio.Text = "Remote audio:";
            // 
            // comboAudio
            // 
            this.comboAudio.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboAudio.FormattingEnabled = true;
            this.comboAudio.Location = new System.Drawing.Point(190, 64);
            this.comboAudio.Name = "comboAudio";
            this.comboAudio.Size = new System.Drawing.Size(330, 28);
            this.comboAudio.TabIndex = 7;
            // 
            // labelKeyboard
            // 
            this.labelKeyboard.AutoSize = true;
            this.labelKeyboard.Location = new System.Drawing.Point(16, 104);
            this.labelKeyboard.Name = "labelKeyboard";
            this.labelKeyboard.TabIndex = 8;
            this.labelKeyboard.Text = "Windows key combos:";
            // 
            // comboKeyboard
            // 
            this.comboKeyboard.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboKeyboard.FormattingEnabled = true;
            this.comboKeyboard.Location = new System.Drawing.Point(190, 100);
            this.comboKeyboard.Name = "comboKeyboard";
            this.comboKeyboard.Size = new System.Drawing.Size(330, 28);
            this.comboKeyboard.TabIndex = 9;
            // 
            // groupBoxConnection
            // 
            this.groupBoxConnection.Controls.Add(this.labelAuth);
            this.groupBoxConnection.Controls.Add(this.comboAuth);
            this.groupBoxConnection.Controls.Add(this.labelGateway);
            this.groupBoxConnection.Controls.Add(this.textBoxGateway);
            this.groupBoxConnection.Controls.Add(this.checkNoGateway);
            this.groupBoxConnection.Controls.Add(this.checkConsole);
            this.groupBoxConnection.Location = new System.Drawing.Point(8, 336);
            this.groupBoxConnection.Name = "groupBoxConnection";
            this.groupBoxConnection.Size = new System.Drawing.Size(764, 140);
            this.groupBoxConnection.TabIndex = 3;
            this.groupBoxConnection.TabStop = false;
            this.groupBoxConnection.Text = "Connection";
            // 
            // labelAuth
            // 
            this.labelAuth.AutoSize = true;
            this.labelAuth.Location = new System.Drawing.Point(16, 32);
            this.labelAuth.Name = "labelAuth";
            this.labelAuth.TabIndex = 0;
            this.labelAuth.Text = "Server authentication:";
            // 
            // comboAuth
            // 
            this.comboAuth.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboAuth.FormattingEnabled = true;
            this.comboAuth.Location = new System.Drawing.Point(190, 28);
            this.comboAuth.Name = "comboAuth";
            this.comboAuth.Size = new System.Drawing.Size(330, 28);
            this.comboAuth.TabIndex = 1;
            // 
            // labelGateway
            // 
            this.labelGateway.AutoSize = true;
            this.labelGateway.Location = new System.Drawing.Point(16, 68);
            this.labelGateway.Name = "labelGateway";
            this.labelGateway.TabIndex = 2;
            this.labelGateway.Text = "RD Gateway:";
            // 
            // textBoxGateway
            // 
            this.textBoxGateway.Location = new System.Drawing.Point(190, 65);
            this.textBoxGateway.Name = "textBoxGateway";
            this.textBoxGateway.Size = new System.Drawing.Size(330, 27);
            this.textBoxGateway.TabIndex = 3;
            // 
            // checkNoGateway
            // 
            this.checkNoGateway.AutoSize = true;
            this.checkNoGateway.Location = new System.Drawing.Point(532, 67);
            this.checkNoGateway.Name = "checkNoGateway";
            this.checkNoGateway.TabIndex = 4;
            this.checkNoGateway.Text = "Don't use a gateway";
            this.checkNoGateway.UseVisualStyleBackColor = true;
            this.checkNoGateway.CheckedChanged += new System.EventHandler(this.checkNoGateway_CheckedChanged);
            // 
            // checkConsole
            // 
            this.checkConsole.AutoSize = true;
            this.checkConsole.Location = new System.Drawing.Point(190, 100);
            this.checkConsole.Name = "checkConsole";
            this.checkConsole.TabIndex = 5;
            this.checkConsole.Text = "Connect to the admin (console) session";
            this.checkConsole.ThreeState = true;
            this.checkConsole.UseVisualStyleBackColor = true;
            // 
            // btnOK
            // 
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Location = new System.Drawing.Point(564, 488);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(100, 32);
            this.btnOK.TabIndex = 4;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(672, 488);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 32);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // dlgRdpSessionOptions
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(120F, 120F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(780, 532);
            this.Controls.Add(this.labelHelp);
            this.Controls.Add(this.groupBoxDisplay);
            this.Controls.Add(this.groupBoxResources);
            this.Controls.Add(this.groupBoxConnection);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "dlgRdpSessionOptions";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "RDP Options";
            this.groupBoxDisplay.ResumeLayout(false);
            this.groupBoxDisplay.PerformLayout();
            this.groupBoxResources.ResumeLayout(false);
            this.groupBoxResources.PerformLayout();
            this.groupBoxConnection.ResumeLayout(false);
            this.groupBoxConnection.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericHeight)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labelHelp;
        private System.Windows.Forms.GroupBox groupBoxDisplay;
        private System.Windows.Forms.Label labelSize;
        private System.Windows.Forms.ComboBox comboDisplayMode;
        private System.Windows.Forms.NumericUpDown numericWidth;
        private System.Windows.Forms.Label labelBy;
        private System.Windows.Forms.NumericUpDown numericHeight;
        private System.Windows.Forms.Label labelColor;
        private System.Windows.Forms.ComboBox comboColorDepth;
        private System.Windows.Forms.CheckBox checkSmartSizing;
        private System.Windows.Forms.GroupBox groupBoxResources;
        private System.Windows.Forms.Label labelRedirect;
        private System.Windows.Forms.CheckBox checkClipboard;
        private System.Windows.Forms.CheckBox checkDrives;
        private System.Windows.Forms.CheckBox checkPrinters;
        private System.Windows.Forms.CheckBox checkSmartCards;
        private System.Windows.Forms.CheckBox checkMicrophone;
        private System.Windows.Forms.Label labelAudio;
        private System.Windows.Forms.ComboBox comboAudio;
        private System.Windows.Forms.Label labelKeyboard;
        private System.Windows.Forms.ComboBox comboKeyboard;
        private System.Windows.Forms.GroupBox groupBoxConnection;
        private System.Windows.Forms.Label labelAuth;
        private System.Windows.Forms.ComboBox comboAuth;
        private System.Windows.Forms.Label labelGateway;
        private System.Windows.Forms.TextBox textBoxGateway;
        private System.Windows.Forms.CheckBox checkNoGateway;
        private System.Windows.Forms.CheckBox checkConsole;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
