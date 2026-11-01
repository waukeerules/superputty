using System;
using System.Drawing;
using System.Resources;
using log4net;
using SuperPutty.Gui;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.DockPanelSuite
{
    /// <summary>
    /// Document tabs as wide as their text, up to <see cref="MaxCharacters"/> characters. DockPanelSuite's tab strips cap
    /// every tab at a fixed 200 pixels, a private constant that isn't scaled for DPI or the tab font, so with SuperPuTTY's
    /// larger tab font a name like "192.168.0.50" was cut short. The strips here are copies of DockPanelSuite 3.1.1's
    /// VS2013DockPaneStrip (used by the VS2015 Light, Blue and Dark themes) and VS2005DockPaneStrip (Classic), MIT
    /// licensed (Resources/third-party/dockpanelsuite-LICENSE.txt), changed only where marked "SuperPuTTY". frmSuperPutty
    /// calls <see cref="Apply"/> on its theme before any pane exists. TabIcons and DocumentTabs reach into the strips by
    /// member and class name, which the copies keep.
    /// </summary>
    public static class WideTabs
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(WideTabs));

        /// <summary>The most characters of a tab's text shown before it's cut short with "..."</summary>
        public const int MaxCharacters = 30;

        /// <summary>
        /// Text as wide as the widest tab's: <see cref="MaxCharacters"/> digits (about an average character's width in
        /// the tab font), with the blanks TabIcons puts in front for the icon
        /// </summary>
        public static string SampleText
        {
            get { return TabIcons.WithPad(new string('0', MaxCharacters)); }
        }

        /// <summary>
        /// Give a theme the wide-tab strip that matches the one it would have made; other themes are left alone.
        /// Call it before the theme is given to the DockPanel, or at least before any pane is created.
        /// </summary>
        /// <param name="theme">The theme</param>
        public static void Apply(ThemeBase theme)
        {
            string factory = theme?.Extender?.DockPaneStripFactory?.GetType().Name;
            if (factory == "VS2013DockPaneStripFactory")
            {
                theme.Extender.DockPaneStripFactory = new VS2013Factory();
            }
            else if (factory == "VS2005DockPaneStripFactory")
            {
                theme.Extender.DockPaneStripFactory = new VS2005Factory();
            }
            else
            {
                Log.InfoFormat("Tabs keep DockPanelSuite's width limit with the strip factory {0}", factory ?? "(none)");
            }
        }

        /// <summary>The Classic strip's button images, from the ThemeVS2005 assembly that VS2005Theme comes from</summary>
        private static readonly ResourceManager ClassicResources =
            new ResourceManager("WeifenLuo.WinFormsUI.ThemeVS2005.Resources", typeof(VS2005Theme).Assembly);

        /// <summary>One of the Classic strip's button images (its copy can't reach the theme's internal Resources class)</summary>
        /// <param name="name">e.g. "DockPane_Close"</param>
        /// <returns>The image, or null if the theme assembly doesn't have it</returns>
        public static Bitmap ClassicImage(string name)
        {
            try
            {
                return ClassicResources.GetObject(name) as Bitmap;
            }
            catch (MissingManifestResourceException ex)
            {
                Log.Warn("No Classic tab strip image " + name, ex);
                return null;
            }
        }

        /// <summary>Makes the wide-tab copy of the VS2015 themes' strip</summary>
        private class VS2013Factory : DockPanelExtender.IDockPaneStripFactory
        {
            /// <summary>Create a pane's strip</summary>
            /// <param name="pane">The pane</param>
            /// <returns>The strip</returns>
            public DockPaneStripBase CreateDockPaneStrip(DockPane pane)
            {
                return new VS2013DockPaneStrip(pane);
            }
        }

        /// <summary>Makes the wide-tab copy of the Classic theme's strip</summary>
        private class VS2005Factory : DockPanelExtender.IDockPaneStripFactory
        {
            /// <summary>Create a pane's strip</summary>
            /// <param name="pane">The pane</param>
            /// <returns>The strip</returns>
            public DockPaneStripBase CreateDockPaneStrip(DockPane pane)
            {
                return new VS2005DockPaneStrip(pane);
            }
        }
    }
}
