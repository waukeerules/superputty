using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SuperPutty.Gui
{
    /// <summary>
    /// A small rotating-arc "busy" animation, shown on a button-bar button and on a tab while a script runs there
    /// (see ButtonBarStrip.UpdateRunningButtons and ctlPuttyPanel.ShowScriptRunning). The frames are drawn once and
    /// reused, so animating never creates new bitmaps or icon handles. Callers advance it from a UI timer.
    /// </summary>
    public static class Spinner
    {
        /// <summary>Frames in one turn (30 degrees apart)</summary>
        public const int FrameCount = 12;

        /// <summary>How long each frame shows, in milliseconds</summary>
        public const int FrameMs = 100;

        /// <summary>The arc's colour, an accent blue readable on light and dark themes</summary>
        private static readonly Color ArcColor = Color.FromArgb(0x44, 0x93, 0xF8);

        /// <summary>Bitmap frames by size, drawn on first use</summary>
        private static readonly Dictionary<int, Bitmap[]> bitmaps = new Dictionary<int, Bitmap[]>();

        /// <summary>16x16 icon frames for tabs, made on first use and kept for the life of the program</summary>
        private static Icon[] icons;

        /// <summary>The frame to show now, so every spinner turns in step</summary>
        public static int CurrentFrame
        {
            get { return (Environment.TickCount & int.MaxValue) / FrameMs % FrameCount; }
        }

        /// <summary>The current frame as a bitmap of the given size; don't dispose it</summary>
        /// <param name="size">Width and height in pixels</param>
        /// <returns>A shared bitmap</returns>
        public static Image CurrentImage(int size)
        {
            Bitmap[] frames;
            if (!bitmaps.TryGetValue(size, out frames))
            {
                frames = new Bitmap[FrameCount];
                for (int i = 0; i < FrameCount; i++)
                {
                    frames[i] = Draw(size, i * 360f / FrameCount);
                }
                bitmaps[size] = frames;
            }
            return frames[CurrentFrame];
        }

        /// <summary>The current frame as a 16x16 icon, for a tab; don't dispose it</summary>
        /// <returns>A shared icon</returns>
        public static Icon CurrentIcon()
        {
            if (icons == null)
            {
                Icon[] made = new Icon[FrameCount];
                for (int i = 0; i < FrameCount; i++)
                {
                    // on a white badge: the selected tab is drawn in the theme's accent blue, which hides a bare blue arc
                    using (Bitmap frame = Draw(16, i * 360f / FrameCount, true))
                    {
                        // the handle is kept with the icon for the life of the program, so it isn't destroyed
                        made[i] = Icon.FromHandle(frame.GetHicon());
                    }
                }
                icons = made;
            }
            return icons[CurrentFrame];
        }

        /// <summary>Draw one frame: a faint ring with a bright 270-degree arc starting at <paramref name="angle"/></summary>
        /// <param name="size">Width and height in pixels</param>
        /// <param name="angle">Where the arc starts, in degrees clockwise from 3 o'clock</param>
        /// <param name="badge">Draw on a white disc, so it stands out on any background (used for tabs)</param>
        /// <returns>A new bitmap</returns>
        private static Bitmap Draw(int size, float angle, bool badge = false)
        {
            Bitmap bitmap = new Bitmap(size, size);
            // a badge leaves a white rim around the arc, so the arc sits a little further in
            float inset = badge ? size / 8f : 0f;
            float pen = Math.Max(2f, (size - 2 * inset) / 6f);
            RectangleF bounds = new RectangleF(inset + pen / 2 + 0.5f, inset + pen / 2 + 0.5f, size - 2 * inset - pen - 1, size - 2 * inset - pen - 1);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush disc = new SolidBrush(Color.White))
            using (Pen ring = new Pen(Color.FromArgb(badge ? 90 : 60, ArcColor), pen))
            using (Pen arc = new Pen(badge ? ArcColorOnBadge : ArcColor, pen) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (badge)
                {
                    g.FillEllipse(disc, 0, 0, size - 1, size - 1);
                }
                g.DrawEllipse(ring, bounds);
                g.DrawArc(arc, bounds, angle, 270);
            }
            return bitmap;
        }

        /// <summary>A deeper blue for the arc on the white badge, for contrast against white</summary>
        private static readonly Color ArcColorOnBadge = Color.FromArgb(0x1F, 0x6F, 0xEB);
    }
}
