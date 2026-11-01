using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Makes the first click on a menu, toolbar or tab strip count when SuperPuTTY isn't the active window (another
    /// program, or the embedded PuTTY window, has the focus). A ToolStrip (and so a MenuStrip) answers
    /// WM_MOUSEACTIVATE with MA_ACTIVATEANDEAT, so that click only activated the window and the menu needed a second
    /// click. ButtonBarStrip fixes this in its own WndProc; this does the same for the other strips without
    /// subclassing them (they're made by the designer and by DockPanelSuite). frmSuperPutty calls <see cref="Apply"/>
    /// on itself, and DocumentTabs on each pane's tab strip, including those in floating windows.
    /// </summary>
    public static class ClickThrough
    {
        /// <summary>WM_MOUSEACTIVATE result: activate the window and pass the click on to the control</summary>
        private const int MA_ACTIVATE = 1;

        /// <summary>WM_MOUSEACTIVATE result: activate the window and discard the click</summary>
        private const int MA_ACTIVATEANDEAT = 2;

        /// <summary>Controls already handled, so each gets one hook and containers one ControlAdded handler</summary>
        private static readonly HashSet<Control> handled = new HashSet<Control>();

        /// <summary>
        /// Hook every menu, toolbar, status strip and tab strip in a control and its children, and those added later
        /// </summary>
        /// <param name="root">A form or container</param>
        public static void Apply(Control root)
        {
            if (root == null || !handled.Add(root))
                return;
            root.Disposed += (s, e) => handled.Remove(root);
            if (root is ToolStrip || root is DockPaneStripBase)
            {
                // ButtonBarStrip already does this itself
                if (!(root is ButtonBarStrip))
                {
                    Hook(root);
                }
                return;
            }
            root.ControlAdded += (s, e) => Apply(e.Control);
            foreach (Control child in root.Controls)
            {
                Apply(child);
            }
        }

        /// <summary>Listen to a control's window messages from when it has a window until it's destroyed</summary>
        /// <param name="control">The strip</param>
        private static void Hook(Control control)
        {
            MouseActivateHook hook = new MouseActivateHook();
            if (control.IsHandleCreated)
            {
                hook.AssignHandle(control.Handle);
            }
            control.HandleCreated += (s, e) => hook.AssignHandle(control.Handle);
            control.HandleDestroyed += (s, e) => hook.ReleaseHandle();
        }

        /// <summary>Turns "activate and discard the click" into "activate and use the click"</summary>
        private class MouseActivateHook : NativeWindow
        {
            /// <summary>Let the control answer, then keep the click if it was going to discard it</summary>
            /// <param name="m">The window message</param>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == (int)NativeMethods.WM.MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT)
                {
                    m.Result = (IntPtr)MA_ACTIVATE;
                }
            }
        }
    }
}
