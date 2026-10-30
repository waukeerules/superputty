namespace SuperPutty.Gui
{
    partial class dlgSearchOutput
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
            this.textBoxFind = new System.Windows.Forms.TextBox();
            this.checkMatchCase = new System.Windows.Forms.CheckBox();
            this.checkRegex = new System.Windows.Forms.CheckBox();
            this.btnFind = new System.Windows.Forms.Button();
            this.listResults = new System.Windows.Forms.ListView();
            this.colLine = new System.Windows.Forms.ColumnHeader();
            this.colText = new System.Windows.Forms.ColumnHeader();
            this.labelSummary = new System.Windows.Forms.Label();
            this.btnOpenLog = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // textBoxFind
            //
            this.textBoxFind.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxFind.Location = new System.Drawing.Point(12, 12);
            this.textBoxFind.Name = "textBoxFind";
            this.textBoxFind.Size = new System.Drawing.Size(420, 27);
            this.textBoxFind.TabIndex = 0;
            //
            // checkMatchCase
            //
            this.checkMatchCase.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.checkMatchCase.AutoSize = true;
            this.checkMatchCase.Location = new System.Drawing.Point(444, 15);
            this.checkMatchCase.Name = "checkMatchCase";
            this.checkMatchCase.TabIndex = 1;
            this.checkMatchCase.Text = "Match case";
            this.checkMatchCase.UseVisualStyleBackColor = true;
            //
            // checkRegex
            //
            this.checkRegex.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.checkRegex.AutoSize = true;
            this.checkRegex.Location = new System.Drawing.Point(560, 15);
            this.checkRegex.Name = "checkRegex";
            this.checkRegex.TabIndex = 2;
            this.checkRegex.Text = "Regex";
            this.checkRegex.UseVisualStyleBackColor = true;
            //
            // btnFind
            //
            this.btnFind.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnFind.Location = new System.Drawing.Point(640, 10);
            this.btnFind.Name = "btnFind";
            this.btnFind.Size = new System.Drawing.Size(100, 31);
            this.btnFind.TabIndex = 3;
            this.btnFind.Text = "Find";
            this.btnFind.UseVisualStyleBackColor = true;
            this.btnFind.Click += new System.EventHandler(this.btnFind_Click);
            //
            // listResults
            //
            this.listResults.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listResults.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colLine,
            this.colText});
            this.listResults.Font = new System.Drawing.Font("Consolas", 9F);
            this.listResults.FullRowSelect = true;
            this.listResults.HideSelection = false;
            this.listResults.Location = new System.Drawing.Point(12, 50);
            this.listResults.Name = "listResults";
            this.listResults.Size = new System.Drawing.Size(728, 372);
            this.listResults.TabIndex = 4;
            this.listResults.UseCompatibleStateImageBehavior = false;
            this.listResults.View = System.Windows.Forms.View.Details;
            this.listResults.DoubleClick += new System.EventHandler(this.listResults_DoubleClick);
            //
            // colLine
            //
            this.colLine.Text = "Line";
            this.colLine.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.colLine.Width = 70;
            //
            // colText
            //
            this.colText.Text = "Text";
            this.colText.Width = 640;
            //
            // labelSummary
            //
            this.labelSummary.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSummary.AutoEllipsis = true;
            this.labelSummary.Location = new System.Drawing.Point(12, 436);
            this.labelSummary.Name = "labelSummary";
            this.labelSummary.Size = new System.Drawing.Size(600, 24);
            this.labelSummary.TabIndex = 5;
            this.labelSummary.Text = "Type text to find in what this tab has printed, then press Enter.";
            //
            // btnOpenLog
            //
            this.btnOpenLog.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenLog.Location = new System.Drawing.Point(620, 431);
            this.btnOpenLog.Name = "btnOpenLog";
            this.btnOpenLog.Size = new System.Drawing.Size(120, 31);
            this.btnOpenLog.TabIndex = 6;
            this.btnOpenLog.Text = "Open log";
            this.btnOpenLog.UseVisualStyleBackColor = true;
            this.btnOpenLog.Click += new System.EventHandler(this.btnOpenLog_Click);
            //
            // dlgSearchOutput
            //
            this.AcceptButton = this.btnFind;
            this.AutoScaleDimensions = new System.Drawing.SizeF(120F, 120F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(752, 472);
            this.Controls.Add(this.textBoxFind);
            this.Controls.Add(this.checkMatchCase);
            this.Controls.Add(this.checkRegex);
            this.Controls.Add(this.btnFind);
            this.Controls.Add(this.listResults);
            this.Controls.Add(this.labelSummary);
            this.Controls.Add(this.btnOpenLog);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(500, 300);
            this.Name = "dlgSearchOutput";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Search Output";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBoxFind;
        private System.Windows.Forms.CheckBox checkMatchCase;
        private System.Windows.Forms.CheckBox checkRegex;
        private System.Windows.Forms.Button btnFind;
        private System.Windows.Forms.ListView listResults;
        private System.Windows.Forms.ColumnHeader colLine;
        private System.Windows.Forms.ColumnHeader colText;
        private System.Windows.Forms.Label labelSummary;
        private System.Windows.Forms.Button btnOpenLog;
    }
}
