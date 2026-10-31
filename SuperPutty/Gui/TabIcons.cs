using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using log4net;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Draws document tab icons larger and centred. DockPanelSuite's tab strips (VS2005 and VS2013/2015 themes) always
    /// draw a tab's icon in a fixed 16x16 box near the tab's bottom edge; the size is a compile-time constant and isn't
    /// scaled for the display, so on a 125%+ screen the icon is small and sits low. Tabs therefore get a transparent
    /// placeholder icon, which keeps the space DockPanelSuite reserves for it, and the real icon is drawn here after the
    /// strip paints: vertically centred and as tall as the tab text. That space is only 24 px, unscaled, so tab text
    /// gets blank padding in front (<see cref="WithPad"/>), which makes DockPanelSuite itself widen the tab and move the
    /// text right by the room the larger icon needs. Callers set icons with
    /// <see cref="Set"/> instead of the Icon property (SuperPuTTY.ApplyIconForWindow, ctlPuttyPanel's badges); the strips
    /// are hooked by DocumentTabs.MarkCurrentTabInLists.
    /// </summary>
    public static class TabIcons
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TabIcons));

        /// <summary>WM_PAINT</summary>
        private const int WmPaint = 0x000F;

        /// <summary>
        /// Where a document tab's text starts, from the tab's left edge: an 8 px gap plus the 16 px icon box. The icon is
        /// drawn before this, so the text doesn't move.
        /// </summary>
        private const int TextStart = 24;

        /// <summary>Whether a strip is the VS2005 (Classic) one, whose tabs are laid out a little differently</summary>
        /// <param name="strip">The strip</param>
        /// <returns>true for Classic</returns>
        private static bool IsClassic(DockPaneStripBase strip)
        {
            return strip.GetType().Name.StartsWith("VS2005", StringComparison.Ordinal);
        }

        /// <summary>
        /// The space for a tab's icon: from <paramref name="start"/> px into the tab, <paramref name="width"/> px wide,
        /// ending a few pixels before the text. Classic tabs have a slanted left edge, so their icons start further in,
        /// and their text starts about 4 px earlier than the VS2013/2015 tabs' (measured on screen).
        /// </summary>
        /// <param name="strip">The strip</param>
        /// <param name="start">Where the icon may start, from the tab's left edge</param>
        /// <param name="width">How wide the icon may be</param>
        private static void IconSlot(DockPaneStripBase strip, out int start, out int width)
        {
            bool classic = IsClassic(strip);
            start = classic ? 9 : 1;
            width = TextStart + padWidth - (classic ? 4 : 0) - TextGap - start;
        }

        /// <summary>
        /// The character padding is made of: U+2800, the blank Braille pattern. It draws as empty space but isn't
        /// whitespace, which DockPanelSuite trims from the start of tab text.
        /// </summary>
        public const char PadChar = '\u2800';

        /// <summary>Blank characters put before tab text to make room for the larger icon; empty until <see cref="Configure"/></summary>
        private static string pad = string.Empty;

        /// <summary>How wide <see cref="pad"/> is in the tab font, in pixels</summary>
        private static int padWidth;

        /// <summary>The icon size, in pixels; the tab font's height</summary>
        private static int iconSize = 16;

        /// <summary>Space kept between the icon and the text, in pixels</summary>
        private const int TextGap = 3;

        /// <summary>
        /// Size the icons for the tab font and work out the padding that makes room for them. frmSuperPutty calls this
        /// when it sets the tab font, before any tab opens.
        /// </summary>
        /// <param name="tabFont">The tab font</param>
        public static void Configure(Font tabFont)
        {
            iconSize = tabFont.Height;
            int needed = 1 + iconSize + TextGap - TextStart;
            pad = string.Empty;
            padWidth = 0;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int baseWidth = TextRenderer.MeasureText("x", tabFont, Size.Empty, flags).Width;
            // as many as it takes; capped in case a font gives them no width
            while (padWidth < needed && pad.Length < 20)
            {
                pad += PadChar;
                padWidth = TextRenderer.MeasureText(pad + "x", tabFont, Size.Empty, flags).Width - baseWidth;
            }
        }

        /// <summary>Tab text with the padding that makes room for the larger icon</summary>
        /// <param name="text">The text to show on the tab</param>
        /// <returns>The padded text</returns>
        public static string WithPad(string text)
        {
            if (text == null || pad.Length == 0 || text.StartsWith(pad, StringComparison.Ordinal))
                return text;
            return pad + text;
        }

        /// <summary>The real icon of each tab, while its Icon property holds the placeholder</summary>
        private static readonly ConditionalWeakTable<DockContent, Icon> icons = new ConditionalWeakTable<DockContent, Icon>();

        /// <summary>Each icon as a bitmap, made once, for drawing</summary>
        private static readonly ConditionalWeakTable<Icon, Bitmap> bitmaps = new ConditionalWeakTable<Icon, Bitmap>();

        /// <summary>The strips already hooked</summary>
        private static readonly HashSet<DockPaneStripBase> hookedStrips = new HashSet<DockPaneStripBase>();

        /// <summary>A transparent 16x16 icon, so DockPanelSuite reserves the icon space but draws nothing in it</summary>
        private static readonly Icon placeholder = MakePlaceholder();

        /// <summary>Make the transparent placeholder icon</summary>
        /// <returns>The icon; kept for the life of the program</returns>
        private static Icon MakePlaceholder()
        {
            using (Bitmap blank = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                return Icon.FromHandle(blank.GetHicon());
            }
        }

        /// <summary>Set a tab's icon. Use this instead of the tab's Icon property.</summary>
        /// <param name="content">The tab</param>
        /// <param name="icon">The icon; null for none</param>
        public static void Set(DockContent content, Icon icon)
        {
            icons.Remove(content);
            if (icon == null)
            {
                content.Icon = null;
            }
            else
            {
                icons.Add(content, icon);
                content.Icon = placeholder;
                // DockPanelSuite shows the window text when there's no tab text
                content.TabText = WithPad(content.TabText ?? content.Text);
            }
            if (content.Pane != null)
            {
                content.Pane.Invalidate(true);
            }
        }

        /// <summary>A tab's icon as set with <see cref="Set"/>, or its own Icon if it wasn't</summary>
        /// <param name="content">The tab</param>
        /// <returns>The icon, or null</returns>
        public static Icon Get(IDockContent content)
        {
            DockContent dockContent = content as DockContent;
            if (dockContent == null)
                return null;
            Icon icon;
            return icons.TryGetValue(dockContent, out icon) ? icon : (dockContent.Icon == placeholder ? null : dockContent.Icon);
        }

        /// <summary>
        /// Start drawing the icons on a pane's tab strip; does nothing if it's already hooked or isn't a document strip
        /// </summary>
        /// <param name="pane">The pane</param>
        /// <param name="strip">Its tab strip</param>
        public static void Hook(DockPane pane, DockPaneStripBase strip)
        {
            if (pane.Appearance != DockPane.AppearanceStyle.Document || !hookedStrips.Add(strip))
                return;
            StripPainter painter = new StripPainter(strip);
            if (strip.IsHandleCreated)
            {
                painter.AssignHandle(strip.Handle);
            }
            strip.HandleCreated += (s, e) => painter.AssignHandle(strip.Handle);
            strip.HandleDestroyed += (s, e) => painter.ReleaseHandle();
            strip.Disposed += (s, e) => hookedStrips.Remove(strip);
            strip.Invalidate();
        }

        /// <summary>A tab's icon as a bitmap, for drawing</summary>
        /// <param name="icon">The icon</param>
        /// <returns>The bitmap, made on first use</returns>
        public static Bitmap BitmapOf(Icon icon)
        {
            return bitmaps.GetValue(icon, i => i.ToBitmap());
        }

        /// <summary>Draw the real icons over a strip's tabs, after the strip has painted</summary>
        /// <param name="strip">The strip</param>
        private static void Paint(DockPaneStripBase strip)
        {
            IEnumerable tabs = typeof(DockPaneStripBase).GetProperty("Tabs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(strip) as IEnumerable;
            if (tabs == null)
                return;
            int slotStart, slot;
            IconSlot(strip, out slotStart, out slot);
            using (Graphics g = strip.CreateGraphics())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int stripe = Math.Max(3, strip.LogicalToDeviceUnits(3));
                foreach (object tab in tabs)
                {
                    Type type = tab.GetType();
                    // tabs scrolled out of view have no width and aren't drawn
                    object width = type.GetProperty("TabWidth")?.GetValue(tab);
                    Rectangle? bounds = type.GetProperty("Rectangle")?.GetValue(tab) as Rectangle?;
                    IDockContent content = type.GetProperty("Content")?.GetValue(tab) as IDockContent;
                    if (bounds == null || (width is int && (int)width == 0))
                        continue;

                    // the environment colour (e.g. red for production) as a stripe along the tab's top edge
                    Color? environment = EnvironmentColors.For((content as ctlPuttyPanel)?.Session);
                    if (environment != null)
                    {
                        using (SolidBrush brush = new SolidBrush(environment.Value))
                        {
                            g.FillRectangle(brush, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, stripe);
                        }
                    }
                    Icon icon = Get(content);
                    if (icon == null)
                        continue;

                    Rectangle rect = bounds.Value;
                    int size = Math.Min(Math.Min(iconSize, slot), rect.Height - 2);
                    int x = rect.X + slotStart + (slot - size) / 2;
                    int y = rect.Y + (rect.Height - size) / 2;
                    g.DrawImage(BitmapOf(icon), new Rectangle(x, y, size, size));
                }
            }
        }

        /// <summary>Listens to a strip's messages and draws the icons after each paint</summary>
        private class StripPainter : NativeWindow
        {
            /// <summary>The strip</summary>
            private readonly DockPaneStripBase strip;

            /// <summary>Listen to a strip</summary>
            /// <param name="strip">The strip</param>
            public StripPainter(DockPaneStripBase strip)
            {
                this.strip = strip;
            }

            /// <summary>Let the strip paint, then draw the icons on top</summary>
            /// <param name="m">The message</param>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WmPaint)
                {
                    try
                    {
                        TabIcons.Paint(this.strip);
                    }
                    catch (Exception ex)
                    {
                        // a failed icon must never break painting the tabs
                        Log.Debug("Could not draw tab icons", ex);
                    }
                }
            }
        }
    }
}
