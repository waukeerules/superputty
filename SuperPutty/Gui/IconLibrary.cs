using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using log4net;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Turns session icon keys into images at any size. A key is a name with an optional colour: "switch",
    /// "switch#2E7D32", "ubuntu", "custom:juniper#1E63B4". Names are drawn from IconTemplates (device and general
    /// symbols), BrandLogos (OS and vendor logos) or CustomIcons (one-colour shapes users add), all in any colour.
    /// Older keys from the PNG icon set ("green_switch", "tux", "flag_red", ...) map onto those, so existing
    /// Sessions.xml files keep their icons, now sharp; the old themes\default\icons folders are no longer read.
    /// SuperPuTTY.LoadImageList builds the shared ImageList with <see cref="Fill"/>; coloured keys are added to it on
    /// first use (<see cref="Ensure"/>).
    /// </summary>
    public static class IconLibrary
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IconLibrary));

        /// <summary>The icon sizes offered in Options, in logical pixels (scaled for the display)</summary>
        public static readonly int[] Sizes = { 16, 20, 24, 32 };

        /// <summary>
        /// The colours offered in the icon picker, by name. Picking one stores its hex in the key, so a shared
        /// Sessions.xml shows the same colour for everyone.
        /// </summary>
        public static readonly KeyValuePair<string, Color>[] Palette =
        {
            new KeyValuePair<string, Color>("Green", Color.FromArgb(0x2E, 0x7D, 0x32)),
            new KeyValuePair<string, Color>("Blue", Color.FromArgb(0x1E, 0x63, 0xB4)),
            new KeyValuePair<string, Color>("Red", Color.FromArgb(0xC6, 0x28, 0x28)),
            new KeyValuePair<string, Color>("Orange", Color.FromArgb(0xEF, 0x6C, 0x00)),
            new KeyValuePair<string, Color>("Yellow", Color.FromArgb(0xF9, 0xA8, 0x25)),
            new KeyValuePair<string, Color>("Purple", Color.FromArgb(0x6A, 0x3D, 0x9A)),
            new KeyValuePair<string, Color>("Pink", Color.FromArgb(0xD8, 0x1B, 0x60)),
            new KeyValuePair<string, Color>("Teal", Color.FromArgb(0x00, 0x83, 0x8F)),
            new KeyValuePair<string, Color>("Grey", Color.FromArgb(0x60, 0x7D, 0x8B)),
            new KeyValuePair<string, Color>("Black", Color.FromArgb(0x26, 0x32, 0x38))
        };

        /// <summary>
        /// Keys of the old PNG icons that templates or logos now draw, and the key each becomes. The PNGs were the
        /// default set plus custom network icons in colour variants.
        /// </summary>
        private static readonly Dictionary<string, string> legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "computer", "workstation" },
            { "monitor", "workstation" },
            { "application_xp_terminal", "terminal" },
            { "application_osx_terminal", "terminal#263238" },
            { "drive_network", "storage" },
            { "telephone", "phone" },
            { "webcam", "camera" },
            { "wifi_device", "wireless" },
            { "green_switch", "switch#2E7D32" },
            { "blue_switch", "switch#1E63B4" },
            { "red_switch", "switch#C62828" },
            { "orange_switch", "switch#EF6C00" },
            { "blue_router", "router#1E63B4" },
            { "red_router", "router#C62828" },
            { "orange_router", "router#EF6C00" },
            { "purple_router", "router#6A3D9A" },
            { "voice_router", "voicerouter" },
            { "green_nexus", "nexus#2E7D32" },
            { "red_nexus", "nexus#C62828" },
            { "red_big_nexus", "nexus#8E1B1B" },
            { "orange_nexus", "nexus#EF6C00" },
            { "new_firewall", "firewall#78909C" },
            { "flag_blue", "flag#1E63B4" },
            { "flag_green", "flag#2E7D32" },
            { "flag_orange", "flag#EF6C00" },
            { "flag_pink", "flag#D81B60" },
            { "flag_purple", "flag#6A3D9A" },
            { "flag_red", "flag#C62828" },
            { "flag_yellow", "flag#F9A825" },
            { "arch_linux", "archlinux" },
            { "linux_mint", "linuxmint" },
            { "puffy", "openbsd" },
            { "suse_linux", "opensuse" },
            { "tux", "linux" },
            { "time", "clock" },
            { "comment", "chat" },
            { "compress", "archive" },
            { "feed", "rss" },
            { "information", "info" },
            { "ipod", "mobile" },
            { "lightning", "bolt" },
            { "lorry", "truck" },
            { "report", "document" },
            { "television", "tv" },
            { "world", "globe" }
        };

        /// <summary>A key: a name, then optionally # and a six-digit hex colour</summary>
        private static readonly Regex KeyPattern = new Regex(@"^(?<name>[^#]+)(#(?<color>[0-9A-Fa-f]{6}))?$", RegexOptions.Compiled);

        /// <summary>Whether the interface is dark, which lightens near-black logos so they show up</summary>
        public static bool DarkBackground { get; set; }

        /// <summary>The logical icon size scaled for the display, e.g. 24 at 125% = 30 pixels</summary>
        /// <param name="logicalSize">The size in logical pixels</param>
        /// <returns>The size in screen pixels</returns>
        public static int ToPixels(int logicalSize)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return (int)Math.Round(logicalSize * g.DpiX / 96f);
            }
        }

        /// <summary>
        /// The size tab icons are drawn at, in pixels. DockPanelSuite draws them as high as the tab font, so
        /// frmSuperPutty sets this when it sets the font (<see cref="TabFontSize"/>).
        /// </summary>
        public static int TabIconPixels { get; set; } = 16;

        /// <summary>The tab font size for the icon size chosen in Options: bigger icons make taller tabs</summary>
        public static float TabFontSize
        {
            get { return LogicalSize <= 20 ? 10f : LogicalSize == 24 ? 11f : 12.5f; }
        }

        /// <summary>The icon size chosen in Options, made one of <see cref="Sizes"/></summary>
        public static int LogicalSize
        {
            get
            {
                int size = SuperPuTTY.Settings.IconSize;
                return Sizes.Contains(size) ? size : 24;
            }
        }

        /// <summary>Build a key from a name and colour, leaving the colour off when it's the name's default</summary>
        /// <param name="name">A template or logo name</param>
        /// <param name="color">The colour</param>
        /// <returns>e.g. "switch#1E63B4", or "switch" for its default green</returns>
        public static string MakeKey(string name, Color color)
        {
            Color defaultColor;
            if (TryGetDefaultColor(name, out defaultColor) && defaultColor.ToArgb() == color.ToArgb())
                return name;
            return string.Format("{0}#{1:X2}{2:X2}{3:X2}", name, color.R, color.G, color.B);
        }

        /// <summary>Split a key into the name to draw and its colour, following old PNG keys to what replaces them</summary>
        /// <param name="key">The key</param>
        /// <param name="name">The template, logo or PNG name</param>
        /// <param name="color">The colour from the key, or null for the name's default</param>
        /// <returns>false if the key isn't well formed</returns>
        public static bool TryParse(string key, out string name, out Color? color)
        {
            name = null;
            color = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;
            string mapped;
            if (legacy.TryGetValue(key, out mapped))
            {
                key = mapped;
            }
            Match match = KeyPattern.Match(key.Trim());
            if (!match.Success)
                return false;
            name = match.Groups["name"].Value;
            if (match.Groups["color"].Success)
            {
                color = Color.FromArgb(int.Parse(match.Groups["color"].Value, NumberStyles.HexNumber) | unchecked((int)0xFF000000));
            }
            return true;
        }

        /// <summary>Whether a name is an icon SuperPuTTY can draw: a template, a logo or a custom icon</summary>
        /// <param name="name">The name, without colour</param>
        /// <returns>true for templates, logos and custom icons</returns>
        public static bool IsDrawn(string name)
        {
            return IconTemplates.Exists(name) || BrandLogos.Exists(name) || CustomIcons.Exists(name);
        }

        /// <summary>Whether a key can take a colour (it names a template or logo, directly or through an old key)</summary>
        /// <param name="key">The key</param>
        /// <returns>true if the picker should offer colours for it</returns>
        public static bool IsColorable(string key)
        {
            string name;
            Color? color;
            return TryParse(key, out name, out color) && IsDrawn(name);
        }

        /// <summary>The colour a key draws in: its own, or its template's or logo's default</summary>
        /// <param name="key">The key</param>
        /// <returns>The colour; Empty for PNG icons</returns>
        public static Color ColorOf(string key)
        {
            string name;
            Color? color;
            Color defaultColor;
            if (!TryParse(key, out name, out color))
                return Color.Empty;
            return color ?? (TryGetDefaultColor(name, out defaultColor) ? defaultColor : Color.Empty);
        }

        /// <summary>The template or logo a key draws, with any old key followed and the colour removed</summary>
        /// <param name="key">The key</param>
        /// <returns>The name, or the key itself for PNG icons</returns>
        public static string NameOf(string key)
        {
            string name;
            Color? color;
            return TryParse(key, out name, out color) ? name : key;
        }

        /// <summary>A template's or logo's default colour</summary>
        private static bool TryGetDefaultColor(string name, out Color color)
        {
            if (IconTemplates.Exists(name))
            {
                color = IconTemplates.DefaultColorOf(name);
                return true;
            }
            if (BrandLogos.Exists(name))
            {
                color = BrandLogos.DefaultColorOf(name);
                return true;
            }
            if (CustomIcons.Exists(name))
            {
                color = CustomIcons.DefaultColorOf(name);
                return true;
            }
            color = Color.Empty;
            return false;
        }

        /// <summary>A name for the picker: the template's or logo's title, or the PNG's file name</summary>
        /// <param name="name">The name</param>
        /// <returns>The title</returns>
        public static string TitleOf(string name)
        {
            if (IconTemplates.Exists(name))
                return IconTemplates.TitleOf(name);
            if (BrandLogos.Exists(name))
                return BrandLogos.TitleOf(name);
            if (CustomIcons.IsCustom(name))
                return CustomIcons.TitleOf(name);
            return name.Replace('_', ' ');
        }

        /// <summary>Draw a template or logo at a size</summary>
        /// <param name="key">The key</param>
        /// <param name="size">The width and height in pixels</param>
        /// <returns>A new bitmap, or null if the key isn't a template or logo</returns>
        public static Bitmap RenderDrawn(string key, int size)
        {
            string name;
            Color? color;
            if (!TryParse(key, out name, out color))
                return null;
            if (IconTemplates.Exists(name))
                return IconTemplates.Render(name, color ?? IconTemplates.DefaultColorOf(name), size);
            if (BrandLogos.Exists(name))
            {
                Color logoColor = color ?? BrandLogos.DefaultColorOf(name);
                if (color == null && DarkBackground && logoColor.GetBrightness() < 0.22f)
                {
                    // black brand colours (Apple, AlmaLinux, ...) would vanish on a dark interface; a colour someone
                    // chose is used as it is
                    logoColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
                }
                return BrandLogos.Render(name, logoColor, size);
            }
            if (CustomIcons.Exists(name))
                return CustomIcons.Render(name, color ?? CustomIcons.DefaultColorOf(name), size);
            return null;
        }

        /// <summary>Scale an image smoothly to a square, keeping its proportions and centring it</summary>
        /// <param name="image">The image, e.g. a 16x16 PNG</param>
        /// <param name="size">The width and height in pixels</param>
        /// <returns>A new bitmap</returns>
        public static Bitmap Scale(Image image, int size)
        {
            Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Min((float)size / image.Width, (float)size / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                g.DrawImage(image, (size - w) / 2, (size - h) / 2, w, h);
            }
            return bitmap;
        }

        /// <summary>
        /// Fill an image list with every icon: the templates and logos, the old keys that now map to them, and the
        /// custom icons
        /// </summary>
        /// <param name="list">The list, already sized</param>
        public static void Fill(ImageList list)
        {
            int size = IconPixels(list);
            foreach (string name in IconTemplates.Keys.Concat(BrandLogos.Keys).Concat(legacy.Keys).Concat(CustomIcons.Keys()))
            {
                Bitmap image = list.Images.ContainsKey(name) ? null : RenderDrawn(name, size);
                if (image != null)
                {
                    list.Images.Add(name, ToCell(list, image));
                }
            }
        }

        /// <summary>
        /// A copy of an icon list whose cells are larger than its icons, each icon at its full size in the middle. The
        /// Sessions tree uses one, so an environment colour's circle (<see cref="EnsureWithEnvironment"/>) can be
        /// bigger than the icon, and its rows get the extra height. The icon size is kept in the list's Tag.
        /// </summary>
        /// <param name="source">The icons (SuperPuTTY.Images)</param>
        /// <returns>The new list, with every icon the source has</returns>
        public static ImageList NewPaddedList(ImageList source)
        {
            int icon = source.ImageSize.Width;
            int cell = icon + 2 * (int)Math.Round(icon * CellPadding);
            ImageList list = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(cell, cell), Tag = icon };
            foreach (string key in source.Images.Keys)
            {
                using (Image image = source.Images[key])
                {
                    list.Images.Add(key, Pad(image, cell));
                }
            }
            return list;
        }

        /// <summary>
        /// How much room a padded list (<see cref="NewPaddedList"/>) leaves on each side of an icon, as a share of the
        /// icon's size: enough for the circle to show round it, and to reach nearly round a square icon's corners
        /// </summary>
        private const float CellPadding = 0.18f;

        /// <summary>The size icons are drawn at in a list: smaller than its cells for a padded list</summary>
        /// <param name="list">The list</param>
        /// <returns>The icon's width and height in pixels</returns>
        private static int IconPixels(ImageList list)
        {
            return list.Tag is int ? (int)list.Tag : list.ImageSize.Width;
        }

        /// <summary>An icon drawn for a list, put in the middle of a cell when the list is padded</summary>
        /// <param name="list">The list it's for</param>
        /// <param name="icon">The icon at <see cref="IconPixels"/>; disposed when a new image is made</param>
        /// <returns>The image to add</returns>
        private static Bitmap ToCell(ImageList list, Bitmap icon)
        {
            if (icon.Width == list.ImageSize.Width)
                return icon;
            using (icon)
            {
                return Pad(icon, list.ImageSize.Width);
            }
        }

        /// <summary>An image centred, at its own size, on a transparent square</summary>
        /// <param name="image">The image</param>
        /// <param name="cell">The square's size in pixels</param>
        /// <returns>A new bitmap</returns>
        private static Bitmap Pad(Image image, int cell)
        {
            Bitmap bitmap = new Bitmap(cell, cell);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(image, (cell - image.Width) / 2, (cell - image.Height) / 2, image.Width, image.Height);
            }
            return bitmap;
        }

        /// <summary>
        /// Make sure an image list has a key, drawing it if it's a coloured template or logo not yet in the list (e.g.
        /// "router#C62828"). Call before using a key from a session or button.
        /// </summary>
        /// <param name="list">The image list</param>
        /// <param name="key">The key</param>
        /// <returns>true if the list now has the key</returns>
        public static bool Ensure(ImageList list, string key)
        {
            if (list == null || string.IsNullOrEmpty(key))
                return false;
            if (list.Images.ContainsKey(key))
                return true;
            Bitmap drawn = RenderDrawn(key, IconPixels(list));
            if (drawn == null)
                return false;
            list.Images.Add(key, ToCell(list, drawn));
            return true;
        }

        /// <summary>Whether a session has a tab open, and how it's doing; shown as a dot on its icon in the Sessions tree</summary>
        public enum OpenState
        {
            /// <summary>No tab open: the plain icon</summary>
            None,
            /// <summary>A tab is open and connected: a green dot</summary>
            Connected,
            /// <summary>Its tabs have all dropped, or are reconnecting: a red dot</summary>
            Disconnected
        }

        /// <summary>
        /// The key of an icon with a dot for its open state, drawn into the list on first use (e.g. "switch#1E63B4|connected").
        /// Used by the Sessions tree to mark sessions that have a tab open.
        /// </summary>
        /// <param name="list">The tree's image list</param>
        /// <param name="baseKey">The session's own icon key; it must be in the list or drawable</param>
        /// <param name="state">The open state</param>
        /// <returns>The key to show: <paramref name="baseKey"/> itself for <see cref="OpenState.None"/></returns>
        public static string EnsureWithState(ImageList list, string baseKey, OpenState state)
        {
            if (state == OpenState.None || !Ensure(list, baseKey))
                return baseKey;
            string key = baseKey + (state == OpenState.Connected ? "|connected" : "|disconnected");
            if (!list.Images.ContainsKey(key))
            {
                using (Image icon = list.Images[baseKey])
                {
                    Bitmap bitmap = new Bitmap(icon);
                    DrawStateDot(bitmap, state == OpenState.Connected ? Color.FromArgb(0x2E, 0xB8, 0x4E) : Color.FromArgb(0xD3, 0x2F, 0x2F), IconPixels(list));
                    list.Images.Add(key, bitmap);
                }
            }
            return key;
        }

        /// <summary>
        /// The key of an icon drawn on a circle of an environment colour (production in red), drawn into the list on
        /// first use (e.g. "switch#1E63B4|env#C62828"). Used by the Sessions tree, so the colour shows without
        /// colouring the name, which then stays readable. <see cref="EnsureWithState"/> can add its dot on top.
        /// </summary>
        /// <param name="list">The tree's image list</param>
        /// <param name="baseKey">The icon's key; it must be in the list or drawable</param>
        /// <param name="color">The environment colour, or null for none</param>
        /// <returns>The key to show: <paramref name="baseKey"/> itself without a colour</returns>
        public static string EnsureWithEnvironment(ImageList list, string baseKey, Color? color)
        {
            if (!color.HasValue || !Ensure(list, baseKey))
                return baseKey;
            string key = string.Format("{0}|env#{1:X2}{2:X2}{3:X2}", baseKey, color.Value.R, color.Value.G, color.Value.B);
            if (!list.Images.ContainsKey(key))
            {
                using (Image icon = list.Images[baseKey])
                {
                    list.Images.Add(key, DrawOnCircle(icon, color.Value, IconPixels(list)));
                }
            }
            return key;
        }

        /// <summary>
        /// An icon on a filled circle of a colour that takes the whole cell, outlined in dark so it reads on any circle
        /// (an orange logo on red, say). In a padded list's cell the icon keeps its full size and the circle shows in
        /// the room round it; in a cell the icon's own size, the icon is drawn smaller so the colour shows.
        /// </summary>
        /// <param name="icon">The icon's cell</param>
        /// <param name="color">The circle's colour</param>
        /// <param name="iconPixels">The icon's size in the cell (<see cref="IconPixels"/>)</param>
        /// <returns>A new image the cell's size</returns>
        public static Bitmap DrawOnCircle(Image icon, Color color, int iconPixels)
        {
            int size = icon.Width;
            Bitmap bitmap = new Bitmap(size, icon.Height);
            float inner = iconPixels < size ? size : size * 0.78f;
            float offset = (size - inner) / 2;
            float outline = Math.Max(1f, iconPixels / 20f);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(color))
            using (ImageAttributes dark = new ImageAttributes())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.FillEllipse(fill, 0, 0, size - 1, icon.Height - 1);
                // the outline is the icon's own shape in near-black, drawn shifted all round beneath it
                dark.SetColorMatrix(new ColorMatrix(new[]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0.85f, 0 },
                    new float[] { 0.08f, 0.08f, 0.08f, 0, 1 }
                }));
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float x = offset + (float)Math.Cos(angle) * outline, y = offset + (float)Math.Sin(angle) * outline;
                    g.DrawImage(icon, new[] { new PointF(x, y), new PointF(x + inner, y), new PointF(x, y + inner) },
                        new RectangleF(0, 0, icon.Width, icon.Height), GraphicsUnit.Pixel, dark);
                }
                g.DrawImage(icon, offset, offset, inner, inner);
            }
            return bitmap;
        }

        /// <summary>
        /// Draw a status dot in an icon's bottom-right corner, with a ring in the background colour so it stands off
        /// the icon beneath
        /// </summary>
        /// <param name="bitmap">The icon, drawn on in place</param>
        /// <param name="color">The dot's colour</param>
        /// <param name="iconPixels">The icon's size in the bitmap: the dot is sized for it, and in a padded cell sits
        /// over the icon's corner, half into the room round it</param>
        public static void DrawStateDot(Bitmap bitmap, Color color, int iconPixels)
        {
            float size = bitmap.Width;
            float dot = iconPixels * 0.42f;
            float ring = Math.Max(1.5f, iconPixels * 0.07f);
            float inset = (size - iconPixels) / 4;
            RectangleF bounds = new RectangleF(size - inset - dot - ring, size - inset - dot - ring, dot, dot);
            Color background = DarkBackground ? Color.FromArgb(0x25, 0x25, 0x26) : Color.White;
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush ringBrush = new SolidBrush(background))
            using (Brush fill = new SolidBrush(color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF outer = RectangleF.Inflate(bounds, ring, ring);
                g.FillEllipse(ringBrush, outer);
                g.FillEllipse(fill, bounds);
            }
        }
    }
}
