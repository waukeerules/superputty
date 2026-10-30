using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using log4net;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Icons users add themselves (icon picker -> Add icon...). Each is stored as a one-colour shape (see MakeShape),
    /// so it can be drawn in any colour like the built-in templates. They're kept as PNGs in an "icons"
    /// folder in the settings folder, next to Sessions.xml, so a team sharing that folder also shares the icons its
    /// sessions use. Keys are "custom:name", optionally with a colour ("custom:juniper#1E63B4"); IconLibrary renders
    /// them through <see cref="Render"/>.
    /// </summary>
    public static class CustomIcons
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CustomIcons));

        /// <summary>What custom icon keys start with</summary>
        public const string Prefix = "custom:";

        /// <summary>The smallest upload accepted, in pixels; smaller ones would look blurry when scaled up</summary>
        public const int MinimumSize = 32;

        /// <summary>The size uploads are stored at most, in pixels, keeping larger ones sharp at larger icon sizes</summary>
        private const int StoredMaximumSize = 128;

        /// <summary>How far, in RGB distance, a solid pixel may be from the shape's average colour and still count as one colour</summary>
        private const double ColorTolerance = 72;

        /// <summary>Names allowed for custom icons: letters, digits, spaces, dots, dashes and underscores</summary>
        private static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9 ._-]{1,40}$", RegexOptions.Compiled);

        /// <summary>The loaded shapes, by name, with their files' write times so a changed file is reloaded</summary>
        private static readonly Dictionary<string, Tuple<DateTime, Bitmap>> cache = new Dictionary<string, Tuple<DateTime, Bitmap>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The folder custom icons are kept in: "icons" in the settings folder</summary>
        public static string Folder
        {
            get { return Path.Combine(SuperPuTTY.Settings.SettingsFolder ?? string.Empty, "icons"); }
        }

        /// <summary>Whether a key or name is a custom icon's ("custom:...")</summary>
        /// <param name="name">The key or name</param>
        /// <returns>true for custom icons</returns>
        public static bool IsCustom(string name)
        {
            return name != null && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The keys of the custom icons that exist, alphabetically</summary>
        /// <returns>Keys like "custom:juniper"</returns>
        public static IList<string> Keys()
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return new List<string>();
                return Directory.GetFiles(Folder, "*.png")
                    .Select(f => Prefix + Path.GetFileNameWithoutExtension(f))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list custom icons in " + Folder, ex);
                return new List<string>();
            }
        }

        /// <summary>Whether a custom icon exists</summary>
        /// <param name="name">Its key, e.g. "custom:juniper"</param>
        /// <returns>true if its file exists</returns>
        public static bool Exists(string name)
        {
            return IsCustom(name) && File.Exists(PathOf(name));
        }

        /// <summary>A custom icon's name for the picker: its key without the prefix</summary>
        /// <param name="name">Its key</param>
        /// <returns>The name</returns>
        public static string TitleOf(string name)
        {
            return name.Substring(Prefix.Length);
        }

        /// <summary>A custom icon's file</summary>
        /// <param name="name">Its key</param>
        /// <returns>The full path</returns>
        private static string PathOf(string name)
        {
            return Path.Combine(Folder, TitleOf(name) + ".png");
        }

        /// <summary>The colour a custom icon was uploaded in, used when its key has no colour</summary>
        /// <param name="name">Its key</param>
        /// <returns>The colour</returns>
        public static Color DefaultColorOf(string name)
        {
            Bitmap shape = Load(name);
            return shape == null ? Color.Gray : AverageColor(shape);
        }

        /// <summary>Draw a custom icon at a size and colour</summary>
        /// <param name="name">Its key</param>
        /// <param name="color">The colour</param>
        /// <param name="size">The width and height in pixels</param>
        /// <returns>A new bitmap, or null if the icon's file is missing or unreadable</returns>
        public static Bitmap Render(string name, Color color, int size)
        {
            Bitmap shape = Load(name);
            if (shape == null)
                return null;
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes tint = new ImageAttributes())
            {
                // keep each pixel's alpha and replace its colour
                tint.SetColorMatrix(new ColorMatrix(new[]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 }
                }));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(shape, new Rectangle(0, 0, size, size), 0, 0, shape.Width, shape.Height, GraphicsUnit.Pixel, tint);
            }
            return result;
        }

        /// <summary>Check that a name can be used for a new custom icon</summary>
        /// <param name="title">The name, without the prefix</param>
        /// <param name="error">Why it can't, if it can't</param>
        /// <returns>true if it can be used</returns>
        public static bool ValidateName(string title, out string error)
        {
            error = null;
            if (title == null || !NamePattern.IsMatch(title.Trim()))
            {
                error = "Use 1 to 40 letters, digits, spaces, dots, dashes or underscores.";
                return false;
            }
            if (File.Exists(Path.Combine(Folder, title.Trim() + ".png")))
            {
                error = "There's already a custom icon called \"" + title.Trim() + "\".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Add a custom icon from an image file (see <see cref="MakeShape"/> for what's accepted). It's stored as a
        /// one-colour PNG of up to 128x128 in <see cref="Folder"/>; the colour it ends up in becomes its default.
        /// </summary>
        /// <param name="file">The image file (PNG recommended)</param>
        /// <param name="title">The icon's name, without the prefix</param>
        /// <param name="key">The new icon's key</param>
        /// <param name="error">Why it can't be added, for the user</param>
        /// <returns>true if it was added</returns>
        public static bool Import(string file, string title, out string key, out string error)
        {
            key = null;
            if (!ValidateName(title, out error))
                return false;
            Bitmap source;
            try
            {
                using (Image image = Image.FromFile(file))
                {
                    source = new Bitmap(image);
                }
            }
            catch (Exception ex)
            {
                error = "Couldn't read the image: " + ex.Message;
                return false;
            }

            using (source)
            {
                Bitmap shape;
                Color color;
                if (!MakeShape(source, out shape, out color, out error))
                    return false;
                using (shape)
                {
                    int size = Math.Min(shape.Width, StoredMaximumSize);
                    try
                    {
                        Directory.CreateDirectory(Folder);
                        using (Bitmap stored = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(stored))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(shape, new Rectangle(0, 0, size, size));
                            }
                            // stored in one flat colour, so tinting is exact
                            using (Bitmap flat = Render(stored, color))
                            {
                                key = Prefix + title.Trim();
                                flat.Save(PathOf(key), ImageFormat.Png);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not save custom icon " + title, ex);
                        error = "Couldn't save the icon in " + Folder + ": " + ex.Message;
                        key = null;
                        return false;
                    }
                }
            }
            Log.InfoFormat("Added custom icon {0}", key);
            return true;
        }

        /// <summary>Delete a custom icon's file; sessions using it then show the default icon</summary>
        /// <param name="name">Its key</param>
        /// <returns>true if it was deleted</returns>
        public static bool Delete(string name)
        {
            try
            {
                cache.Remove(name);
                File.Delete(PathOf(name));
                Log.InfoFormat("Deleted custom icon {0}", name);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not delete custom icon " + name, ex);
                return false;
            }
        }

        /// <summary>
        /// Turn an upload into a one-colour shape. It must be square and at least 32x32. A single-colour shape on a
        /// transparent background is used as it is. Anything else (a black outline with a white fill, line art on a
        /// white background, a two-tone icon) uses its dark parts as the shape: the darker a pixel against white, the
        /// more solid it is, so light areas become see-through. Refused only if no clear shape comes out: a blank
        /// image, or one that's dark all over, like a photo.
        /// </summary>
        /// <param name="image">The upload</param>
        /// <param name="shape">The shape: same size, any colour, alpha giving the shape</param>
        /// <param name="color">The shape's colour, which becomes the icon's default</param>
        /// <param name="error">Why it can't be used, for the user</param>
        /// <returns>true if it can be used</returns>
        private static bool MakeShape(Bitmap image, out Bitmap shape, out Color color, out string error)
        {
            shape = null;
            color = Color.Empty;
            error = null;
            if (image.Width != image.Height)
            {
                error = string.Format("The image must be square; this one is {0}x{1}.", image.Width, image.Height);
                return false;
            }
            if (image.Width < MinimumSize)
            {
                error = string.Format("The image must be at least {0}x{0}; this one is {1}x{1}.", MinimumSize, image.Width);
                return false;
            }

            int w = image.Width, h = image.Height;
            int transparent = 0;
            List<Color> solidPixels = new List<Color>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.A < 32)
                    {
                        transparent++;
                    }
                    else if (c.A >= 200)
                    {
                        solidPixels.Add(c);
                    }
                }
            }
            Color average = Average(solidPixels);
            int off = solidPixels.Count(c => Distance(c, average) > ColorTolerance);
            bool oneColour = transparent > 0 && solidPixels.Count > 0 && off <= solidPixels.Count / 50;

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            if (oneColour)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        result.SetPixel(x, y, Color.FromArgb(image.GetPixel(x, y).A, average));
                    }
                }
                color = average;
            }
            else
            {
                // how dark each pixel is against white; the darkest becomes fully solid
                double[] darkness = new double[w * h];
                double darkest = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = image.GetPixel(x, y);
                        double a = c.A / 255.0;
                        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                        double d = a * (1 - luminance);
                        darkness[y * w + x] = d;
                        darkest = Math.Max(darkest, d);
                    }
                }
                if (darkest < 0.25)
                {
                    result.Dispose();
                    error = "Couldn't find a shape in the image: it has nothing dark enough on a light or transparent background.";
                    return false;
                }
                const double floor = 0.1;
                List<Color> shapePixels = new List<Color>();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        double level = Math.Max(0, Math.Min(1, (darkness[y * w + x] - floor) / (darkest - floor)));
                        result.SetPixel(x, y, Color.FromArgb((int)(level * 255), Color.Black));
                        if (level > 0.6)
                        {
                            shapePixels.Add(image.GetPixel(x, y));
                        }
                    }
                }
                color = Average(shapePixels);
            }

            int covered = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (result.GetPixel(x, y).A > 128)
                    {
                        covered++;
                    }
                }
            }
            if (covered == 0 || covered > w * h * 0.9)
            {
                result.Dispose();
                error = covered == 0
                    ? "Couldn't find a shape in the image: it's blank or almost fully transparent."
                    : "Couldn't find a shape in the image: it's dark all over, like a photo or a dark background.";
                return false;
            }

            // a black or near-black shape would vanish on the dark theme, so it defaults to a mid grey instead
            if (color.GetBrightness() < 0.2f)
            {
                color = Color.FromArgb(0x60, 0x7D, 0x8B);
            }
            shape = result;
            return true;
        }

        /// <summary>The average of some colours</summary>
        /// <param name="colors">The colours</param>
        /// <returns>Their average; grey if there are none</returns>
        private static Color Average(IList<Color> colors)
        {
            if (colors.Count == 0)
                return Color.Gray;
            return Color.FromArgb((int)colors.Average(c => c.R), (int)colors.Average(c => c.G), (int)colors.Average(c => c.B));
        }

        /// <summary>How far apart two colours are, in RGB</summary>
        /// <param name="a">One colour</param>
        /// <param name="b">The other</param>
        /// <returns>The distance</returns>
        private static double Distance(Color a, Color b)
        {
            return Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        }

        /// <summary>The average colour of an image's solid pixels</summary>
        /// <param name="image">The image</param>
        /// <returns>The colour; grey if there are none</returns>
        private static Color AverageColor(Bitmap image)
        {
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.A >= 200)
                    {
                        r += c.R;
                        g += c.G;
                        b += c.B;
                        n++;
                    }
                }
            }
            return n == 0 ? Color.Gray : Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
        }

        /// <summary>An image's shape in one flat colour, at its own size</summary>
        /// <param name="shape">The image</param>
        /// <param name="color">The colour</param>
        /// <returns>A new bitmap</returns>
        private static Bitmap Render(Bitmap shape, Color color)
        {
            Bitmap flat = new Bitmap(shape.Width, shape.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < shape.Height; y++)
            {
                for (int x = 0; x < shape.Width; x++)
                {
                    flat.SetPixel(x, y, Color.FromArgb(shape.GetPixel(x, y).A, color));
                }
            }
            return flat;
        }

        /// <summary>A custom icon's stored shape, loaded once and reloaded if its file changes</summary>
        /// <param name="name">Its key</param>
        /// <returns>The shape, or null if the file is missing or unreadable</returns>
        private static Bitmap Load(string name)
        {
            string path = PathOf(name);
            try
            {
                if (!File.Exists(path))
                    return null;
                DateTime written = File.GetLastWriteTimeUtc(path);
                Tuple<DateTime, Bitmap> cached;
                if (cache.TryGetValue(name, out cached) && cached.Item1 == written)
                    return cached.Item2;
                // copied, so the file isn't kept locked and can be replaced or deleted
                Bitmap shape;
                using (Image image = Image.FromFile(path))
                {
                    shape = new Bitmap(image);
                }
                cache[name] = Tuple.Create(written, shape);
                return shape;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load custom icon " + path, ex);
                return null;
            }
        }
    }
}
