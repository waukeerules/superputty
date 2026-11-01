using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Chooses and reads the log file each PuTTY session writes (PuTTY's -sessionlog), which is how SuperPuTTY can see
    /// a session's output: PuTTY draws the terminal itself. With "Session logs" on (Tools -> Options -> Advanced) the
    /// logs are kept in the chosen folder; otherwise each session writes a temporary log in %TEMP%\SuperPuTTY\logs,
    /// which is deleted after the tab closes. SPSL's WAITFOR (ctlPuttyPanel.WaitForOutput) reads these logs.
    /// </summary>
    public static class SessionLog
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionLog));

        /// <summary>Where temporary logs go when "Session logs" is off</summary>
        public static readonly string TemporaryFolder = Path.Combine(Path.GetTempPath(), "SuperPuTTY", "logs");

        /// <summary>How long after its tab closes a temporary log is deleted, so PuTTY has exited and released it</summary>
        private const int DeleteDelayMs = 5000;

        /// <summary>The folder kept logs go to: the chosen folder, or Documents\SuperXPuTTY\logs</summary>
        public static string KeptLogFolder
        {
            get
            {
                string folder = SuperPuTTY.Settings.SessionLogFolder;
                return string.IsNullOrWhiteSpace(folder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SuperXPuTTY", "logs")
                    : folder.Trim();
            }
        }

        /// <summary>
        /// A new, unique log file path for a session, in the kept-log folder or the temporary folder. The name has the
        /// session name and start time, so PuTTY never finds an existing file and never asks whether to overwrite it.
        /// </summary>
        /// <param name="session">The session being started</param>
        /// <returns>The full path; its folder exists</returns>
        public static string NewLogPath(SessionData session)
        {
            string folder = SuperPuTTY.Settings.SessionLogEnabled ? KeptLogFolder : TemporaryFolder;
            Directory.CreateDirectory(folder);
            string name = Regex.Replace(session.SessionName ?? session.Host ?? "session", @"[^\w\-. ]", "_");
            return Path.Combine(folder, string.Format("{0}_{1:yyyyMMdd_HHmmss_fff}.log", name, DateTime.Now));
        }

        /// <summary>Whether a log is a temporary one (in <see cref="TemporaryFolder"/>) rather than a kept one</summary>
        /// <param name="path">The log file path</param>
        /// <returns>true for a temporary log</returns>
        public static bool IsTemporary(string path)
        {
            return !string.IsNullOrEmpty(path)
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(TemporaryFolder), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Delete a temporary log a few seconds after its tab closes; kept logs are left alone</summary>
        /// <param name="path">The log file path; may be null</param>
        public static void DeleteLaterIfTemporary(string path)
        {
            if (!IsTemporary(path))
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(DeleteDelayMs);
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    // still open; PurgeTemporaryLogs removes it next time SuperPuTTY starts
                    Log.DebugFormat("Could not delete temporary log {0}: {1}", path, ex.Message);
                }
            });
        }

        /// <summary>Delete temporary logs left from earlier runs (older than a day). Called once at startup.</summary>
        public static void PurgeTemporaryLogs()
        {
            try
            {
                if (!Directory.Exists(TemporaryFolder))
                    return;
                foreach (FileInfo file in new DirectoryInfo(TemporaryFolder).GetFiles("*.log"))
                {
                    if (file.LastWriteTime < DateTime.Now.AddDays(-1))
                    {
                        try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not clean up temporary session logs", ex);
            }
        }

        /// <summary>
        /// Read what a log has gained since a byte position, without disturbing PuTTY, which keeps writing to it
        /// </summary>
        /// <param name="path">The log file path</param>
        /// <param name="position">Where to start reading, in bytes</param>
        /// <returns>The new text, decoded as UTF-8; empty if there is none or the file doesn't exist yet</returns>
        public static string ReadFrom(string path, long position)
        {
            if (!File.Exists(path))
                return string.Empty;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (position >= stream.Length)
                    return string.Empty;
                stream.Seek(position, SeekOrigin.Begin);
                byte[] buffer = new byte[stream.Length - position];
                int read = stream.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer, 0, read);
            }
        }

        /// <summary>
        /// Read up to <paramref name="max"/> raw bytes of a log from a byte position, without disturbing PuTTY. Used by
        /// Gui.HighlightedOutputPanel, which hands the bytes to a terminal emulator; unlike <see cref="ReadFrom"/> it doesn't
        /// decode, so a UTF-8 character split between two reads isn't garbled.
        /// </summary>
        /// <param name="path">The log file path</param>
        /// <param name="position">Where to start reading, in bytes</param>
        /// <param name="max">The most bytes to return</param>
        /// <returns>The bytes; empty if there are none or the file doesn't exist</returns>
        public static byte[] ReadBytesFrom(string path, long position, int max)
        {
            if (!File.Exists(path))
                return new byte[0];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (position >= stream.Length)
                    return new byte[0];
                stream.Seek(position, SeekOrigin.Begin);
                byte[] buffer = new byte[Math.Min(max, stream.Length - position)];
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length)
                    Array.Resize(ref buffer, read);
                return buffer;
            }
        }

        /// <summary>Terminal control sequences that can be left in a log: colours and cursor moves ("[01;34m"), with or without the ESC</summary>
        private static readonly Regex ControlSequence = new Regex(@"\x1b?\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        /// <summary>
        /// Window title sequences that can be left in a log ("]0;root@host: ~"), only when they still end with BEL or
        /// ESC \. Without that end there's no telling where the title stops and real output (e.g. the prompt and
        /// command after it) starts, so the text is kept rather than risk hiding output.
        /// </summary>
        private static readonly Regex TitleSequence = new Regex(@"\x1b?\][0-9]+;[^\a\x1b\n]*(\a|\x1b\\)", RegexOptions.Compiled);

        /// <summary>
        /// Turn a log's text into the lines a person would have seen: control and title sequences removed, other
        /// control characters dropped, carriage returns ignored. Used by "Search Output...".
        /// </summary>
        /// <param name="text">The raw log text</param>
        /// <returns>The lines, in order</returns>
        public static string[] CleanForDisplay(string text)
        {
            string cleaned = TitleSequence.Replace(ControlSequence.Replace(text ?? string.Empty, string.Empty), string.Empty);
            StringBuilder visible = new StringBuilder(cleaned.Length);
            foreach (char c in cleaned)
            {
                if (c == '\n' || c == '\t' || !char.IsControl(c))
                {
                    visible.Append(c);
                }
            }
            return visible.ToString().Split('\n');
        }

        /// <summary>The current length of a log in bytes</summary>
        /// <param name="path">The log file path</param>
        /// <returns>The length, or 0 if the file doesn't exist yet</returns>
        public static long LengthOf(string path)
        {
            FileInfo file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
    }
}
