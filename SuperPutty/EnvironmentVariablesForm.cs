using System;
using System.Linq;
using System.Windows.Forms;
using SuperPutty.Models;
using SuperPutty.Utils;
using log4net;

namespace SuperPutty
{
    public partial class EnvironmentVariablesForm : Form
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(EnvironmentVariablesForm));
        private readonly EnvironmentVariablesManager _envManager;
        private EnvironmentVariables _variables;

        public EnvironmentVariablesForm()
        {
            InitializeComponent();
            _envManager = new EnvironmentVariablesManager();
            LoadVariables();
        }

        private void LoadVariables()
        {
            try
            {
                _variables = _envManager.LoadVariables();
                dgvEnvironment.AutoGenerateColumns = false;
                dgvEnvironment.Columns["colKey"].DataPropertyName = "Key";
                dgvEnvironment.Columns["colValue"].DataPropertyName = "Value";
                dgvEnvironment.DataSource = _variables.Variables;
                Log.InfoFormat("Loaded {0} environment variables into DataGridView", _variables.Variables.Count);
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Error loading variables: {0}", ex.Message);
                MessageBox.Show($"Error loading variables: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveVariables()
        {
            try
            {
                // Remove any incomplete entries (e.g., empty keys)
                var validVariables = _variables.Variables
                    .Where(v => !string.IsNullOrWhiteSpace(v.Key))
                    .ToList();
                _variables.Variables.Clear();
                foreach (var v in validVariables)
                {
                    _variables.Variables.Add(v);
                }

                _envManager.SaveVariables(_variables);
                Log.InfoFormat("Saved {0} environment variables", _variables.Variables.Count);
                MessageBox.Show("Variables saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Error saving variables: {0}", ex.Message);
                MessageBox.Show($"Error saving variables: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            SaveVariables();
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            _variables.Variables.Add(new EnvironmentVariable { Key = "NewKey", Value = "NewValue" });
            dgvEnvironment.Refresh();
            Log.Info("Added new environment variable");
        }

        private void dgvEnvironment_UserAddedRow(object sender, DataGridViewRowEventArgs e)
        {
            // BindingList automatically handles new row additions, no need to add explicitly
            Log.Info("User added new row via DataGridView");
        }

        private void dgvEnvironment_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == 0) // Key column
            {
                string newKey = e.FormattedValue?.ToString();
                if (string.IsNullOrWhiteSpace(newKey))
                {
                    e.Cancel = true;
                    MessageBox.Show("Key cannot be empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (_variables.Variables.Any(v => v.Key != null &&
                                                     v.Key.Equals(newKey, StringComparison.OrdinalIgnoreCase) &&
                                                     _variables.Variables.IndexOf(v) != e.RowIndex))
                {
                    e.Cancel = true;
                    MessageBox.Show("Key must be unique.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}