using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using log4net;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Loads and saves the button bars (<see cref="ButtonBarList"/>) as ButtonBars.xml in SuperPuTTY's
    /// settings folder, next to Sessions.xml. When that folder is a shared drive, a team shares the same
    /// buttons; {key} placeholders in the buttons keep per-user values out of the shared file.
    /// Used by <see cref="Gui.ButtonBarStrip"/>.
    /// </summary>
    public static class ButtonBarStore
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ButtonBarStore));

        /// <summary>Name of the bar created when no file exists yet</summary>
        public const string DefaultBarName = "Default";

        /// <summary>
        /// Full path of ButtonBars.xml: in SettingsFolder (shared with Sessions.xml), or next to the
        /// user's SuperXPuTTY.settings if no settings folder is configured
        /// </summary>
        public static string FilePath
        {
            get
            {
                string folder = SuperPuTTY.Settings.SettingsFolder;
                if (string.IsNullOrEmpty(folder))
                {
                    folder = Path.GetDirectoryName(Properties.Settings.Default.SettingsFilePath ?? Path.Combine(Application.UserAppDataPath, "SuperXPuTTY.settings"));
                }
                return Path.Combine(folder, "ButtonBars.xml");
            }
        }

        /// <summary>
        /// Load the button bars. A missing or unreadable file gives a single empty "Default" bar,
        /// so the strip always has a bar to show.
        /// </summary>
        /// <returns>The button bars, with at least one bar</returns>
        public static ButtonBarList Load()
        {
            ButtonBarList list = null;
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        list = (ButtonBarList)new XmlSerializer(typeof(ButtonBarList)).Deserialize(stream);
                    }
                    Log.InfoFormat("Loaded {0} button bars from {1}", list.Bars.Count, path);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not load button bars from " + path, ex);
            }

            if (list == null)
            {
                list = new ButtonBarList();
            }
            if (!list.Bars.Any())
            {
                list.Bars.Add(new ButtonBar { Name = DefaultBarName });
            }
            return list;
        }

        /// <summary>Save the button bars, overwriting ButtonBars.xml</summary>
        /// <param name="list">The button bars to save</param>
        /// <returns>true if saved; false if the file could not be written (e.g. a read-only shared folder), which is logged</returns>
        public static bool Save(ButtonBarList list)
        {
            string path = FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // write to a temporary file first so a failed write can't leave a truncated file for the whole team
                string tempPath = path + ".tmp";
                using (FileStream stream = File.Create(tempPath))
                {
                    new XmlSerializer(typeof(ButtonBarList)).Serialize(stream, list);
                }
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                Log.InfoFormat("Saved {0} button bars to {1}", list.Bars.Count, path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save button bars to " + path, ex);
                return false;
            }
        }
    }
}
