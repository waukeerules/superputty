using System;
using System.Linq;
using System.Windows.Forms;
using SuperPutty.Models;
using SuperPutty.Utils;
using log4net;
using DarkModeForms;

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

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        private void LoadVariables()
        {
            try
            {
                _variables = _envManager.LoadVariables();
                dgvEnvironment.AutoGenerateColumns = false;
                dgvEnvironment.Columns["colKey"].DataPropertyName = "Key";
                dgvEnvironment.Columns["colValue"].DataPropertyName = "Value";
                dgvEnvironment.Columns["colSecret"].DataPropertyName = "Secret";
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

        /// <summary>Text shown instead of a secret's value</summary>
        private const string MaskedValue = "••••••••";

        /// <summary>Whether the variable in a grid row is marked secret</summary>
        /// <param name="rowIndex">The grid row</param>
        /// <returns>true for a secret; false for an ordinary variable or the new-row line</returns>
        private bool IsSecretRow(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex < _variables.Variables.Count && _variables.Variables[rowIndex].Secret;
        }

        /// <summary>Show dots instead of a secret's value</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">The cell being drawn</param>
        private void dgvEnvironment_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == colValue.Index && IsSecretRow(e.RowIndex))
            {
                e.Value = MaskedValue;
                e.FormattingApplied = true;
            }
        }

        /// <summary>Edit a secret's value in a password box; the grid reuses one text box, so reset it for other cells</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">The editing text box</param>
        private void dgvEnvironment_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            TextBox textBox = e.Control as TextBox;
            if (textBox != null)
            {
                bool secret = dgvEnvironment.CurrentCell.ColumnIndex == colValue.Index && IsSecretRow(dgvEnvironment.CurrentCell.RowIndex);
                textBox.UseSystemPasswordChar = secret;
                if (secret)
                {
                    // the box starts with the masked text from CellFormatting; give it the real value so leaving it unchanged keeps it
                    textBox.Text = _variables.Variables[dgvEnvironment.CurrentCell.RowIndex].Value;
                }
            }
        }

        /// <summary>Apply a Secret tick straight away, so the value is masked or shown without leaving the cell</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">Not used</param>
        private void dgvEnvironment_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvEnvironment.CurrentCell != null && dgvEnvironment.CurrentCell.ColumnIndex == colSecret.Index && dgvEnvironment.IsCurrentCellDirty)
            {
                dgvEnvironment.CommitEdit(DataGridViewDataErrorContexts.Commit);
                dgvEnvironment.InvalidateRow(dgvEnvironment.CurrentCell.RowIndex);
            }
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
                else if (!System.Text.RegularExpressions.Regex.IsMatch(newKey, @"^\w+$"))
                {
                    e.Cancel = true;
                    MessageBox.Show("Key can only contain letters, numbers and underscores, e.g. user or vault_user.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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