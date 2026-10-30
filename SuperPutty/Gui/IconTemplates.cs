using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Session icons drawn in code instead of loaded from PNGs: network-diagram style symbols (switch, router,
    /// firewall, ...) whose body takes any colour, so one template covers "green switch = layer 2, blue switch =
    /// layer 3" and every other colour code. Being vector drawings, they're sharp at every icon size. IconLibrary
    /// renders them for session keys like "switch#2E7D32"; each template has a default colour for its plain key.
    /// </summary>
    public static class IconTemplates
    {
        /// <summary>Draws one template into the unit square (0..1 on both axes) in the given body colour</summary>
        /// <param name="g">Graphics already scaled so the icon is 1x1</param>
        /// <param name="body">The body colour</param>
        private delegate void Painter(Graphics g, Color body);

        /// <summary>One template: how to draw it, its default colour, and its name for the picker</summary>
        private class Template
        {
            public Painter Paint;
            public Color DefaultColor;
            public string Title;
            public bool General;
        }

        /// <summary>The templates by key, in picker order</summary>
        private static readonly Dictionary<string, Template> templates = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The template keys in picker order</summary>
        private static readonly List<string> order = new List<string>();

        /// <summary>The glyph colour drawn on coloured bodies</summary>
        private static readonly Color Glyph = Color.White;

        static IconTemplates()
        {
            Add("switch", "Switch", Color.FromArgb(0x2E, 0x7D, 0x32), PaintSwitch);
            Add("l3switch", "Layer 3 switch", Color.FromArgb(0x1E, 0x63, 0xB4), PaintL3Switch);
            Add("router", "Router", Color.FromArgb(0x1E, 0x63, 0xB4), PaintRouter);
            Add("voicerouter", "Voice router", Color.FromArgb(0x6A, 0x3D, 0x9A), PaintVoiceRouter);
            Add("nexus", "Data centre switch", Color.FromArgb(0x2E, 0x7D, 0x32), PaintNexus);
            Add("firewall", "Firewall", Color.FromArgb(0xC6, 0x4B, 0x2B), PaintFirewall);
            Add("wireless", "Wireless", Color.FromArgb(0x1E, 0x63, 0xB4), PaintWireless);
            Add("loadbalancer", "Load balancer", Color.FromArgb(0x00, 0x83, 0x8F), PaintLoadBalancer);
            Add("server", "Server", Color.FromArgb(0x60, 0x7D, 0x8B), PaintServer);
            Add("workstation", "Workstation", Color.FromArgb(0x60, 0x7D, 0x8B), PaintWorkstation);
            Add("terminal", "Terminal", Color.FromArgb(0x45, 0x5A, 0x64), PaintTerminal);
            Add("windows", "Windows", Color.FromArgb(0x00, 0x78, 0xD4), PaintWindows);
            Add("database", "Database", Color.FromArgb(0xEF, 0x6C, 0x00), PaintDatabase);
            Add("storage", "Storage", Color.FromArgb(0x60, 0x7D, 0x8B), PaintStorage);
            Add("cloud", "Cloud", Color.FromArgb(0x03, 0x9B, 0xE5), PaintCloud);
            Add("phone", "IP phone", Color.FromArgb(0x60, 0x7D, 0x8B), PaintPhone);
            Add("camera", "Camera", Color.FromArgb(0x60, 0x7D, 0x8B), PaintCamera);
            Add("printer", "Printer", Color.FromArgb(0x60, 0x7D, 0x8B), PaintPrinter);
            Add("folder", "Folder", Color.FromArgb(0xF2, 0xB1, 0x34), PaintFolder);
            Add("flag", "Flag", Color.FromArgb(0xC6, 0x28, 0x28), PaintFlag);

            // general icons, replacing the old Silk PNGs of the same meaning
            AddGeneral("house", "Home", Color.FromArgb(0xC6, 0x4B, 0x2B), PaintHouse);
            AddGeneral("globe", "Globe", Color.FromArgb(0x1E, 0x88, 0xE5), PaintGlobe);
            AddGeneral("map", "Map", Color.FromArgb(0x43, 0xA0, 0x47), PaintMap);
            AddGeneral("truck", "Truck", Color.FromArgb(0xEF, 0x6C, 0x00), PaintTruck);
            AddGeneral("book", "Book", Color.FromArgb(0x1E, 0x63, 0xB4), PaintBook);
            AddGeneral("document", "Document", Color.FromArgb(0x1E, 0x63, 0xB4), PaintDocument);
            AddGeneral("archive", "Archive", Color.FromArgb(0xA1, 0x88, 0x7F), PaintArchive);
            AddGeneral("email", "Email", Color.FromArgb(0x1E, 0x63, 0xB4), PaintEmail);
            AddGeneral("chat", "Chat", Color.FromArgb(0x1E, 0x88, 0xE5), PaintChat);
            AddGeneral("rss", "Feed", Color.FromArgb(0xF5, 0x7C, 0x00), PaintRss);
            AddGeneral("clock", "Clock", Color.FromArgb(0x1E, 0x63, 0xB4), PaintClock);
            AddGeneral("help", "Help", Color.FromArgb(0x1E, 0x63, 0xB4), PaintHelp);
            AddGeneral("info", "Information", Color.FromArgb(0x1E, 0x63, 0xB4), PaintInfo);
            AddGeneral("lightbulb", "Light bulb", Color.FromArgb(0xF9, 0xA8, 0x25), PaintLightbulb);
            AddGeneral("bolt", "Lightning", Color.FromArgb(0xF9, 0xA8, 0x25), PaintBolt);
            AddGeneral("tv", "Television", Color.FromArgb(0x60, 0x7D, 0x8B), PaintTv);
            AddGeneral("mobile", "Mobile device", Color.FromArgb(0x60, 0x7D, 0x8B), PaintMobile);

            // toolbar glyphs: drawn the same way so they match and scale with the icons, but not offered for sessions
            AddGlyph("check", Color.FromArgb(0x2E, 0x9E, 0x44), PaintCheck);
            AddGlyph("clear", Color.FromArgb(0x78, 0x86, 0x8F), PaintClear);
            AddGlyph("mask", Color.FromArgb(0x78, 0x86, 0x8F), PaintMask);
            AddGlyph("stop", Color.FromArgb(0xD3, 0x2F, 0x2F), PaintStop);
            AddGlyph("script", Color.FromArgb(0x1E, 0x63, 0xB4), PaintScript);
            AddGlyph("selecttabs", Color.FromArgb(0x1E, 0x63, 0xB4), PaintSelectTabs);
        }

        /// <summary>Register a template</summary>
        /// <param name="key">Its key, used in session icon keys</param>
        /// <param name="title">Its name in the picker</param>
        /// <param name="color">The colour for its plain key</param>
        /// <param name="paint">Its drawing</param>
        private static void Add(string key, string title, Color color, Painter paint)
        {
            templates[key] = new Template { Paint = paint, DefaultColor = color, Title = title };
            order.Add(key);
        }

        /// <summary>Register a general icon: a template listed under "General" rather than "Devices" in the picker</summary>
        /// <param name="key">Its key</param>
        /// <param name="title">Its name in the picker</param>
        /// <param name="color">The colour for its plain key</param>
        /// <param name="paint">Its drawing</param>
        private static void AddGeneral(string key, string title, Color color, Painter paint)
        {
            Add(key, title, color, paint);
            templates[key].General = true;
        }

        /// <summary>Whether a template is a general icon rather than a device</summary>
        /// <param name="key">The template key</param>
        /// <returns>true for general icons</returns>
        public static bool IsGeneral(string key)
        {
            return templates[key].General;
        }

        /// <summary>Register a toolbar glyph: drawable like a template, but not listed in <see cref="Keys"/></summary>
        /// <param name="key">Its key</param>
        /// <param name="color">Its colour</param>
        /// <param name="paint">Its drawing</param>
        private static void AddGlyph(string key, Color color, Painter paint)
        {
            templates[key] = new Template { Paint = paint, DefaultColor = color, Title = key };
        }

        /// <summary>The template keys, in the order the picker shows them</summary>
        public static IList<string> Keys
        {
            get { return order.AsReadOnly(); }
        }

        /// <summary>Whether a template exists</summary>
        /// <param name="key">The template key, e.g. "switch"</param>
        /// <returns>true if it's a template</returns>
        public static bool Exists(string key)
        {
            return key != null && templates.ContainsKey(key);
        }

        /// <summary>A template's name for the picker</summary>
        /// <param name="key">The template key</param>
        /// <returns>Its title</returns>
        public static string TitleOf(string key)
        {
            return templates[key].Title;
        }

        /// <summary>A template's colour when its key has none</summary>
        /// <param name="key">The template key</param>
        /// <returns>The default colour</returns>
        public static Color DefaultColorOf(string key)
        {
            return templates[key].DefaultColor;
        }

        /// <summary>Draw a template at a size and colour</summary>
        /// <param name="key">The template key</param>
        /// <param name="color">The body colour</param>
        /// <param name="size">The width and height in pixels</param>
        /// <returns>A new 32-bit bitmap with a transparent background</returns>
        public static Bitmap Render(string key, Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size, size);
                templates[key].Paint(g, color);
            }
            return bitmap;
        }

        #region Drawing helpers

        /// <summary>A colour made lighter (amount &gt; 0) or darker (amount &lt; 0), keeping its hue</summary>
        /// <param name="color">The colour</param>
        /// <param name="amount">-1 (black) to 1 (white)</param>
        /// <returns>The adjusted colour</returns>
        private static Color Shade(Color color, float amount)
        {
            Func<int, int> mix = c => amount >= 0
                ? (int)(c + (255 - c) * amount)
                : (int)(c * (1 + amount));
            return Color.FromArgb(color.A, mix(color.R), mix(color.G), mix(color.B));
        }

        /// <summary>A rectangle with rounded corners</summary>
        /// <param name="r">The rectangle</param>
        /// <param name="radius">The corner radius</param>
        /// <returns>The outline</returns>
        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Fill a shape as a coloured body: a top-to-bottom gradient around the colour and a darker edge, which keeps
        /// it readable on light and dark backgrounds
        /// </summary>
        /// <param name="g">The graphics</param>
        /// <param name="path">The shape</param>
        /// <param name="body">The body colour</param>
        private static void FillBody(Graphics g, GraphicsPath path, Color body)
        {
            RectangleF bounds = path.GetBounds();
            using (Brush fill = new LinearGradientBrush(new PointF(0, bounds.Top - 0.01f), new PointF(0, bounds.Bottom + 0.01f),
                Shade(body, 0.18f), Shade(body, -0.12f)))
            using (Pen edge = new Pen(Shade(body, -0.35f), 0.045f))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
        }

        /// <summary>A filled arrow from one point to another, with its head at the end</summary>
        /// <param name="g">The graphics</param>
        /// <param name="brush">The fill</param>
        /// <param name="from">The tail</param>
        /// <param name="to">The tip</param>
        /// <param name="shaft">The shaft's thickness</param>
        /// <param name="head">The head's length and half-width</param>
        private static void Arrow(Graphics g, Brush brush, PointF from, PointF to, float shaft, float head)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            float ux = dx / length, uy = dy / length;
            float nx = -uy, ny = ux;
            PointF neck = new PointF(to.X - ux * head, to.Y - uy * head);
            float s = shaft / 2;
            g.FillPolygon(brush, new[]
            {
                new PointF(from.X + nx * s, from.Y + ny * s),
                new PointF(neck.X + nx * s, neck.Y + ny * s),
                new PointF(neck.X + nx * head, neck.Y + ny * head),
                to,
                new PointF(neck.X - nx * head, neck.Y - ny * head),
                new PointF(neck.X - nx * s, neck.Y - ny * s),
                new PointF(from.X - nx * s, from.Y - ny * s)
            });
        }

        /// <summary>Four arrows pointing out from a centre, as on router symbols</summary>
        /// <param name="g">The graphics</param>
        /// <param name="cx">Centre x</param>
        /// <param name="cy">Centre y</param>
        /// <param name="reach">How far the tips reach from the centre</param>
        private static void OutwardCross(Graphics g, float cx, float cy, float reach)
        {
            using (Brush white = new SolidBrush(Glyph))
            {
                float gap = reach * 0.18f;
                Arrow(g, white, new PointF(cx - gap, cy), new PointF(cx - reach, cy), 0.075f, 0.11f);
                Arrow(g, white, new PointF(cx + gap, cy), new PointF(cx + reach, cy), 0.075f, 0.11f);
                Arrow(g, white, new PointF(cx, cy - gap), new PointF(cx, cy - reach), 0.075f, 0.11f);
                Arrow(g, white, new PointF(cx, cy + gap), new PointF(cx, cy + reach), 0.075f, 0.11f);
            }
        }

        #endregion

        #region Templates

        /// <summary>Switch: a rounded square with two rows of opposing arrows</summary>
        private static void PaintSwitch(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.14f))
            {
                FillBody(g, path, body);
            }
            using (Brush white = new SolidBrush(Glyph))
            {
                Arrow(g, white, new PointF(0.24f, 0.34f), new PointF(0.80f, 0.34f), 0.075f, 0.12f);
                Arrow(g, white, new PointF(0.76f, 0.66f), new PointF(0.20f, 0.66f), 0.075f, 0.12f);
            }
        }

        /// <summary>Layer 3 switch: a rounded square with arrows pointing out on all four sides</summary>
        private static void PaintL3Switch(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.14f))
            {
                FillBody(g, path, body);
            }
            OutwardCross(g, 0.5f, 0.5f, 0.34f);
        }

        /// <summary>Router: a disc with arrows pointing out on all four sides</summary>
        private static void PaintRouter(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0.05f, 0.05f, 0.9f, 0.9f);
                FillBody(g, path, body);
            }
            OutwardCross(g, 0.5f, 0.5f, 0.34f);
        }

        /// <summary>Voice router: a disc with a telephone handset</summary>
        private static void PaintVoiceRouter(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0.05f, 0.05f, 0.9f, 0.9f);
                FillBody(g, path, body);
            }
            PaintHandset(g, new RectangleF(0.24f, 0.24f, 0.52f, 0.52f));
        }

        /// <summary>A white telephone handset in a box</summary>
        private static void PaintHandset(Graphics g, RectangleF r)
        {
            using (Pen handle = new Pen(Glyph, r.Width * 0.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Brush white = new SolidBrush(Glyph))
            {
                g.DrawArc(handle, r.Left + r.Width * 0.12f, r.Top + r.Height * 0.12f, r.Width * 0.76f, r.Height * 0.76f, 200, 140);
                g.FillEllipse(white, r.Left, r.Top + r.Height * 0.38f, r.Width * 0.3f, r.Height * 0.26f);
                g.FillEllipse(white, r.Right - r.Width * 0.3f, r.Top + r.Height * 0.38f, r.Width * 0.3f, r.Height * 0.26f);
                g.FillRectangle(white, r.Left + r.Width * 0.2f, r.Top + r.Height * 0.62f, r.Width * 0.6f, r.Height * 0.3f);
            }
        }

        /// <summary>Data centre switch: a box seen from above-front, arrows on the front</summary>
        private static void PaintNexus(Graphics g, Color body)
        {
            PointF[] front = { new PointF(0.06f, 0.36f), new PointF(0.72f, 0.36f), new PointF(0.72f, 0.94f), new PointF(0.06f, 0.94f) };
            PointF[] top = { new PointF(0.06f, 0.36f), new PointF(0.28f, 0.10f), new PointF(0.94f, 0.10f), new PointF(0.72f, 0.36f) };
            PointF[] side = { new PointF(0.72f, 0.36f), new PointF(0.94f, 0.10f), new PointF(0.94f, 0.68f), new PointF(0.72f, 0.94f) };
            using (Brush topFill = new SolidBrush(Shade(body, 0.28f)))
            using (Brush sideFill = new SolidBrush(Shade(body, -0.25f)))
            using (Pen edge = new Pen(Shade(body, -0.4f), 0.04f) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(topFill, top);
                g.FillPolygon(sideFill, side);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddPolygon(front);
                    FillBody(g, path, body);
                }
                g.DrawPolygon(edge, top);
                g.DrawPolygon(edge, side);
            }
            using (Brush white = new SolidBrush(Glyph))
            {
                Arrow(g, white, new PointF(0.16f, 0.53f), new PointF(0.62f, 0.53f), 0.07f, 0.1f);
                Arrow(g, white, new PointF(0.62f, 0.77f), new PointF(0.16f, 0.77f), 0.07f, 0.1f);
            }
        }

        /// <summary>Firewall: a brick wall</summary>
        private static void PaintFirewall(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.12f, 0.88f, 0.76f), 0.06f))
            {
                FillBody(g, path, body);
            }
            using (Pen mortar = new Pen(Shade(body, 0.75f), 0.05f))
            {
                float[] rows = { 0.31f, 0.50f, 0.69f };
                foreach (float y in rows)
                {
                    g.DrawLine(mortar, 0.08f, y, 0.92f, y);
                }
                // joints alternate per course, like bricks
                float[] tops = { 0.14f, 0.31f, 0.50f, 0.69f };
                float[] bottoms = { 0.31f, 0.50f, 0.69f, 0.86f };
                for (int row = 0; row < 4; row++)
                {
                    float[] xs = row % 2 == 0 ? new[] { 0.36f, 0.64f } : new[] { 0.22f, 0.5f, 0.78f };
                    foreach (float x in xs)
                    {
                        g.DrawLine(mortar, x, tops[row], x, bottoms[row]);
                    }
                }
            }
        }

        /// <summary>Wireless: an access point with signal arcs above it</summary>
        private static void PaintWireless(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.12f, 0.66f, 0.76f, 0.26f), 0.08f))
            {
                FillBody(g, path, body);
            }
            using (Pen arc = new Pen(body, 0.085f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(arc, 0.34f, 0.40f, 0.32f, 0.32f, 225, 90);
                g.DrawArc(arc, 0.20f, 0.24f, 0.60f, 0.60f, 225, 90);
                g.DrawArc(arc, 0.06f, 0.08f, 0.88f, 0.88f, 225, 90);
            }
            using (Brush led = new SolidBrush(Glyph))
            {
                g.FillEllipse(led, 0.22f, 0.75f, 0.08f, 0.08f);
                g.FillEllipse(led, 0.34f, 0.75f, 0.08f, 0.08f);
            }
        }

        /// <summary>Load balancer: a rounded square with one arrow splitting into three</summary>
        private static void PaintLoadBalancer(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.14f))
            {
                FillBody(g, path, body);
            }
            using (Brush white = new SolidBrush(Glyph))
            using (Pen line = new Pen(Glyph, 0.075f))
            {
                g.DrawLine(line, 0.18f, 0.5f, 0.42f, 0.5f);
                Arrow(g, white, new PointF(0.42f, 0.5f), new PointF(0.82f, 0.22f), 0.07f, 0.11f);
                Arrow(g, white, new PointF(0.42f, 0.5f), new PointF(0.84f, 0.5f), 0.07f, 0.11f);
                Arrow(g, white, new PointF(0.42f, 0.5f), new PointF(0.82f, 0.78f), 0.07f, 0.11f);
            }
        }

        /// <summary>Server: a tower case with drive bays and a light</summary>
        private static void PaintServer(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.22f, 0.04f, 0.56f, 0.92f), 0.08f))
            {
                FillBody(g, path, body);
            }
            using (Brush bay = new SolidBrush(Shade(body, 0.6f)))
            using (Brush led = new SolidBrush(Color.FromArgb(0x7C, 0xFC, 0x5C)))
            {
                g.FillRectangle(bay, 0.32f, 0.16f, 0.36f, 0.08f);
                g.FillRectangle(bay, 0.32f, 0.30f, 0.36f, 0.08f);
                g.FillRectangle(bay, 0.32f, 0.44f, 0.36f, 0.08f);
                g.FillEllipse(led, 0.58f, 0.78f, 0.09f, 0.09f);
            }
        }

        /// <summary>Workstation: a monitor on a stand</summary>
        private static void PaintWorkstation(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.1f, 0.88f, 0.6f), 0.07f))
            {
                FillBody(g, path, body);
            }
            using (Brush screen = new SolidBrush(Color.FromArgb(0xD6, 0xEC, 0xFA)))
            using (Brush stand = new SolidBrush(Shade(body, -0.2f)))
            {
                g.FillRectangle(screen, 0.13f, 0.17f, 0.74f, 0.45f);
                g.FillRectangle(stand, 0.43f, 0.70f, 0.14f, 0.14f);
                using (GraphicsPath foot = RoundRect(new RectangleF(0.26f, 0.83f, 0.48f, 0.09f), 0.04f))
                {
                    g.FillPath(stand, foot);
                }
            }
        }

        /// <summary>Terminal: a window with a title bar and a prompt</summary>
        private static void PaintTerminal(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.05f, 0.1f, 0.9f, 0.8f), 0.08f))
            {
                FillBody(g, path, body);
            }
            using (Brush titleBar = new SolidBrush(Shade(body, 0.35f)))
            {
                g.FillRectangle(titleBar, 0.08f, 0.13f, 0.84f, 0.12f);
            }
            using (Pen prompt = new Pen(Glyph, 0.075f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(prompt, new[] { new PointF(0.2f, 0.4f), new PointF(0.36f, 0.53f), new PointF(0.2f, 0.66f) });
                g.DrawLine(prompt, 0.44f, 0.7f, 0.66f, 0.7f);
            }
        }

        /// <summary>Windows: four panes</summary>
        private static void PaintWindows(Graphics g, Color body)
        {
            float gap = 0.06f, size = (0.88f - gap) / 2;
            for (int i = 0; i < 4; i++)
            {
                float x = 0.06f + (i % 2) * (size + gap), y = 0.06f + (i / 2) * (size + gap);
                using (GraphicsPath path = RoundRect(new RectangleF(x, y, size, size), 0.03f))
                {
                    FillBody(g, path, body);
                }
            }
        }

        /// <summary>Database: a stacked cylinder</summary>
        private static void PaintDatabase(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(0.14f, 0.06f, 0.72f, 0.24f, 180, 180);
                path.AddLine(0.86f, 0.18f, 0.86f, 0.82f);
                path.AddArc(0.14f, 0.70f, 0.72f, 0.24f, 0, 180);
                path.CloseFigure();
                FillBody(g, path, body);
            }
            using (Pen ring = new Pen(Shade(body, 0.55f), 0.045f))
            using (Brush top = new SolidBrush(Shade(body, 0.3f)))
            {
                g.FillEllipse(top, 0.14f, 0.06f, 0.72f, 0.24f);
                g.DrawArc(ring, 0.14f, 0.30f, 0.72f, 0.24f, 0, 180);
                g.DrawArc(ring, 0.14f, 0.50f, 0.72f, 0.24f, 0, 180);
            }
        }

        /// <summary>Storage: stacked disk shelves</summary>
        private static void PaintStorage(Graphics g, Color body)
        {
            for (int i = 0; i < 3; i++)
            {
                using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.08f + i * 0.29f, 0.88f, 0.24f), 0.05f))
                {
                    FillBody(g, path, body);
                }
                using (Brush led = new SolidBrush(Color.FromArgb(0x7C, 0xFC, 0x5C)))
                using (Brush slot = new SolidBrush(Shade(body, 0.55f)))
                {
                    g.FillRectangle(slot, 0.16f, 0.17f + i * 0.29f, 0.46f, 0.06f);
                    g.FillEllipse(led, 0.74f, 0.16f + i * 0.29f, 0.08f, 0.08f);
                }
            }
        }

        /// <summary>Cloud</summary>
        private static void PaintCloud(Graphics g, Color body)
        {
            // winding fill, so where the circles overlap stays filled
            using (GraphicsPath path = new GraphicsPath(FillMode.Winding))
            {
                path.AddEllipse(0.06f, 0.42f, 0.36f, 0.36f);
                path.AddEllipse(0.24f, 0.2f, 0.46f, 0.46f);
                path.AddEllipse(0.56f, 0.38f, 0.38f, 0.38f);
                path.AddRectangle(new RectangleF(0.24f, 0.52f, 0.52f, 0.26f));
                using (Region outline = new Region(path))
                using (Brush fill = new LinearGradientBrush(new PointF(0, 0.18f), new PointF(0, 0.8f), Shade(body, 0.22f), Shade(body, -0.1f)))
                {
                    // the ellipses overlap, so fill them as one region rather than drawing every outline
                    g.FillRegion(fill, outline);
                }
            }
        }

        /// <summary>IP phone: a desk phone with a handset and keypad</summary>
        private static void PaintPhone(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.08f, 0.3f, 0.84f, 0.62f), 0.1f))
            {
                FillBody(g, path, body);
            }
            using (GraphicsPath handset = RoundRect(new RectangleF(0.08f, 0.1f, 0.84f, 0.16f), 0.08f))
            {
                FillBody(g, handset, body);
            }
            using (Brush key = new SolidBrush(Shade(body, 0.65f)))
            {
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        g.FillRectangle(key, 0.26f + col * 0.18f, 0.42f + row * 0.15f, 0.1f, 0.08f);
                    }
                }
            }
        }

        /// <summary>Camera: a security camera on a mount</summary>
        private static void PaintCamera(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.08f, 0.22f, 0.68f, 0.36f), 0.08f))
            {
                FillBody(g, path, body);
            }
            using (Brush lens = new SolidBrush(Color.FromArgb(0xD6, 0xEC, 0xFA)))
            using (Brush mount = new SolidBrush(Shade(body, -0.2f)))
            {
                g.FillPolygon(mount, new[] { new PointF(0.76f, 0.3f), new PointF(0.94f, 0.2f), new PointF(0.94f, 0.6f), new PointF(0.76f, 0.5f) });
                g.FillEllipse(lens, 0.14f, 0.3f, 0.2f, 0.2f);
                g.FillRectangle(mount, 0.4f, 0.58f, 0.1f, 0.24f);
                g.FillRectangle(mount, 0.26f, 0.8f, 0.38f, 0.08f);
            }
        }

        /// <summary>Printer</summary>
        private static void PaintPrinter(Graphics g, Color body)
        {
            using (Brush paper = new SolidBrush(Color.White))
            using (Pen paperEdge = new Pen(Color.FromArgb(0x90, 0x90, 0x90), 0.03f))
            {
                g.FillRectangle(paper, 0.26f, 0.06f, 0.48f, 0.3f);
                g.DrawRectangle(paperEdge, 0.26f, 0.06f, 0.48f, 0.3f);
                using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.32f, 0.88f, 0.42f), 0.08f))
                {
                    FillBody(g, path, body);
                }
                g.FillRectangle(paper, 0.26f, 0.62f, 0.48f, 0.3f);
                g.DrawRectangle(paperEdge, 0.26f, 0.62f, 0.48f, 0.3f);
            }
        }

        /// <summary>Folder</summary>
        private static void PaintFolder(Graphics g, Color body)
        {
            using (GraphicsPath back = new GraphicsPath())
            {
                back.AddPolygon(new[] { new PointF(0.06f, 0.16f), new PointF(0.38f, 0.16f), new PointF(0.46f, 0.26f), new PointF(0.94f, 0.26f), new PointF(0.94f, 0.84f), new PointF(0.06f, 0.84f) });
                using (Brush fill = new SolidBrush(Shade(body, -0.15f)))
                {
                    g.FillPath(fill, back);
                }
            }
            using (GraphicsPath front = RoundRect(new RectangleF(0.06f, 0.34f, 0.88f, 0.52f), 0.05f))
            {
                FillBody(g, front, body);
            }
        }

        /// <summary>Flag: a pennant on a pole, for colour-coding anything</summary>
        private static void PaintFlag(Graphics g, Color body)
        {
            using (Brush pole = new SolidBrush(Color.FromArgb(0x78, 0x78, 0x78)))
            {
                g.FillRectangle(pole, 0.14f, 0.06f, 0.08f, 0.9f);
            }
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(0.22f, 0.1f, 0.42f, 0.02f, 0.62f, 0.2f, 0.88f, 0.1f);
                path.AddLine(0.88f, 0.1f, 0.88f, 0.56f);
                path.AddBezier(0.88f, 0.56f, 0.62f, 0.66f, 0.42f, 0.48f, 0.22f, 0.56f);
                path.CloseFigure();
                FillBody(g, path, body);
            }
        }

        #endregion

        #region General icons

        /// <summary>White text centred in a box, drawn as a path so it scales with the icon</summary>
        private static void CentredText(Graphics g, string text, RectangleF box, float height, Color color, FontStyle style = FontStyle.Bold)
        {
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (Brush brush = new SolidBrush(color))
            {
                path.AddString(text, new FontFamily("Segoe UI"), (int)style, height, box, format);
                g.FillPath(brush, path);
            }
        }

        /// <summary>Home: a house with a roof, door and window</summary>
        private static void PaintHouse(Graphics g, Color body)
        {
            using (GraphicsPath walls = RoundRect(new RectangleF(0.18f, 0.44f, 0.64f, 0.5f), 0.04f))
            using (Brush wallFill = new SolidBrush(Color.FromArgb(0xEC, 0xEF, 0xF1)))
            using (Pen wallEdge = new Pen(Color.FromArgb(0x90, 0xA4, 0xAE), 0.04f))
            {
                g.FillPath(wallFill, walls);
                g.DrawPath(wallEdge, walls);
            }
            using (GraphicsPath roof = new GraphicsPath())
            {
                roof.AddPolygon(new[] { new PointF(0.5f, 0.06f), new PointF(0.96f, 0.5f), new PointF(0.04f, 0.5f) });
                FillBody(g, roof, body);
            }
            using (Brush door = new SolidBrush(Shade(body, -0.25f)))
            {
                g.FillRectangle(door, 0.43f, 0.64f, 0.16f, 0.3f);
            }
        }

        /// <summary>Globe: a sphere with meridians and parallels</summary>
        private static void PaintGlobe(Graphics g, Color body)
        {
            using (GraphicsPath sphere = new GraphicsPath())
            {
                sphere.AddEllipse(0.06f, 0.06f, 0.88f, 0.88f);
                FillBody(g, sphere, body);
            }
            using (Pen line = new Pen(Color.FromArgb(200, Glyph), 0.045f))
            {
                g.DrawEllipse(line, 0.3f, 0.07f, 0.4f, 0.86f);
                g.DrawLine(line, 0.5f, 0.07f, 0.5f, 0.93f);
                g.DrawLine(line, 0.07f, 0.5f, 0.93f, 0.5f);
                g.DrawArc(line, 0.12f, 0.16f, 0.76f, 0.3f, 20, 140);
                g.DrawArc(line, 0.12f, 0.54f, 0.76f, 0.3f, 200, 140);
            }
        }

        /// <summary>Map: a folded map with a route and a pin</summary>
        private static void PaintMap(Graphics g, Color body)
        {
            PointF[][] panels =
            {
                new[] { new PointF(0.06f, 0.16f), new PointF(0.36f, 0.08f), new PointF(0.36f, 0.84f), new PointF(0.06f, 0.92f) },
                new[] { new PointF(0.36f, 0.08f), new PointF(0.64f, 0.16f), new PointF(0.64f, 0.92f), new PointF(0.36f, 0.84f) },
                new[] { new PointF(0.64f, 0.16f), new PointF(0.94f, 0.08f), new PointF(0.94f, 0.84f), new PointF(0.64f, 0.92f) }
            };
            for (int i = 0; i < panels.Length; i++)
            {
                using (GraphicsPath panel = new GraphicsPath())
                {
                    panel.AddPolygon(panels[i]);
                    FillBody(g, panel, i == 1 ? Shade(body, -0.12f) : body);
                }
            }
            using (Pen route = new Pen(Color.White, 0.05f) { DashStyle = DashStyle.Dash })
            {
                g.DrawBezier(route, 0.12f, 0.78f, 0.36f, 0.5f, 0.6f, 0.75f, 0.84f, 0.32f);
            }
            using (Brush pin = new SolidBrush(Color.FromArgb(0xE5, 0x39, 0x35)))
            {
                g.FillEllipse(pin, 0.74f, 0.2f, 0.18f, 0.18f);
            }
        }

        /// <summary>Truck: a lorry with a cab and wheels</summary>
        private static void PaintTruck(Graphics g, Color body)
        {
            using (GraphicsPath box = RoundRect(new RectangleF(0.04f, 0.2f, 0.58f, 0.5f), 0.04f))
            {
                FillBody(g, box, body);
            }
            using (GraphicsPath cab = new GraphicsPath())
            {
                cab.AddPolygon(new[] { new PointF(0.64f, 0.34f), new PointF(0.82f, 0.34f), new PointF(0.96f, 0.52f), new PointF(0.96f, 0.7f), new PointF(0.64f, 0.7f) });
                FillBody(g, cab, Shade(body, -0.15f));
            }
            using (Brush window = new SolidBrush(Color.FromArgb(0xD6, 0xEC, 0xFA)))
            using (Brush tyre = new SolidBrush(Color.FromArgb(0x37, 0x47, 0x4F)))
            using (Brush hub = new SolidBrush(Color.FromArgb(0xCF, 0xD8, 0xDC)))
            {
                g.FillPolygon(window, new[] { new PointF(0.7f, 0.39f), new PointF(0.8f, 0.39f), new PointF(0.89f, 0.51f), new PointF(0.7f, 0.51f) });
                foreach (float x in new[] { 0.14f, 0.7f })
                {
                    g.FillEllipse(tyre, x, 0.62f, 0.2f, 0.2f);
                    g.FillEllipse(hub, x + 0.06f, 0.68f, 0.08f, 0.08f);
                }
            }
        }

        /// <summary>Book: a closed book with pages showing</summary>
        private static void PaintBook(Graphics g, Color body)
        {
            using (Brush pages = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Pen pageEdge = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0), 0.03f))
            {
                g.FillRectangle(pages, 0.22f, 0.12f, 0.64f, 0.82f);
                g.DrawRectangle(pageEdge, 0.22f, 0.12f, 0.64f, 0.82f);
            }
            using (GraphicsPath cover = RoundRect(new RectangleF(0.14f, 0.06f, 0.66f, 0.82f), 0.05f))
            {
                FillBody(g, cover, body);
            }
            using (Brush spine = new SolidBrush(Shade(body, -0.3f)))
            using (Brush label = new SolidBrush(Shade(body, 0.6f)))
            {
                g.FillRectangle(spine, 0.14f, 0.08f, 0.09f, 0.78f);
                g.FillRectangle(label, 0.34f, 0.24f, 0.36f, 0.12f);
            }
        }

        /// <summary>Document: a page with a folded corner and lines of text</summary>
        private static void PaintDocument(Graphics g, Color body)
        {
            PointF[] outline = { new PointF(0.16f, 0.04f), new PointF(0.62f, 0.04f), new PointF(0.86f, 0.28f), new PointF(0.86f, 0.96f), new PointF(0.16f, 0.96f) };
            using (Brush page = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Pen edge = new Pen(Color.FromArgb(0x90, 0x90, 0x90), 0.04f) { LineJoin = LineJoin.Round })
            using (Brush fold = new SolidBrush(Color.FromArgb(0xD0, 0xD0, 0xD0)))
            {
                g.FillPolygon(page, outline);
                g.FillPolygon(fold, new[] { new PointF(0.62f, 0.04f), new PointF(0.86f, 0.28f), new PointF(0.62f, 0.28f) });
                g.DrawPolygon(edge, outline);
            }
            using (Pen text = new Pen(body, 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(text, 0.28f, 0.42f, 0.74f, 0.42f);
                g.DrawLine(text, 0.28f, 0.56f, 0.74f, 0.56f);
                g.DrawLine(text, 0.28f, 0.70f, 0.74f, 0.70f);
                g.DrawLine(text, 0.28f, 0.84f, 0.56f, 0.84f);
            }
        }

        /// <summary>Archive: a box with a lid and a zip down the front</summary>
        private static void PaintArchive(Graphics g, Color body)
        {
            using (GraphicsPath box = RoundRect(new RectangleF(0.1f, 0.3f, 0.8f, 0.62f), 0.05f))
            {
                FillBody(g, box, body);
            }
            using (GraphicsPath lid = RoundRect(new RectangleF(0.04f, 0.1f, 0.92f, 0.22f), 0.05f))
            {
                FillBody(g, lid, Shade(body, 0.12f));
            }
            using (Brush zip = new SolidBrush(Color.FromArgb(0xEC, 0xEF, 0xF1)))
            {
                for (int i = 0; i < 4; i++)
                {
                    g.FillRectangle(zip, i % 2 == 0 ? 0.44f : 0.5f, 0.36f + i * 0.1f, 0.06f, 0.06f);
                }
                g.FillRectangle(zip, 0.42f, 0.76f, 0.16f, 0.1f);
            }
        }

        /// <summary>Email: an envelope</summary>
        private static void PaintEmail(Graphics g, Color body)
        {
            using (GraphicsPath envelope = RoundRect(new RectangleF(0.06f, 0.18f, 0.88f, 0.64f), 0.06f))
            {
                FillBody(g, envelope, body);
            }
            using (Pen flap = new Pen(Glyph, 0.06f) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(flap, new[] { new PointF(0.12f, 0.26f), new PointF(0.5f, 0.56f), new PointF(0.88f, 0.26f) });
            }
        }

        /// <summary>Chat: a speech bubble with dots</summary>
        private static void PaintChat(Graphics g, Color body)
        {
            using (GraphicsPath bubble = RoundRect(new RectangleF(0.06f, 0.1f, 0.88f, 0.6f), 0.16f))
            {
                bubble.AddPolygon(new[] { new PointF(0.24f, 0.62f), new PointF(0.2f, 0.92f), new PointF(0.48f, 0.66f) });
                bubble.FillMode = FillMode.Winding;
                using (Brush fill = new LinearGradientBrush(new PointF(0, 0.08f), new PointF(0, 0.94f), Shade(body, 0.18f), Shade(body, -0.12f)))
                {
                    g.FillPath(fill, bubble);
                }
            }
            using (Brush dot = new SolidBrush(Glyph))
            {
                foreach (float x in new[] { 0.26f, 0.44f, 0.62f })
                {
                    g.FillEllipse(dot, x, 0.34f, 0.12f, 0.12f);
                }
            }
        }

        /// <summary>Feed: the RSS symbol on a rounded square</summary>
        private static void PaintRss(Graphics g, Color body)
        {
            using (GraphicsPath tile = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.16f))
            {
                FillBody(g, tile, body);
            }
            using (Brush dot = new SolidBrush(Glyph))
            using (Pen arc = new Pen(Glyph, 0.1f))
            {
                g.FillEllipse(dot, 0.2f, 0.66f, 0.15f, 0.15f);
                g.DrawArc(arc, -0.08f, 0.38f, 0.66f, 0.66f, 270, 90);
                g.DrawArc(arc, -0.34f, 0.12f, 1.18f, 1.18f, 270, 90);
            }
        }

        /// <summary>Clock: a face with hands inside a coloured rim</summary>
        private static void PaintClock(Graphics g, Color body)
        {
            using (GraphicsPath rim = new GraphicsPath())
            {
                rim.AddEllipse(0.05f, 0.05f, 0.9f, 0.9f);
                FillBody(g, rim, body);
            }
            using (Brush face = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Pen hand = new Pen(Color.FromArgb(0x37, 0x47, 0x4F), 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.FillEllipse(face, 0.16f, 0.16f, 0.68f, 0.68f);
                g.DrawLine(hand, 0.5f, 0.5f, 0.5f, 0.27f);
                g.DrawLine(hand, 0.5f, 0.5f, 0.68f, 0.58f);
            }
        }

        /// <summary>Help: a question mark on a disc</summary>
        private static void PaintHelp(Graphics g, Color body)
        {
            Disc(g, body);
            CentredText(g, "?", new RectangleF(0.1f, 0.08f, 0.8f, 0.86f), 0.7f, Glyph);
        }

        /// <summary>Information: an i on a disc</summary>
        private static void PaintInfo(Graphics g, Color body)
        {
            Disc(g, body);
            CentredText(g, "i", new RectangleF(0.1f, 0.08f, 0.8f, 0.86f), 0.7f, Glyph);
        }

        /// <summary>Light bulb: a glowing bulb on a screw base</summary>
        private static void PaintLightbulb(Graphics g, Color body)
        {
            using (GraphicsPath bulb = new GraphicsPath())
            {
                bulb.AddArc(0.18f, 0.04f, 0.64f, 0.64f, 140, 260);
                bulb.AddLine(0.66f, 0.62f, 0.62f, 0.74f);
                bulb.AddLine(0.38f, 0.74f, 0.34f, 0.62f);
                bulb.CloseFigure();
                FillBody(g, bulb, body);
            }
            using (Brush shine = new SolidBrush(Color.FromArgb(150, Color.White)))
            using (Brush screwBase = new SolidBrush(Color.FromArgb(0x78, 0x90, 0x9C)))
            using (Pen thread = new Pen(Color.FromArgb(0xB0, 0xBE, 0xC5), 0.035f))
            {
                g.FillEllipse(shine, 0.3f, 0.14f, 0.16f, 0.16f);
                g.FillRectangle(screwBase, 0.36f, 0.76f, 0.28f, 0.16f);
                g.DrawLine(thread, 0.36f, 0.82f, 0.64f, 0.82f);
                g.DrawLine(thread, 0.36f, 0.88f, 0.64f, 0.88f);
            }
        }

        /// <summary>Lightning: a bolt</summary>
        private static void PaintBolt(Graphics g, Color body)
        {
            using (GraphicsPath bolt = new GraphicsPath())
            {
                bolt.AddPolygon(new[]
                {
                    new PointF(0.58f, 0.04f), new PointF(0.18f, 0.56f), new PointF(0.46f, 0.56f),
                    new PointF(0.36f, 0.96f), new PointF(0.82f, 0.4f), new PointF(0.54f, 0.4f)
                });
                FillBody(g, bolt, body);
            }
        }

        /// <summary>Television: a screen on legs with an aerial</summary>
        private static void PaintTv(Graphics g, Color body)
        {
            using (Pen aerial = new Pen(Shade(body, -0.2f), 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(aerial, 0.5f, 0.24f, 0.32f, 0.06f);
                g.DrawLine(aerial, 0.5f, 0.24f, 0.7f, 0.06f);
                g.DrawLine(aerial, 0.26f, 0.84f, 0.2f, 0.94f);
                g.DrawLine(aerial, 0.74f, 0.84f, 0.8f, 0.94f);
            }
            using (GraphicsPath set = RoundRect(new RectangleF(0.06f, 0.22f, 0.88f, 0.64f), 0.08f))
            {
                FillBody(g, set, body);
            }
            using (Brush screen = new SolidBrush(Color.FromArgb(0x81, 0xD4, 0xFA)))
            {
                g.FillRectangle(screen, 0.14f, 0.3f, 0.6f, 0.48f);
            }
            using (Brush knob = new SolidBrush(Shade(body, 0.6f)))
            {
                g.FillEllipse(knob, 0.79f, 0.36f, 0.08f, 0.08f);
                g.FillEllipse(knob, 0.79f, 0.52f, 0.08f, 0.08f);
            }
        }

        /// <summary>Mobile device: a phone or handheld with a screen and button</summary>
        private static void PaintMobile(Graphics g, Color body)
        {
            using (GraphicsPath device = RoundRect(new RectangleF(0.24f, 0.04f, 0.52f, 0.92f), 0.1f))
            {
                FillBody(g, device, body);
            }
            using (Brush screen = new SolidBrush(Color.FromArgb(0xD6, 0xEC, 0xFA)))
            using (Brush button = new SolidBrush(Shade(body, 0.55f)))
            {
                g.FillRectangle(screen, 0.31f, 0.13f, 0.38f, 0.6f);
                g.FillEllipse(button, 0.44f, 0.79f, 0.12f, 0.1f);
            }
        }

        #endregion

        #region Toolbar glyphs

        /// <summary>A coloured disc, the base of most toolbar glyphs</summary>
        private static void Disc(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0.06f, 0.06f, 0.88f, 0.88f);
                FillBody(g, path, body);
            }
        }

        /// <summary>Connect / send: a white tick on a disc</summary>
        private static void PaintCheck(Graphics g, Color body)
        {
            Disc(g, body);
            using (Pen tick = new Pen(Glyph, 0.12f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(tick, new[] { new PointF(0.28f, 0.52f), new PointF(0.44f, 0.67f), new PointF(0.73f, 0.35f) });
            }
        }

        /// <summary>Clear: a white cross on a disc</summary>
        private static void PaintClear(Graphics g, Color body)
        {
            Disc(g, body);
            using (Pen cross = new Pen(Glyph, 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(cross, 0.34f, 0.34f, 0.66f, 0.66f);
                g.DrawLine(cross, 0.66f, 0.34f, 0.34f, 0.66f);
            }
        }

        /// <summary>Mask text: a white asterisk on a disc</summary>
        private static void PaintMask(Graphics g, Color body)
        {
            Disc(g, body);
            using (Pen spoke = new Pen(Glyph, 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < 3; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 3;
                    float dx = (float)Math.Cos(a) * 0.22f, dy = (float)Math.Sin(a) * 0.22f;
                    g.DrawLine(spoke, 0.5f - dx, 0.5f - dy, 0.5f + dx, 0.5f + dy);
                }
            }
        }

        /// <summary>Stop / no filter: a white bar on a disc</summary>
        private static void PaintStop(Graphics g, Color body)
        {
            Disc(g, body);
            using (Brush bar = new SolidBrush(Glyph))
            {
                g.FillRectangle(bar, 0.26f, 0.44f, 0.48f, 0.12f);
            }
        }

        /// <summary>Run script: a page with a code bracket</summary>
        private static void PaintScript(Graphics g, Color body)
        {
            using (Brush page = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Pen edge = new Pen(Color.FromArgb(0x8A, 0x8A, 0x8A), 0.045f))
            {
                PointF[] outline = { new PointF(0.16f, 0.06f), new PointF(0.62f, 0.06f), new PointF(0.84f, 0.28f), new PointF(0.84f, 0.94f), new PointF(0.16f, 0.94f) };
                g.FillPolygon(page, outline);
                g.DrawPolygon(edge, outline);
            }
            using (Pen code = new Pen(body, 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(code, new[] { new PointF(0.40f, 0.42f), new PointF(0.28f, 0.58f), new PointF(0.40f, 0.74f) });
                g.DrawLines(code, new[] { new PointF(0.60f, 0.42f), new PointF(0.72f, 0.58f), new PointF(0.60f, 0.74f) });
            }
        }

        /// <summary>Select tabs: a window with a pencil</summary>
        private static void PaintSelectTabs(Graphics g, Color body)
        {
            using (Brush window = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Pen edge = new Pen(Shade(body, -0.2f), 0.045f))
            using (Brush titleBar = new SolidBrush(body))
            {
                g.FillRectangle(window, 0.06f, 0.1f, 0.78f, 0.7f);
                g.FillRectangle(titleBar, 0.06f, 0.1f, 0.78f, 0.16f);
                g.DrawRectangle(edge, 0.06f, 0.1f, 0.78f, 0.7f);
            }
            using (Pen pencil = new Pen(Color.FromArgb(0xF2, 0x9C, 0x1F), 0.14f) { StartCap = LineCap.Triangle })
            {
                g.DrawLine(pencil, 0.9f, 0.42f, 0.5f, 0.86f);
            }
        }

        #endregion
    }
}
