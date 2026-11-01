using System;
using System.Collections.Generic;
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

            // a right-click removes the row under the mouse (or the selected rows, if it's one of them)
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Remove", null, (s, e) => RemoveSelectedRows());
            dgvEnvironment.ContextMenuStrip = menu;
            dgvEnvironment.CellMouseDown += dgvEnvironment_CellMouseDown;
            new ToolTip().SetToolTip(btnRemove, "Remove the selected variables (also on the right-click menu)");

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
                Messenger.MessageBox($"Error loading variables: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Error saving variables: {0}", ex.Message);
                Messenger.MessageBox($"Error saving variables: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        /// <summary>
        /// Add a variable with a key that isn't taken yet (new_key, new_key2, ...) and an empty value, and start editing
        /// its key with the text selected, so typing replaces it. The grid has no empty "new row" line of its own: that
        /// made rows by accident, which an empty key then wouldn't let go of.
        /// </summary>
        /// <param name="sender">The Add button</param>
        /// <param name="e">Not used</param>
        private void btnAdd_Click(object sender, EventArgs e)
        {
            if (dgvEnvironment.IsCurrentCellInEditMode && !dgvEnvironment.EndEdit())
                return;
            string key = "new_key";
            for (int n = 2; _variables.Variables.Any(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)); n++)
            {
                key = "new_key" + n;
            }
            _variables.Variables.Add(new EnvironmentVariable { Key = key, Value = string.Empty });
            int row = _variables.Variables.Count - 1;
            dgvEnvironment.CurrentCell = dgvEnvironment.Rows[row].Cells[colKey.Index];
            dgvEnvironment.BeginEdit(true);
            Log.Info("Added new environment variable");
        }

        /// <summary>Remove the selected variables</summary>
        /// <param name="sender">The Remove button</param>
        /// <param name="e">Not used</param>
        private void btnRemove_Click(object sender, EventArgs e)
        {
            RemoveSelectedRows();
        }

        /// <summary>
        /// Remove the rows with a selected cell (or the current row). An edit in progress is dropped first, so a row
        /// whose key is empty or not valid can still be removed; the Remove button doesn't trigger the key's validation.
        /// </summary>
        private void RemoveSelectedRows()
        {
            dgvEnvironment.CancelEdit();
            dgvEnvironment.EndEdit();
            List<int> rows = dgvEnvironment.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex)
                .Concat(dgvEnvironment.CurrentCell == null ? Enumerable.Empty<int>() : new[] { dgvEnvironment.CurrentCell.RowIndex })
                .Where(r => r >= 0 && r < _variables.Variables.Count)
                .Distinct().OrderByDescending(r => r).ToList();
            foreach (int row in rows)
            {
                _variables.Variables.RemoveAt(row);
            }
            if (rows.Count > 0)
            {
                Log.InfoFormat("Removed {0} environment variables", rows.Count);
            }
        }

        /// <summary>Before the right-click menu opens, select the row under the mouse unless it's already selected</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">The cell under the mouse</param>
        private void dgvEnvironment_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            DataGridViewCell cell = dgvEnvironment.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (!cell.Selected)
            {
                dgvEnvironment.CancelEdit();
                dgvEnvironment.CurrentCell = cell;
            }
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

        /// <summary>
        /// Whether the left button is down on a control: the grid checks a key when it loses focus, before a button's
        /// click, so this is how a click on Remove or Cancel is told apart from leaving the key some other way
        /// </summary>
        /// <param name="control">The control</param>
        /// <returns>true while it's being clicked</returns>
        private static bool BeingClicked(Control control)
        {
            return (Control.MouseButtons & MouseButtons.Left) != 0 && control.RectangleToScreen(control.ClientRectangle).Contains(Control.MousePosition);
        }

        private void dgvEnvironment_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            // no key check on the way to Remove (the row goes) or Cancel (nothing is saved)
            if (BeingClicked(btnRemove) || BeingClicked(btnCancel))
                return;
            if (e.ColumnIndex == 0) // Key column
            {
                string newKey = e.FormattedValue?.ToString();
                if (string.IsNullOrWhiteSpace(newKey))
                {
                    e.Cancel = true;
                    Messenger.MessageBox("Key cannot be empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(newKey, @"^\w+$"))
                {
                    e.Cancel = true;
                    Messenger.MessageBox("Key can only contain letters, numbers and underscores, e.g. user or vault_user.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (_variables.Variables.Any(v => v.Key != null &&
                                                     v.Key.Equals(newKey, StringComparison.OrdinalIgnoreCase) &&
                                                     _variables.Variables.IndexOf(v) != e.RowIndex))
                {
                    e.Cancel = true;
                    Messenger.MessageBox("Key must be unique.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}