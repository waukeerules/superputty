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
        /// <summary>Line that SPSL scripts must start with to be run by <see cref="SuperPuTTY.Scripting.SPSL"/></summary>
        private const string SpslHeader = "#!/bin/spsl";

        /// <summary>Help shown under the text box for SPSL scripts</summary>
        private const string SpslHelpText = "One command per line: SENDLINE text, SENDKEY ENTER, SENDCHAR c, SLEEP ms. Lines starting with # are comments.";

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
            this.labelHelp.Text = (isScript ? SpslHelpText : SendStringParser.HelpText) + "  {key} placeholders are replaced too.";
            if (isScript && string.IsNullOrWhiteSpace(this.textBoxText.Text))
            {
                this.textBoxText.Text = SpslHeader + Environment.NewLine;
            }
        }

        /// <summary>
        /// Fill the Icon list (none, preset colors, the button's own custom color if any, "Custom color...",
        /// then every session icon) and select the button's current icon
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

            if (SuperPuTTY.Images != null)
            {
                foreach (string key in SuperPuTTY.Images.Images.Keys)
                {
                    this.comboBoxIcon.Items.Add(new IconChoice { Name = key, ImageKey = key });
                }
            }

            IconChoice selected = this.comboBoxIcon.Items.Cast<IconChoice>().FirstOrDefault(c =>
                !string.IsNullOrEmpty(this.EditedButton.ImageKey)
                    ? c.ImageKey == this.EditedButton.ImageKey
                    : c.Color != null && string.Equals(c.Color, this.EditedButton.Color, StringComparison.OrdinalIgnoreCase));
            this.comboBoxIcon.SelectedItem = selected ?? this.comboBoxIcon.Items[0];
            this.lastIconChoice = (IconChoice)this.comboBoxIcon.SelectedItem;
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
            if (choice.ImageKey != null)
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
