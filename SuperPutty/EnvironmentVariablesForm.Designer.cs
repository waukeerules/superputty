namespace SuperPutty
{
    partial class EnvironmentVariablesForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.dgvEnvironment = new System.Windows.Forms.DataGridView();
            this.colKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.lblInstruction = new System.Windows.Forms.Label();
            this.flowLayoutPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnAdd = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgvEnvironment)).BeginInit();
            this.flowLayoutPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // lblInstruction
            //
            this.lblInstruction.AutoSize = true;
            this.lblInstruction.Location = new System.Drawing.Point(12, 9);
            this.lblInstruction.Name = "lblInstruction";
            this.lblInstruction.Size = new System.Drawing.Size(300, 13);
            this.lblInstruction.Text = "Define global variables like {API_KEY} for use in session fields.";
            this.lblInstruction.Dock = System.Windows.Forms.DockStyle.Top;
            //
            // dgvEnvironment
            //
            this.dgvEnvironment.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvEnvironment.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colKey,
                this.colValue});
            this.dgvEnvironment.Location = new System.Drawing.Point(12, 30);
            this.dgvEnvironment.Name = "dgvEnvironment";
            this.dgvEnvironment.Size = new System.Drawing.Size(560, 300);
            this.dgvEnvironment.TabIndex = 0;
            this.dgvEnvironment.AllowUserToAddRows = true;
            this.dgvEnvironment.AllowUserToDeleteRows = true;
            this.dgvEnvironment.RowHeadersVisible = false;
            this.dgvEnvironment.CellValidating += new System.Windows.Forms.DataGridViewCellValidatingEventHandler(this.dgvEnvironment_CellValidating);
            this.dgvEnvironment.UserAddedRow += new System.Windows.Forms.DataGridViewRowEventHandler(this.dgvEnvironment_UserAddedRow);
            this.dgvEnvironment.Dock = System.Windows.Forms.DockStyle.Fill;
            //
            // colKey
            //
            this.colKey.HeaderText = "Key";
            this.colKey.Name = "colKey";
            this.colKey.Width = 200;
            //
            // colValue
            //
            this.colValue.HeaderText = "Value";
            this.colValue.Name = "colValue";
            this.colValue.Width = 300;
            //
            // flowLayoutPanel
            //
            this.flowLayoutPanel.Controls.Add(this.btnAdd);
            this.flowLayoutPanel.Controls.Add(this.btnOK);
            this.flowLayoutPanel.Controls.Add(this.btnCancel);
            this.flowLayoutPanel.Location = new System.Drawing.Point(12, 336);
            this.flowLayoutPanel.Name = "flowLayoutPanel";
            this.flowLayoutPanel.Size = new System.Drawing.Size(560, 40);
            this.flowLayoutPanel.TabIndex = 1;
            this.flowLayoutPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.flowLayoutPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            //
            // btnAdd
            //
            this.btnAdd.Location = new System.Drawing.Point(575, 8);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(75, 23);
            this.btnAdd.TabIndex = 2;
            this.btnAdd.Text = "Add";
            this.btnAdd.UseVisualStyleBackColor = true;
            this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);
            //
            // btnOK
            //
            this.btnOK.Location = new System.Drawing.Point(494, 8);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 0;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.Location = new System.Drawing.Point(413, 8);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // EnvironmentVariablesForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(584, 386);
            this.Controls.Add(this.dgvEnvironment);
            this.Controls.Add(this.flowLayoutPanel);
            this.Controls.Add(this.lblInstruction);
            this.Name = "EnvironmentVariablesForm";
            this.Text = "Environment Variables";
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            ((System.ComponentModel.ISupportInitialize)(this.dgvEnvironment)).EndInit();
            this.flowLayoutPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.DataGridView dgvEnvironment;
        private System.Windows.Forms.DataGridViewTextBoxColumn colKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn colValue;
        private System.Windows.Forms.Label lblInstruction;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnAdd;
    }
}