using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Manage Button Bars, as SuperXTerminal's ManageBars.svelte (its 334a0a2, 05e8376): every bar on the left, the chosen
    /// bar's buttons on the right, for tidying up what the button bar's right-click menu does one button at a time.
    /// Ticked buttons can be moved or copied to another bar (the Move to and Copy to menus, or dragged onto a bar, Alt to
    /// copy), put in order (the arrows, or dragged), duplicated (beside themselves, as Label-1, -2...), edited and
    /// deleted; bars can be added, renamed, copied (as Name-1), put in order, merged into another, given tab icons and
    /// deleted. A bar imported under a name already taken arrives as "Name (2)", so a bar that looks like another's copy
    /// gets a Merge hint, a button the same as one before it is marked Duplicate (Remove Duplicates takes those off),
    /// and buttons sharing a label with another command are marked Same label. Nothing is written until Save, which
    /// hands the bars back to ButtonBarStrip to write ButtonBars.xml; Cancel asks first when there are changes. The
    /// changes themselves are in Utils.ButtonBarManage.
    /// </summary>
    public class dlgManageButtonBars : Form
    {
        /// <summary>The bars as changed so far</summary>
        private List<ButtonBar> draft;

        /// <summary>The bar whose buttons show on the right, by name</summary>
        private string chosen;

        /// <summary>Whether anything has changed, so Cancel can ask</summary>
        private bool changed;

        /// <summary>Set while the lists are being filled, so their events don't act on the filling</summary>
        private bool filling;

        private readonly ListView barsList = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, HeaderStyle = ColumnHeaderStyle.None, Dock = DockStyle.Fill, AllowDrop = true };
        private readonly ListView buttonsList = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, CheckBoxes = true, Dock = DockStyle.Fill, AllowDrop = true };
        private readonly ImageList buttonImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly Label buttonsTitle = new Label { AutoSize = true };
        private readonly Label noButtons = new Label { Text = "No buttons", AutoSize = true, ForeColor = SystemColors.GrayText, BackColor = Color.Transparent };
        private readonly Label hint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Visible = false };
        private readonly Label status = new Label { AutoSize = true };

        private Button barRename, barCopy, barMerge, barIcons, barDelete, barUp, barDown;
        private Button buttonEdit, buttonMove, buttonCopyTo, buttonDuplicate, buttonDelete, buttonRemoveDuplicates, buttonUp, buttonDown;

        /// <summary>The bars as saved, once the dialog closes with OK</summary>
        public List<ButtonBar> Bars { get; private set; }

        /// <summary>The bar chosen last, which the button bar then shows</summary>
        public string Shown { get; private set; }

        /// <summary>Open on the bars as ButtonBars.xml has them, the bar the button bar shows chosen</summary>
        /// <param name="bars">The bars; copied, so nothing changes them until Save</param>
        /// <param name="shown">The bar chosen first</param>
        public dlgManageButtonBars(IList<ButtonBar> bars, string shown)
        {
            this.draft = ButtonBarManage.CopyOf(bars);
            this.chosen = ButtonBarManage.Find(this.draft, shown) >= 0 ? this.draft[ButtonBarManage.Find(this.draft, shown)].Name : this.draft[0].Name;

            this.Text = "Manage Button Bars";
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));
            this.ClientSize = new Size(this.LogicalToDeviceUnits(900), this.LogicalToDeviceUnits(560));
            this.MinimumSize = new Size(this.LogicalToDeviceUnits(760), this.LogicalToDeviceUnits(420));

            this.barsList.Columns.Add("Bar");
            this.barsList.Columns.Add("Buttons", -2, HorizontalAlignment.Right);
            this.barsList.SizeChanged += (s, e) => this.SizeColumns();
            this.barsList.SelectedIndexChanged += (s, e) =>
            {
                if (!this.filling && this.barsList.SelectedItems.Count == 1)
                    this.Choose((string)this.barsList.SelectedItems[0].Tag);
            };
            this.buttonImages.ImageSize = new Size(this.LogicalToDeviceUnits(16), this.LogicalToDeviceUnits(16));
            this.buttonsList.SmallImageList = this.buttonImages;
            this.buttonsList.Columns.Add("Button");
            this.buttonsList.Columns.Add("Sends");
            this.buttonsList.Columns.Add("");
            this.buttonsList.SizeChanged += (s, e) => this.SizeColumns();
            this.buttonsList.ItemChecked += (s, e) => { if (!this.filling) this.UpdateButtons(); };
            this.buttonsList.SelectedIndexChanged += (s, e) => { if (!this.filling) this.UpdateButtons(); };
            this.buttonsList.DoubleClick += (s, e) => { if (this.buttonsList.SelectedIndices.Count == 1) this.EditButton(this.buttonsList.SelectedIndices[0]); };
            this.buttonsList.Controls.Add(this.noButtons);
            this.noButtons.Location = new Point(this.LogicalToDeviceUnits(8), this.LogicalToDeviceUnits(8));
            this.WireDragging();

            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = "Bars", AutoSize = true, Font = new Font(this.Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) }, 0, 0);
            this.buttonsTitle.Font = new Font(this.Font, FontStyle.Bold);
            this.buttonsTitle.Margin = new Padding(0, 0, 0, 4);
            table.Controls.Add(this.buttonsTitle, 1, 0);
            this.barsList.Margin = new Padding(0, 0, this.LogicalToDeviceUnits(8), 0);
            this.buttonsList.Margin = new Padding(0);
            table.Controls.Add(this.barsList, 0, 1);
            table.Controls.Add(this.buttonsList, 1, 1);
            table.Controls.Add(this.BarActions(), 0, 2);
            table.Controls.Add(this.ButtonActions(), 1, 2);
            table.Controls.Add(this.hint, 0, 3);
            table.SetColumnSpan(this.hint, 2);
            Label help = new Label
            {
                Text = "Tick buttons to move, copy or delete them. Drag buttons onto a bar to move them there (hold Alt to copy), or drag them and bars to put them in order. Double-click a button to edit it.",
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(860), 0),
                Margin = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0)
            };
            table.Controls.Add(help, 0, 4);
            table.SetColumnSpan(help, 2);

            FlowLayoutPanel foot = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            save.Click += (s, e) => this.SaveAndClose();
            Button cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            cancel.Click += (s, e) => this.Close();
            this.status.Margin = new Padding(0, this.LogicalToDeviceUnits(6), this.LogicalToDeviceUnits(12), 0);
            foot.Controls.AddRange(new Control[] { save, cancel, this.status });
            table.Controls.Add(foot, 0, 5);
            table.SetColumnSpan(foot, 2);
            this.Controls.Add(table);
            this.AcceptButton = save;
            this.CancelButton = cancel;

            this.FillBars();
            this.FillButtons();

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>A button for the rows under the lists</summary>
        /// <param name="text">Its text</param>
        /// <param name="click">What it does</param>
        /// <returns>The button</returns>
        private Button Action(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, this.LogicalToDeviceUnits(6), this.LogicalToDeviceUnits(4), 0) };
            button.Click += click;
            return button;
        }

        /// <summary>The bar actions under the list of bars</summary>
        /// <returns>The row of buttons</returns>
        private Control BarActions()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.Controls.Add(this.Action("New...", (s, e) => this.NewBar()));
            row.Controls.Add(this.barRename = this.Action("Rename...", (s, e) => this.RenameBar()));
            row.Controls.Add(this.barCopy = this.Action("Copy", (s, e) => this.CopyBar()));
            row.Controls.Add(this.barMerge = this.Action("Merge into ▾", (s, e) => this.BarMenu((Control)s, into => this.MergeBar(this.chosen, into))));
            row.Controls.Add(this.barIcons = this.Action("Tab Icons...", (s, e) => this.TabIcons((Control)s)));
            row.Controls.Add(this.barDelete = this.Action("Delete", (s, e) => this.DeleteBar()));
            row.Controls.Add(this.barUp = this.Action("▲", (s, e) => this.MoveBar(-1)));
            row.Controls.Add(this.barDown = this.Action("▼", (s, e) => this.MoveBar(1)));
            return row;
        }

        /// <summary>The button actions under the list of buttons</summary>
        /// <returns>The row of buttons</returns>
        private Control ButtonActions()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.Controls.Add(this.Action("New...", (s, e) => this.EditButton(-1)));
            row.Controls.Add(this.buttonEdit = this.Action("Edit...", (s, e) => this.EditButton(this.Picked().First())));
            row.Controls.Add(this.buttonMove = this.Action("Move to ▾", (s, e) => this.BarMenu((Control)s, to => this.Send(to, false, this.Picked()))));
            row.Controls.Add(this.buttonCopyTo = this.Action("Copy to ▾", (s, e) => this.BarMenu((Control)s, to => this.Send(to, true, this.Picked()))));
            row.Controls.Add(this.buttonDuplicate = this.Action("Duplicate", (s, e) => this.DuplicateButtons()));
            row.Controls.Add(this.buttonDelete = this.Action("Delete", (s, e) => this.DeleteButtons()));
            row.Controls.Add(this.buttonRemoveDuplicates = this.Action("Remove Duplicates", (s, e) => this.RemoveDuplicates()));
            row.Controls.Add(this.buttonUp = this.Action("▲", (s, e) => this.MoveButton(-1)));
            row.Controls.Add(this.buttonDown = this.Action("▼", (s, e) => this.MoveButton(1)));
            return row;
        }

        /// <summary>The chosen bar</summary>
        private ButtonBar Bar
        {
            get { return this.draft[Math.Max(0, ButtonBarManage.Find(this.draft, this.chosen))]; }
        }

        /// <summary>The buttons acted on: the ticked ones, else the one selected</summary>
        /// <returns>Their indexes on the chosen bar, in order</returns>
        private List<int> Picked()
        {
            List<int> ticked = this.buttonsList.CheckedIndices.Cast<int>().OrderBy(i => i).ToList();
            if (ticked.Count > 0)
                return ticked;
            return this.buttonsList.SelectedIndices.Cast<int>().ToList();
        }

        /// <summary>"1 button" or "3 buttons"</summary>
        /// <param name="n">How many</param>
        /// <returns>The words</returns>
        private static string Count(int n)
        {
            return n + (n == 1 ? " button" : " buttons");
        }

        /// <summary>Fill the list of bars, the chosen one selected, and the Merge hint for bars that look like copies</summary>
        private void FillBars()
        {
            this.filling = true;
            try
            {
                this.barsList.BeginUpdate();
                this.barsList.Items.Clear();
                foreach (ButtonBar bar in this.draft)
                {
                    ListViewItem item = new ListViewItem(new[] { bar.Name, bar.Buttons.Count.ToString() }) { Tag = bar.Name };
                    this.barsList.Items.Add(item);
                    if (string.Equals(bar.Name, this.chosen, StringComparison.OrdinalIgnoreCase))
                        item.Selected = true;
                }
                this.barsList.EndUpdate();
                this.SizeColumns();
            }
            finally
            {
                this.filling = false;
            }
            List<KeyValuePair<string, string>> copies = ButtonBarManage.Copies(this.draft);
            this.hint.Text = string.Join("   ", copies.Select(c => "\"" + c.Key + "\" looks like a copy of \"" + c.Value + "\": choose it and Merge into \"" + c.Value + "\"."));
            this.hint.Visible = copies.Count > 0;
        }

        /// <summary>Fill the list of the chosen bar's buttons, marking duplicates and labels used for another command</summary>
        /// <param name="tick">The buttons to tick afterwards, by index; none when null</param>
        private void FillButtons(IEnumerable<int> tick = null)
        {
            ButtonBar bar = this.Bar;
            HashSet<int> repeated = ButtonBarManage.Duplicates(bar);
            HashSet<int> clashing = ButtonBarManage.Clashes(bar);
            HashSet<int> ticked = new HashSet<int>(tick ?? Enumerable.Empty<int>());
            this.filling = true;
            try
            {
                this.buttonsList.BeginUpdate();
                this.buttonsList.Items.Clear();
                foreach (Image image in this.buttonImages.Images)
                    image.Dispose();
                this.buttonImages.Images.Clear();
                for (int i = 0; i < bar.Buttons.Count; i++)
                {
                    CommandButton button = bar.Buttons[i];
                    string sends = (button.Action == CommandButtonAction.SpslScript ? "Script: " : "") + FirstLine(button.Text);
                    string note = repeated.Contains(i) ? "Duplicate" : clashing.Contains(i) ? "Same label" : "";
                    ListViewItem item = new ListViewItem(new[] { button.Label, sends, note }) { Checked = ticked.Contains(i) };
                    Image picture = this.PictureOf(button);
                    if (picture != null)
                    {
                        this.buttonImages.Images.Add(picture);
                        item.ImageIndex = this.buttonImages.Images.Count - 1;
                    }
                    this.buttonsList.Items.Add(item);
                }
                this.buttonsList.EndUpdate();
                this.SizeColumns();
            }
            finally
            {
                this.filling = false;
            }
            this.buttonsTitle.Text = "Buttons on \"" + bar.Name + "\"";
            this.noButtons.Visible = bar.Buttons.Count == 0;
            this.UpdateButtons();
        }

        /// <summary>A button's first line of text, for the list</summary>
        /// <param name="text">The text</param>
        /// <returns>Its first line that isn't blank, with "..." when there's more</returns>
        private static string FirstLine(string text)
        {
            string[] lines = (text ?? string.Empty).Split('\n').Select(l => l.Trim('\r')).Where(l => l.Trim().Length > 0).ToArray();
            return lines.Length == 0 ? "" : lines[0] + (lines.Length > 1 ? " ..." : "");
        }

        /// <summary>The picture a button shows on the bar: its icon, else its coloured circle; null for neither</summary>
        /// <param name="button">The button</param>
        /// <returns>A new image the list's size</returns>
        private Image PictureOf(CommandButton button)
        {
            Size size = this.buttonImages.ImageSize;
            if (!string.IsNullOrEmpty(button.ImageKey) && SuperPuTTY.Images != null && IconLibrary.Ensure(SuperPuTTY.Images, button.ImageKey))
                return new Bitmap(SuperPuTTY.Images.Images[button.ImageKey], size);
            Color color;
            if (ButtonBarStrip.TryParseColor(button.Color, out color))
            {
                Bitmap canvas = new Bitmap(size.Width, size.Height);
                using (Bitmap circle = ButtonBarStrip.CreateColorCircle(color, size.Width * 3 / 4))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.DrawImage(circle, (size.Width - circle.Width) / 2, (size.Height - circle.Height) / 2);
                }
                return canvas;
            }
            return null;
        }

        /// <summary>Size the lists' columns to their width: the name or label taking the room</summary>
        private void SizeColumns()
        {
            if (this.barsList.Columns.Count == 2)
            {
                int count = this.LogicalToDeviceUnits(44);
                this.barsList.Columns[1].Width = count;
                this.barsList.Columns[0].Width = Math.Max(40, this.barsList.ClientSize.Width - count - 2);
            }
            if (this.buttonsList.Columns.Count == 3)
            {
                int note = this.LogicalToDeviceUnits(86);
                int rest = Math.Max(80, this.buttonsList.ClientSize.Width - note - 2);
                this.buttonsList.Columns[0].Width = rest * 2 / 5;
                this.buttonsList.Columns[1].Width = rest - rest * 2 / 5;
                this.buttonsList.Columns[2].Width = note;
            }
        }

        /// <summary>Enable the actions for what's chosen and ticked</summary>
        private void UpdateButtons()
        {
            List<int> picked = this.Picked();
            bool others = this.draft.Count > 1;
            int at = ButtonBarManage.Find(this.draft, this.chosen);
            this.barRename.Enabled = this.barCopy.Enabled = this.barIcons.Enabled = true;
            this.barMerge.Enabled = others;
            this.barDelete.Enabled = others;
            this.barUp.Enabled = at > 0;
            this.barDown.Enabled = at < this.draft.Count - 1;
            this.buttonEdit.Enabled = picked.Count == 1;
            this.buttonMove.Enabled = this.buttonCopyTo.Enabled = picked.Count > 0 && others;
            this.buttonDuplicate.Enabled = this.buttonDelete.Enabled = picked.Count > 0;
            this.buttonRemoveDuplicates.Enabled = ButtonBarManage.Duplicates(this.Bar).Count > 0;
            this.buttonUp.Enabled = picked.Count == 1 && picked[0] > 0;
            this.buttonDown.Enabled = picked.Count == 1 && picked[0] < this.Bar.Buttons.Count - 1;
        }

        /// <summary>Put changed bars in place, say what was done, and keep the chosen bar if it's still there</summary>
        /// <param name="next">The bars after</param>
        /// <param name="message">What was done; null leaves the last message</param>
        /// <param name="choose">The bar to choose; null keeps the chosen one</param>
        /// <param name="tick">The buttons to tick afterwards</param>
        private void Apply(List<ButtonBar> next, string message, string choose = null, IEnumerable<int> tick = null)
        {
            this.draft = next;
            string wanted = choose ?? this.chosen;
            int at = ButtonBarManage.Find(next, wanted);
            this.chosen = at >= 0 ? next[at].Name : next[0].Name;
            if (message != null)
                this.status.Text = message;
            this.changed = true;
            this.FillBars();
            this.FillButtons(tick);
        }

        /// <summary>Choose a bar, which clears the ticks</summary>
        /// <param name="name">Its name</param>
        private void Choose(string name)
        {
            if (string.Equals(name, this.chosen, StringComparison.Ordinal))
                return;
            this.chosen = name;
            this.FillButtons();
        }

        /// <summary>A menu of the other bars under a control, running an action with the one picked</summary>
        /// <param name="under">The control clicked</param>
        /// <param name="run">What to do with the bar picked</param>
        private void BarMenu(Control under, Action<string> run)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            foreach (ButtonBar bar in this.draft.Where(b => !string.Equals(b.Name, this.chosen, StringComparison.OrdinalIgnoreCase)))
            {
                string name = bar.Name;
                menu.Items.Add(new ToolStripMenuItem(name + "  (" + Count(bar.Buttons.Count) + ")", null, (s, e) => run(name)));
            }
            menu.Closed += (s, e) => this.BeginInvoke(new MethodInvoker(menu.Dispose));
            menu.Show(under, new Point(0, under.Height));
        }

        /// <summary>Move or copy buttons to another bar</summary>
        /// <param name="to">The bar they go to</param>
        /// <param name="copy">true to copy</param>
        /// <param name="indexes">The buttons, by index on the chosen bar</param>
        private void Send(string to, bool copy, List<int> indexes)
        {
            if (indexes.Count == 0)
                return;
            int skipped;
            List<ButtonBar> next = ButtonBarManage.Transfer(this.draft, this.chosen, indexes, to, copy, out skipped);
            string left = skipped > 0 ? "; " + Count(skipped) + " already there" + (copy ? "" : ", taken off here") : "";
            this.Apply(next, (copy ? "Copied " : "Moved ") + Count(indexes.Count - skipped) + " to \"" + to + "\"" + left);
        }

        /// <summary>Duplicate the picked buttons, each copy beside its button as "Label-1", and tick the copies</summary>
        private void DuplicateButtons()
        {
            List<int> picked = this.Picked();
            List<int> copies;
            List<ButtonBar> next = ButtonBarManage.DuplicateButtons(this.draft, this.chosen, picked, out copies);
            this.Apply(next, "Duplicated " + Count(picked.Count), null, copies);
        }

        /// <summary>Delete the picked buttons (they're only gone from the file at Save)</summary>
        private void DeleteButtons()
        {
            HashSet<int> gone = new HashSet<int>(this.Picked());
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            ButtonBar bar = next[ButtonBarManage.Find(next, this.chosen)];
            bar.Buttons = bar.Buttons.Where((b, i) => !gone.Contains(i)).ToList();
            this.Apply(next, "Deleted " + Count(gone.Count));
        }

        /// <summary>Take off the buttons the same as one before them</summary>
        private void RemoveDuplicates()
        {
            HashSet<int> gone = ButtonBarManage.Duplicates(this.Bar);
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            ButtonBar bar = next[ButtonBarManage.Find(next, this.chosen)];
            bar.Buttons = bar.Buttons.Where((b, i) => !gone.Contains(i)).ToList();
            this.Apply(next, "Removed " + Count(gone.Count) + " the same as another");
        }

        /// <summary>Move the one picked button up or down</summary>
        /// <param name="by">-1 up, 1 down</param>
        private void MoveButton(int by)
        {
            List<int> picked = this.Picked();
            if (picked.Count != 1)
                return;
            this.MoveButtonTo(picked[0], picked[0] + by);
        }

        /// <summary>Put a button somewhere else on its bar, and tick it there</summary>
        /// <param name="from">Where it is</param>
        /// <param name="to">Where it goes</param>
        private void MoveButtonTo(int from, int to)
        {
            if (to < 0 || to >= this.Bar.Buttons.Count || from == to)
                return;
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            ButtonBar bar = next[ButtonBarManage.Find(next, this.chosen)];
            bar.Buttons = ButtonBarManage.Reorder(bar.Buttons, from, to);
            this.Apply(next, null, null, new[] { to });
            this.buttonsList.Items[to].Selected = true;
            this.buttonsList.EnsureVisible(to);
        }

        /// <summary>Move the chosen bar up or down the list (the button bar's dropdown follows this order)</summary>
        /// <param name="by">-1 up, 1 down</param>
        private void MoveBar(int by)
        {
            int at = ButtonBarManage.Find(this.draft, this.chosen);
            this.Apply(ButtonBarManage.Reorder(this.draft, at, at + by), null);
        }

        /// <summary>Add a button to the chosen bar, or edit one, with the button editor</summary>
        /// <param name="index">The button's index; -1 for a new one</param>
        private void EditButton(int index)
        {
            using (dlgEditCommandButton dialog = new dlgEditCommandButton(index >= 0 ? this.Bar.Buttons[index] : null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
                ButtonBar bar = next[ButtonBarManage.Find(next, this.chosen)];
                if (index >= 0)
                    bar.Buttons[index] = dialog.EditedButton;
                else
                    bar.Buttons.Add(dialog.EditedButton);
                this.Apply(next, (index >= 0 ? "Changed \"" : "Added \"") + dialog.EditedButton.Label + "\"");
            }
        }

        /// <summary>Ask for a bar's name, refusing an empty one or one taken</summary>
        /// <param name="title">The dialog's title</param>
        /// <param name="current">The name to start from</param>
        /// <param name="except">A name that may be kept (the bar's own when renaming); null for none</param>
        /// <returns>The name, trimmed; null when cancelled</returns>
        private string AskName(string title, string current, string except)
        {
            dlgRenameItem.ItemNameValidationHandler validator = delegate (string name, out string error)
            {
                error = string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    error = "Give the bar a name";
                else if (!string.Equals(name.Trim(), except, StringComparison.OrdinalIgnoreCase) && ButtonBarManage.Find(this.draft, name.Trim()) >= 0)
                    error = "There's already a bar with that name";
                return error.Length == 0;
            };
            using (dlgRenameItem dialog = new dlgRenameItem { Text = title, ItemName = current, DetailName = "Button bar name", ItemNameValidator = validator })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return null;
                string message;
                if (!validator(dialog.ItemName, out message))
                {
                    Messenger.MessageBox(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return dialog.ItemName.Trim();
            }
        }

        /// <summary>Add an empty bar and choose it</summary>
        private void NewBar()
        {
            string name = this.AskName("New Button Bar", string.Empty, null);
            if (name == null)
                return;
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            next.Add(new ButtonBar { Name = name });
            this.Apply(next, "Added the bar \"" + name + "\"", name);
        }

        /// <summary>Give the chosen bar a new name</summary>
        private void RenameBar()
        {
            string old = this.chosen;
            string name = this.AskName("Rename Button Bar", old, old);
            if (name == null || name == old)
                return;
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            next[ButtonBarManage.Find(next, old)].Name = name;
            this.Apply(next, "Renamed \"" + old + "\" to \"" + name + "\"", name);
        }

        /// <summary>Copy the chosen bar as "Name-1", and choose the copy</summary>
        private void CopyBar()
        {
            List<ButtonBar> next = ButtonBarManage.DuplicateBar(this.draft, this.chosen);
            string copy = next[ButtonBarManage.Find(next, this.chosen) + 1].Name;
            this.Apply(next, "Copied \"" + this.chosen + "\" as \"" + copy + "\"", copy);
        }

        /// <summary>Merge one bar into another, and choose the one merged into</summary>
        /// <param name="from">The bar merged</param>
        /// <param name="into">The bar it goes into</param>
        private void MergeBar(string from, string into)
        {
            int added, skipped, clashing;
            List<ButtonBar> next = ButtonBarManage.Merge(this.draft, from, into, out added, out skipped, out clashing);
            string message = "Merged \"" + from + "\" into \"" + into + "\": " + Count(added) + " added";
            if (skipped > 0)
                message += ", " + skipped + " already there";
            if (clashing > 0)
                message += ", " + clashing + " with a label already there and another command, marked";
            this.Apply(next, message, into);
        }

        /// <summary>Delete the chosen bar (it's only gone from the file at Save)</summary>
        private void DeleteBar()
        {
            if (this.draft.Count < 2)
                return;
            string name = this.chosen;
            int buttons = this.Bar.Buttons.Count;
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            next.RemoveAt(ButtonBarManage.Find(next, name));
            this.Apply(next, "Deleted the bar \"" + name + "\" and its " + Count(buttons));
        }

        /// <summary>
        /// The chosen bar's tab icons: a checklist of every session icon under the button, ticked for the icons the bar
        /// is shown for, as the button bar's Show for Tab Icons; it stays open while icons are ticked
        /// </summary>
        /// <param name="under">The Tab Icons button</param>
        private void TabIcons(Control under)
        {
            if (SuperPuTTY.Images == null)
                return;
            List<ButtonBar> next = ButtonBarManage.CopyOf(this.draft);
            ButtonBar bar = next[ButtonBarManage.Find(next, this.chosen)];
            ContextMenuStrip menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = true };
            menu.Items.Add(new ToolStripLabel("Show \"" + bar.Name + "\" for tabs with these icons:") { ForeColor = SystemColors.GrayText });
            bool touched = false;
            foreach (string key in SuperPuTTY.Images.Images.Keys)
            {
                string icon = key;
                ToolStripMenuItem item = new ToolStripMenuItem(icon, SuperPuTTY.Images.Images[icon])
                {
                    Checked = bar.TabIcons.Any(i => string.Equals(i, icon, StringComparison.OrdinalIgnoreCase))
                };
                item.Click += delegate
                {
                    item.Checked = !item.Checked;
                    bar.TabIcons.RemoveAll(i => string.Equals(i, icon, StringComparison.OrdinalIgnoreCase));
                    if (item.Checked)
                        bar.TabIcons.Add(icon);
                    touched = true;
                };
                menu.Items.Add(item);
            }
            menu.Closing += (s, e) => e.Cancel = e.CloseReason == ToolStripDropDownCloseReason.ItemClicked;
            menu.Closed += (s, e) => this.BeginInvoke(new MethodInvoker(() =>
            {
                menu.Dispose();
                if (touched)
                    this.Apply(next, "\"" + bar.Name + "\" shows for " + (bar.TabIcons.Count == 0 ? "no tab icons" : string.Join(", ", bar.TabIcons)));
            }));
            menu.Show(under, new Point(0, under.Height));
        }

        /// <summary>What's being dragged: buttons (to put in order, or onto a bar) or a bar (to put in order)</summary>
        private class Dragged
        {
            /// <summary>The buttons, by index on the chosen bar; null when a bar is dragged</summary>
            public List<int> Buttons;

            /// <summary>The bar's index; -1 when buttons are dragged</summary>
            public int Bar = -1;
        }

        /// <summary>
        /// Drag and drop: buttons onto a bar move there (Alt copies), buttons and bars dropped within their own list go to
        /// where they're dropped
        /// </summary>
        private void WireDragging()
        {
            this.buttonsList.ItemDrag += (s, e) =>
            {
                int index = ((ListViewItem)e.Item).Index;
                List<int> ticked = this.buttonsList.CheckedIndices.Cast<int>().OrderBy(i => i).ToList();
                this.buttonsList.DoDragDrop(new Dragged { Buttons = ticked.Contains(index) ? ticked : new List<int> { index } }, DragDropEffects.Move | DragDropEffects.Copy);
            };
            this.barsList.ItemDrag += (s, e) => this.barsList.DoDragDrop(new Dragged { Bar = ((ListViewItem)e.Item).Index }, DragDropEffects.Move);

            this.barsList.DragOver += (s, e) =>
            {
                Dragged drag = e.Data.GetData(typeof(Dragged)) as Dragged;
                ListViewItem over = this.barsList.GetItemAt(this.barsList.PointToClient(new Point(e.X, e.Y)).X, this.barsList.PointToClient(new Point(e.X, e.Y)).Y);
                if (drag == null || over == null || (drag.Buttons != null && string.Equals((string)over.Tag, this.chosen, StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }
                // Alt held (KeyState 32) copies buttons rather than moving them
                e.Effect = drag.Buttons != null && (e.KeyState & 32) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
            };
            this.barsList.DragDrop += (s, e) =>
            {
                Dragged drag = e.Data.GetData(typeof(Dragged)) as Dragged;
                Point at = this.barsList.PointToClient(new Point(e.X, e.Y));
                ListViewItem over = this.barsList.GetItemAt(at.X, at.Y);
                if (drag == null || over == null)
                    return;
                if (drag.Buttons != null)
                    this.Send((string)over.Tag, (e.KeyState & 32) != 0, drag.Buttons);
                else if (drag.Bar != over.Index)
                    this.Apply(ButtonBarManage.Reorder(this.draft, drag.Bar, over.Index), null);
            };

            this.buttonsList.DragOver += (s, e) =>
            {
                Dragged drag = e.Data.GetData(typeof(Dragged)) as Dragged;
                e.Effect = drag != null && drag.Buttons != null && drag.Buttons.Count == 1 ? DragDropEffects.Move : DragDropEffects.None;
            };
            this.buttonsList.DragDrop += (s, e) =>
            {
                Dragged drag = e.Data.GetData(typeof(Dragged)) as Dragged;
                Point at = this.buttonsList.PointToClient(new Point(e.X, e.Y));
                ListViewItem over = this.buttonsList.GetItemAt(at.X, at.Y);
                if (drag != null && drag.Buttons != null && drag.Buttons.Count == 1)
                    this.MoveButtonTo(drag.Buttons[0], over != null ? over.Index : this.Bar.Buttons.Count - 1);
            };
        }

        /// <summary>Hand the bars back and close</summary>
        private void SaveAndClose()
        {
            this.Bars = this.draft;
            this.Shown = this.chosen;
            this.changed = false;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>Cancel or Escape: ask first when there are changes, which would be lost</summary>
        /// <param name="e">Whether to stop closing</param>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK && this.changed
                && Messenger.MessageBox("Leave without saving your changes to the button bars?", "Manage Button Bars", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        /// <summary>Dispose of the buttons' pictures with the dialog</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.buttonImages.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
