using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Dialog for creating or editing one <see cref="CommandButton"/> on the button bar: its label, tooltip,
    /// action (send string or SPSL script) and the text it sends. Opened from <see cref="ButtonBarStrip"/>'s
    /// right-click menu. Works on a copy, so the original button only changes if the user clicks OK.
    /// </summary>
    public partial class dlgEditCommandButton : Form
    {
        /// <summary>Help shown under the text box for SPSL scripts</summary>
        private const string SpslHelpText = "SENDLINE text · SENDKEY ENTER · SLEEP ms · WAITFOR text (30 s, or /t:10 text) · # comment.";

        /// <summary>Preset circle colors offered in the Icon list, as name and HTML color</summary>
        private static readonly string[][] PresetColors =
        {
            new[] { "Red", "#E5534B" },
            new[] { "Orange", "#F0883E" },
            new[] { "Yellow", "#E3B341" },
            new[] { "Green", "#3FB950" },
            new[] { "Teal", "#2AA198" },
            new[] { "Blue", "#4493F8" },
            new[] { "Purple", "#A371F7" },
            new[] { "Gray", "#8B949E" }
        };

        /// <summary>One entry in the Icon list: no icon, a circle color, the "Custom color..." entry, or a session icon</summary>
        private class IconChoice
        {
            /// <summary>Text shown in the list</summary>
            public string Name;

            /// <summary>HTML circle color, for a color entry</summary>
            public string Color;

            /// <summary>Session icon key, for an icon entry</summary>
            public string ImageKey;

            /// <summary>True for the "Custom color..." entry, which opens a color picker</summary>
            public bool PickCustom;

            /// <summary>True for the "Choose icon..." entry, which opens the icon picker</summary>
            public bool PickIcon;
        }

        /// <summary>The Icon list entry selected before the current one, to go back to if the color picker is cancelled</summary>
        private IconChoice lastIconChoice;

        /// <summary>Create the dialog to edit a copy of <paramref name="button"/></summary>
        /// <param name="button">The button to edit; null to create a new one</param>
        public dlgEditCommandButton(CommandButton button)
        {
            InitializeComponent();

            this.EditedButton = button != null ? button.Clone() : new CommandButton { Action = CommandButtonAction.SendString };
            this.Text = button != null ? "Edit Button" : "New Button";
            this.textBoxLabel.Text = this.EditedButton.Label;
            this.textBoxTooltip.Text = this.EditedButton.Tooltip;
            this.comboBoxAction.SelectedIndex = (int)this.EditedButton.Action;
            // the text box needs Windows line endings to show multi-line scripts
            this.textBoxText.Text = (this.EditedButton.Text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            this.FillIconChoices();

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>The edited button. Only meaningful after the dialog returns <see cref="DialogResult.OK"/></summary>
        public CommandButton EditedButton { get; private set; }

        /// <summary>Update the text box label and help text to match the selected action, and start a new script with the SPSL header</summary>
        /// <param name="sender">The action combo box</param>
        /// <param name="e">Not used</param>
        private void comboBoxAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isScript = this.comboBoxAction.SelectedIndex == (int)CommandButtonAction.SpslScript;
            this.labelText.Text = isScript ? "&Script:" : "&Send:";
            // scripts get the same commands, saved scripts and check as the Script Editor, beside the script box
            this.ShowScriptHelp(isScript);
            this.labelHelp.Text = (isScript ? SpslHelpText : SendStringParser.HelpText) + "  {key} placeholders are replaced too."
                + "  {prompt:Name} asks for a value when clicked ({prompt:Name=default}; {secret:Name} masked).";
        }

        /// <summary>
        /// Fill the Icon list (none, preset colors, the button's own custom color if any, "Custom color...", the
        /// button's icon if it has one, and "Choose icon..." for the icon picker) and select the button's current icon
        /// </summary>
        private void FillIconChoices()
        {
            this.comboBoxIcon.Items.Add(new IconChoice { Name = "(none)" });
            foreach (string[] preset in PresetColors)
            {
                this.comboBoxIcon.Items.Add(new IconChoice { Name = preset[0], Color = preset[1] });
            }

            Color current;
            if (string.IsNullOrEmpty(this.EditedButton.ImageKey) && ButtonBarStrip.TryParseColor(this.EditedButton.Color, out current)
                && !PresetColors.Any(p => string.Equals(p[1], this.EditedButton.Color, StringComparison.OrdinalIgnoreCase)))
            {
                this.comboBoxIcon.Items.Add(new IconChoice { Name = "Custom " + this.EditedButton.Color, Color = this.EditedButton.Color });
            }
            this.comboBoxIcon.Items.Add(new IconChoice { Name = "Custom color...", PickCustom = true });

            // the button's icon, if it has one; others are chosen in the icon picker, so old and duplicate keys
            // aren't listed
            if (!string.IsNullOrEmpty(this.EditedButton.ImageKey))
            {
                this.comboBoxIcon.Items.Add(IconEntry(this.EditedButton.ImageKey));
            }
            this.comboBoxIcon.Items.Add(new IconChoice { Name = "Choose icon...", PickIcon = true });

            IconChoice selected = this.comboBoxIcon.Items.Cast<IconChoice>().FirstOrDefault(c =>
                !string.IsNullOrEmpty(this.EditedButton.ImageKey)
                    ? c.ImageKey == this.EditedButton.ImageKey
                    : c.Color != null && string.Equals(c.Color, this.EditedButton.Color, StringComparison.OrdinalIgnoreCase));
            this.comboBoxIcon.SelectedItem = selected ?? this.comboBoxIcon.Items[0];
            this.lastIconChoice = (IconChoice)this.comboBoxIcon.SelectedItem;
        }

        /// <summary>The Icon list entry for an icon key, named after the icon</summary>
        /// <param name="key">The icon key, e.g. "switch#1E63B4"</param>
        /// <returns>The entry</returns>
        private static IconChoice IconEntry(string key)
        {
            string title = IconLibrary.TitleOf(IconLibrary.NameOf(key));
            Color color = IconLibrary.ColorOf(key);
            return new IconChoice
            {
                Name = color.IsEmpty ? title : title + " " + ColorTranslator.ToHtml(color),
                ImageKey = key
            };
        }

        /// <summary>Draw an Icon list entry: its circle or session icon, then its name</summary>
        /// <param name="sender">The Icon list</param>
        /// <param name="e">Which entry to draw, and where</param>
        private void comboBoxIcon_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
                return;

            IconChoice choice = (IconChoice)this.comboBoxIcon.Items[e.Index];
            int size = e.Bounds.Height - 4;
            Rectangle iconBounds = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 2, size, size);
            Color color;
            if (choice.ImageKey != null && IconLibrary.Ensure(SuperPuTTY.Images, choice.ImageKey))
            {
                e.Graphics.DrawImage(SuperPuTTY.Images.Images[choice.ImageKey], iconBounds);
            }
            else if (ButtonBarStrip.TryParseColor(choice.Color, out color))
            {
                using (Bitmap circle = ButtonBarStrip.CreateColorCircle(color, size))
                {
                    e.Graphics.DrawImage(circle, iconBounds.Location);
                }
            }

            TextRenderer.DrawText(e.Graphics, choice.Name, e.Font,
                new Rectangle(iconBounds.Right + 4, e.Bounds.Top, e.Bounds.Width - size - 8, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        /// <summary>
        /// When "Custom color..." is picked, open a color picker and add the chosen color as an entry;
        /// if the picker is cancelled, go back to the previous entry
        /// </summary>
        /// <param name="sender">The Icon list</param>
        /// <param name="e">Not used</param>
        private void comboBoxIcon_SelectionChangeCommitted(object sender, EventArgs e)
        {
            IconChoice choice = (IconChoice)this.comboBoxIcon.SelectedItem;
            if (choice != null && choice.PickIcon)
            {
                this.ChooseIcon();
                return;
            }
            if (choice == null || !choice.PickCustom)
            {
                this.lastIconChoice = choice;
                return;
            }

            using (ColorDialog dialog = new ColorDialog { FullOpen = true })
            {
                Color start;
                if (this.lastIconChoice != null && ButtonBarStrip.TryParseColor(this.lastIconChoice.Color, out start))
                {
                    dialog.Color = start;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string html = ColorTranslator.ToHtml(Color.FromArgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                    IconChoice custom = new IconChoice { Name = "Custom " + html, Color = html };
                    this.comboBoxIcon.Items.Insert(this.comboBoxIcon.SelectedIndex, custom);
                    this.comboBoxIcon.SelectedItem = custom;
                    this.lastIconChoice = custom;
                }
                else
                {
                    this.comboBoxIcon.SelectedItem = this.lastIconChoice;
                }
            }
        }

        /// <summary>
        /// Open the icon picker (the same one Edit Session uses) and make the chosen icon the button's, replacing any
        /// icon entry already in the list; if the picker is cancelled, go back to the previous entry
        /// </summary>
        private void ChooseIcon()
        {
            string current = this.lastIconChoice != null ? this.lastIconChoice.ImageKey : null;
            using (dlgIconPicker picker = new dlgIconPicker(current))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedKey))
                {
                    this.comboBoxIcon.SelectedItem = this.lastIconChoice;
                    return;
                }
                IconChoice old = this.comboBoxIcon.Items.Cast<IconChoice>().FirstOrDefault(c => c.ImageKey != null);
                if (old != null)
                {
                    this.comboBoxIcon.Items.Remove(old);
                }
                IconChoice chosen = IconEntry(picker.SelectedKey);
                int pickIndex = this.comboBoxIcon.Items.Cast<IconChoice>().ToList().FindIndex(c => c.PickIcon);
                this.comboBoxIcon.Items.Insert(pickIndex, chosen);
                this.comboBoxIcon.SelectedItem = chosen;
                this.lastIconChoice = chosen;
            }
        }

        /// <summary>
        /// Show or hide the SPSL panel (SpslHelpPanel: commands, saved scripts, check), widening the dialog to make room
        /// for it to the right of the script box; shown whenever the action is "Run SPSL script"
        /// </summary>
        /// <param name="show">true to show it</param>
        private void ShowScriptHelp(bool show)
        {
            if (show == this.helpPanel.Visible)
                return;
            this.helpPanel.Editor = this.textBoxText;
            this.helpPanel.ShowSavedScripts = true;
            this.helpPanel.Height = this.btnOK.Bottom - this.helpPanel.Top;
            this.helpPanel.Visible = show;
            int margin = this.textBoxLabel.Top;
            // taller too, so the panel's lists have room; the script box grows with it
            int extra = this.Font.Height * 12;
            this.ClientSize = new Size(show ? this.helpPanel.Right + margin : this.helpPanel.Left,
                this.ClientSize.Height + (show ? extra : -extra));
        }

        /// <summary>Validate the input, copy it into <see cref="EditedButton"/> and close with OK</summary>
        /// <param name="sender">The OK button</param>
        /// <param name="e">Not used</param>
        private void btnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(this.textBoxLabel.Text))
            {
                Messenger.MessageBox("Please enter a label for the button.", "Edit Button", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.textBoxLabel.Focus();
                return;
            }

            if (this.comboBoxAction.SelectedIndex == (int)CommandButtonAction.SpslScript)
            {
                // check the script, showing any problems beside it
                this.helpPanel.Editor = this.textBoxText;
                int errors = this.helpPanel.Check().Count(i => i.IsError);
                if (errors > 0)
                {
                    if (Messenger.MessageBox(string.Format("The script has {0} error{1}, listed beside it. Save the button anyway?",
                            errors, errors == 1 ? "" : "s"), "Edit Button", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }
                }
            }

            this.EditedButton.Label = this.textBoxLabel.Text.Trim();
            this.EditedButton.Tooltip = string.IsNullOrWhiteSpace(this.textBoxTooltip.Text) ? null : this.textBoxTooltip.Text.Trim();
            this.EditedButton.Action = (CommandButtonAction)this.comboBoxAction.SelectedIndex;
            IconChoice icon = (IconChoice)this.comboBoxIcon.SelectedItem;
            this.EditedButton.Color = icon?.Color;
            this.EditedButton.ImageKey = icon?.ImageKey;
            // store scripts with \n line endings, which SPSL splits on
            this.EditedButton.Text = this.textBoxText.Text.Replace("\r\n", "\n");

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
