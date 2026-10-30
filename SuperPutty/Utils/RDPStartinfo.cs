/*
 * Copyright (c) 2017 Anish Sane https://stackoverflow.com/users/793796/anishsane
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using log4net;
using SuperPutty.Data;
using System.Text.RegularExpressions;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace SuperPutty.Utils
{

    /// <summary>
    /// Helper class for RDP support.
    /// <para>
    /// For FreeRDP the remote desktop is sized to the tab: ApplicationPanel calls <see cref="GetArgs"/> with the
    /// tab's size just before starting the client. mstsc is started full screen as upstream does; RDP tabs
    /// normally use the built-in Remote Desktop control (Gui.RdpControl) instead, which fits the tab.
    /// </para>
    /// </summary>
    public class RDPStartInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RDPStartInfo));

        /// <summary>Smallest desktop size mstsc accepts</summary>
        private const int MinDesktopSize = 200;

        /// <summary>Largest desktop size mstsc accepts</summary>
        private const int MaxDesktopSize = 8192;

        private SessionData session;

        /// <summary>True for FreeRDP (wfreerdp.exe), false for Microsoft's mstsc</summary>
        private readonly bool isFreeRdp;

        public RDPStartInfo(SessionData session, String binName)
        {
            this.session = session;
            this.isFreeRdp = binName.EndsWith("\\wfreerdp.exe");

            // a fallback for callers that don't know the tab size, e.g. "Connect External"
            this.Args = this.GetArgs(Screen.PrimaryScreen.WorkingArea.Size);
            this.StartingDir = "%userprofile%\\Desktop";
        }

        public string Args { get; set; }
        public string StartingDir { get; set; }

        /// <summary>Build the command line for a remote desktop of the given size</summary>
        /// <param name="desktopSize">The size of the area the remote desktop is shown in, in pixels</param>
        /// <returns>The arguments for mstsc or wfreerdp</returns>
        public string GetArgs(Size desktopSize)
        {
            int width = Math.Max(MinDesktopSize, Math.Min(MaxDesktopSize, desktopSize.Width));
            int height = Math.Max(MinDesktopSize, Math.Min(MaxDesktopSize, desktopSize.Height));
            string args;

            if (this.isFreeRdp)
            {
                // /disp lets FreeRDP change the remote resolution when the window is resized
                args = "/cert-ignore /size:" + width + "x" + height + " +window-drag /disp /workarea /network:wan +auto-reconnect /auto-reconnect-max-retries:0 +bitmap-cache /v:" + session.Host;

                if (session.Port != 0)
                    args += ":" + session.Port.ToString() + " ";

                if (session.Username != "")
                    args += "/u:"+session.Username;

                if (!String.IsNullOrEmpty(session.ExtraArgs))
                    args += session.ExtraArgs + " ";
            }
            else
            {
                // upstream's mstsc command line: full screen, which is the window ApplicationPanel can embed, so
                // the size can't be applied. RDP tabs normally use the built-in client (Gui.RdpControl) instead.
                args = "-v:" + session.Host;

                if (session.Port != 0)
                    args += ":" + session.Port.ToString() + " ";

                if (!String.IsNullOrEmpty(session.ExtraArgs))
                    args += session.ExtraArgs + " ";

                args += "/span";
            }
            return args;
        }

    }
}
