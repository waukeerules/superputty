using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using SuperPutty.Data;
using SuperPutty.Utils;
using SuperPuTTY.Scripting;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The button bar: a toolbar of user-defined buttons that send a string or run an SPSL script in
    /// terminal sessions, modelled on SecureCRT's Button Bar.
    /// <para>
    /// A click sends to the active tab; Shift+click sends to every tab selected for the Send Commands
    /// toolbar. The tabs come from the callback given to the constructor (frmSuperPutty.GetButtonBarTargets).
    /// A dropdown switches between named bars; right-click adds, edits, deletes and reorders buttons and bars.
    /// Bars are stored by <see cref="ButtonBarStore"/>; the visible bar is remembered per user in
    /// Settings.ActiveButtonBar.
    /// </para>
    /// </summary>
    public class ButtonBarStrip : ToolStrip
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ButtonBarStrip));

        /// <summary>WM_MOUSEACTIVATE result: activate the window and pass the click on to the control</summary>
        private const int MA_ACTIVATE = 1;

        /// <summary>WM_MOUSEACTIVATE result: activate the window and discard the click</summary>
        private const int MA_ACTIVATEANDEAT = 2;

        /// <summary>Size of the colored circle drawn on a button, before DPI scaling</summary>
        private const int CircleSize = 14;

        /// <summary>Circle bitmaps made for the current buttons, disposed when the buttons are rebuilt</summary>
        private readonly List<Image> circleImages = new List<Image>();

        /// <summary>Returns the tabs to send to: the active tab (false) or the tabs selected for Send Commands (true)</summary>
        private readonly Func<bool, IList<ctlPuttyPanel>> getTargets;

        /// <summary>Dropdown for switching between bars</summary>
        private readonly ToolStripComboBox comboBars;

        /// <summary>Separates the dropdown from the buttons; every item after it is rebuilt by <see cref="RefreshButtons"/></summary>
        private readonly ToolStripSeparator separator;

        private readonly ToolStripMenuItem menuNewButton;
        private readonly ToolStripMenuItem menuEditButton;
        private readonly ToolStripMenuItem menuDeleteButton;
        private readonly ToolStripMenuItem menuMoveLeft;
        private readonly ToolStripMenuItem menuMoveRight;
        private readonly ToolStripMenuItem menuRenameBar;
        private readonly ToolStripMenuItem menuDeleteBar;

        /// <summary>All bars, as last loaded from or saved to ButtonBars.xml</summary>
        private ButtonBarList bars;

        /// <summary>Index in the current bar of the button that was right-clicked, or -1 if the click wasn't on a button</summary>
        private int contextButtonIndex = -1;

        /// <summary>True while the dropdown is being filled, so its change event doesn't switch bars</summary>
        private bool updatingCombo;

        /// <summary>Create the button bar and load the saved bars</summary>
        /// <param name="getTargets">Returns the tabs to send to: the active tab (false) or the tabs selected for Send Commands (true)</param>
        public ButtonBarStrip(Func<bool, IList<ctlPuttyPanel>> getTargets)
        {
            this.getTargets = getTargets;
            this.Name = "tsButtonBar";
            this.Dock = DockStyle.Bottom;
            this.GripStyle = ToolStripGripStyle.Hidden;
            this.ShowItemToolTips = true;

            this.comboBars = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = DpiUtils.ScaleWidth(140),
                ToolTipText = "Button bar"
            };
            this.comboBars.SelectedIndexChanged += this.comboBars_SelectedIndexChanged;
            this.separator = new ToolStripSeparator();
            this.Items.Add(this.comboBars);
            this.Items.Add(this.separator);

            this.menuNewButton = new ToolStripMenuItem("&New Button...", null, (s, e) => this.NewButton());
            this.menuEditButton = new ToolStripMenuItem("&Edit Button...", null, (s, e) => this.EditButton(this.contextButtonIndex));
            this.menuDeleteButton = new ToolStripMenuItem("&Delete Button", null, (s, e) => this.DeleteButton(this.contextButtonIndex));
            this.menuMoveLeft = new ToolStripMenuItem("Move &Left", null, (s, e) => this.MoveButton(this.contextButtonIndex, -1));
            this.menuMoveRight = new ToolStripMenuItem("Move &Right", null, (s, e) => this.MoveButton(this.contextButtonIndex, 1));
            this.menuRenameBar = new ToolStripMenuItem("Rena&me Button Bar...", null, (s, e) => this.RenameBar());
            this.menuDeleteBar = new ToolStripMenuItem("Delete Button &Bar", null, (s, e) => this.DeleteBar());
            this.ContextMenuStrip = new ContextMenuStrip();
            this.ContextMenuStrip.Items.AddRange(new ToolStripItem[] {
                this.menuNewButton,
                this.menuEditButton,
                this.menuDeleteButton,
                this.menuMoveLeft,
                this.menuMoveRight,
                new ToolStripSeparator(),
                new ToolStripMenuItem("New Button Bar...", null, (s, e) => this.NewBar()),
                this.menuRenameBar,
                this.menuDeleteBar,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Reload", null, (s, e) => this.Reload())
            });
            this.ContextMenuStrip.Opening += this.ContextMenuStrip_Opening;

            this.Reload();
        }

        /// <summary>
        /// Let the first click press a button even when SuperPuTTY isn't the active window, which is the
        /// usual case: while typing in a terminal, the embedded PuTTY window has the focus. A ToolStrip
        /// normally answers WM_MOUSEACTIVATE with MA_ACTIVATEANDEAT, so that click only activated the form.
        /// </summary>
        /// <param name="m">The window message</param>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == (int)NativeMethods.WM.MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT)
            {
                m.Result = (IntPtr)MA_ACTIVATE;
            }
        }

        /// <summary>
        /// The picture shown on a button: its session icon if it has one, otherwise a circle in its color,
        /// otherwise none. Circles are created here, and the caller must dispose them.
        /// </summary>
        /// <param name="command">The button</param>
        /// <param name="isCircle">Set to true if the returned image is a new circle bitmap</param>
        /// <returns>The image, or null for a text-only button</returns>
        internal static Image GetButtonImage(CommandButton command, out bool isCircle)
        {
            isCircle = false;
            if (!string.IsNullOrEmpty(command.ImageKey) && SuperPuTTY.Images != null && SuperPuTTY.Images.Images.ContainsKey(command.ImageKey))
            {
                return SuperPuTTY.Images.Images[command.ImageKey];
            }
            Color color;
            if (TryParseColor(command.Color, out color))
            {
                isCircle = true;
                return CreateColorCircle(color, DpiUtils.ScaleWidth(CircleSize));
            }
            return null;
        }

        /// <summary>Parse a color stored in ButtonBars.xml, e.g. "#3FB950" or "Red"</summary>
        /// <param name="text">The stored color; may be null or empty</param>
        /// <param name="color">The parsed color</param>
        /// <returns>true if <paramref name="text"/> is a valid color</returns>
        internal static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            try
            {
                color = ColorTranslator.FromHtml(text.Trim());
                return !color.IsEmpty;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Draw a filled, anti-aliased circle, like SecureCRT's colored buttons</summary>
        /// <param name="color">Fill color</param>
        /// <param name="size">Width and height of the bitmap in pixels</param>
        /// <returns>A new bitmap; the caller must dispose it</returns>
        internal static Bitmap CreateColorCircle(Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen outline = new Pen(ControlPaint.Dark(color, 0.1f)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(brush, 1, 1, size - 3, size - 3);
                g.DrawEllipse(outline, 1, 1, size - 3, size - 3);
            }
            return bitmap;
        }

        /// <summary>The bar currently shown: the one named in Settings.ActiveButtonBar, or the first bar</summary>
        private ButtonBar CurrentBar
        {
            get
            {
                return this.bars.Bars.FirstOrDefault(b => string.Equals(b.Name, SuperPuTTY.Settings.ActiveButtonBar, StringComparison.OrdinalIgnoreCase))
                    ?? this.bars.Bars[0];
            }
        }

        /// <summary>Reload the bars from ButtonBars.xml, picking up changes made by others sharing the file, and redraw</summary>
        public void Reload()
        {
            this.bars = ButtonBarStore.Load();
            this.RefreshBarList();
            this.RefreshButtons();
        }

        /// <summary>Fill the dropdown with the bar names and select the current bar</summary>
        private void RefreshBarList()
        {
            this.updatingCombo = true;
            try
            {
                this.comboBars.Items.Clear();
                foreach (ButtonBar bar in this.bars.Bars)
                {
                    this.comboBars.Items.Add(bar.Name);
                }
                this.comboBars.SelectedIndex = this.bars.Bars.IndexOf(this.CurrentBar);
            }
            finally
            {
                this.updatingCombo = false;
            }
        }

        /// <summary>Replace the buttons on the strip with those of the current bar</summary>
        private void RefreshButtons()
        {
            this.SuspendLayout();
            while (this.Items.Count > this.Items.IndexOf(this.separator) + 1)
            {
                ToolStripItem item = this.Items[this.Items.Count - 1];
                this.Items.Remove(item);
                item.Dispose();
            }
            foreach (Image image in this.circleImages)
            {
                image.Dispose();
            }
            this.circleImages.Clear();

            ButtonBar bar = this.CurrentBar;
            if (bar.Buttons.Count == 0)
            {
                this.Items.Add(new ToolStripLabel("Right-click to add a button") { ForeColor = SystemColors.GrayText });
            }
            foreach (CommandButton command in bar.Buttons)
            {
                bool isCircle;
                Image image = GetButtonImage(command, out isCircle);
                if (isCircle)
                {
                    this.circleImages.Add(image);
                }
                ToolStripButton button = new ToolStripButton(command.Label)
                {
                    DisplayStyle = image != null ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
                    Image = image,
                    // circles are drawn at the right size already; session icons scale like the rest of the toolbar
                    ImageScaling = isCircle ? ToolStripItemImageScaling.None : ToolStripItemImageScaling.SizeToFit,
                    Tag = command,
                    ToolTipText = (string.IsNullOrEmpty(command.Tooltip) ? command.Text : command.Tooltip) + "\n(Shift+click: send to selected tabs)"
                };
                button.Click += (s, e) => this.RunButton(command, (Control.ModifierKeys & Keys.Shift) == Keys.Shift);
                this.Items.Add(button);
            }
            this.ResumeLayout();
        }

        /// <summary>Show the bar picked in the dropdown and remember it for this user</summary>
        /// <param name="sender">The dropdown</param>
        /// <param name="e">Not used</param>
        private void comboBars_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.updatingCombo || this.comboBars.SelectedIndex < 0)
                return;

            SuperPuTTY.Settings.ActiveButtonBar = this.bars.Bars[this.comboBars.SelectedIndex].Name;
            SuperPuTTY.Settings.Save();
            this.RefreshButtons();
        }

        /// <summary>
        /// Send a button's string or run its script in the target tabs. Placeholders are checked and replaced
        /// first, and the clipboard is read here on the UI thread; the sending itself runs on background
        /// threads, one per tab, so pauses don't freeze the window.
        /// </summary>
        /// <param name="command">The button that was clicked</param>
        /// <param name="toSelected">true to send to the tabs selected for Send Commands (Shift+click), false for the active tab</param>
        private void RunButton(CommandButton command, bool toSelected)
        {
            // terminal tabs and RDP tabs; ctlPuttyPanel.SendCommand types into either
            List<ctlPuttyPanel> targets = this.getTargets(toSelected).Where(panel => panel.CanReceiveCommands).ToList();
            if (targets.Count == 0)
            {
                SuperPuTTY.ReportStatus(toSelected ? "No selected sessions to send to" : "No active session to send to");
                return;
            }

            if (!SessionVariables.EnsureDefined(command.Text, $"Button \"{command.Label}\"", this.FindForm()))
                return;
            string text = SessionVariables.Substitute(command.Text, SessionVariables.Load());

            if (command.Action == CommandButtonAction.SpslScript)
            {
                string[] lines = text.Split('\n');
                foreach (ctlPuttyPanel panel in targets)
                {
                    // like SPSL.BeginExecuteScript, but sending through the tab so RDP sessions work too
                    new Thread(() =>
                    {
                        foreach (string line in lines)
                        {
                            CommandData step;
                            SPSL.TryParseScriptLine(line.TrimEnd('\r'), out step);
                            if (step != null)
                            {
                                panel.SendCommand(step);
                            }
                        }
                    }) { IsBackground = true, Name = "ButtonBar" }.Start();
                }
            }
            else
            {
                List<CommandData> steps = SendStringParser.Parse(text, () => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
                foreach (ctlPuttyPanel panel in targets)
                {
                    new Thread(() =>
                    {
                        foreach (CommandData step in steps)
                        {
                            panel.SendCommand(step);
                        }
                    }) { IsBackground = true, Name = "ButtonBar" }.Start();
                }
            }

            Log.InfoFormat("Button bar: ran \"{0}\" in {1} session(s)", command.Label, targets.Count);
            SuperPuTTY.ReportStatus($"Sent \"{command.Label}\" to {targets.Count} session(s)");

            // clicking the bar took the keyboard away from the terminal; give it back to the active tab
            this.BeginInvoke(new MethodInvoker(() => SuperPuTTY.MainForm.FocusActiveDocument("ButtonBar")));
        }

        /// <summary>
        /// Before the right-click menu opens: reload the bars so edits apply to the latest shared file,
        /// note which button was clicked, and enable only the items that apply
        /// </summary>
        /// <param name="sender">The context menu</param>
        /// <param name="e">Not used</param>
        private void ContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Reload();
            ToolStripItem item = this.GetItemAt(this.PointToClient(Cursor.Position));
            this.contextButtonIndex = item != null && item.Tag is CommandButton
                ? this.CurrentBar.Buttons.IndexOf((CommandButton)item.Tag)
                : -1;

            bool onButton = this.contextButtonIndex >= 0;
            this.menuEditButton.Enabled = onButton;
            this.menuDeleteButton.Enabled = onButton;
            this.menuMoveLeft.Enabled = onButton && this.contextButtonIndex > 0;
            this.menuMoveRight.Enabled = onButton && this.contextButtonIndex < this.CurrentBar.Buttons.Count - 1;
            this.menuDeleteBar.Enabled = this.bars.Bars.Count > 1;
        }

        /// <summary>Ask for a new button and add it to the end of the current bar</summary>
        private void NewButton()
        {
            using (dlgEditCommandButton dialog = new dlgEditCommandButton(null))
            {
                if (dialog.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    this.CurrentBar.Buttons.Add(dialog.EditedButton);
                    this.SaveAndRefresh();
                }
            }
        }

        /// <summary>Edit a button of the current bar</summary>
        /// <param name="index">The button's index in the current bar</param>
        private void EditButton(int index)
        {
            if (index < 0)
                return;

            using (dlgEditCommandButton dialog = new dlgEditCommandButton(this.CurrentBar.Buttons[index]))
            {
                if (dialog.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    this.CurrentBar.Buttons[index] = dialog.EditedButton;
                    this.SaveAndRefresh();
                }
            }
        }

        /// <summary>Delete a button of the current bar, after asking</summary>
        /// <param name="index">The button's index in the current bar</param>
        private void DeleteButton(int index)
        {
            if (index < 0)
                return;

            CommandButton button = this.CurrentBar.Buttons[index];
            if (Messenger.MessageBox($"Delete the button \"{button.Label}\"?", "Delete Button", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                this.CurrentBar.Buttons.RemoveAt(index);
                this.SaveAndRefresh();
            }
        }

        /// <summary>Move a button of the current bar one place left or right</summary>
        /// <param name="index">The button's index in the current bar</param>
        /// <param name="offset">-1 to move left, 1 to move right</param>
        private void MoveButton(int index, int offset)
        {
            List<CommandButton> buttons = this.CurrentBar.Buttons;
            int newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= buttons.Count)
                return;

            CommandButton button = buttons[index];
            buttons.RemoveAt(index);
            buttons.Insert(newIndex, button);
            this.SaveAndRefresh();
        }

        /// <summary>Ask for a name, add an empty bar with it and switch to it</summary>
        private void NewBar()
        {
            string name = this.AskBarName("New Button Bar", string.Empty, null);
            if (name != null)
            {
                this.bars.Bars.Add(new ButtonBar { Name = name });
                SuperPuTTY.Settings.ActiveButtonBar = name;
                SuperPuTTY.Settings.Save();
                this.SaveAndRefresh();
            }
        }

        /// <summary>Ask for a new name for the current bar</summary>
        private void RenameBar()
        {
            ButtonBar bar = this.CurrentBar;
            string name = this.AskBarName("Rename Button Bar", bar.Name, bar);
            if (name != null)
            {
                bar.Name = name;
                SuperPuTTY.Settings.ActiveButtonBar = name;
                SuperPuTTY.Settings.Save();
                this.SaveAndRefresh();
            }
        }

        /// <summary>Delete the current bar after asking, and switch to the first remaining bar. The last bar can't be deleted</summary>
        private void DeleteBar()
        {
            ButtonBar bar = this.CurrentBar;
            if (this.bars.Bars.Count <= 1)
                return;

            if (Messenger.MessageBox($"Delete the button bar \"{bar.Name}\" and its {bar.Buttons.Count} button(s)?\n\nEveryone sharing ButtonBars.xml will lose it.",
                    "Delete Button Bar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                this.bars.Bars.Remove(bar);
                SuperPuTTY.Settings.ActiveButtonBar = this.bars.Bars[0].Name;
                SuperPuTTY.Settings.Save();
                this.SaveAndRefresh();
            }
        }

        /// <summary>Ask for a bar name that isn't empty and isn't used by another bar</summary>
        /// <param name="title">Dialog title</param>
        /// <param name="current">Initial text</param>
        /// <param name="renaming">The bar being renamed, which may keep its own name; null for a new bar</param>
        /// <returns>The trimmed name, or null if cancelled</returns>
        private string AskBarName(string title, string current, ButtonBar renaming)
        {
            dlgRenameItem.ItemNameValidationHandler validator = delegate (string name, out string error)
            {
                error = string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    error = "Name cannot be empty";
                }
                else if (this.bars.Bars.Any(b => b != renaming && string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    error = "A button bar with this name already exists";
                }
                return error.Length == 0;
            };

            using (dlgRenameItem dialog = new dlgRenameItem { Text = title, ItemName = current, DetailName = "Button bar name", ItemNameValidator = validator })
            {
                if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
                    return null;

                // the dialog only validates when focus leaves the text box, so pressing Enter can skip it
                string message;
                if (!validator(dialog.ItemName, out message))
                {
                    Messenger.MessageBox(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return dialog.ItemName.Trim();
            }
        }

        /// <summary>Save all bars to ButtonBars.xml, telling the user if that fails, and redraw</summary>
        private void SaveAndRefresh()
        {
            if (!ButtonBarStore.Save(this.bars))
            {
                Messenger.MessageBox($"Could not save the button bars to\n{ButtonBarStore.FilePath}\n\nThe folder may be read-only. See the log for details.",
                    "Button Bar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            this.RefreshBarList();
            this.RefreshButtons();
        }
    }
}
