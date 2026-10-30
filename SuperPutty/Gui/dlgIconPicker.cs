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
    /// Chooses a session icon and, for device templates and logos, its colour (IconLibrary). Icons are grouped as
    /// Devices, Logos and Other icons (the theme folders' PNGs), with a search box. Picking a colour shows every device
    /// template in that colour, so it's easy to see what "blue switch" looks like before choosing. Returns the key to
    /// store in the session, e.g. "switch#1E63B4". Opened from Edit Session's icon button.
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

            Button more = new Button { Text = "More...", AutoSize = true, Height = size + 4, Margin = new Padding(6, 0, 0, 0) };
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
        }

        /// <summary>
        /// Fill the grid: device templates (in the chosen colour), logos, then other icons, filtered by the search box
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

            this.listIcons.BeginUpdate();
            this.listIcons.Items.Clear();
            this.listIcons.Groups.Clear();
            this.gridImages.Images.Clear();

            ListViewGroup devices = this.listIcons.Groups.Add("devices", "Devices");
            ListViewGroup general = this.listIcons.Groups.Add("general", "General");
            ListViewGroup logos = this.listIcons.Groups.Add("logos", "Logos");
            ListViewGroup other = this.listIcons.Groups.Add("other", "Other icons");
            IEnumerable<string> pngs = SuperPuTTY.Images == null ? Enumerable.Empty<string>()
                : SuperPuTTY.Images.Images.Keys.Cast<string>()
                    .Where(k => k != "stop" && !k.Contains("#") && !IconLibrary.IsColorable(k))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            foreach (Tuple<string, ListViewGroup> entry in IconTemplates.Keys.Select(k => Tuple.Create(k, IconTemplates.IsGeneral(k) ? general : devices))
                .Concat(BrandLogos.Keys.Select(k => Tuple.Create(k, logos)))
                .Concat(pngs.Select(k => Tuple.Create(k, other))))
            {
                string name = entry.Item1;
                string title = IconLibrary.TitleOf(name);
                if (filter.Length > 0 && title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // only device templates show the chosen colour here; logos keep their brand colours in the grid
                Image image = IconTemplates.Exists(name) ? IconLibrary.RenderDrawn(this.KeyFor(name), size)
                    : IconLibrary.IsDrawn(name) ? IconLibrary.RenderDrawn(name, size)
                    : IconLibrary.Scale(SuperPuTTY.Images.Images[name], size);
                this.gridImages.Images.Add(name, image);
                ListViewItem item = new ListViewItem(title, name, entry.Item2) { Tag = name, ToolTipText = name };
                this.listIcons.Items.Add(item);
                if (string.Equals(name, selectName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                }
            }
            this.listIcons.EndUpdate();
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

        /// <summary>Use a colour for templates and logos, and redraw the grid in it</summary>
        /// <param name="color">The colour; Empty for each icon's own</param>
        private void ChooseColor(Color color)
        {
            this.chosenColor = color;
            this.FillList(null);
        }

        /// <summary>Show the selected icon large, mark the chosen colour, and enable what applies</summary>
        private void UpdateSelection()
        {
            string key = this.CurrentKey();
            this.btnOK.Enabled = key != null;
            bool colorable = key != null && IconLibrary.IsColorable(key);
            this.panelSwatches.Enabled = colorable;
            foreach (KeyValuePair<Button, Color> swatch in this.swatches)
            {
                bool chosen = swatch.Value.ToArgb() == this.chosenColor.ToArgb();
                swatch.Key.FlatAppearance.BorderSize = chosen ? 3 : 1;
                swatch.Key.FlatAppearance.BorderColor = chosen ? this.ForeColor : Color.Gray;
            }

            Image old = this.pictureBoxPreview.Image;
            this.pictureBoxPreview.Image = key == null ? null
                : IconLibrary.IsColorable(key)
                    ? IconLibrary.RenderDrawn(key, IconLibrary.ToPixels(PreviewSize))
                    : IconLibrary.Scale(SuperPuTTY.Images.Images[key], IconLibrary.ToPixels(PreviewSize));
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
            this.UpdateSelection();
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
