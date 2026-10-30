using System;

namespace SuperPutty.Utils
{
    /// <summary>
    /// An application hosted inside a tab as a WinForms control, instead of an external process whose window
    /// is captured (the usual way <see cref="ApplicationPanel"/> works). The implementing class must also be a
    /// <see cref="System.Windows.Forms.Control"/>; ApplicationPanel adds it to the tab and fills the tab with it.
    /// Used by <see cref="Gui.RdpControl"/>.
    /// </summary>
    public interface IEmbeddedApplication
    {
        /// <summary>Start the application (e.g. connect); called once the control is in the tab and laid out</summary>
        void Start();

        /// <summary>Give the application the keyboard focus</summary>
        void FocusApplication();

        /// <summary>Raised when the application ends (disconnected, logged off, failed); the tab then closes</summary>
        event EventHandler Ended;
    }
}
