using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SuperPutty.Utils
{
    /// <summary>
    /// One SPSL script running in a tab (ctlPuttyPanel.RunScript): its progress, for the status-bar indicator, and a
    /// way to stop it. The script thread updates <see cref="LineNumber"/> and <see cref="CurrentStep"/> as it goes and
    /// checks <see cref="Token"/> between lines and while waiting; frmSuperPutty polls <see cref="Running"/> to show it.
    /// </summary>
    public class ScriptRun
    {
        /// <summary>Scripts running now; guarded by locking itself</summary>
        private static readonly List<ScriptRun> running = new List<ScriptRun>();

        /// <summary>Signalled when the user stops the script</summary>
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        /// <summary>Create the record for a script about to start; it's listed once <see cref="Begin"/> is called</summary>
        /// <param name="name">What the script is, e.g. a button label or "Login script"</param>
        /// <param name="owner">The tab it runs in</param>
        /// <param name="tabName">The tab's name, for display</param>
        /// <param name="lineCount">How many command lines the script has (not counting comments and blank lines)</param>
        /// <param name="source">What started it, e.g. the button bar's CommandButton, so that button can show a spinner; may be null</param>
        public ScriptRun(string name, object owner, string tabName, int lineCount, object source = null)
        {
            this.Name = name;
            this.Owner = owner;
            this.Source = source;
            this.TabName = tabName;
            this.LineCount = lineCount;
            this.Started = DateTime.Now;
            this.CurrentStep = "starting";
        }

        /// <summary>What the script is, e.g. a button label</summary>
        public string Name { get; private set; }

        /// <summary>The tab the script runs in, so its scripts can be stopped when it closes</summary>
        public object Owner { get; private set; }

        /// <summary>What started the script, e.g. a button bar CommandButton; null for a session's own login script</summary>
        public object Source { get; private set; }

        /// <summary>The tab's name, for display</summary>
        public string TabName { get; private set; }

        /// <summary>How many command lines the script has</summary>
        public int LineCount { get; private set; }

        /// <summary>The command line being run, counting from 1</summary>
        public int LineNumber { get; set; }

        /// <summary>The command being run, e.g. "WAITFOR password:"; never a SENDLINE's text, which may be a secret</summary>
        public string CurrentStep { get; set; }

        /// <summary>When the script started</summary>
        public DateTime Started { get; private set; }

        /// <summary>Cancelled when the user stops the script</summary>
        public CancellationToken Token { get { return this.cancellation.Token; } }

        /// <summary>Whether the user has stopped the script</summary>
        public bool IsCancelled { get { return this.cancellation.IsCancellationRequested; } }

        /// <summary>A copy of the list of scripts running now</summary>
        public static IList<ScriptRun> Running
        {
            get { lock (running) { return running.ToList(); } }
        }

        /// <summary>Stop the script: it stops before its next line, or straight away if it's waiting</summary>
        public void Cancel()
        {
            this.cancellation.Cancel();
        }

        /// <summary>Stop every running script, or every script in one tab</summary>
        /// <param name="owner">Only scripts in this tab; null for all</param>
        public static void CancelAll(object owner = null)
        {
            foreach (ScriptRun run in Running.Where(r => owner == null || r.Owner == owner))
            {
                run.Cancel();
            }
        }

        /// <summary>List the script as running</summary>
        public void Begin()
        {
            lock (running) { running.Add(this); }
        }

        /// <summary>Remove the script from the list when it ends, however it ends</summary>
        public void End()
        {
            // the token source isn't disposed: a Stop clicked in a menu opened just before the script ended must not throw
            lock (running) { running.Remove(this); }
        }

        /// <summary>One line describing the script's progress, e.g. "Login (r1): line 2/4 · WAITFOR password: · 8 s"</summary>
        /// <returns>The description</returns>
        public override string ToString()
        {
            return string.Format("{0} ({1}): line {2}/{3} · {4} · {5:0} s",
                this.Name, this.TabName, this.LineNumber, this.LineCount, this.CurrentStep, (DateTime.Now - this.Started).TotalSeconds);
        }
    }
}
