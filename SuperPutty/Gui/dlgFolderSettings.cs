using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;

namespace SuperPutty.Gui
{
    /// <summary>
    /// A folder's settings: its environment colour, and the username, PuTTY profile and port that sessions below it
    /// use when they have none of their own (Data.FolderSettings.WithDefaults). Opened from a folder's "Folder
    /// settings..." in SessionTreeview, which saves the result (Folders.xml, PrivateFolders.xml or the table). Built in
    /// code like dlgDatabaseSettings.
    /// </summary>
    public class dlgFolderSettings : Form
    {
        /// <summary>The colour choices: a name and "#RRGGBB" ("" for none)</summary>
        private readonly List<KeyValuePair<string, string>> colors;

        /// <summary>The editors</summary>
        private readonly ComboBox comboColor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox textUsername = new TextBox();
        private readonly ComboBox comboProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly NumericUpDown numericPort = new NumericUpDown { Minimum = 0, Maximum = 65535 };

        /// <summary>The settings as edited, read after OK</summary>
        public FolderSetting Setting { get; private set; }

        /// <summary>Build the dialog for a folder</summary>
        /// <param name="folderPath">The folder's path, e.g. "HQ/Core", for the title</param>
        /// <param name="current">Its settings now; null if it has none</param>
        public dlgFolderSettings(string folderPath, FolderSetting current)
        {
            this.Setting = current?.Clone() ?? new FolderSetting { Path = folderPath };
            this.Text = "Folder settings: " + folderPath;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));

            this.colors = EnvironmentColors.Presets.ToList();
            string currentColor = EnvironmentColors.Normalize(this.Setting.Color);
            if (currentColor != null && !this.colors.Any(c => c.Value == currentColor))
                this.colors.Add(new KeyValuePair<string, string>("Custom (" + currentColor + ")", currentColor));
            this.colors.Add(new KeyValuePair<string, string>("None (use a parent's or a rule's)", ""));
            this.comboColor.Items.AddRange(this.colors.Select(c => c.Key).ToArray());
            this.comboColor.SelectedIndex = Math.Max(0, this.colors.FindIndex(c => c.Value == (currentColor ?? "")));
            this.comboProfile.Items.Add(string.Empty);
            this.comboProfile.Items.AddRange(PuttyDataHelper.GetSessionNames().Where(n => !string.IsNullOrEmpty(n)).ToArray());
            this.textUsername.Text = this.Setting.Username ?? string.Empty;
            this.comboProfile.Text = this.Setting.PuttyProfile ?? string.Empty;
            this.numericPort.Value = Math.Max(0, Math.Min(65535, this.Setting.Port));
            HintText.Set(this.textUsername, "e.g. netops, or {user}");
            foreach (Control box in new Control[] { this.comboColor, this.textUsername, this.comboProfile })
            {
                box.Width = this.LogicalToDeviceUnits(300);
            }
            this.numericPort.Width = this.LogicalToDeviceUnits(90);

            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            Label intro = new Label
            {
                Text = "Sessions in this folder and its subfolders use these when they have none of their own (an empty username, port 0, "
                    + "or no PuTTY profile / Default Settings). A nearer folder's value wins.",
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(420), 0),
                Margin = new Padding(0, 0, 0, this.LogicalToDeviceUnits(8))
            };
            grid.Controls.Add(intro, 0, 0);
            grid.SetColumnSpan(intro, 2);
            AddRow(grid, 1, "Colour:", this.comboColor);
            AddRow(grid, 2, "Username:", this.textUsername);
            AddRow(grid, 3, "PuTTY profile:", this.comboProfile);
            AddRow(grid, 4, "Port (0 = none):", this.numericPort);

            FlowLayoutPanel close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            ok.Click += (s, e) => this.ReadBack();
            close.Controls.AddRange(new Control[] { cancel, ok });
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.Add(grid);
            this.Controls.Add(close);

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>Add a label and an editor as a row</summary>
        /// <param name="grid">The layout</param>
        /// <param name="row">The row</param>
        /// <param name="label">The label</param>
        /// <param name="editor">The editor</param>
        private static void AddRow(TableLayoutPanel grid, int row, string label, Control editor)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
            grid.Controls.Add(editor, 1, row);
        }

        /// <summary>Put the edited values in <see cref="Setting"/></summary>
        private void ReadBack()
        {
            this.Setting.Color = this.colors[this.comboColor.SelectedIndex].Value;
            this.Setting.Username = this.textUsername.Text.Trim();
            this.Setting.PuttyProfile = this.comboProfile.Text.Trim();
            this.Setting.Port = (int)this.numericPort.Value;
        }
    }
}
