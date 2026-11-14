using System.Diagnostics;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Opens web links, documents and folders with the program Windows has for them. On .NET Framework
    /// Process.Start("https://...") did that, because UseShellExecute defaulted to true; on .NET 10 it defaults to
    /// false, and the same call throws "The system cannot find the file specified". Called wherever SuperPuTTY
    /// opens a link or a file (About box, Options, Help, the update check, the session log, terminal links);
    /// programs started by their exe path still use Process.Start directly.
    /// </summary>
    public static class ShellOpen
    {
        /// <summary>Open a URL, document or folder with its default program</summary>
        /// <param name="target">The URL or path to open</param>
        /// <exception cref="System.ComponentModel.Win32Exception">Nothing is registered to open it, or it doesn't exist</exception>
        public static void Start(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }
}
