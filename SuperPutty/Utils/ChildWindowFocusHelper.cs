using System;
using System.Windows.Forms;
using log4net;
using System.Collections.Generic;
using System.Linq;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Utils
{
    public class ChildWindowFocusHelper : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ChildWindowFocusHelper));

        private int m_shellHookNotify;
        private bool m_externalWindow = false;

        /// <summary>
        /// The terminal tabs. Their window handles are read when needed rather than stored: a tab is added
        /// before its application starts (SuperPuTTY.OpenProtoSession starts it after Show), so the handle
        /// is still zero here, and keying by it made every second new tab throw a duplicate-key exception.
        /// </summary>
        private readonly List<ctlPuttyPanel> childPanels = new List<ctlPuttyPanel>();

        public ChildWindowFocusHelper(frmSuperPutty form)
        {
            this.MainForm = form;
            this.MainForm.ResizeEnd += HandleResizeEnd;

            foreach (IDockContent doc in this.MainForm.DockPanel.Contents)
            {
                ctlPuttyPanel pp = doc as ctlPuttyPanel;
                if (pp != null)
                {
                    this.childPanels.Add(pp);
                }
            }
            this.MainForm.DockPanel.ContentAdded += DockPanel_ContentAdded;
            this.MainForm.DockPanel.ContentRemoved += DockPanel_ContentRemoved;
        }

        void DockPanel_ContentAdded(object sender, DockContentEventArgs e)
        {
            ctlPuttyPanel pp = e.Content as ctlPuttyPanel;
            if (pp != null && !this.childPanels.Contains(pp))
            {
                this.childPanels.Add(pp);
            }
        }

        void DockPanel_ContentRemoved(object sender, DockContentEventArgs e)
        {
            ctlPuttyPanel pp = e.Content as ctlPuttyPanel;
            if (pp != null)
            {
                this.childPanels.Remove(pp);
            }
        }

        public void Start()
        {
            this.m_shellHookNotify = NativeMethods.RegisterWindowMessage("SHELLHOOK");
            NativeMethods.RegisterShellHookWindow(this.MainForm.Handle);
        }

        public void Dispose()
        {
            this.MainForm.ResizeEnd -= HandleResizeEnd;
            this.MainForm.DockPanel.ContentAdded -= DockPanel_ContentAdded;
            this.MainForm.DockPanel.ContentRemoved -= DockPanel_ContentRemoved;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="m"></param>
        /// <returns>true if base winproc should be called</returns>
        public bool WndProcForFocus(ref Message m)
        {
            NativeMethods.WM wm = (NativeMethods.WM) m.Msg;

            if (wm != NativeMethods.WM.GETICON && wm != NativeMethods.WM.NCHITTEST 
                && wm != NativeMethods.WM.NCMOUSEMOVE && wm != NativeMethods.WM.NCMOUSELEAVE
                && wm != NativeMethods.WM.GETTEXT && wm != NativeMethods.WM.GETTEXTLENGTH 
                && wm != NativeMethods.WM.MOUSEMOVE && wm != NativeMethods.WM.SETCURSOR
                && wm != NativeMethods.WM.WINDOWPOSCHANGING && wm != NativeMethods.WM.WINDOWPOSCHANGED)
            {
                Log.DebugFormat("WndProcForFocus: shellhook={3}, wm={0}, wParam={1}, lParam={2}", wm, m.WParam, m.LParam, m.Msg == m_shellHookNotify);
            }
            switch (wm)
            {
                case NativeMethods.WM.ACTIVATE:
                    // http://msdn.microsoft.com/en-us/library/windows/desktop/ms646274(v=vs.85).aspx
                    /*
                    if (m.WParam.ToInt32() > 0)
                    {
                        NativeMethods.BringWindowToTop(this.MainForm.Handle);
                        this.MainForm.FocusActiveDocument("ACTIVATE");
                        return true;
                    }
                     * */
                    break;
                case NativeMethods.WM.ACTIVATEAPP:
                    // Never allow this window to display itself as inactive
                    //NativeMethods.DefWindowProc(this.MainForm.Handle, m.Msg, (IntPtr)1, m.LParam);
                    //m.Result = (IntPtr)1;
                    //return false;
                    //return true;
                    break;
                case NativeMethods.WM.NCACTIVATE:
                    // Never allow this window to display itself as inactive
                    // http://msdn.microsoft.com/en-us/library/windows/desktop/ms632633(v=vs.85).aspx
                    NativeMethods.DefWindowProc(this.MainForm.Handle, m.Msg, (IntPtr)1, m.LParam);
                    m.Result = (IntPtr)1;
                    return false;
                case NativeMethods.WM.SYSCOMMAND:
                    // Check for maximizing and restoring from maxed.
                    // Removing the last 4 bits. This is necessary because
                    // maximizing by double click gives you 0xF032, not 0xF030.
                    switch ((int)m.WParam & 0xFFF0)
                    {
                        case NativeMethods.SC_MAXIMIZE:
                        case NativeMethods.SC_RESTORE:
                            //Log.InfoFormat("SysCommand: {0}", m.WParam);
                            this.MainForm.BeginInvoke(new Action<string>(this.MainForm.FocusActiveDocument), "SYSCommand-Restore");
                            break;
                    }
                    break;
                default:

                    if (m.Msg == m_shellHookNotify)
                    {
                        //Log.InfoFormat("ShellHook:  param={0}", m.WParam.ToInt32());
                        switch (m.WParam.ToInt32())
                        {
                            case 4:
                            case 32772:
                                IntPtr current = NativeMethods.GetForegroundWindow();
                                if (current != this.MainForm.Handle && !this.ContainsChild(current))
                                {
                                    m_externalWindow = true;
                                } 
                                else if (m_externalWindow)
                                {
                                    m_externalWindow = false;
                                    NativeMethods.BringWindowToTop(this.MainForm.Handle);
                                    // coming back by clicking a box, list or tree in SuperPuTTY: the click already put the
                                    // focus there, and taking it to the terminal lost what was typed next
                                    if (!this.IsClickOnInputControl())
                                    {
                                        this.MainForm.FocusActiveDocument("SHELLHOOK");
                                    }
                                    //return false;
                                }
                                else if (current == this.MainForm.Handle)
                                {
                                    // round trip alt-tab or first alt-tab in 2x sequence...also when menus popup
                                    //NativeMethods.SetForegroundWindow(this.MainForm.Handle);
                                    //this.MainForm.FocusActiveDocument("SHELLHOOK2");
                                }
                                //{
                                //    //Log.Info("### hwd=" + this.MainForm.Handle + ", m.h=" + m.HWnd + ", blah=" + DesktopWindow.GetFirstDesktopWindow().Title);
                                //    foreach (DesktopWindow dw in DesktopWindow.GetDesktopWindows())
                                //    {
                                //        //Log.Info("blah=" + dw.Title);
                                //    }
                                //}
                                break;
                            default:
                                break;
                        }
                    }
                    break;
            }

            return true;
        }

        /// <summary>
        /// Whether the window is being activated by a click on one of SuperPuTTY's own controls that takes typing or
        /// arrow keys (the toolbar's Host, Login and Password boxes, the Sessions search box and tree, ...), so the
        /// focus should stay where the click put it. Checked when the shell reports the activation, while the button is
        /// still down; coming back with Alt+Tab, the taskbar or the title bar still returns to the terminal.
        /// </summary>
        /// <returns>true if a mouse button is down over such a control</returns>
        bool IsClickOnInputControl()
        {
            bool buttonDown = new[] { Keys.LButton, Keys.RButton, Keys.MButton }.Any(b => (NativeMethods.GetAsyncKeyState((int)b) & 0x8000) != 0);
            if (!buttonDown)
            {
                return false;
            }
            // only finds controls of this process; PuTTY's own window isn't one
            Control control = Control.FromChildHandle(NativeMethods.WindowFromPoint(Cursor.Position));
            return control is TextBoxBase || control is ComboBox || control is TreeView || control is ListView
                || control is ListBox || control is DataGridView || control is UpDownBase;
        }

        /// <summary>Whether a window is the embedded application of one of the terminal tabs</summary>
        /// <param name="childHandle">The window to look for, e.g. the foreground window</param>
        /// <returns>true if a tab's application window has this handle</returns>
        bool ContainsChild(IntPtr childHandle)
        {
            return childHandle != IntPtr.Zero && this.childPanels.Any(pp => pp.AppPanel.AppWindowHandle == childHandle);
        }

        /// <summary>
        /// Handles resizing of the super putty window AND moving the form from the title bar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HandleResizeEnd(Object sender, EventArgs e)
        {
            this.MainForm.FocusActiveDocument("ResizeEnd");
        }

        frmSuperPutty MainForm { get; set; }
    }
}
