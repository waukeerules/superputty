using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Makes menu check marks readable on dark menus. ToolStripProfessionalRenderer, which both DarkModeCS's renderer and
    /// DockPanelSuite's Visual Studio renderer build on, draws Windows' black check glyph, which nearly vanishes on a
    /// dark check box. After it draws, this paints a light check over it, only when the check box behind it is dark, so
    /// light themes are untouched. Used for each tab's menu (ctlPuttyPanel) and the tab-list menus (DocumentTabs).
    /// </summary>
    public static class MenuCheckMarks
    {
        /// <summary>The renderers already given the handler, so each draws the mark once</summary>
        private static readonly HashSet<ToolStripRenderer> hookedRenderers = new HashSet<ToolStripRenderer>();

        /// <summary>
        /// Keep a menu's check marks readable. Hooks whichever renderer the menu has each time it opens, since themes
        /// may set or replace the renderer after the menu is created.
        /// </summary>
        /// <param name="menu">The menu</param>
        public static void Apply(ContextMenuStrip menu)
        {
            menu.Opening += (sender, e) => Hook(menu.Renderer);
        }

        /// <summary>Add the light check mark to a renderer, once</summary>
        /// <param name="renderer">The renderer drawing a menu</param>
        private static void Hook(ToolStripRenderer renderer)
        {
            if (renderer != null && hookedRenderers.Add(renderer))
            {
                renderer.RenderItemCheck += DrawLightCheck;
            }
        }

        /// <summary>
        /// Paint over the black glyph with a light tick, when the check box is dark. Runs after the renderer has drawn
        /// the box and the glyph (ToolStripRenderer raises RenderItemCheck at the end of OnRenderItemCheck).
        /// </summary>
        /// <param name="sender">The renderer</param>
        /// <param name="e">The item and where its check goes</param>
        private static void DrawLightCheck(object sender, ToolStripItemImageRenderEventArgs e)
        {
            ToolStripProfessionalRenderer professional = sender as ToolStripProfessionalRenderer;
            if (professional == null)
                return;
            Color box = e.Item.Selected ? professional.ColorTable.CheckSelectedBackground : professional.ColorTable.CheckBackground;
            if (box.A == 0 || box.GetBrightness() >= 0.5f)
                return;

            Rectangle r = e.ImageRectangle;
            Graphics g = e.Graphics;
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush background = new SolidBrush(box))
            using (Pen tick = new Pen(Color.FromArgb(0xF1, 0xF1, 0xF1), 1.8f))
            {
                g.FillRectangle(background, r);
                g.DrawLines(tick, new[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f)
                });
            }
            g.SmoothingMode = smoothing;
        }
    }
}
