using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Chooses a session icon and its colour (IconLibrary). Icons are grouped as Devices, General, Logos and Custom (the
    /// one-colour icons users add with Add icon..., kept by CustomIcons), with a search box. The chosen colour shows on
    /// the selected icon's tile and in the large preview; every other icon keeps its own colours. Returns the key to
    /// store, e.g. "switch#1E63B4". Opened from Edit Session's icon button and Edit Button's "Choose icon...".
    /// </summary>
    public partial class dlgIconPicker : Form
    {
        /// <summary>The size icons are shown at in the grid, in logical pixels</summary>
        private const int GridSize = 40;

        /// <summary>The size of the preview, in logical pixels</summary>
        private const int PreviewSize = 64;

        /// <summary>The grid's images, by item key</summary>
        private readonly ImageList gridImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit };

        /// <summary>The colour chosen for templates and logos; Empty for each icon's own colour</summary>
        private Color chosenColor = Color.Empty;

        /// <summary>The swatch buttons, with the colour each picks (Empty for "Default")</summary>
        private readonly Dictionary<Button, Color> swatches = new Dictionary<Button, Color>();

        /// <summary>The More... button, outlined when the chosen colour isn't one of the swatches</summary>
        private Button moreButton;

        /// <summary>The icon whose grid tile shows the chosen colour: the selected one; null if none</summary>
        private string tintedName;

        /// <summary>Set while the grid is being refilled, so selecting the remembered item doesn't refill it again</summary>
        private bool filling;

        /// <summary>The key to store in the session; set when the dialog closes with OK</summary>
        public string SelectedKey { get; private set; }

        /// <summary>Create the picker with the session's current icon selected</summary>
        /// <param name="currentKey">The session's icon key; may be an old key like "green_switch"</param>
        public dlgIconPicker(string currentKey)
        {
            InitializeComponent();
            int grid = IconLibrary.ToPixels(GridSize);
            this.gridImages.ImageSize = new Size(grid, grid);
            this.listIcons.LargeImageList = this.gridImages;
            this.listIcons.View = View.LargeIcon;
            this.textBoxSearch.Text = string.Empty;
            this.toolTip.SetToolTip(this.textBoxSearch, "Type to filter the icons by name");

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }

            this.AddSwatches();
            if (IconLibrary.IsColorable(currentKey))
            {
                Color current = IconLibrary.ColorOf(currentKey);
                Color defaultColor = IconLibrary.ColorOf(IconLibrary.NameOf(currentKey));
                this.chosenColor = current.ToArgb() == defaultColor.ToArgb() ? Color.Empty : current;
            }
            this.FillList(IconLibrary.NameOf(currentKey ?? string.Empty));
            this.btnOK.Click += (s, e) => this.SelectedKey = this.CurrentKey();
        }

        /// <summary>Add the colour row: Default, the palette, and More... for any colour</summary>
        private void AddSwatches()
        {
            int size = IconLibrary.ToPixels(22);
            Button defaultButton = new Button { Text = "Default", AutoSize = true, Height = size + 4, Margin = new Padding(0, 0, 6, 0) };
            this.toolTip.SetToolTip(defaultButton, "Each icon's own colour");
            this.swatches[defaultButton] = Color.Empty;
            defaultButton.Click += (s, e) => this.ChooseColor(Color.Empty);
            this.panelSwatches.Controls.Add(defaultButton);

            foreach (KeyValuePair<string, Color> entry in IconLibrary.Palette)
            {
                Color color = entry.Value;
                Button swatch = new Button
                {
                    Width = size,
                    Height = size,
                    BackColor = color,
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(1, 3, 1, 0),
                    Cursor = Cursors.Hand
                };
                swatch.FlatAppearance.BorderSize = 1;
                swatch.FlatAppearance.MouseOverBackColor = color;
                this.toolTip.SetToolTip(swatch, entry.Key);
                this.swatches[swatch] = color;
                swatch.Click += (s, e) => this.ChooseColor(color);
                this.panelSwatches.Controls.Add(swatch);
            }

            Button more = new Button { Text = "More...", AutoSize = true, Height = size + 4, Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat };
            this.moreButton = more;
            this.toolTip.SetToolTip(more, "Any colour");
            more.Click += (s, e) =>
            {
                using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = this.chosenColor.IsEmpty ? Color.White : this.chosenColor })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.ChooseColor(dialog.Color);
                    }
                }
            };
            this.panelSwatches.Controls.Add(more);
        }

        /// <summary>
        /// Colour the swatches again (DarkModeCS recolours buttons during Load) and make the grid's group headings
        /// readable in dark mode
        /// </summary>
        /// <param name="e">Not used</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                // DarkModeCS gives list views the dark Explorer style, whose group headings stay dark blue; File
                // Explorer's dark items view draws them light
                NativeMethods.SetWindowTheme(this.listIcons.Handle, "DarkMode_ItemsView", null);
            }
            foreach (KeyValuePair<Button, Color> swatch in this.swatches.Where(s => !s.Value.IsEmpty))
            {
                swatch.Key.BackColor = swatch.Value;
                swatch.Key.FlatAppearance.MouseOverBackColor = swatch.Value;
            }
            this.UpdateSelection();
            // the grid was filled before it had a window, so scrolling to the selection did nothing then
            if (this.listIcons.SelectedItems.Count > 0)
            {
                this.BeginInvoke(new Action(() => this.listIcons.SelectedItems[0].EnsureVisible()));
            }
        }

        /// <summary>
        /// Fill the grid: device and general icons, logos, then custom icons, filtered by the search box. Only the
        /// selected icon's tile shows the chosen colour; the others show their own.
        /// </summary>
        /// <param name="selectName">The icon to select afterwards; null keeps the current selection</param>
        private void FillList(string selectName)
        {
            if (selectName == null && this.listIcons.SelectedItems.Count > 0)
            {
                selectName = (string)this.listIcons.SelectedItems[0].Tag;
            }
            string filter = this.textBoxSearch.Text.Trim();
            int size = this.gridImages.ImageSize.Width;
            this.tintedName = null;

            this.filling = true;
            this.listIcons.BeginUpdate();
            this.listIcons.Items.Clear();
            this.listIcons.Groups.Clear();
            this.gridImages.Images.Clear();

            ListViewGroup devices = this.listIcons.Groups.Add("devices", "Devices");
            ListViewGroup general = this.listIcons.Groups.Add("general", "General");
            ListViewGroup logos = this.listIcons.Groups.Add("logos", "Logos");
            ListViewGroup custom = this.listIcons.Groups.Add("custom", "Custom");

            foreach (Tuple<string, ListViewGroup> entry in IconTemplates.Keys.Select(k => Tuple.Create(k, IconTemplates.IsGeneral(k) ? general : devices))
                .Concat(BrandLogos.Keys.Select(k => Tuple.Create(k, logos)))
                .Concat(CustomIcons.Keys().Select(k => Tuple.Create(k, custom))))
            {
                string name = entry.Item1;
                string title = IconLibrary.TitleOf(name);
                if (filter.Length > 0 && title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool selected = string.Equals(name, selectName, StringComparison.OrdinalIgnoreCase);
                Image image = IconLibrary.RenderDrawn(selected ? this.KeyFor(name) : name, size);
                if (image == null)
                    continue;
                this.gridImages.Images.Add(name, image);
                ListViewItem item = new ListViewItem(title, name, entry.Item2) { Tag = name, ToolTipText = name };
                this.listIcons.Items.Add(item);
                if (selected)
                {
                    item.Selected = true;
                    this.tintedName = name;
                }
            }
            this.listIcons.EndUpdate();
            this.filling = false;
            if (this.listIcons.SelectedItems.Count > 0)
            {
                this.listIcons.SelectedItems[0].EnsureVisible();
            }
            this.UpdateSelection();
        }

        /// <summary>The key for an icon with the chosen colour (drawn icons only take a colour)</summary>
        /// <param name="name">The icon name</param>
        /// <returns>The key</returns>
        private string KeyFor(string name)
        {
            return IconLibrary.IsDrawn(name) && !this.chosenColor.IsEmpty ? IconLibrary.MakeKey(name, this.chosenColor) : name;
        }

        /// <summary>The key of the selected icon in the chosen colour; null if nothing is selected</summary>
        /// <returns>The key</returns>
        private string CurrentKey()
        {
            return this.listIcons.SelectedItems.Count > 0 ? this.KeyFor((string)this.listIcons.SelectedItems[0].Tag) : null;
        }

        /// <summary>Use a colour for the selected icon, and redraw its tile and the preview in it</summary>
        /// <param name="color">The colour; Empty for the icon's own</param>
        private void ChooseColor(Color color)
        {
            this.chosenColor = color;
            if (this.tintedName != null)
            {
                this.SetTile(this.tintedName, this.KeyFor(this.tintedName));
            }
            this.UpdateSelection();
        }

        /// <summary>Redraw one grid tile</summary>
        /// <param name="name">The icon whose tile it is</param>
        /// <param name="key">The key to draw it as, with or without a colour</param>
        private void SetTile(string name, string key)
        {
            int index = this.gridImages.Images.IndexOfKey(name);
            Image image = index < 0 ? null : IconLibrary.RenderDrawn(key, this.gridImages.ImageSize.Width);
            if (image != null)
            {
                this.gridImages.Images[index] = image;
                this.listIcons.Invalidate();
            }
        }

        /// <summary>Show the selected icon large, mark the chosen colour, and enable what applies</summary>
        private void UpdateSelection()
        {
            string key = this.CurrentKey();
            this.btnOK.Enabled = key != null;
            bool colorable = key != null && IconLibrary.IsColorable(key);
            this.panelSwatches.Enabled = colorable;
            bool fromSwatch = false;
            foreach (KeyValuePair<Button, Color> swatch in this.swatches)
            {
                bool chosen = swatch.Value.ToArgb() == this.chosenColor.ToArgb();
                fromSwatch |= chosen;
                swatch.Key.FlatAppearance.BorderSize = chosen ? 3 : 1;
                swatch.Key.FlatAppearance.BorderColor = chosen ? this.ForeColor : Color.Gray;
            }
            // a colour from More... has no swatch, so outline More... instead
            this.moreButton.FlatAppearance.BorderSize = fromSwatch ? 1 : 3;
            this.moreButton.FlatAppearance.BorderColor = fromSwatch ? Color.Gray : this.ForeColor;
            this.toolTip.SetToolTip(this.moreButton, fromSwatch ? "Any colour" : "Any colour (now " + ColorTranslator.ToHtml(this.chosenColor) + ")");

            Image old = this.pictureBoxPreview.Image;
            this.pictureBoxPreview.Image = key == null ? null : IconLibrary.RenderDrawn(key, IconLibrary.ToPixels(PreviewSize));
            this.btnRemoveIcon.Enabled = key != null && CustomIcons.IsCustom(IconLibrary.NameOf(key));
            if (old != null)
            {
                old.Dispose();
            }
            this.labelSelected.Text = key == null ? string.Empty
                : IconLibrary.TitleOf(IconLibrary.NameOf(key)) + (colorable ? "  ·  " + ColorTranslator.ToHtml(IconLibrary.ColorOf(key)) : string.Empty);
        }

        /// <summary>Filter the grid as the search text changes</summary>
        /// <param name="sender">The search box</param>
        /// <param name="e">Not used</param>
        private void textBoxSearch_TextChanged(object sender, EventArgs e)
        {
            this.FillList(null);
        }

        /// <summary>Update the preview when another icon is selected</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">Not used</param>
        private void listIcons_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.filling)
                return;
            string selected = this.listIcons.SelectedItems.Count > 0 ? (string)this.listIcons.SelectedItems[0].Tag : null;
            if (selected != null && !string.Equals(selected, this.tintedName, StringComparison.OrdinalIgnoreCase))
            {
                // the colour moves to the newly selected tile; the previous one goes back to its own
                if (this.tintedName != null)
                {
                    this.SetTile(this.tintedName, this.tintedName);
                }
                this.tintedName = selected;
                this.SetTile(selected, this.KeyFor(selected));
            }
            this.UpdateSelection();
        }

        /// <summary>
        /// Add a custom icon: pick an image, name it, and store it as a one-colour shape (CustomIcons.Import), then select
        /// it so its large preview shows how it came out
        /// </summary>
        /// <param name="sender">The Add icon button</param>
        /// <param name="e">Not used</param>
        private void btnAddIcon_Click(object sender, EventArgs e)
        {
            string file;
            using (OpenFileDialog open = new OpenFileDialog
            {
                Title = "Add icon",
                Filter = "PNG images (*.png)|*.png|All images (*.png;*.gif;*.bmp)|*.png;*.gif;*.bmp"
            })
            {
                if (open.ShowDialog(this) != DialogResult.OK)
                    return;
                file = open.FileName;
            }

            dlgRenameItem.ItemNameValidationHandler validator = delegate (string name, out string error)
            {
                return CustomIcons.ValidateName(name, out error);
            };
            using (dlgRenameItem naming = new dlgRenameItem
            {
                Text = "Name the icon",
                ItemName = System.IO.Path.GetFileNameWithoutExtension(file),
                DetailName = "Stored in " + CustomIcons.Folder,
                ItemNameValidator = validator
            })
            {
                if (naming.ShowDialog(this) != DialogResult.OK)
                    return;
                string key, error;
                if (!CustomIcons.Import(file, naming.ItemName, out key, out error))
                {
                    Messenger.MessageBox(error + "\n\nUse a square PNG, 32x32 or bigger, with a dark or one-colour shape " +
                        "on a light or transparent background.", "Add icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                this.textBoxSearch.Text = string.Empty;
                this.chosenColor = Color.Empty;
                this.FillList(key);
            }
        }

        /// <summary>Delete the selected custom icon, after asking; sessions using it then show the default icon</summary>
        /// <param name="sender">The Remove button</param>
        /// <param name="e">Not used</param>
        private void btnRemoveIcon_Click(object sender, EventArgs e)
        {
            string name = this.listIcons.SelectedItems.Count > 0 ? (string)this.listIcons.SelectedItems[0].Tag : null;
            if (!CustomIcons.IsCustom(name))
                return;
            if (Messenger.MessageBox("Delete the custom icon \"" + CustomIcons.TitleOf(name) + "\" from " + CustomIcons.Folder +
                "?\n\nSessions and buttons using it will show the default icon.", "Remove icon",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            CustomIcons.Delete(name);
            this.FillList(string.Empty);
        }

        /// <summary>Double-clicking an icon chooses it</summary>
        /// <param name="sender">The grid</param>
        /// <param name="e">Not used</param>
        private void listIcons_DoubleClick(object sender, EventArgs e)
        {
            if (this.CurrentKey() != null)
            {
                this.SelectedKey = this.CurrentKey();
                this.DialogResult = DialogResult.OK;
            }
        }
    }
}
