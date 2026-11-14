using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
using log4net;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Reloads the sessions when Sessions.xml is changed outside SuperPuTTY: by a teammate's copy saving to the same
    /// shared settings folder, or by a script or text editor. SuperPuTTY.LoadSessions calls <see cref="Watch"/> after
    /// reading the file and SuperPuTTY.SaveSessions calls <see cref="Saved"/> after writing it, each recording the
    /// file's SHA-256, so SuperPuTTY's own saves never cause a reload. A FileSystemWatcher flags changes as they happen,
    /// and the file's time and size are compared every 30 seconds too, since watchers miss changes on some network
    /// shares. The file is reloaded only when its contents' hash differs (a copy tool rewriting it unchanged doesn't
    /// count), and not while a dialog is open, so an edit in progress isn't lost. Turned off with
    /// Settings.ReloadSessionsWhenChanged (Tools > Options > GUI).
    /// </summary>
    public static class SessionsFileWatcher
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionsFileWatcher));

        /// <summary>How often the file's time and size are compared, in ticks of <see cref="timer"/> (one a second)</summary>
        private const int PollEveryTicks = 30;

        /// <summary>The file being watched</summary>
        private static string path;

        /// <summary>The SHA-256 of the contents last read or written by SuperPuTTY; null if there was no file</summary>
        private static string knownHash;

        /// <summary>The file's last write time and size when <see cref="knownHash"/> was taken</summary>
        private static DateTime knownWriteTime;

        /// <summary>The file's size when <see cref="knownHash"/> was taken</summary>
        private static long knownLength = -1;

        /// <summary>Notices changes as they happen; null where it can't watch the folder</summary>
        private static FileSystemWatcher watcher;

        /// <summary>Ticks once a second on the UI thread, to act on flagged changes and to poll</summary>
        private static Timer timer;

        /// <summary>Set by the watcher (on its own thread) when the file may have changed</summary>
        private static volatile bool changed;

        /// <summary>Ticks since the last poll</summary>
        private static int ticks;

        /// <summary>Raised on the UI thread after the sessions were reloaded because the file changed</summary>
        public static event EventHandler Reloaded;

        /// <summary>
        /// Start (or move) watching a sessions file, and remember its contents as read now. Called on the UI thread.
        /// </summary>
        /// <param name="file">The file SuperPuTTY just read</param>
        public static void Watch(string file)
        {
            if (!string.Equals(path, file, StringComparison.OrdinalIgnoreCase))
            {
                path = file;
                StartWatcher();
            }
            Remember();
            if (timer == null && !waitingForMessageLoop)
            {
                if (Application.MessageLoop)
                {
                    StartTimer();
                }
                else
                {
                    // at start-up the sessions are read before Program.Main has finished setting up WinForms: a
                    // timer made now would create a window too early (SetCompatibleTextRenderingDefault then throws
                    // and the application quits), so start it once the message loop first goes idle
                    waitingForMessageLoop = true;
                    Application.Idle += StartTimerWhenIdle;
                }
            }
        }

        /// <summary>Whether the timer waits for the message loop to start</summary>
        private static bool waitingForMessageLoop;

        /// <summary>Start the timer the first time the message loop is idle</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        private static void StartTimerWhenIdle(object sender, EventArgs e)
        {
            Application.Idle -= StartTimerWhenIdle;
            waitingForMessageLoop = false;
            if (timer == null && path != null)
            {
                StartTimer();
            }
        }

        /// <summary>The UI thread's context, where a check's result is acted on</summary>
        private static SynchronizationContext ui;

        /// <summary>Set while a check of the file runs in the background, so checks don't pile up on a slow share</summary>
        private static volatile bool checking;

        /// <summary>Start the once-a-second check on the UI thread</summary>
        private static void StartTimer()
        {
            // the checks' results are acted on here, on the UI thread; a WinForms context made here if there isn't one yet
            ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
            timer = new Timer { Interval = 1000 };
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        /// <summary>Remember the file's contents as SuperPuTTY just wrote them, so this save doesn't cause a reload</summary>
        public static void Saved()
        {
            Remember();
        }

        /// <summary>Stop watching (tests)</summary>
        public static void Stop()
        {
            timer?.Stop();
            timer?.Dispose();
            timer = null;
            watcher?.Dispose();
            watcher = null;
            path = null;
        }

        /// <summary>Record the file's hash, time and size as SuperPuTTY's own</summary>
        private static void Remember()
        {
            changed = false;
            knownHash = Hash(path);
            FileInfo info = new FileInfo(path ?? string.Empty);
            knownWriteTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            knownLength = info.Exists ? info.Length : -1;
        }

        /// <summary>Watch the folder for changes to the file; where that fails (some shares), polling still works</summary>
        private static void StartWatcher()
        {
            watcher?.Dispose();
            watcher = null;
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return;
                watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
                };
                FileSystemEventHandler flag = (s, e) => changed = true;
                watcher.Changed += flag;
                watcher.Created += flag;
                watcher.Deleted += flag;
                watcher.Renamed += (s, e) => changed = true;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Warn("Can't watch " + path + " for changes; checking it every " + PollEveryTicks + " s instead", ex);
                watcher?.Dispose();
                watcher = null;
            }
        }

        /// <summary>What a background check found</summary>
        private sealed class CheckResult
        {
            /// <summary>Nothing to do: the file's time and size are as known</summary>
            public bool Unchanged;

            /// <summary>Whether the file exists</summary>
            public bool Exists;

            /// <summary>Its last write time (UTC), when it exists</summary>
            public DateTime WriteTime;

            /// <summary>Its size, or -1</summary>
            public long Length = -1;

            /// <summary>Its contents' hash; null if it's missing or couldn't be read</summary>
            public string Hash;
        }

        /// <summary>
        /// Once a second: if the watcher flagged a change, or every <see cref="PollEveryTicks"/> seconds, check the
        /// file in the background (<see cref="Check"/>) and act on what it finds back on this thread (<see cref="Apply"/>).
        /// The check used to run here, on the UI thread, where a network drive that had gone away could freeze the
        /// window for many seconds every poll.
        /// </summary>
        private static void Tick()
        {
            if (path == null || !SuperPuTTY.Settings.ReloadSessionsWhenChanged || checking)
                return;
            bool poll = ++ticks >= PollEveryTicks;
            if (!changed && !poll)
                return;
            if (Application.OpenForms.Cast<Form>().Any(f => f.Modal))
            {
                // a session (or anything) is being edited: try again when the dialog is closed
                return;
            }
            if (poll)
                ticks = 0;
            bool flagged = changed;
            changed = false;
            string file = path;
            long length = knownLength;
            DateTime writeTime = knownWriteTime;
            SynchronizationContext context = ui;
            checking = true;
            Task.Run(() => Check(file, flagged, length, writeTime)).ContinueWith(task =>
            {
                CheckResult result = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                Action apply = () =>
                {
                    checking = false;
                    if (result != null)
                        Apply(file, result);
                };
                context.Post(_ => apply(), null);
            });
        }

        /// <summary>
        /// Look at the file, off the UI thread: its time and size, and when those differ from what's known (or the
        /// watcher flagged it), its hash
        /// </summary>
        /// <param name="file">The file</param>
        /// <param name="flagged">The watcher saw a change</param>
        /// <param name="knownSize">The size last known</param>
        /// <param name="knownTime">The write time last known</param>
        /// <returns>What it found</returns>
        private static CheckResult Check(string file, bool flagged, long knownSize, DateTime knownTime)
        {
            CheckResult result = new CheckResult();
            try
            {
                FileInfo info = new FileInfo(file);
                result.Exists = info.Exists;
                result.Length = info.Exists ? info.Length : -1;
                result.WriteTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look at " + file, ex);
            }
            if (!flagged && result.Length == knownSize && (!result.Exists || result.WriteTime == knownTime))
            {
                result.Unchanged = true;
                return result;
            }
            result.Hash = Hash(file);
            return result;
        }

        /// <summary>Act on a check, on the UI thread: reload the sessions if the file's contents changed</summary>
        /// <param name="file">The file checked; nothing is done if another is watched by now</param>
        /// <param name="result">What the check found</param>
        private static void Apply(string file, CheckResult result)
        {
            if (result.Unchanged || !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                return;
            if (result.Hash == null && result.Exists)
            {
                // being written: look again next second
                changed = true;
                return;
            }
            if (result.Hash == knownHash)
            {
                // touched or rewritten, but the same sessions
                knownWriteTime = result.WriteTime;
                knownLength = result.Length;
                return;
            }
            Log.InfoFormat("{0} was changed outside SuperPuTTY (or its folder went away or came back); reloading the sessions", path);
            SuperPuTTY.LoadSessions();
            SuperPuTTY.ReportStatus(SuperPuTTY.SavedSessionsOffline
                ? "The settings folder can't be reached; Saved sessions are shown from the copy on this computer"
                : "Sessions.xml was changed outside SuperXPuTTY; sessions reloaded");
            Reloaded?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The SHA-256 of a file's contents</summary>
        /// <param name="file">The file</param>
        /// <returns>The hash in hex; null if the file doesn't exist or can't be read right now (e.g. being written)</returns>
        public static string Hash(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return null;
            try
            {
                using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha = new SHA256CryptoServiceProvider())
                {
                    // the Windows provider: MD5, and .NET's own SHA-256, are refused where FIPS mode is enforced
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
