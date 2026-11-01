using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using log4net;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Adjustments to DockPanelSuite's document tabs that its themes don't offer as settings. frmSuperPutty calls
    /// <see cref="SoftenDarkTheme"/> when it picks the Dark theme, and <see cref="MarkCurrentTabInLists"/> whenever tabs
    /// or panes may have changed, which also hooks each strip for TabIcons.
    /// </summary>
    public static class DocumentTabs
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(DocumentTabs));

        /// <summary>The tab-list menus already hooked, so each gets one Opening handler</summary>
        private static readonly HashSet<ContextMenuStrip> hookedMenus = new HashSet<ContextMenuStrip>();

        /// <summary>
        /// Replace the Dark theme's bright blue (#007ACC) selected and hovered tab colours with a muted slate blue and
        /// grey that sit better next to a black terminal. Called before the theme is given to the DockPanel.
        /// </summary>
        /// <param name="theme">A new VS2015DarkTheme</param>
        public static void SoftenDarkTheme(ThemeBase theme)
        {
            DockPanelColorPalette palette = theme.ColorPalette;
            Color selected = Color.FromArgb(0x3C, 0x4A, 0x5C);
            Color hovered = Color.FromArgb(0x3E, 0x3E, 0x42);
            Color buttonHovered = Color.FromArgb(0x55, 0x65, 0x7A);
            Color buttonPressed = Color.FromArgb(0x2B, 0x35, 0x42);
            Color glyph = Color.FromArgb(0xC8, 0xD2, 0xDC);

            palette.TabSelectedActive.Background = selected;
            palette.TabSelectedActive.Button = glyph;
            palette.TabSelectedActive.Text = Color.White;
            palette.TabUnselectedHovered.Background = hovered;
            palette.TabUnselectedHovered.Button = glyph;
            palette.TabUnselectedHovered.Text = Color.White;
            palette.TabButtonSelectedActiveHovered.Background = buttonHovered;
            palette.TabButtonSelectedActiveHovered.Border = buttonHovered;
            palette.TabButtonSelectedActivePressed.Background = buttonPressed;
            palette.TabButtonSelectedActivePressed.Border = buttonPressed;
            palette.TabButtonUnselectedTabHoveredButtonHovered.Background = buttonHovered;
            palette.TabButtonUnselectedTabHoveredButtonHovered.Border = buttonHovered;
            palette.TabButtonUnselectedTabHoveredButtonPressed.Background = buttonPressed;
            palette.TabButtonUnselectedTabHoveredButtonPressed.Border = buttonPressed;
        }

        /// <summary>
        /// Make the tab-list drop-down (the arrow at the right end of each tab strip) tick and bold the tab that's
        /// showing, which DockPanelSuite doesn't do; with many tabs of the same name there's otherwise no telling which
        /// one is current. Hooks each pane's menu once; safe to call often (frmSuperPutty calls it when content is added
        /// and when the active document changes, since new panes appear when tabs are split off or floated).
        /// </summary>
        /// <param name="dockPanel">The main DockPanel</param>
        public static void MarkCurrentTabInLists(DockPanel dockPanel)
        {
            foreach (DockPane pane in dockPanel.Panes)
            {
                foreach (DockPaneStripBase strip in pane.Controls.OfType<DockPaneStripBase>())
                {
                    // larger, centred tab icons
                    TabIcons.Hook(pane, strip);
                    // floating windows aren't inside the main form, so their strips are hooked here
                    ClickThrough.Apply(strip);
                    ContextMenuStrip menu = FindTabListMenu(strip);
                    if (menu != null && hookedMenus.Add(menu))
                    {
                        menu.ShowCheckMargin = true;
                        MenuCheckMarks.Apply(menu);
                        menu.Opening += (sender, e) => MarkCurrentTab(menu, pane);
                        menu.Disposed += (sender, e) => hookedMenus.Remove(menu);
                    }
                }
            }
        }

        /// <summary>
        /// The strip's tab-list menu. It's a private field in each theme's strip (VS2005DockPaneStrip,
        /// VS2013DockPaneStrip), found by its type rather than its name so a renamed field still works.
        /// </summary>
        /// <param name="strip">A pane's tab strip</param>
        /// <returns>The menu, or null if the strip has none</returns>
        private static ContextMenuStrip FindTabListMenu(DockPaneStripBase strip)
        {
            for (Type type = strip.GetType(); type != null && type != typeof(DockPaneStripBase); type = type.BaseType)
            {
                FieldInfo field = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => typeof(ContextMenuStrip).IsAssignableFrom(f.FieldType));
                if (field != null)
                {
                    return field.GetValue(strip) as ContextMenuStrip;
                }
            }
            Log.DebugFormat("No tab-list menu found in {0}", strip.GetType().Name);
            return null;
        }

        /// <summary>
        /// Tick and bold the item for the pane's current tab. The strip fills the menu just before showing it, with each
        /// item's Tag set to its tab's content, so this runs in Opening, after the items are there.
        /// </summary>
        /// <param name="menu">The tab-list menu about to open</param>
        /// <param name="pane">The pane it lists</param>
        private static void MarkCurrentTab(ContextMenuStrip menu, DockPane pane)
        {
            IDockContent current = pane.ActiveContent;
            foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
            {
                bool isCurrent = item.Tag != null && ReferenceEquals(item.Tag, current);
                item.Checked = isCurrent;
                // the strip uses the tab text, which TabIcons pads with blank characters
                item.Text = item.Text.TrimStart(TabIcons.PadChar);
                // the strip took the item's image from the tab's placeholder icon (see TabIcons)
                Icon icon = TabIcons.Get(item.Tag as IDockContent);
                if (icon != null)
                {
                    item.Image = TabIcons.BitmapOf(icon);
                }
                item.Font = isCurrent ? new Font(menu.Font, FontStyle.Bold) : menu.Font;
            }
        }
    }
}
