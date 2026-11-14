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
            Add("stackswitch", "Stack switch", Color.FromArgb(0x2E, 0x7D, 0x32), PaintStackSwitch);
            Add("coreswitch", "Core switch", Color.FromArgb(0x1E, 0x63, 0xB4), PaintCoreSwitch);
            Add("distswitch", "Distribution switch", Color.FromArgb(0x2E, 0x7D, 0x32), PaintDistributionSwitch);
            Add("router", "Router", Color.FromArgb(0x1E, 0x63, 0xB4), PaintRouter);
            Add("voicerouter", "Voice router", Color.FromArgb(0x6A, 0x3D, 0x9A), PaintVoiceRouter);
            Add("nexus", "Data centre switch", Color.FromArgb(0x2E, 0x7D, 0x32), PaintNexus);
            Add("firewall", "Firewall", Color.FromArgb(0xC6, 0x4B, 0x2B), PaintFirewall);
            Add("vpn", "VPN", Color.FromArgb(0x6A, 0x3D, 0x9A), PaintVpn);
            Add("wireless", "Wireless", Color.FromArgb(0x1E, 0x63, 0xB4), PaintWireless);
            Add("accesspoint", "Access point", Color.FromArgb(0x1E, 0x63, 0xB4), PaintAccessPoint);
            Add("wlc", "Wireless controller", Color.FromArgb(0x1E, 0x63, 0xB4), PaintWirelessController);
            Add("loadbalancer", "Load balancer", Color.FromArgb(0x00, 0x83, 0x8F), PaintLoadBalancer);
            Add("consoleserver", "Console server", Color.FromArgb(0x45, 0x5A, 0x64), PaintConsoleServer);
            Add("ups", "UPS", Color.FromArgb(0xEF, 0x6C, 0x00), PaintUps);
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
            AddGeneral("star", "Star", Color.FromArgb(0xF9, 0xA8, 0x25), PaintStar);
            AddGeneral("lock", "Padlock", Color.FromArgb(0x60, 0x7D, 0x8B), PaintLock);
            // general icons from SuperXTerminal, for what sessions are often sorted by
            AddGeneral("building", "Building", Color.FromArgb(0x60, 0x7D, 0x8B), PaintBuilding);
            AddGeneral("person", "Person", Color.FromArgb(0x1E, 0x63, 0xB4), PaintPerson);
            AddGeneral("key", "Key", Color.FromArgb(0xF9, 0xA8, 0x25), PaintKey);
            AddGeneral("shield", "Shield", Color.FromArgb(0x2E, 0x7D, 0x32), PaintShield);
            AddGeneral("gear", "Settings", Color.FromArgb(0x60, 0x7D, 0x8B), PaintGear);
            AddGeneral("wrench", "Wrench", Color.FromArgb(0x78, 0x90, 0x9C), PaintWrench);
            AddGeneral("warning", "Warning", Color.FromArgb(0xF9, 0xA8, 0x25), PaintWarning);
            AddGeneral("bell", "Bell", Color.FromArgb(0xF9, 0xA8, 0x25), PaintBell);
            AddGeneral("calendar", "Calendar", Color.FromArgb(0xC6, 0x28, 0x28), PaintCalendar);
            AddGeneral("flask", "Lab", Color.FromArgb(0x8E, 0x24, 0xAA), PaintFlask);
            AddGeneral("bug", "Bug", Color.FromArgb(0x43, 0xA0, 0x47), PaintBug);
            AddGeneral("code", "Code", Color.FromArgb(0x45, 0x5A, 0x64), PaintCode);
            AddGeneral("heart", "Heart", Color.FromArgb(0xD8, 0x1B, 0x60), PaintHeart);
            AddGeneral("tag", "Tag", Color.FromArgb(0x00, 0x83, 0x8F), PaintTag);

            // toolbar glyphs: drawn the same way so they match and scale with the icons, but not offered for sessions
            AddGlyph("check", Color.FromArgb(0x2E, 0x9E, 0x44), PaintCheck);
            AddGlyph("clear", Color.FromArgb(0x78, 0x86, 0x8F), PaintClear);
            AddGlyph("mask", Color.FromArgb(0x78, 0x86, 0x8F), PaintMask);
            AddGlyph("masked", Color.FromArgb(0xE6, 0x8A, 0x00), PaintMask);
            AddGlyph("unmasked", Color.FromArgb(0x78, 0x86, 0x8F), PaintEye);
            AddGlyph("stop", Color.FromArgb(0xD3, 0x2F, 0x2F), PaintStop);
            AddGlyph("script", Color.FromArgb(0x1E, 0x63, 0xB4), PaintScript);
            AddGlyph("selecttabs", Color.FromArgb(0x1E, 0x63, 0xB4), PaintSelectTabs);
            AddGlyph("search", Color.FromArgb(0x1E, 0x63, 0xB4), PaintSearch);
            AddGlyph("newsession", Color.FromArgb(0x2E, 0x9E, 0x44), PaintNewSession);
            AddGlyph("newfolder", Color.FromArgb(0x2E, 0x9E, 0x44), PaintNewFolder);
            AddGlyph("expandall", Color.FromArgb(0x60, 0x7D, 0x8B), (g, c) => PaintTreeBox(g, c, true));
            AddGlyph("collapseall", Color.FromArgb(0x60, 0x7D, 0x8B), (g, c) => PaintTreeBox(g, c, false));
            AddGlyph("sortaz", Color.FromArgb(0x1E, 0x63, 0xB4), (g, c) => PaintSort(g, c, "A", "Z"));
            AddGlyph("sortza", Color.FromArgb(0x1E, 0x63, 0xB4), (g, c) => PaintSort(g, c, "Z", "A"));
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

        /// <summary>
        /// A box seen from above-front, like the data centre switch: a lighter top, a darker side and the front filled
        /// as a body
        /// </summary>
        /// <param name="g">The graphics</param>
        /// <param name="body">The body colour</param>
        /// <param name="front">The front face</param>
        /// <param name="depth">How far right the back edge sits from the front</param>
        /// <param name="lift">How far up the back edge sits from the front</param>
        private static void IsoBox(Graphics g, Color body, RectangleF front, float depth, float lift)
        {
            PointF[] top = { new PointF(front.Left, front.Top), new PointF(front.Left + depth, front.Top - lift), new PointF(front.Right + depth, front.Top - lift), new PointF(front.Right, front.Top) };
            PointF[] side = { new PointF(front.Right, front.Top), new PointF(front.Right + depth, front.Top - lift), new PointF(front.Right + depth, front.Bottom - lift), new PointF(front.Right, front.Bottom) };
            using (Brush topFill = new SolidBrush(Shade(body, 0.28f)))
            using (Brush sideFill = new SolidBrush(Shade(body, -0.25f)))
            using (Pen edge = new Pen(Shade(body, -0.4f), 0.04f) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(topFill, top);
                g.FillPolygon(sideFill, side);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddRectangle(front);
                    FillBody(g, path, body);
                }
                g.DrawPolygon(edge, top);
                g.DrawPolygon(edge, side);
            }
        }

        /// <summary>The wireless symbol: three arcs over a dot</summary>
        /// <param name="g">The graphics</param>
        /// <param name="color">Its colour</param>
        /// <param name="cx">Centre x</param>
        /// <param name="baseY">The dot's y, where the arcs are centred</param>
        /// <param name="reach">The outer arc's radius</param>
        /// <param name="width">The arcs' thickness</param>
        private static void WirelessArcs(Graphics g, Color color, float cx, float baseY, float reach, float width)
        {
            using (Pen arc = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Brush dot = new SolidBrush(color))
            {
                for (int i = 1; i <= 3; i++)
                {
                    float r = reach * i / 3f;
                    g.DrawArc(arc, cx - r, baseY - r, r * 2, r * 2, 225, 90);
                }
                g.FillEllipse(dot, cx - width * 0.8f, baseY - width * 0.8f, width * 1.6f, width * 1.6f);
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

        /// <summary>Stack switch: two switch boxes, one on the other, with traffic each way</summary>
        private static void PaintStackSwitch(Graphics g, Color body)
        {
            using (Brush white = new SolidBrush(Glyph))
            {
                IsoBox(g, Shade(body, -0.1f), new RectangleF(0.06f, 0.66f, 0.66f, 0.28f), 0.2f, 0.23f);
                Arrow(g, white, new PointF(0.14f, 0.80f), new PointF(0.64f, 0.80f), 0.065f, 0.1f);
                IsoBox(g, body, new RectangleF(0.06f, 0.32f, 0.66f, 0.28f), 0.2f, 0.23f);
                Arrow(g, white, new PointF(0.64f, 0.46f), new PointF(0.14f, 0.46f), 0.065f, 0.1f);
            }
        }

        /// <summary>Core switch: the layer 3 switch's arrows going out from a hub, as the backbone everything crosses</summary>
        private static void PaintCoreSwitch(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.14f))
            {
                FillBody(g, path, body);
            }
            OutwardCross(g, 0.5f, 0.5f, 0.36f);
            using (Brush white = new SolidBrush(Glyph))
            using (Brush hole = new SolidBrush(body))
            {
                g.FillEllipse(white, 0.34f, 0.34f, 0.32f, 0.32f);
                g.FillEllipse(hole, 0.42f, 0.42f, 0.16f, 0.16f);
            }
        }

        /// <summary>Distribution switch: one uplink fanning out to three arrows down to the access layer</summary>
        private static void PaintDistributionSwitch(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.14f))
            {
                FillBody(g, path, body);
            }
            using (Brush white = new SolidBrush(Glyph))
            using (Pen line = new Pen(Glyph, 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(line, 0.5f, 0.16f, 0.5f, 0.44f);
                g.DrawLine(line, 0.22f, 0.44f, 0.78f, 0.44f);
                foreach (float x in new[] { 0.22f, 0.5f, 0.78f })
                {
                    Arrow(g, white, new PointF(x, 0.44f), new PointF(x, 0.84f), 0.07f, 0.11f);
                }
            }
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

        /// <summary>Voice router: a disc with a call symbol, a handset on the diagonal with arrows going each way</summary>
        private static void PaintVoiceRouter(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0.05f, 0.05f, 0.9f, 0.9f);
                FillBody(g, path, body);
            }
            using (Brush white = new SolidBrush(Glyph))
            using (GraphicsPath handset = Handset(new RectangleF(0.22f, 0.31f, 0.52f, 0.49f)))
            {
                g.FillPath(white, handset);
            }
            using (Pen line = new Pen(Glyph, 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(line, 0.45f, 0.25f, 0.66f, 0.25f);
                g.DrawLines(line, new[] { new PointF(0.61f, 0.20f), new PointF(0.66f, 0.25f), new PointF(0.61f, 0.30f) });
                g.DrawLine(line, 0.70f, 0.38f, 0.49f, 0.38f);
                g.DrawLines(line, new[] { new PointF(0.54f, 0.33f), new PointF(0.49f, 0.38f), new PointF(0.54f, 0.43f) });
            }
        }

        /// <summary>
        /// A telephone handset on the diagonal, as on call buttons, as one smooth outline: the earpiece at the top left,
        /// the mouthpiece at the bottom right, and the grip curving between them
        /// </summary>
        /// <param name="r">The box it fills</param>
        /// <returns>The outline</returns>
        private static GraphicsPath Handset(RectangleF r)
        {
            Func<float, float, PointF> p = (x, y) => new PointF(r.Left + r.Width * x, r.Top + r.Height * y);
            GraphicsPath path = new GraphicsPath();
            // the outer edge, from the earpiece's tip round to the mouthpiece's
            path.AddBezier(p(0.08f, 0.03f), p(0.02f, 0.07f), p(-0.02f, 0.22f), p(0.04f, 0.40f));
            path.AddBezier(p(0.04f, 0.40f), p(0.12f, 0.62f), p(0.36f, 0.86f), p(0.62f, 0.97f));
            path.AddBezier(p(0.62f, 0.97f), p(0.72f, 1.01f), p(0.86f, 1.00f), p(0.92f, 0.94f));
            path.AddLine(p(0.92f, 0.94f), p(0.98f, 0.88f));
            path.AddBezier(p(0.98f, 0.88f), p(1.01f, 0.85f), p(1.00f, 0.79f), p(0.96f, 0.76f));
            // the mouthpiece's end, the inner edge of the grip, and the earpiece's end
            path.AddLine(p(0.96f, 0.76f), p(0.80f, 0.65f));
            path.AddBezier(p(0.80f, 0.65f), p(0.76f, 0.62f), p(0.71f, 0.63f), p(0.68f, 0.66f));
            path.AddLine(p(0.68f, 0.66f), p(0.62f, 0.72f));
            path.AddBezier(p(0.62f, 0.72f), p(0.50f, 0.66f), p(0.36f, 0.52f), p(0.29f, 0.39f));
            path.AddLine(p(0.29f, 0.39f), p(0.35f, 0.33f));
            path.AddBezier(p(0.35f, 0.33f), p(0.38f, 0.30f), p(0.38f, 0.25f), p(0.36f, 0.21f));
            path.AddLine(p(0.36f, 0.21f), p(0.25f, 0.05f));
            path.AddBezier(p(0.25f, 0.05f), p(0.22f, 0.01f), p(0.13f, 0.00f), p(0.08f, 0.03f));
            path.CloseFigure();
            return path;
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

        /// <summary>A shield's outline, a flat top and curved sides meeting at a point: the VPN's and the Shield's (General)</summary>
        /// <returns>The outline</returns>
        private static GraphicsPath ShieldPath()
        {
            GraphicsPath path = new GraphicsPath();
            path.AddLine(0.5f, 0.05f, 0.88f, 0.17f);
            path.AddBezier(0.88f, 0.17f, 0.9f, 0.6f, 0.72f, 0.82f, 0.5f, 0.95f);
            path.AddBezier(0.5f, 0.95f, 0.28f, 0.82f, 0.1f, 0.6f, 0.12f, 0.17f);
            path.CloseFigure();
            return path;
        }

        /// <summary>A padlock: a shackle over a body with a keyhole</summary>
        /// <param name="g">The graphics</param>
        /// <param name="cx">Its centre's x</param>
        /// <param name="cy">Its centre's y</param>
        /// <param name="size">Its height</param>
        /// <param name="color">The shackle's and body's colour</param>
        /// <param name="hole">The keyhole's colour</param>
        private static void Padlock(Graphics g, float cx, float cy, float size, Color color, Color hole)
        {
            float w = size * 0.78f, bodyH = size * 0.56f, top = cy - size / 2, sw = w * 0.62f;
            using (Pen shackle = new Pen(color, size * 0.13f))
            {
                g.DrawArc(shackle, cx - sw / 2, top + size * 0.065f, sw, size * 0.62f, 180, 180);
                g.DrawLine(shackle, cx - sw / 2, top + size * 0.37f, cx - sw / 2, top + size * 0.5f);
                g.DrawLine(shackle, cx + sw / 2, top + size * 0.37f, cx + sw / 2, top + size * 0.5f);
            }
            using (GraphicsPath lockBody = RoundRect(new RectangleF(cx - w / 2, cy + size / 2 - bodyH, w, bodyH), size * 0.08f))
            using (Brush fill = new SolidBrush(color))
            using (Brush keyhole = new SolidBrush(hole))
            {
                g.FillPath(fill, lockBody);
                float ky = cy + size / 2 - bodyH * 0.62f;
                g.FillEllipse(keyhole, cx - size * 0.075f, ky - size * 0.075f, size * 0.15f, size * 0.15f);
                g.FillRectangle(keyhole, cx - size * 0.03f, ky, size * 0.06f, size * 0.17f);
            }
        }

        /// <summary>
        /// VPN: a shield with a padlock, as SuperXTerminal draws it. It was a box with two tunnels through it, which
        /// couldn't be told for anything at a tab's 16 pixels. The Shield icon (General) has a tick where this has the lock
        /// </summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintVpn(Graphics g, Color body)
        {
            using (GraphicsPath shield = ShieldPath())
            {
                FillBody(g, shield, body);
            }
            Padlock(g, 0.5f, 0.47f, 0.42f, Glyph, body);
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

        /// <summary>Access point: a flat puck with the signal rising from it</summary>
        private static void PaintAccessPoint(Graphics g, Color body)
        {
            using (GraphicsPath side = new GraphicsPath())
            {
                side.AddArc(0.08f, 0.60f, 0.84f, 0.24f, 180, -180);
                side.AddLine(0.92f, 0.72f, 0.92f, 0.80f);
                side.AddArc(0.08f, 0.68f, 0.84f, 0.24f, 0, 180);
                side.CloseFigure();
                FillBody(g, side, Shade(body, -0.15f));
            }
            using (Brush top = new SolidBrush(Shade(body, 0.2f)))
            using (Pen edge = new Pen(Shade(body, -0.35f), 0.04f))
            {
                g.FillEllipse(top, 0.08f, 0.60f, 0.84f, 0.24f);
                g.DrawEllipse(edge, 0.08f, 0.60f, 0.84f, 0.24f);
            }
            WirelessArcs(g, body, 0.5f, 0.62f, 0.5f, 0.08f);
        }

        /// <summary>
        /// Wireless controller, after the network-diagram symbol: a low box with arrows converging on its top and a
        /// braided wave across its front
        /// </summary>
        private static void PaintWirelessController(Graphics g, Color body)
        {
            RectangleF front = new RectangleF(0.02f, 0.54f, 0.80f, 0.40f);
            IsoBox(g, body, front, 0.16f, 0.36f);
            using (Brush white = new SolidBrush(Glyph))
            {
                // drawn flat in a unit square (across, then front to back) mapped onto the top face, so the arrows
                // lie on it in perspective; drawn straight onto the screen they looked like they were floating
                Matrix saved = g.Transform;
                g.MultiplyTransform(new Matrix(0.80f, 0f, 0.16f, -0.36f, 0.02f, 0.54f));
                Arrow(g, white, new PointF(0.10f, 0.10f), new PointF(0.36f, 0.44f), 0.07f, 0.11f);
                Arrow(g, white, new PointF(0.50f, 0.06f), new PointF(0.50f, 0.44f), 0.07f, 0.11f);
                Arrow(g, white, new PointF(0.90f, 0.10f), new PointF(0.64f, 0.44f), 0.07f, 0.11f);
                Arrow(g, white, new PointF(0.50f, 0.52f), new PointF(0.50f, 0.94f), 0.13f, 0.17f);
                g.Transform = saved;
            }
            float mid = front.Top + front.Height / 2, amplitude = front.Height * 0.28f;
            float left = front.Left + 0.06f, right = front.Right - 0.06f;
            const int steps = 60;
            PointF[] up = new PointF[steps + 1], down = new PointF[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float x = left + (right - left) * i / steps;
                float y = amplitude * (float)Math.Sin(i * Math.PI * 4 / steps);
                up[i] = new PointF(x, mid + y);
                down[i] = new PointF(x, mid - y);
            }
            // each strand gets a dark outline, so the second one drawn appears to pass over the first
            using (Pen outline = new Pen(Shade(body, -0.45f), 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen strand = new Pen(Glyph, 0.065f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLines(outline, down);
                g.DrawLines(strand, down);
                g.DrawLines(outline, up);
                g.DrawLines(strand, up);
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

        /// <summary>Console server: a unit with a prompt above a row of serial ports</summary>
        private static void PaintConsoleServer(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.05f, 0.14f, 0.9f, 0.72f), 0.1f))
            {
                FillBody(g, path, body);
            }
            using (Pen prompt = new Pen(Glyph, 0.075f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(prompt, new[] { new PointF(0.18f, 0.28f), new PointF(0.32f, 0.39f), new PointF(0.18f, 0.50f) });
                g.DrawLine(prompt, 0.40f, 0.52f, 0.60f, 0.52f);
            }
            using (Brush port = new SolidBrush(Shade(body, 0.6f)))
            {
                for (int i = 0; i < 4; i++)
                {
                    g.FillRectangle(port, 0.15f + i * 0.19f, 0.64f, 0.13f, 0.1f);
                }
            }
        }

        /// <summary>UPS: a battery with a lightning bolt</summary>
        private static void PaintUps(Graphics g, Color body)
        {
            using (GraphicsPath path = RoundRect(new RectangleF(0.16f, 0.14f, 0.68f, 0.82f), 0.09f))
            {
                FillBody(g, path, body);
            }
            using (Brush terminal = new SolidBrush(Shade(body, -0.3f)))
            using (Brush white = new SolidBrush(Glyph))
            {
                g.FillRectangle(terminal, 0.36f, 0.04f, 0.28f, 0.1f);
                g.FillPolygon(white, new[]
                {
                    new PointF(0.56f, 0.24f), new PointF(0.30f, 0.60f), new PointF(0.48f, 0.60f),
                    new PointF(0.42f, 0.86f), new PointF(0.70f, 0.48f), new PointF(0.52f, 0.48f)
                });
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

        /// <summary>
        /// Globe: the usual globe sign, as SuperXTerminal draws it: the meridians curving round, the equator, and two
        /// latitudes straight across, as seen from level with the equator. The old pinched arcs for latitudes didn't
        /// read as a globe at a tab's 16 pixels
        /// </summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintGlobe(Graphics g, Color body)
        {
            using (GraphicsPath sphere = new GraphicsPath())
            {
                sphere.AddEllipse(0.06f, 0.06f, 0.88f, 0.88f);
                FillBody(g, sphere, body);
            }
            using (Pen line = new Pen(Color.FromArgb(225, Glyph), 0.05f))
            {
                g.DrawEllipse(line, 0.31f, 0.08f, 0.38f, 0.84f);
                g.DrawLine(line, 0.5f, 0.08f, 0.5f, 0.92f);
                g.DrawLine(line, 0.08f, 0.5f, 0.92f, 0.5f);
                // a latitude 0.2 above and below the equator: the chord of the 0.42 circle there
                float half = (float)Math.Sqrt(0.42 * 0.42 - 0.2 * 0.2);
                g.DrawLine(line, 0.5f - half, 0.3f, 0.5f + half, 0.3f);
                g.DrawLine(line, 0.5f - half, 0.7f, 0.5f + half, 0.7f);
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
                // the tail runs the same way round as the bubble: the other way, the overlap just above the bubble's
                // bottom edge wound to zero and the background showed through a sliver there
                bubble.AddPolygon(new[] { new PointF(0.48f, 0.66f), new PointF(0.2f, 0.92f), new PointF(0.24f, 0.62f) });
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
            // the dot and both arcs round one centre, the outer arc ending well inside the square's edge (it used to
            // run into it)
            const float x = 0.28f, y = 0.72f;
            using (Brush dot = new SolidBrush(Glyph))
            using (Pen arc = new Pen(Glyph, 0.1f))
            {
                g.FillEllipse(dot, x - 0.075f, y - 0.075f, 0.15f, 0.15f);
                foreach (float r in new[] { 0.24f, 0.43f })
                {
                    g.DrawArc(arc, x - r, y - r, r * 2, r * 2, 270, 90);
                }
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

        /// <summary>Star: a five-pointed star, the Sessions tree's Favorites</summary>
        private static void PaintStar(Graphics g, Color body)
        {
            PointF[] points = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                float r = i % 2 == 0 ? 0.47f : 0.2f;
                points[i] = new PointF(0.5f + (float)Math.Cos(a) * r, 0.53f + (float)Math.Sin(a) * r);
            }
            using (GraphicsPath star = new GraphicsPath())
            {
                star.AddPolygon(points);
                FillBody(g, star, body);
            }
        }

        /// <summary>Padlock: a shackle over a coloured body with a keyhole, the Sessions tree's private sessions</summary>
        private static void PaintLock(Graphics g, Color body)
        {
            using (Pen shackle = new Pen(Shade(body, -0.2f), 0.1f))
            {
                g.DrawArc(shackle, 0.29f, 0.1f, 0.42f, 0.5f, 180, 180);
                g.DrawLine(shackle, 0.29f, 0.35f, 0.29f, 0.48f);
                g.DrawLine(shackle, 0.71f, 0.35f, 0.71f, 0.48f);
            }
            using (GraphicsPath lockBody = RoundRect(new RectangleF(0.17f, 0.44f, 0.66f, 0.5f), 0.08f))
            {
                FillBody(g, lockBody, body);
            }
            using (Brush hole = new SolidBrush(Glyph))
            {
                g.FillEllipse(hole, 0.43f, 0.56f, 0.14f, 0.14f);
                g.FillRectangle(hole, 0.47f, 0.64f, 0.06f, 0.16f);
            }
        }

        /// <summary>Fill a path in one colour</summary>
        /// <param name="g">The graphics</param>
        /// <param name="path">The shape; disposed afterwards</param>
        /// <param name="color">The colour</param>
        private static void FillSolid(Graphics g, GraphicsPath path, Color color)
        {
            using (path)
            using (Brush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>Fill a path as a coloured body (<see cref="FillBody"/>), then dispose of it</summary>
        /// <param name="g">The graphics</param>
        /// <param name="path">The shape; disposed afterwards</param>
        /// <param name="body">The body colour</param>
        private static void FillBodyOf(Graphics g, GraphicsPath path, Color body)
        {
            using (path)
            {
                FillBody(g, path, body);
            }
        }

        /// <summary>A closed polygon as a path, filled by GDI+'s default Alternate rule</summary>
        /// <param name="points">The corners, as x, y pairs</param>
        /// <returns>The path</returns>
        private static GraphicsPath PolygonPath(params float[] points)
        {
            PointF[] corners = new PointF[points.Length / 2];
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = new PointF(points[i * 2], points[i * 2 + 1]);
            }
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(corners);
            return path;
        }

        /// <summary>Draw rotated about the icon's centre, as SVG's rotate(angle 0.5 0.5)</summary>
        /// <param name="g">The graphics</param>
        /// <param name="degrees">Clockwise on screen</param>
        /// <param name="draw">What to draw rotated</param>
        private static void Rotated(Graphics g, float degrees, Action draw)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(0.5f, 0.5f);
            g.RotateTransform(degrees);
            g.TranslateTransform(-0.5f, -0.5f);
            draw();
            g.Restore(state);
        }

        /// <summary>Building: an office block with rows of windows and a door, for a site</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintBuilding(Graphics g, Color body)
        {
            FillBodyOf(g, RoundRect(new RectangleF(0.18f, 0.08f, 0.64f, 0.86f), 0.04f), body);
            using (Brush window = new SolidBrush(Color.FromArgb(225, Glyph)))
            {
                foreach (float y in new[] { 0.18f, 0.34f, 0.5f })
                {
                    foreach (float x in new[] { 0.28f, 0.44f, 0.6f })
                    {
                        g.FillRectangle(window, x, y, 0.12f, 0.1f);
                    }
                }
            }
            using (Brush door = new SolidBrush(Shade(body, -0.4f)))
            {
                g.FillRectangle(door, 0.42f, 0.7f, 0.16f, 0.24f);
            }
        }

        /// <summary>Person: a head and shoulders, for a user's own session</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintPerson(Graphics g, Color body)
        {
            GraphicsPath head = new GraphicsPath();
            head.AddEllipse(0.32f, 0.06f, 0.36f, 0.36f);
            FillBodyOf(g, head, body);
            GraphicsPath shoulders = new GraphicsPath();
            shoulders.AddArc(0.12f, 0.48f, 0.76f, 0.6f, 180, 180);
            shoulders.AddLine(0.88f, 0.78f, 0.88f, 0.94f);
            shoulders.AddLine(0.88f, 0.94f, 0.12f, 0.94f);
            shoulders.CloseFigure();
            FillBodyOf(g, shoulders, body);
        }

        /// <summary>Key: a key on the slant, its bow a ring the background shows through</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintKey(Graphics g, Color body)
        {
            Rotated(g, -40, () =>
            {
                FillBodyOf(g, PolygonPath(0.4f, 0.45f, 0.96f, 0.45f, 0.96f, 0.66f, 0.88f, 0.66f, 0.88f, 0.56f, 0.8f, 0.56f,
                    0.8f, 0.66f, 0.72f, 0.66f, 0.72f, 0.56f, 0.4f, 0.56f), body);
                // Alternate fill: the inner circle is the bow's hole
                GraphicsPath bow = new GraphicsPath();
                bow.AddEllipse(0.04f, 0.3f, 0.42f, 0.42f);
                bow.AddEllipse(0.15f, 0.41f, 0.2f, 0.2f);
                FillBodyOf(g, bow, body);
            });
        }

        /// <summary>Shield: a shield with a tick, for something secured</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintShield(Graphics g, Color body)
        {
            FillBodyOf(g, ShieldPath(), body);
            using (Pen tick = new Pen(Glyph, 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(tick, new[] { new PointF(0.31f, 0.5f), new PointF(0.45f, 0.64f), new PointF(0.7f, 0.36f) });
            }
        }

        /// <summary>Settings: a gear of eight teeth, its middle a hole the background shows through</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintGear(Graphics g, Color body)
        {
            const double step = Math.PI * 2 / 8;
            Func<double, float, PointF> at = (angle, r) => new PointF(0.5f + (float)Math.Cos(angle) * r, 0.5f + (float)Math.Sin(angle) * r);
            List<PointF> points = new List<PointF>();
            for (int i = 0; i < 8; i++)
            {
                double a = i * step;
                points.Add(at(a - step * 0.32, 0.34f));
                points.Add(at(a - step * 0.17, 0.46f));
                points.Add(at(a + step * 0.17, 0.46f));
                points.Add(at(a + step * 0.32, 0.34f));
            }
            // Alternate fill: the circle is the hole
            GraphicsPath gear = new GraphicsPath();
            gear.AddPolygon(points.ToArray());
            gear.AddEllipse(0.36f, 0.36f, 0.28f, 0.28f);
            FillBodyOf(g, gear, body);
        }

        /// <summary>Wrench: an open-ended spanner on the slant, for maintenance</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintWrench(Graphics g, Color body)
        {
            // the head: a disc with a square notch out of its end
            const double cx = 0.5, cy = 0.25, r = 0.2;
            double edge = Math.Asin(0.07 / r);
            List<PointF> head = new List<PointF>();
            for (int i = 0; i <= 24; i++)
            {
                double a = -Math.PI / 2 + edge + (Math.PI * 2 - edge * 2) * i / 24;
                head.Add(new PointF((float)(cx + Math.Cos(a) * r), (float)(cy + Math.Sin(a) * r)));
            }
            head.Add(new PointF(0.43f, 0.22f));
            head.Add(new PointF(0.57f, 0.22f));
            Rotated(g, 45, () =>
            {
                FillBodyOf(g, RoundRect(new RectangleF(0.43f, 0.36f, 0.14f, 0.58f), 0.07f), body);
                GraphicsPath headPath = new GraphicsPath();
                headPath.AddPolygon(head.ToArray());
                FillBodyOf(g, headPath, body);
            });
        }

        /// <summary>Warning: a triangle with an exclamation mark, dark on the body so it reads on yellow</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintWarning(Graphics g, Color body)
        {
            FillBodyOf(g, PolygonPath(0.5f, 0.07f, 0.96f, 0.9f, 0.04f, 0.9f), body);
            Color mark = Shade(body, -0.65f);
            FillSolid(g, RoundRect(new RectangleF(0.455f, 0.33f, 0.09f, 0.33f), 0.04f), mark);
            using (Brush dot = new SolidBrush(mark))
            {
                g.FillEllipse(dot, 0.45f, 0.71f, 0.1f, 0.1f);
            }
        }

        /// <summary>Bell: a bell with its clapper, for alerts</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintBell(Graphics g, Color body)
        {
            using (Brush dark = new SolidBrush(Shade(body, -0.35f)))
            {
                g.FillEllipse(dark, 0.4f, 0.78f, 0.2f, 0.17f);
                GraphicsPath bell = new GraphicsPath();
                bell.AddBezier(0.2f, 0.74f, 0.27f, 0.6f, 0.18f, 0.15f, 0.5f, 0.15f);
                bell.AddBezier(0.5f, 0.15f, 0.82f, 0.15f, 0.73f, 0.6f, 0.8f, 0.74f);
                bell.AddLine(0.8f, 0.74f, 0.88f, 0.82f);
                bell.AddLine(0.88f, 0.82f, 0.12f, 0.82f);
                bell.CloseFigure();
                FillBodyOf(g, bell, body);
                g.FillEllipse(dark, 0.45f, 0.06f, 0.1f, 0.1f);
            }
        }

        /// <summary>Calendar: a page with a coloured top, its rings and its days, for something on a schedule</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintCalendar(Graphics g, Color body)
        {
            FillBodyOf(g, RoundRect(new RectangleF(0.08f, 0.12f, 0.84f, 0.82f), 0.08f), body);
            using (Brush page = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFA)))
            using (Brush day = new SolidBrush(Shade(body, -0.1f)))
            {
                g.FillRectangle(page, 0.14f, 0.34f, 0.72f, 0.54f);
                foreach (float y in new[] { 0.42f, 0.57f, 0.72f })
                {
                    foreach (float x in new[] { 0.21f, 0.37f, 0.53f, 0.69f })
                    {
                        g.FillRectangle(day, x, y, 0.1f, 0.09f);
                    }
                }
            }
            Color ring = Shade(body, -0.4f);
            FillSolid(g, RoundRect(new RectangleF(0.26f, 0.05f, 0.08f, 0.16f), 0.04f), ring);
            FillSolid(g, RoundRect(new RectangleF(0.66f, 0.05f, 0.08f, 0.16f), 0.04f), ring);
        }

        /// <summary>Lab: a flask of the body's colour, for a lab or test system</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintFlask(Graphics g, Color body)
        {
            PointF[] flask =
            {
                new PointF(0.38f, 0.06f), new PointF(0.62f, 0.06f), new PointF(0.62f, 0.36f),
                new PointF(0.9f, 0.88f), new PointF(0.1f, 0.88f), new PointF(0.38f, 0.36f)
            };
            using (Brush glass = new SolidBrush(Color.FromArgb(200, 0xEC, 0xEF, 0xF1)))
            {
                g.FillPolygon(glass, flask);
            }
            // the liquid: the flask below 0.58, where its sides are at x 0.268 and 0.732
            using (GraphicsPath liquid = PolygonPath(0.262f, 0.58f, 0.738f, 0.58f, 0.9f, 0.88f, 0.1f, 0.88f))
            using (Brush fill = new LinearGradientBrush(new PointF(0, 0.58f), new PointF(0, 0.89f), Shade(body, 0.18f), Shade(body, -0.12f)))
            {
                g.FillPath(fill, liquid);
            }
            using (Pen edge = new Pen(Shade(body, -0.35f), 0.045f) { LineJoin = LineJoin.Round })
            using (Brush bubble = new SolidBrush(Color.FromArgb(200, Glyph)))
            {
                g.DrawPolygon(edge, flask);
                g.FillEllipse(bubble, 0.4f, 0.66f, 0.08f, 0.08f);
                g.FillEllipse(bubble, 0.54f, 0.74f, 0.06f, 0.06f);
            }
        }

        /// <summary>Bug: a beetle with its legs and feelers, for testing or a known problem</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintBug(Graphics g, Color body)
        {
            using (Pen leg = new Pen(Shade(body, -0.35f), 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float[][] legs = { new[] { 0.48f, -0.08f }, new[] { 0.62f, 0f }, new[] { 0.76f, 0.08f } };
                foreach (float[] l in legs)
                {
                    g.DrawLine(leg, 0.3f, l[0], 0.1f, l[0] + l[1]);
                    g.DrawLine(leg, 0.7f, l[0], 0.9f, l[0] + l[1]);
                }
                g.DrawLine(leg, 0.43f, 0.2f, 0.33f, 0.06f);
                g.DrawLine(leg, 0.57f, 0.2f, 0.67f, 0.06f);
            }
            GraphicsPath headPath = new GraphicsPath();
            headPath.AddEllipse(0.37f, 0.14f, 0.26f, 0.22f);
            FillBodyOf(g, headPath, Shade(body, -0.2f));
            GraphicsPath shell = new GraphicsPath();
            shell.AddEllipse(0.26f, 0.3f, 0.48f, 0.64f);
            FillBodyOf(g, shell, body);
            using (Pen seam = new Pen(Color.FromArgb(200, Glyph), 0.04f))
            {
                g.DrawLine(seam, 0.5f, 0.34f, 0.5f, 0.9f);
            }
        }

        /// <summary>Code: &lt;/&gt; on a rounded square, for development</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintCode(Graphics g, Color body)
        {
            FillBodyOf(g, RoundRect(new RectangleF(0.06f, 0.06f, 0.88f, 0.88f), 0.16f), body);
            using (Pen pen = new Pen(Glyph, 0.08f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[] { new PointF(0.34f, 0.32f), new PointF(0.18f, 0.5f), new PointF(0.34f, 0.68f) });
                g.DrawLines(pen, new[] { new PointF(0.66f, 0.32f), new PointF(0.82f, 0.5f), new PointF(0.66f, 0.68f) });
                g.DrawLine(pen, 0.56f, 0.27f, 0.44f, 0.73f);
            }
        }

        /// <summary>Heart: for a favourite</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintHeart(Graphics g, Color body)
        {
            GraphicsPath heart = new GraphicsPath();
            heart.AddBezier(0.5f, 0.9f, 0.2f, 0.68f, 0.04f, 0.52f, 0.06f, 0.32f);
            heart.AddBezier(0.06f, 0.32f, 0.08f, 0.1f, 0.4f, 0.04f, 0.5f, 0.26f);
            heart.AddBezier(0.5f, 0.26f, 0.6f, 0.04f, 0.92f, 0.1f, 0.94f, 0.32f);
            heart.AddBezier(0.94f, 0.32f, 0.96f, 0.52f, 0.8f, 0.68f, 0.5f, 0.9f);
            heart.CloseFigure();
            FillBodyOf(g, heart, body);
        }

        /// <summary>Tag: a price tag on the slant, its hole one the background shows through, for a label</summary>
        /// <param name="g">Graphics scaled to the unit square</param>
        /// <param name="body">The body colour</param>
        private static void PaintTag(Graphics g, Color body)
        {
            Rotated(g, -45, () =>
            {
                // Alternate fill: the circle is the hole
                GraphicsPath tag = PolygonPath(0.1f, 0.5f, 0.28f, 0.31f, 0.86f, 0.31f, 0.86f, 0.69f, 0.28f, 0.69f);
                tag.AddEllipse(0.23f, 0.445f, 0.11f, 0.11f);
                FillBodyOf(g, tag, body);
            });
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

        /// <summary>Text shows (not masked): a white eye on a disc</summary>
        private static void PaintEye(Graphics g, Color body)
        {
            Disc(g, body);
            using (GraphicsPath eye = new GraphicsPath())
            using (Brush white = new SolidBrush(Glyph))
            using (Brush pupil = new SolidBrush(body))
            {
                eye.AddBezier(0.2f, 0.5f, 0.32f, 0.3f, 0.68f, 0.3f, 0.8f, 0.5f);
                eye.AddBezier(0.8f, 0.5f, 0.68f, 0.7f, 0.32f, 0.7f, 0.2f, 0.5f);
                g.FillPath(white, eye);
                g.FillEllipse(pupil, 0.42f, 0.42f, 0.16f, 0.16f);
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

        /// <summary>Search: a white magnifying glass on a disc</summary>
        private static void PaintSearch(Graphics g, Color body)
        {
            Disc(g, body);
            using (Pen glass = new Pen(Glyph, 0.08f))
            using (Pen handle = new Pen(Glyph, 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(glass, 0.28f, 0.26f, 0.3f, 0.3f);
                g.DrawLine(handle, 0.54f, 0.53f, 0.7f, 0.69f);
            }
        }

        /// <summary>A small disc with a white plus in the bottom-right corner, for "new ..." buttons</summary>
        private static void PlusBadge(Graphics g, Color body)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0.5f, 0.5f, 0.48f, 0.48f);
                FillBody(g, path, body);
            }
            using (Pen plus = new Pen(Glyph, 0.08f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(plus, 0.74f, 0.62f, 0.74f, 0.86f);
                g.DrawLine(plus, 0.62f, 0.74f, 0.86f, 0.74f);
            }
        }

        /// <summary>New session: a grey workstation with a plus badge</summary>
        private static void PaintNewSession(Graphics g, Color body)
        {
            PaintWorkstation(g, Color.FromArgb(0x60, 0x7D, 0x8B));
            PlusBadge(g, body);
        }

        /// <summary>New folder: a folder with a plus badge</summary>
        private static void PaintNewFolder(Graphics g, Color body)
        {
            PaintFolder(g, Color.FromArgb(0xF2, 0xB1, 0x34));
            PlusBadge(g, body);
        }

        /// <summary>Expand all / collapse all: a tree's plus or minus box</summary>
        private static void PaintTreeBox(Graphics g, Color body, bool plus)
        {
            using (GraphicsPath box = RoundRect(new RectangleF(0.12f, 0.12f, 0.76f, 0.76f), 0.12f))
            {
                FillBody(g, box, body);
            }
            using (Pen line = new Pen(Glyph, 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(line, 0.3f, 0.5f, 0.7f, 0.5f);
                if (plus)
                {
                    g.DrawLine(line, 0.5f, 0.3f, 0.5f, 0.7f);
                }
            }
        }

        /// <summary>Sort: two letters, one above the other, beside a downward arrow, on a disc</summary>
        private static void PaintSort(Graphics g, Color body, string top, string bottom)
        {
            Disc(g, body);
            CentredText(g, top, new RectangleF(0.14f, 0.16f, 0.4f, 0.34f), 0.32f, Glyph);
            CentredText(g, bottom, new RectangleF(0.14f, 0.48f, 0.4f, 0.34f), 0.32f, Glyph);
            using (Brush arrow = new SolidBrush(Glyph))
            {
                Arrow(g, arrow, new PointF(0.66f, 0.22f), new PointF(0.66f, 0.8f), 0.08f, 0.16f);
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
