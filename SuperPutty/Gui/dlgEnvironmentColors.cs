using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DarkModeForms;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Tools -> Environment colours: the name rules that colour sessions by their folders' or their own names, e.g.
    /// "Prod*" red (EnvironmentColors.Rules, Settings.EnvironmentColorRules). Colours set on a folder or a session,
    /// from their menus in the Sessions tree, come before these. Opened by frmSuperPutty; built in code like
    /// dlgDatabaseSettings.
    /// </summary>
    public class dlgEnvironmentColors : Form
    {
        /// <summary>The rules: a name pattern and a colour per row</summary>
        private readonly DataGridView grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Dock = DockStyle.Fill
        };

        /// <summary>Build the dialog and fill it from the setting</summary>
        public dlgEnvironmentColors()
        {
            this.Text = "Environment colours";
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));
            this.ClientSize = new Size(this.LogicalToDeviceUnits(520), this.LogicalToDeviceUnits(380));
            this.MinimumSize = new Size(this.LogicalToDeviceUnits(420), this.LogicalToDeviceUnits(300));

            Label intro = new Label
            {
                Text = "Sessions get the colour of the first rule matching their own name or one of their folders' names (nearest first). "
                    + "* and ? are wildcards; case doesn't matter. A colour set on a folder or a session (right-click it in the Sessions tree) comes first.",
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(496), 0),
                Padding = new Padding(0, 0, 0, this.LogicalToDeviceUnits(8))
            };
            this.grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pattern", HeaderText = "Folder or session name", FillWeight = 70 });
            // as SuperXTerminal's window: a swatch to click for the colour picker, then the colour as #RRGGBB to type
            this.grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Swatch", HeaderText = "", ReadOnly = true, Resizable = DataGridViewTriState.False,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = this.LogicalToDeviceUnits(36), ToolTipText = "Choose the colour"
            });
            this.grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Color", HeaderText = "Colour", FillWeight = 30, ToolTipText = "The colour as #RRGGBB" });
            this.grid.CellFormatting += this.Grid_CellFormatting;
            // the empty part of the list in the window's colour rather than grey (the dark theme sets its own)
            this.grid.BackgroundColor = SystemColors.Window;
            this.grid.CellPainting += this.Grid_CellPainting;
            this.grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == SwatchColumn) this.PickColor(); };
            this.grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == ColorColumn) this.PickColor(); };
            this.grid.CellValueChanged += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == ColorColumn) this.grid.InvalidateCell(SwatchColumn, e.RowIndex); };

            FlowLayoutPanel side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(this.LogicalToDeviceUnits(8), 0, 0, 0) };
            Button add = this.SideButton("Add", (s, e) => this.AddRule());
            Button remove = this.SideButton("Remove", (s, e) => { if (this.grid.CurrentRow != null) this.grid.Rows.Remove(this.grid.CurrentRow); });
            Button up = this.SideButton("Move up", (s, e) => this.MoveRule(-1));
            Button down = this.SideButton("Move down", (s, e) => this.MoveRule(1));
            Button defaults = this.SideButton("Defaults", (s, e) => this.Fill(EnvironmentColors.ParseRules(EnvironmentColors.DefaultRules)));
            side.Controls.AddRange(new Control[] { add, remove, up, down, defaults, this.PaletteSwatches() });

            FlowLayoutPanel close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            ok.Click += (s, e) => this.Save();
            close.Controls.AddRange(new Control[] { cancel, ok });
            this.AcceptButton = ok;
            this.CancelButton = cancel;

            this.Controls.Add(this.grid);
            this.Controls.Add(side);
            this.Controls.Add(close);
            this.Controls.Add(intro);
            this.Fill(EnvironmentColors.Rules);

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>The grid's columns: the name, the swatch to click, and the colour as #RRGGBB</summary>
        private const int PatternColumn = 0, SwatchColumn = 1, ColorColumn = 2;

        /// <summary>The palette's tooltips</summary>
        private readonly ToolTip tips = new ToolTip();

        /// <summary>
        /// The usual colours under the buttons, as SuperXTerminal's window: clicking one sets the selected rule's colour.
        /// Images rather than coloured controls, which the dark theme would recolour.
        /// </summary>
        /// <returns>The palette, five to a row</returns>
        private Control PaletteSwatches()
        {
            int size = this.LogicalToDeviceUnits(18), gap = this.LogicalToDeviceUnits(2);
            FlowLayoutPanel palette = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, this.LogicalToDeviceUnits(6), 0, 0), MaximumSize = new Size(5 * (size + 2 * gap), 0) };
            foreach (KeyValuePair<string, string> preset in EnvironmentColors.Presets)
            {
                string hex = preset.Value;
                PictureBox swatch = new PictureBox { Size = new Size(size, size), Margin = new Padding(gap), Cursor = Cursors.Hand, Image = Swatch(EnvironmentColors.Parse(hex).Value, size) };
                this.tips.SetToolTip(swatch, preset.Key + " " + hex + ": the selected rule's colour");
                swatch.Click += (s, e) =>
                {
                    if (this.grid.CurrentRow != null)
                        this.grid.CurrentRow.Cells[ColorColumn].Value = hex;
                };
                palette.Controls.Add(swatch);
            }
            return palette;
        }

        /// <summary>A square of a colour with a grey edge</summary>
        /// <param name="color">The colour</param>
        /// <param name="size">Its size in pixels</param>
        /// <returns>A new bitmap, kept by its swatch for the dialog's life</returns>
        private static Image Swatch(Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (SolidBrush fill = new SolidBrush(color))
            {
                g.FillRectangle(fill, 0, 0, size, size);
                g.DrawRectangle(Pens.Gray, 0, 0, size - 1, size - 1);
            }
            return bitmap;
        }

        /// <summary>Draw the swatch column: a square of the row's colour, to click for the colour picker</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">The cell</param>
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != SwatchColumn || e.RowIndex < 0)
                return;
            e.PaintBackground(e.CellBounds, false);
            Color? color = EnvironmentColors.Parse(this.grid.Rows[e.RowIndex].Cells[ColorColumn].Value as string);
            int side = Math.Min(e.CellBounds.Width, e.CellBounds.Height) - this.LogicalToDeviceUnits(8);
            Rectangle square = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - side) / 2, e.CellBounds.Y + (e.CellBounds.Height - side) / 2, side, side);
            if (color.HasValue)
            {
                using (SolidBrush fill = new SolidBrush(color.Value))
                {
                    e.Graphics.FillRectangle(fill, square);
                }
            }
            e.Graphics.DrawRectangle(Pens.Gray, square);
            e.Handled = true;
        }

        /// <summary>Dispose of the palette's tooltips with the dialog</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.tips.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>A button for the side column</summary>
        /// <param name="text">Its text</param>
        /// <param name="click">What it does</param>
        /// <returns>The button</returns>
        private Button SideButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(96), 0), Margin = new Padding(0, 0, 0, this.LogicalToDeviceUnits(4)) };
            button.Click += click;
            return button;
        }

        /// <summary>Show rules in the grid</summary>
        /// <param name="rules">The rules</param>
        private void Fill(IEnumerable<EnvironmentColors.Rule> rules)
        {
            this.grid.Rows.Clear();
            foreach (EnvironmentColors.Rule rule in rules)
            {
                this.grid.Rows.Add(rule.Pattern, "", rule.Color);
            }
        }

        /// <summary>Show each Colour cell in its colour, with readable text</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">The cell</param>
        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex != ColorColumn || e.RowIndex < 0)
                return;
            Color? color = EnvironmentColors.Parse(e.Value as string);
            if (color != null)
            {
                e.CellStyle.BackColor = color.Value;
                e.CellStyle.SelectionBackColor = color.Value;
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = color.Value.GetBrightness() < 0.55f ? Color.White : Color.Black;
            }
        }

        /// <summary>Add a rule and start typing its pattern</summary>
        private void AddRule()
        {
            int row = this.grid.Rows.Add("", "", EnvironmentColors.Presets[0].Value);
            this.grid.CurrentCell = this.grid.Rows[row].Cells[0];
            this.grid.BeginEdit(true);
        }

        /// <summary>Choose the selected rule's colour</summary>
        private void PickColor()
        {
            DataGridViewRow row = this.grid.CurrentRow;
            if (row == null)
                return;
            this.grid.EndEdit();
            using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = EnvironmentColors.Parse(row.Cells[ColorColumn].Value as string) ?? Color.Red })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    row.Cells[ColorColumn].Value = EnvironmentColors.Format(dialog.Color);
            }
        }

        /// <summary>Move the selected rule up or down; the first matching rule wins, so order matters</summary>
        /// <param name="by">-1 up, 1 down</param>
        private void MoveRule(int by)
        {
            DataGridViewRow row = this.grid.CurrentRow;
            if (row == null)
                return;
            int to = row.Index + by;
            if (to < 0 || to >= this.grid.Rows.Count)
                return;
            this.grid.Rows.Remove(row);
            this.grid.Rows.Insert(to, row);
            this.grid.CurrentCell = row.Cells[0];
        }

        /// <summary>Save the rules, refusing a colour that isn't #RRGGBB</summary>
        private void Save()
        {
            this.grid.EndEdit();
            List<EnvironmentColors.Rule> rules = new List<EnvironmentColors.Rule>();
            foreach (DataGridViewRow row in this.grid.Rows)
            {
                string pattern = (row.Cells[PatternColumn].Value as string ?? string.Empty).Trim();
                string color = row.Cells[ColorColumn].Value as string;
                if (pattern.Length == 0)
                    continue;
                if (EnvironmentColors.Normalize(color) == null)
                {
                    Messenger.MessageBox("\"" + color + "\" isn't a colour. Use #RRGGBB, e.g. #C62828, or click its swatch to choose one.",
                        "Environment colours", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.grid.CurrentCell = row.Cells[ColorColumn];
                    return;
                }
                rules.Add(new EnvironmentColors.Rule { Pattern = pattern, Color = color });
            }
            SuperPuTTY.Settings.EnvironmentColorRules = EnvironmentColors.FormatRules(rules);
            SuperPuTTY.Settings.Save();
            EnvironmentColors.RaiseChanged();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
