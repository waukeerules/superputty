using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
    /// are hooked by DocumentTabs.MarkCurrentTabInLists. The same paint also shows each tab's environment colour
    /// (EnvironmentColors): the selected tab filled with it, the others tinted and striped; and a watched tab with new
    /// output pulses in that colour, or the theme's accent (<see cref="SetPulse"/>, as SuperXTerminal's e98ae20).
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

        /// <summary>The tab font, for redrawing the text of a tab filled with its environment colour</summary>
        private static Font tabFont;

        /// <summary>How strongly an unselected tab is tinted with its environment colour, out of 255</summary>
        private const int TintAlpha = 70;

        /// <summary>How much of the environment colour a hovered unselected tab gets: more than the tint, less than selected</summary>
        private const float HoverMix = 0.55f;

        /// <summary>The tabs pulsing for new output (ctlPuttyPanel's Watch for Activity)</summary>
        private static readonly HashSet<DockContent> pulsing = new HashSet<DockContent>();

        /// <summary>Repaints the strips with a pulsing tab about 25 times a second, while there are any</summary>
        private static Timer pulseTimer;

        /// <summary>When the pulse started, so every pulsing tab is in step</summary>
        private static readonly DateTime pulseStart = DateTime.Now;

        /// <summary>How long one pulse takes, from the usual tint to nearly the full colour and back</summary>
        private const double PulsePeriodMs = 1600;

        /// <summary>How strong the pulse is held when Windows' animations are off: the colour without the movement</summary>
        private const float HeldPulse = 0.6f;

        /// <summary>
        /// Start or stop a tab pulsing for new output (ctlPuttyPanel.SetOutputAlert), in place of the orange dot it
        /// used to get: easy to miss among many tabs. It pulses until it's looked at.
        /// </summary>
        /// <param name="content">The tab</param>
        /// <param name="on">true to pulse</param>
        public static void SetPulse(DockContent content, bool on)
        {
            bool changed = on ? pulsing.Add(content) : pulsing.Remove(content);
            if (!changed)
                return;
            if (pulseTimer == null)
            {
                pulseTimer = new Timer { Interval = 40 };
                pulseTimer.Tick += PulseTick;
            }
            // with Windows' animations off (Settings > Accessibility > Visual effects) the colour is held still
            pulseTimer.Enabled = pulsing.Count > 0 && SystemInformation.UIEffectsEnabled;
            Redraw(content);
        }

        /// <summary>
        /// Repaint the tab strip a tab is on, after its state dot or pulse changed; only the strip, not the session
        /// window under it
        /// </summary>
        /// <param name="content">The tab</param>
        public static void Redraw(DockContent content)
        {
            DockPane pane = content?.Pane;
            if (pane == null)
                return;
            foreach (KeyValuePair<DockPaneStripBase, DockPane> hooked in hookedPanes)
            {
                if (hooked.Value == pane)
                {
                    hooked.Key.Invalidate();
                    return;
                }
            }
            pane.Invalidate(true);
        }

        /// <summary>Whether a tab is pulsing for new output</summary>
        /// <param name="content">The tab</param>
        /// <returns>true while it pulses</returns>
        public static bool IsPulsing(IDockContent content)
        {
            DockContent dockContent = content as DockContent;
            return dockContent != null && pulsing.Contains(dockContent);
        }

        /// <summary>
        /// How far into its colour a pulsing tab is now: 0 at its usual look, 1 at the full colour, easing in and out
        /// over <see cref="PulsePeriodMs"/>; held at <see cref="HeldPulse"/> when not animated
        /// </summary>
        /// <param name="elapsed">Time since the pulse started</param>
        /// <param name="animate">Whether Windows' animations are on</param>
        /// <returns>From 0 to 1</returns>
        public static float PulseLevel(TimeSpan elapsed, bool animate)
        {
            if (!animate)
                return HeldPulse;
            double phase = (elapsed.TotalMilliseconds % PulsePeriodMs) / PulsePeriodMs;
            return (float)((1 - Math.Cos(2 * Math.PI * phase)) / 2);
        }

        /// <summary>
        /// A pulsing tab's fill: from its usual background towards nearly its colour; a tab already filled with its
        /// colour (the selected one) pulses towards white instead, as more of the same wouldn't show
        /// </summary>
        /// <param name="color">The tab's environment colour, or the theme's accent</param>
        /// <param name="background">The tab's usual background</param>
        /// <param name="selected">Whether it's the pane's selected tab, already filled with its colour</param>
        /// <param name="level">From <see cref="PulseLevel"/></param>
        /// <returns>The colour to fill the tab with</returns>
        public static Color PulseColour(Color color, Color background, bool selected, float level)
        {
            return selected ? Blend(Color.White, color, 0.45f * level) : Blend(color, background, 0.15f + 0.7f * level);
        }

        /// <summary>
        /// Move the pulse on: repaint each strip that has a pulsing tab. The strips paint double-buffered, the pulse
        /// included (<see cref="PaintOver"/>), so each frame shows whole and nothing flickers
        /// </summary>
        /// <param name="sender">The timer</param>
        /// <param name="e">Not used</param>
        private static void PulseTick(object sender, EventArgs e)
        {
            pulsing.RemoveWhere(c => c.IsDisposed);
            if (pulsing.Count == 0)
            {
                pulseTimer.Stop();
                return;
            }
            foreach (KeyValuePair<DockPaneStripBase, DockPane> hooked in hookedPanes)
            {
                if (hooked.Key.IsHandleCreated && hooked.Key.Visible && hooked.Value.Contents.Cast<IDockContent>().Any(IsPulsing))
                {
                    hooked.Key.Invalidate();
                }
            }
        }

        /// <summary>
        /// Size the icons for the tab font and work out the padding that makes room for them. frmSuperPutty calls this
        /// when it sets the tab font, before any tab opens.
        /// </summary>
        /// <param name="tabFont">The tab font</param>
        public static void Configure(Font tabFont)
        {
            TabIcons.tabFont = tabFont;
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

        /// <summary>The pane each hooked strip belongs to, for repainting the strip a tab is on</summary>
        private static readonly Dictionary<DockPaneStripBase, DockPane> hookedPanes = new Dictionary<DockPaneStripBase, DockPane>();

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
            hookedPanes[strip] = pane;
            strip.Disposed += (s, e) => hookedPanes.Remove(strip);
            // SuperPuTTY's own copies of the strips call PaintOver from their paint; only others need drawing over after
            if (strip.GetType().Namespace == "SuperPutty.DockPanelSuite")
            {
                strip.Disposed += (s, e) => hookedStrips.Remove(strip);
                strip.Invalidate();
                return;
            }
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
            using (Graphics g = strip.CreateGraphics())
            {
                Draw(strip, g);
            }
        }

        /// <summary>
        /// Draw the icons, environment colours, state dots and pulses as part of a strip's own paint: SuperPuTTY's
        /// copies of DockPanelSuite's strips call this at the end of OnPaint, into their double buffer
        /// </summary>
        /// <param name="strip">The strip</param>
        /// <param name="g">The paint's graphics</param>
        public static void PaintOver(DockPaneStripBase strip, Graphics g)
        {
            if (!hookedStrips.Contains(strip))
                return;
            try
            {
                Draw(strip, g);
            }
            catch (Exception ex)
            {
                // a failed icon must never break painting the tabs
                Log.Debug("Could not draw tab icons", ex);
            }
        }

        /// <summary>Draw the real icons, environment colours, state dots and pulses on a strip's tabs</summary>
        /// <param name="strip">The strip</param>
        /// <param name="g">Where to draw: the strip's own paint, or the screen after it</param>
        private static void Draw(DockPaneStripBase strip, Graphics g)
        {
            IEnumerable tabs = typeof(DockPaneStripBase).GetProperty("Tabs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(strip) as IEnumerable;
            if (tabs == null)
                return;
            int slotStart, slot;
            IconSlot(strip, out slotStart, out slot);
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
                    bool pulse = IsPulsing(content);

                    // the environment colour (e.g. red for production): the selected tab is filled with it, others are
                    // tinted with it and get a stripe along the top edge
                    Color? environment = EnvironmentColors.For((content as ctlPuttyPanel)?.Session);
                    if (pulse)
                    {
                        // new output: the whole tab pulses in its colour, or the theme's accent
                        DockContent dockContent = (DockContent)content;
                        bool selected = dockContent.Pane != null && dockContent.Pane.ActiveContent == content;
                        Color color = environment ?? Accent(dockContent);
                        float level = PulseLevel(DateTime.Now - pulseStart, SystemInformation.UIEffectsEnabled);                        FillSelectedTab(g, strip, dockContent, bounds.Value, PulseColour(color, selected && environment != null ? color : TabBackground(dockContent), selected && environment != null, level));
                        if (environment != null)
                        {
                            using (SolidBrush brush = new SolidBrush(environment.Value))
                            {
                                g.FillRectangle(brush, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, stripe);
                            }
                        }
                    }
                    else if (environment != null)
                    {
                        DockContent dockContent = content as DockContent;
                        bool selected = dockContent?.Pane != null && dockContent.Pane.ActiveContent == content;
                        // the theme's hover colour (blue) would show through the tint, so a hovered tab gets its
                        // own colour, stronger, painted solid
                        bool hovered = !selected && bounds.Value.Contains(strip.PointToClient(Control.MousePosition));
                        if (selected)
                        {
                            FillSelectedTab(g, strip, dockContent, bounds.Value, environment.Value);
                        }
                        else if (hovered)
                        {
                            FillSelectedTab(g, strip, dockContent, bounds.Value, Blend(environment.Value, TabBackground(dockContent), HoverMix));
                            using (SolidBrush brush = new SolidBrush(environment.Value))
                            {
                                g.FillRectangle(brush, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, stripe);
                            }
                        }
                        else
                        {
                            using (SolidBrush tint = new SolidBrush(Color.FromArgb(TintAlpha, environment.Value)))
                            using (SolidBrush brush = new SolidBrush(environment.Value))
                            {
                                g.FillRectangle(tint, bounds.Value);
                                g.FillRectangle(brush, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, stripe);
                            }
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
                    // the session's connection, as the Sessions tree shows it: yellow while connecting, green once up
                    Color? dot = (content as ctlPuttyPanel)?.TabDotColor;
                    if (dot.HasValue)
                    {
                        DrawDot(g, new Rectangle(x, y, size, size), dot.Value, TabBackground(content as DockContent));
                    }
                }
            }
        }

        /// <summary>
        /// A state dot in an icon's bottom-right corner, ringed in the tab's background so it stands off the icon, as
        /// IconLibrary.DrawStateDot draws the tree's
        /// </summary>
        /// <param name="g">The strip's graphics</param>
        /// <param name="icon">Where the icon is</param>
        /// <param name="color">The dot's colour</param>
        /// <param name="ring">The ring's colour</param>
        private static void DrawDot(Graphics g, Rectangle icon, Color color, Color ring)
        {
            float size = Math.Max(5f, icon.Width * 0.42f);
            float edge = Math.Max(1.5f, icon.Width * 0.07f);
            RectangleF bounds = new RectangleF(icon.Right - size, icon.Bottom - size, size, size);
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush ringBrush = new SolidBrush(ring))
            using (SolidBrush fill = new SolidBrush(color))
            {
                g.FillEllipse(ringBrush, RectangleF.Inflate(bounds, edge, edge));
                g.FillEllipse(fill, bounds);
            }
            g.SmoothingMode = smoothing;
        }

        /// <summary>The theme's accent, which a tab without an environment colour pulses in: its selected tab's colour</summary>
        /// <param name="content">A tab's content, for its dock panel's theme</param>
        /// <returns>The colour; Windows' highlight colour if the theme doesn't say</returns>
        private static Color Accent(DockContent content)
        {
            Color? accent = content?.DockPanel?.Theme?.ColorPalette?.TabSelectedActive?.Background;
            return accent.HasValue && accent.Value != Color.Empty ? accent.Value : SystemColors.Highlight;
        }

        /// <summary>The theme's background for unselected tabs, which a hovered tab's colour is mixed with</summary>
        /// <param name="content">A tab's content, for its dock panel's theme</param>
        /// <returns>The colour; a light grey if the theme doesn't say</returns>
        private static Color TabBackground(DockContent content)
        {
            Color? background = content?.DockPanel?.Theme?.ColorPalette?.TabUnselected?.Background;
            return background.HasValue && background.Value != Color.Empty ? background.Value : SystemColors.Control;
        }

        /// <summary>Mix two colours</summary>
        /// <param name="color">The first colour</param>
        /// <param name="with">The second colour</param>
        /// <param name="amount">How much of the first, 0 to 1</param>
        /// <returns>The mixed, opaque colour</returns>
        public static Color Blend(Color color, Color with, float amount)
        {
            return Color.FromArgb(
                (int)Math.Round(color.R * amount + with.R * (1 - amount)),
                (int)Math.Round(color.G * amount + with.G * (1 - amount)),
                (int)Math.Round(color.B * amount + with.B * (1 - amount)));
        }

        /// <summary>
        /// Fill a tab with a colour in place of the theme's (the selected tab's environment colour, or a hovered tab's
        /// stronger tint), then draw its text and close button again on top in white or black, whichever reads better. DockPanelSuite picks tab colours per theme,
        /// not per tab, so this paints over what it drew; the close button keeps working but loses its hover highlight.
        /// The icon is drawn afterwards by <see cref="Paint"/>.
        /// </summary>
        /// <param name="g">The strip's graphics</param>
        /// <param name="strip">The strip</param>
        /// <param name="content">The tab's content</param>
        /// <param name="rect">The tab's rectangle</param>
        /// <param name="color">The environment colour</param>
        private static void FillSelectedTab(Graphics g, DockPaneStripBase strip, DockContent content, Rectangle rect, Color color)
        {
            Color ink = color.GetBrightness() < 0.6f ? Color.White : Color.Black;
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, rect);
            }

            // VS2013/2015 strips put a close button in the selected tab; Classic has one for the whole strip
            Rectangle close = Rectangle.Empty;
            MethodInfo closeRect = strip.GetType().GetMethod("GetCloseButtonRect", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Rectangle) }, null);
            if (closeRect != null)
            {
                close = (Rectangle)closeRect.Invoke(strip, new object[] { rect });
                int inset = Math.Max(3, close.Width / 4);
                using (Pen pen = new Pen(ink, Math.Max(1.5f, strip.LogicalToDeviceUnits(1) * 1.5f)))
                {
                    SmoothingMode smoothing = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
                    g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
                    g.SmoothingMode = smoothing;
                }
            }

            string text = (content.TabText ?? content.Text ?? string.Empty).TrimStart(PadChar);
            int left = rect.X + TextStart + padWidth - (IsClassic(strip) ? 4 : 0);
            int right = close.IsEmpty ? rect.Right - 4 : close.Left - 2;
            if (tabFont != null && right > left)
            {
                TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                PropertyInfo format = strip.GetType().GetProperty("DocumentTextFormat", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (format != null)
                {
                    flags = (TextFormatFlags)format.GetValue(strip) | TextFormatFlags.NoPrefix;
                }
                TextRenderer.DrawText(g, text, tabFont, new Rectangle(left, rect.Y, right - left, rect.Height), ink, flags);
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
