using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using WeifenLuo.WinFormsUI.ThemeVS2013;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Draws a thin line down the middle of the splitters beside docked windows (the Sessions pane's edge, say),
    /// lighter under the pointer, so it's clear where to grab. Without it the splitter is a strip of the background
    /// colour, and the line people aim at is the pane's own border, just outside it: there the cursor flips between the
    /// resize arrow and the normal pointer with every small movement. frmSuperPutty calls <see cref="Apply"/> on the
    /// theme before giving it to the DockPanel. Only the VS2015 themes' splitters (VS2013WindowSplitterControl) get the
    /// line; the Classic theme's are left as they are.
    /// </summary>
    public static class SplitterGrips
    {
        /// <summary>The VS2015 themes' splitter, with the line painted over it</summary>
        private class GripSplitter : VS2013WindowSplitterControl
        {
            /// <summary>Whether the interface is dark, for the line's colours</summary>
            private readonly bool dark;

            /// <summary>Whether the pointer is over the splitter: the line is lighter</summary>
            private bool hover;

            /// <summary>Make the splitter for a docked window</summary>
            /// <param name="host">The docked window it belongs to</param>
            /// <param name="dark">Whether the interface is dark</param>
            public GripSplitter(ISplitterHost host, bool dark) : base(host)
            {
                this.dark = dark;
            }

            /// <summary>Paint the theme's splitter, then the line along its middle</summary>
            /// <param name="e">Where to paint</param>
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Color color = this.hover
                    ? (this.dark ? Color.FromArgb(0x8A, 0x8A, 0x8A) : Color.FromArgb(0x70, 0x70, 0x70))
                    : (this.dark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xC8, 0xC8, 0xC8));
                int thickness = Math.Max(2, this.LogicalToDeviceUnits(2));
                Rectangle line = this.Height >= this.Width
                    ? new Rectangle((this.Width - thickness) / 2, 0, thickness, this.Height)
                    : new Rectangle(0, (this.Height - thickness) / 2, this.Width, thickness);
                using (SolidBrush brush = new SolidBrush(color))
                {
                    e.Graphics.FillRectangle(brush, line);
                }
            }

            /// <summary>Light the line while the pointer is over it</summary>
            /// <param name="e">Not used</param>
            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                this.hover = true;
                this.Invalidate();
            }

            /// <summary>Dim the line again</summary>
            /// <param name="e">Not used</param>
            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                this.hover = false;
                this.Invalidate();
            }
        }

        /// <summary>Makes <see cref="GripSplitter"/>s in place of the VS2015 themes' splitters</summary>
        private class Factory : DockPanelExtender.IWindowSplitterControlFactory
        {
            /// <summary>Whether the interface is dark, for the line's colours</summary>
            private readonly bool dark;

            /// <summary>Make the factory</summary>
            /// <param name="dark">Whether the interface is dark</param>
            public Factory(bool dark)
            {
                this.dark = dark;
            }

            /// <summary>Make a splitter with the line</summary>
            /// <param name="host">The docked window the splitter belongs to</param>
            /// <returns>The splitter</returns>
            public SplitterBase CreateSplitterControl(ISplitterHost host)
            {
                return new GripSplitter(host, this.dark);
            }
        }

        /// <summary>
        /// Give a VS2015 theme's splitters beside docked windows the line; call before the theme is set. Other themes,
        /// whose splitters are a different control, are left as they are.
        /// </summary>
        /// <param name="theme">The DockPanel's theme</param>
        /// <param name="dark">Whether the interface is dark</param>
        public static void Apply(ThemeBase theme, bool dark)
        {
            // the theme's factory is internal to DockPanelSuite, so it's recognised by name
            if (theme.Extender.WindowSplitterControlFactory?.GetType().Name == "VS2013WindowSplitterControlFactory")
            {
                theme.Extender.WindowSplitterControlFactory = new Factory(dark);
            }
        }
    }
}
