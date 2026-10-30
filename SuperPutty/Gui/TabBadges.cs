using System.Drawing;
using System.Drawing.Drawing2D;

namespace SuperPutty.Gui
{
    /// <summary>
    /// 16x16 tab icons for output alerts (ctlPuttyPanel.CheckOutputAlerts): an orange dot when a watched tab prints
    /// something, and grey pause bars when it goes quiet. Drawn on a white disc, like the tab spinner, so they stand out
    /// on the blue selected tab and on dark or light tabs. Created once and kept for the life of the program.
    /// </summary>
    public static class TabBadges
    {
        private static Icon activity;
        private static Icon silence;

        /// <summary>The "new output" badge: an orange dot</summary>
        public static Icon Activity
        {
            get
            {
                if (activity == null)
                {
                    activity = Make(g =>
                    {
                        using (Brush dot = new SolidBrush(Color.FromArgb(0xF0, 0x88, 0x3E)))
                        {
                            g.FillEllipse(dot, 4, 4, 8, 8);
                        }
                    });
                }
                return activity;
            }
        }

        /// <summary>The "gone quiet" badge: grey pause bars</summary>
        public static Icon Silence
        {
            get
            {
                if (silence == null)
                {
                    silence = Make(g =>
                    {
                        using (Brush bar = new SolidBrush(Color.FromArgb(0x6E, 0x76, 0x81)))
                        {
                            g.FillRectangle(bar, 5, 4, 2, 8);
                            g.FillRectangle(bar, 9, 4, 2, 8);
                        }
                    });
                }
                return silence;
            }
        }

        /// <summary>Draw a white disc, then <paramref name="drawSymbol"/> on it, and turn it into an icon</summary>
        /// <param name="drawSymbol">Draws the symbol in the middle of a 16x16 canvas</param>
        /// <returns>A new icon; its handle is kept for the life of the program</returns>
        private static Icon Make(System.Action<Graphics> drawSymbol)
        {
            using (Bitmap bitmap = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                using (Brush disc = new SolidBrush(Color.White))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(disc, 0, 0, 15, 15);
                    drawSymbol(g);
                }
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
    }
}
