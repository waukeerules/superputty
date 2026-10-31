using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using SuperPutty.Data;
using SuperPutty.Properties;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Drawing;
using SuperPutty.Scp;
using SuperPuTTY.Scripting;
using System.Net;
using DarkModeForms;
using SuperPutty.Gui;

namespace SuperPutty
{
    /// <summary>
    /// Represents the SuperPuTTY application itself
    /// </summary>
    public static class SuperPuTTY 
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SuperPuTTY));

        public static event EventHandler<LayoutChangedEventArgs> LayoutChanging;
        public static event EventHandler<LayoutChangedEventArgs> LayoutChanged;

        public static event Action<string> StatusEvent;

        static BindingList<LayoutData> layouts = new BindingList<LayoutData>();
        static SortedList<string, SessionData> sessionsMap = new SortedList<string, SessionData>();
        static BindingList<SessionData> sessionsList = new BindingList<SessionData>();
        static bool? isFirstRun;

        public static void Initialize(string[] args)
        {
            Log.InfoFormat($"Initializing.  Version={Version}, UserSettings={Settings.SettingsFilePath}, SettingsFolder={Settings.SettingsFolder}");

            SessionLog.PurgeTemporaryLogs();

            Images = LoadImageList(false);
            ImagesWithStop = LoadImageList(true);

            if (!IsFirstRun)
            {
                // parse command line args
                CommandLine = new CommandLineOptions(args);

                // display help if --help specified
                if (CommandLine.Help)
                {
                    Messenger.MessageBox(CommandLineOptions.Usage(), "SuperPutty CLI Help", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                    Environment.Exit(0);
                }

                // load data                
                LoadLayouts();
                LoadSessions();                

                // determine starting layout, if any.  CLI has priority
                if (CommandLine.IsValid)
                {
                    if (CommandLine.Layout != null)
                    {
                        StartingLayout = FindLayout(CommandLine.Layout);
                        if (StartingLayout != null)
                        {
                            Log.InfoFormat($"Starting with layout from command line, {CommandLine.Layout}");
                        }
                    }
                    else
                    {
                        // ad-hoc session specified
                        SessionDataStartInfo sessionStartInfo = CommandLine.ToSessionStartInfo();
                        if (sessionStartInfo != null)
                        {
                            StartingSession = sessionStartInfo;
                            Log.InfoFormat($"Starting adhoc Session from command line, {StartingSession.Session.SessionId}");
                        }
                    }

                }

                // if nothing specified, then try the default layout
                if (StartingLayout == null && StartingSession == null)
                {
                    StartingLayout = FindLayout(Settings.DefaultLayoutName);
                    if (StartingLayout != null)
                    {
                        Log.InfoFormat($"Starting with default layout, {Settings.DefaultLayoutName}");
                    }
                }
            }

            // Register IpcChanncel for single instance support
            SingleInstanceHelper.RegisterRemotingService();
            WindowEvents = new GlobalWindowEvents();

            Log.Info("Initialized");
        }

        /// <summary>Called when application is shutting down, sends message to log.</summary>
        public static void Shutdown()
        {
            Log.Info("Shutting down...");
        }

        /// <summary>Send status message to toolstrip</summary>
        /// <param name="status">A string containing the message</param>
        /// <param name="args">optional arguments <seealso>
        ///         <cref>String.Format</cref>
        ///     </seealso>
        /// </param>
        public static void ReportStatus(string status, params object[] args)
        {
            string msg = args.Length > 0 ? string.Format(status, args) : status;
            Log.DebugFormat($"STATUS: {msg}");

            if (StatusEvent != null)
            {
                StatusEvent(msg);
            }
        }

        #region Layouts

        public static bool IsLayoutChanging { get; private set; }

        public static void AddLayout(string file)
        {
            LayoutData layout = new LayoutData(file);
            if (FindLayout(layout.Name) == null)
            {
                layouts.Add(layout);
                LoadLayout(layout, true);
            }
        }

        public static void RemoveLayout(string name, bool deleteFile)
        {
            LayoutData layout = FindLayout(name);
            if (layout != null)
            {
                layouts.Remove(layout);
                if (deleteFile)
                {
                    File.Delete(layout.FilePath);
                }
            }
        }

        public static void RenameLayout(LayoutData layout, string newName)
        {
            if (layout != null)
            {
                LayoutData existing = FindLayout(newName);
                if (existing == null)
                {
                    Log.InfoFormat($"Renaming layout: {layout.Name} -> {newName}");
                    // rename layout and file
                    string fileOld = layout.FilePath;
                    string fileNew = Path.Combine(Path.GetDirectoryName(layout.FilePath), newName) + ".xml";
                    File.Move(fileOld, fileNew);
                    layout.Name = newName;
                    layout.FilePath = fileNew;

                    // Notify
                    layouts.ResetItem(layouts.IndexOf(layout));
                }
                else
                {
                    throw new ArgumentException($"Layout with the same name exists: {newName}");
                }
            }
        }

        public static LayoutData FindLayout(string name)
        {
            return layouts.FirstOrDefault(layout => name == layout.Name);
        }

        public static void LoadLayouts()
        {
            if (!String.IsNullOrEmpty(Settings.SettingsFolder))
            {
                LayoutData autoRestore = new LayoutData(AutoRestoreLayoutPath) { Name = LayoutData.AutoRestore, IsReadOnly = true };
                if (Directory.Exists(LayoutsDir))
                {
                    List<LayoutData> newLayouts = Directory.GetFiles(LayoutsDir).Select(file => new LayoutData(file)).ToList();

                    layouts.Clear();
                    layouts.Add(autoRestore);
                    foreach (LayoutData layout in newLayouts)
                    {
                        layouts.Add(layout);
                    }
                    Log.InfoFormat($"Loaded {newLayouts.Count} layouts");
                }
                else
                {
                    Log.InfoFormat($"Creating layouts directory: {LayoutsDir}");
                    Directory.CreateDirectory(LayoutsDir);
                    layouts.Add(autoRestore);
                }
            }            
        }

        public static void LoadLayout(LayoutData layout)
        {
            LoadLayout(layout, false);
        }

        public static void LoadLayout(LayoutData layout, bool isNewLayoutAlreadyActive)
        {
            Log.InfoFormat("LoadLayout: layout={0}, isNewLayoutAlreadyActive={1}", layout == null ? "NULL" : layout.Name, isNewLayoutAlreadyActive);
            LayoutChangedEventArgs args = new LayoutChangedEventArgs
            {
                New = layout,
                Old = CurrentLayout,
                IsNewLayoutAlreadyActive = isNewLayoutAlreadyActive
            };

            try
            {
                IsLayoutChanging = true;

                if (LayoutChanging != null)
                {
                    LayoutChanging(typeof(SuperPuTTY), args);
                }

            }
            finally
            {
                IsLayoutChanging = false;
            }


            if (LayoutChanged != null)
            {
                CurrentLayout = layout;
                LayoutChanged(typeof(SuperPuTTY), args);
            }
        }

        public static void LoadLayoutInNewInstance(LayoutData layout)
        {
            ReportStatus($"Starting new instance with layout, {layout.Name}");
            Process.Start(Assembly.GetExecutingAssembly().Location, "-layout \"" + layout.Name + "\"");
        }

        public static void LoadSessionInNewInstance(string sessionId)
        {
            ReportStatus($"Starting session in new instance, {sessionId}");
            Process.Start(Assembly.GetExecutingAssembly().Location, "-session \"" + sessionId + "\"");
        }

        public static void SetLayoutAsDefault(string layoutName)
        {
            if (!string.IsNullOrEmpty(layoutName))
            {
                LayoutData layout = FindLayout(layoutName);
                if (layout != null)
                {
                    ReportStatus($"Setting {layoutName} as default layout");
                    SuperPuTTY.Settings.DefaultLayoutName = layoutName;
                    SuperPuTTY.Settings.Save();

                    // so gui change is propagated via events
                    LoadLayouts();
                }
            }
        }

        #endregion

        #region Sessions

        /// <summary>Returns A string containing the path to the saved sessions database on disk</summary>
        private static string SessionsFileName { get { return Path.Combine(Settings.SettingsFolder, "Sessions.xml"); } }

        /// <summary>
        /// The folder holding this user's own files: SuperXPuTTY.settings, EnvironmentVariables.json,
        /// SessionShortcuts.json and PrivateSessions.xml. Unlike <see cref="Settings"/>.SettingsFolder, it's never shared.
        /// </summary>
        public static string UserSettingsFolder
        {
            get
            {
                return Path.GetDirectoryName(Settings.SettingsFilePath ?? Path.Combine(Application.UserAppDataPath, "SuperXPuTTY.settings"));
            }
        }

        /// <summary>
        /// The user's private sessions: sessions kept out of the shared Sessions.xml, shown under "Private sessions"
        /// in the tree (see <see cref="SessionData.IsPrivate"/>). Set it to read and write elsewhere (tests).
        /// </summary>
        public static string PrivateSessionsFileName
        {
            get { return privateSessionsFileName ?? Path.Combine(UserSettingsFolder, "PrivateSessions.xml"); }
            set { privateSessionsFileName = value; }
        }
        private static string privateSessionsFileName;

        /// <summary>True while frmSuperPutty is reopening a layout's sessions, so they aren't added to Recent</summary>
        public static bool IsRestoringLayout { get; set; }

        /// <summary>
        /// Load the sessions from Sessions.xml and the user's PrivateSessions.xml into the application. A private
        /// session whose ID is already taken by a shared one gets a new ID, since IDs must be unique.
        /// </summary>
        public static void LoadSessions()
        {
            string fileName = SessionsFileName;
            string privateFileName = PrivateSessionsFileName;
            Log.InfoFormat($"Loading all sessions.  file={fileName}, private={privateFileName}");

            List<SessionData> sessions = null;
            List<SessionData> privateSessions = null;
            try
            {
                if (File.Exists(fileName))
                {
                    sessions = SessionData.LoadSessionsFromFile(fileName);
                }
                else
                {
                    Log.WarnFormat($"Sessions file does not exist, nothing loaded.  file={fileName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error while loading sessions from {fileName}", ex);
            }
            try
            {
                if (File.Exists(privateFileName))
                {
                    privateSessions = SessionData.LoadSessionsFromFile(privateFileName);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error while loading private sessions from {privateFileName}", ex);
            }

            // reload when the file is changed outside SuperPuTTY (also starts watching a file that doesn't exist yet)
            SessionsFileWatcher.Watch(fileName);
            if (sessions == null && privateSessions == null)
            {
                return;
            }

            // remove old, keeping the sessions read from the Postgres table, which aren't in these files
            List<SessionData> databaseSessions = sessionsList.Where(s => s.IsDatabase).ToList();
            sessionsMap.Clear();
            sessionsList.Clear();
            loadingSessions = true;
            try
            {
                foreach (SessionData session in sessions ?? new List<SessionData>())
                {
                    AddSession(session);
                }
                foreach (SessionData session in privateSessions ?? new List<SessionData>())
                {
                    session.IsPrivate = true;
                    if (GetSessionById(session.SessionId) != null)
                    {
                        string newId = MakeUniqueSessionId(session.SessionId);
                        Log.WarnFormat($"Private session {session.SessionId} has the same ID as a shared one; renamed to {newId}");
                        session.SessionId = newId;
                        session.SessionName = SessionData.GetSessionNameFromId(newId);
                    }
                    AddSession(session);
                }
            }
            finally
            {
                loadingSessions = false;
            }
            Log.InfoFormat($"Loaded {(sessions ?? new List<SessionData>()).Count} sessions from {fileName} and {(privateSessions ?? new List<SessionData>()).Count} private sessions");
            // the database sessions go back in silently; the tree redraws its root for them once
            if (databaseSessions.Count > 0)
            {
                ReplaceDatabaseSessions(databaseSessions);
            }
        }

        /// <summary>
        /// Save the sessions: shared ones to Sessions.xml in the settings folder, private ones to the user's
        /// PrivateSessions.xml. The private file is written only once there is, or was, a private session.
        /// </summary>
        public static void SaveSessions()
        {
            Log.InfoFormat("Saving all sessions");
            // sessions from the Postgres table live there (and in its local cache), never in these files
            List<SessionData> all = GetAllSessions().Where(s => !s.IsDatabase).ToList();
            SessionData.SaveSessionsToFile(all.Where(s => !s.IsPrivate).ToList(), SessionsFileName);
            // our own save: not a change to reload
            SessionsFileWatcher.Saved();
            List<SessionData> privateSessions = all.Where(s => s.IsPrivate).ToList();
            if (privateSessions.Count > 0 || File.Exists(PrivateSessionsFileName))
            {
                SessionData.SaveSessionsToFile(privateSessions, PrivateSessionsFileName);
            }
        }

        /// <summary>
        /// Remove a session from the in-application sessions database. 
        /// </summary>
        /// <param name="sessionId">The <seealso cref="SessionData.SessionId"/> of the session to remove</param>
        /// <returns>true on success, or false on failure or if session did not exist</returns>
        public static bool RemoveSession(string sessionId)
        {
            SessionData session = GetSessionById(sessionId);
            if (session != null)
            {
                sessionsMap.Remove(sessionId);
                sessionsList.Remove(session);
                Log.InfoFormat($"Removed Session, id={session}, success=true");
                return true;
            }            
            return false;
        }

        /// <summary>Get a Session by its <seealso cref="SessionData.SessionId"/></summary>
        /// <param name="sessionId">A string which represents a session</param>
        /// <returns>A <seealso cref="SessionData"/> object containing the session details</returns>
        public static SessionData GetSessionById(string sessionId)
        {
            SessionData session = null;
            if (sessionId != null)
            {
                if (!sessionsMap.TryGetValue(sessionId, out session))
                {
                    // no hit by id...so try the list
                    // @TODO: Revisit...this is a work around the sessionId changing in tree
                    foreach (SessionData sd in sessionsList)
                    {
                        if (sd.SessionId == sessionId)
                        {
                            session = sd;
                            // reindex list
                            sessionsMap.Clear();
                            foreach (SessionData s in sessionsList)
                            {
                                sessionsMap[s.SessionId] = s;
                            }
                            break;
                        }
                    }
                }
            }
            return session;
        }

        /// <summary>Add a new session to the in-application session database</summary>
        /// <param name="session">A <seealso cref="SessionData"/> object containing the configuration of a session</param>
        /// <returns>true on success, false on failure</returns>
        public static bool AddSession(SessionData session)
        {
            bool success = false;
            if (GetSessionById(session.SessionId) == null)
            {
                // not one line per session while loading thousands of them (LoadSessions logs a summary instead)
                if (!loadingSessions)
                {
                    Log.InfoFormat($"Added Session, id={session.SessionId}");
                }
                sessionsMap.Add(session.SessionId, session);
                sessionsList.Add(session);
                success = true;
            }
            else
            {
                Log.InfoFormat($"Failed to Add Session, id={session.SessionId}.  Session already exists");
            }
            return success;
        }

        /// <summary>True while <see cref="LoadSessions"/> adds the sessions it read, so they aren't logged one by one</summary>
        private static bool loadingSessions;

        /// <summary>Raised after <see cref="ReplaceDatabaseSessions"/>, so the Sessions tree can redraw "Database sessions"</summary>
        public static event EventHandler DatabaseSessionsReplaced;

        /// <summary>
        /// Swap the sessions read from the Postgres table (DatabaseSessions) for a new set, in one go: the session
        /// list doesn't announce each one, since thousands of rows would redraw the tree thousands of times; the tree
        /// redraws its Database sessions root on <see cref="DatabaseSessionsReplaced"/> instead.
        /// </summary>
        /// <param name="sessions">The new database sessions; empty to remove them all</param>
        public static void ReplaceDatabaseSessions(IList<SessionData> sessions)
        {
            sessionsList.RaiseListChangedEvents = false;
            try
            {
                foreach (SessionData old in sessionsList.Where(s => s.IsDatabase).ToList())
                {
                    sessionsMap.Remove(old.SessionId);
                    sessionsList.Remove(old);
                }
                foreach (SessionData session in sessions)
                {
                    if (session.SessionId != null && !sessionsMap.ContainsKey(session.SessionId))
                    {
                        sessionsMap.Add(session.SessionId, session);
                        sessionsList.Add(session);
                    }
                }
            }
            finally
            {
                sessionsList.RaiseListChangedEvents = true;
            }
            DatabaseSessionsReplaced?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Get a list of all sessions from the in-application database</summary>
        /// <returns>A List of <seealso cref="SessionData"/> objects</returns>
        public static List<SessionData> GetAllSessions()
        {
            return sessionsMap.Values.ToList();
        }

        /// <summary>Retrieve a <seealso cref="SessionData"/> object and open a new putty window</summary>
        /// <param name="sessionId">A string containing the <seealso cref="SessionData.SessionId"/> of the session</param>
        public static void OpenProtoSession(string sessionId)
        {
            OpenProtoSession(GetSessionById(sessionId));
        }

        /// <summary>Open a new putty window with its settings being passed in a <seealso cref="SessionData"/> object</summary>
        /// <param name="session">The <seealso cref="SessionData"/> object containing the settings</param>
        public static ctlPuttyPanel OpenProtoSession(SessionData session)
        {
            Log.InfoFormat("Opening putty session, id={0}", session == null ? "" : session.SessionId);
            ctlPuttyPanel panel = null;
            if (session != null)
            {
                if (!SessionVariables.EnsureDefined(session, MainForm))
                {
                    return null;
                }

                string Executable = PuttyStartInfo.GetExecutable(session);
                if (string.IsNullOrEmpty(Executable))
                {
                    Messenger.MessageBox("Error trying to create session: " + session.ToString() +
                        "\nExecutable not set for " + session.Proto.ToString() + " protocol." +
                        "\nGo to tools->options->General tab to set the path to the executable."
                        , "Failed to create a session", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
                if (!File.Exists(Executable))
                {
                    Messenger.MessageBox("Error trying to create session: " + session.ToString() +
                        "\nExecutable not found for " + session.Proto.ToString() + " protocol." +
                        "\nThe path for the executable was set as \"" + Executable + "\"." +
                        "\nGo to tools->options->General tab to set the path to the executable."
                        , "Failed to create a session", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

                // This is the callback fired when the panel containing the terminal is closed
                // We use this to save the last docking location and to close the panel
                PuttyClosedCallback callback = delegate
                {
                    if (panel != null)
                    {
                        // save the last dockstate (if it has been changed)
                        if (session.LastDockstate != panel.DockState
                            && panel.DockState != DockState.Unknown
                            && panel.DockState != DockState.Hidden)
                        {
                            session.LastDockstate = panel.DockState;
                            SaveSessions();
                        }

                        if (panel.InvokeRequired)
                        {
                            panel.BeginInvoke((MethodInvoker)delegate {
                                panel.Close();
                            });
                        }
                        else
                        {
                            panel.Close();
                        }
                    }
                };

                try {
                    panel = new ctlPuttyPanel(session, callback);

                    ApplyDockRestrictions(panel);
                    ApplyIconForWindow(panel, session);
                    panel.Show(MainForm.DockPanel, session.LastDockstate);

                    // Start PuTTY only now that Show() has laid out the tab. Starting it earlier (as the panel
                    // used to, when it was created or first became visible) makes it connect with a tiny
                    // terminal on the first tab, and the server then wraps its output at that width.
                    panel.AppPanel.StartApplication("Opened");
                    ReportStatus($"Opened session: {session.SessionId} [{session.Proto}]");

                    // only saved sessions the user opens go in Recent: not quick connections, nor a layout's sessions
                    if (!IsRestoringLayout && GetSessionById(session.SessionId) == session)
                    {
                        SessionShortcuts.AddRecent(session.SessionId);
                    }

                    if (!string.IsNullOrWhiteSpace(session.SPSLFileName))
                    {
                        string fileName = session.SPSLFileName;
                        string script = string.Empty;

                        if(Regex.IsMatch(fileName, @"^https?:\/\/", RegexOptions.IgnoreCase))
                        {
                            try
                            {
                                HttpWebRequest req = WebRequest.CreateHttp(fileName);
                                var response = req.GetResponse();
                                using (var stream = new StreamReader(response.GetResponseStream()))
                                {
                                    script = stream.ReadToEnd();
                                }
                            }
                            catch(Exception)
                            {
                                script = String.Empty;
                            }
                        }
                        else
                        {
                            if (fileName.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                            {
                                fileName = fileName.Substring("file://".Length);
                            }

                            if (File.Exists(fileName))
                            {
                                script = File.ReadAllText(fileName);
                            }
                        }

                        if (!String.IsNullOrEmpty(script))
                        {
                            // run through the tab so WAITFOR can read the session log (and RDP tabs get keystrokes)
                            if (new ExecuteScriptEventArgs() { Script = script }.IsSPSL)
                            {
                                panel.RunScript(script, fromSessionStart: true, name: "Login script");
                            }
                        }
                    }
                } catch (InvalidOperationException ex)
                {
                    Messenger.MessageBox("Error trying to create session " + ex.Message, "Failed to create session panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return panel;
        }

        /// <summary>Retrieve a <seealso cref="SessionData"/> object and open a new putty scp window</summary>
        /// <param name="sessionId">A string containing the <seealso cref="SessionData.SessionId"/> of the session</param>
        public static void OpenScpSession(string sessionId)
        {
            OpenScpSession(GetSessionById(sessionId));
        }

        /// <summary>Open a new putty scp window with its settings being passed in a <seealso cref="SessionData"/> object</summary>
        /// <param name="session">The <seealso cref="SessionData"/> object containing the settings</param>
        public static void OpenScpSession(SessionData session)
        {
            Log.InfoFormat("Opening scp session, id={0}", session == null ? "" : session.SessionId);
            if (!IsScpEnabled)
            {
                ReportStatus($"Could not open session, pscp not found: {session.SessionId} [SCP]");
            }
            else if (session != null)
            {
                if (!SessionVariables.EnsureDefined(session, MainForm))
                {
                    return;
                }

                // pscp gets a copy with {key} placeholders replaced so the stored session keeps them
                SessionData resolved = SessionVariables.Resolve(session);
                var homePrefix = resolved.Username.ToLower().Equals("root") ? Settings.PscpRootHomePrefix : Settings.PscpHomePrefix;
                PscpBrowserPanel panel = new PscpBrowserPanel(
                    resolved, new PscpOptions { PscpLocation = Settings.PscpExe, PscpHomePrefix = homePrefix });
                ApplyDockRestrictions(panel);
                ApplyIconForWindow(panel, session);
                panel.Show(MainForm.DockPanel, session.LastDockstate);

                ReportStatus($"Opened session: {session.SessionId} [SCP]");
            }
            else
            {
                Log.Warn("Could not open null session");
            }
        }

        /// <summary>Apply docking restrictions to a <seealso cref="ToolWindowDocument"/> window such as preventing a window opening from outside the tabbed interface</summary>
        /// <param name="panel">The <seealso cref="DockPanel"/> to apply the restrictions to</param>
        public static void ApplyDockRestrictions(DockPanel panel)
        {
            foreach (DockContent doc in panel.Documents)
            {
                if (doc is ToolWindowDocument)
                {
                    ApplyDockRestrictions(doc);
                }
            }
        }

        /// <summary>Apply docking restrictions to a panel, such as restricting a panel from floating</summary>
        /// <param name="panel">The <seealso cref="DockContent"/> panel to apply the restrictions to</param>
        public static void ApplyDockRestrictions(DockContent panel)
        {
            if (Settings.RestrictContentToDocumentTabs)
            {
                panel.DockAreas = DockAreas.Document | DockAreas.Float;
            }

            if (Settings.DockingRestrictFloatingWindows)
            {
                panel.DockAreas = panel.DockAreas ^ DockAreas.Float;
            }
        }

        public static void OpenSession(SessionDataStartInfo ssi)
        {
            if (MainForm.InvokeRequired)
            {
                MainForm.BeginInvoke(new Action<SessionDataStartInfo>(OpenSession), ssi);
                return;
            }

            if (ssi != null)
            {
                if (ssi.UseScp)
                {
                    OpenScpSession(ssi.Session);
                }
                else
                {
                    OpenProtoSession(ssi.Session);
                }
            }
        }

        /// <summary>Import sessions from the specified file into the in-application database</summary>
        /// <param name="fileName">A string containing the path of the filename that holds session configuration</param>
        public static void ImportSessionsFromFile(string fileName)
        {
            if (fileName == null) { return; }
            if (File.Exists(fileName))
            {
                Log.InfoFormat($"Importing sessions from file, path={fileName}");
                List<SessionData> sessions = SessionData.LoadSessionsFromFile(fileName);
                ImportSessions(sessions, "Imported");
            }
        }

        /// <summary>Import sessions from the specified folder into the in-application database</summary>
        /// <param name="folderName">A string containing the path of the folder that holds session configuration files</param>
        public static void ImportSessionsFromFolder(string folderName)
        {
            if (folderName == null) { return; }
            if (Directory.Exists(folderName))
            {
                Log.InfoFormat($"Importing sessions from folder, path={folderName}");
                List<SessionData> sessions = SessionData.LoadSessionsFromFolder(folderName);
                ImportSessions(sessions, "ImportedFromPortablePuTTY");
            }
        }

        /// <summary>Import sessions from Windows Registry which were set by PuTTY or KiTTY and load them into the in-application sessions database</summary>
        public static void ImportSessionsFromPuTTY()
        {
            Log.InfoFormat("Importing sessions from PuTTY/KiTTY");
            List<SessionData> sessions = PuttyDataHelper.GetAllSessionsFromPuTTY();
            ImportSessions(sessions, "ImportedFromPuTTY");
        }

        /// <summary>Import sessions from file exported by PuttYCM and load them into the in-application sessions database</summary>
        public static void ImportSessionsFromPuttyCM(string fileExport)
        {
            Log.InfoFormat("Importing sessions from PuttyCM");
            List<SessionData> sessions = PuttyDataHelper.GetAllSessionsFromPuTTYCM(fileExport);
            ImportSessions(sessions, "ImportedFromPuTTYCM");
        }

        /// <summary>Import sessions from MobaXterm sessions file and load them into the in-application sessions database</summary>
        public static void ImportSessionsFromMoba(string fileExport)
        {
            Log.InfoFormat("Importing sessions from MobaXterm");
            List<SessionData> sessions = MobaDataHelper.GetAllSessionsFromMoba(fileExport);
            ImportSessions(sessions, "ImportedFromMobaXterm");
        }

        /// <summary>Import sessions from Windows Registry which were set by PuTTY or KiTTY and load them into the in-application sessions database</summary>
        public static void ImportRDPSessionsFromWinReg()
        {
            Log.InfoFormat("Importing RDP sessions from Windows registry");
            List<SessionData> sessions = RDPDataHelper.GetAllSessionsFromRegistry();
            ImportSessions(sessions, "ImportRDPSessionsFromWinReg");
        }

        /// <summary>Import sessions from a from a List object into the specified folder</summary>
        /// <param name="sessions">A List of <seealso cref="SessionData"/> objects</param>
        /// <param name="folder">The destination folder name</param>
        public static void ImportSessions(List<SessionData> sessions, string folder)
        {
            foreach (SessionData session in sessions)
            {
                // pre-pend session id with the provided folder to put them
                session.SessionId = MakeUniqueSessionId(SessionData.CombineSessionIds(folder, session.SessionId));
                session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                AddSession(session);
            }
            Log.InfoFormat($"Imported {sessions.Count} sessions into {folder}");

            SaveSessions();
        }

        /// <summary>Import sessions from older version of SuperPuTTY from the Windows Registry</summary>
        public static void ImportSessionsFromSuperPutty1030()
        {
            try
            {
                List<SessionData> sessions = SessionData.LoadSessionsFromRegistry();
                if (sessions != null && sessions.Count > 0)
                {
                    foreach (SessionData session in sessions)
                    {
                        AddSession(session);
                    }
                    SaveSessions();

                    Log.InfoFormat($"Imported {sessions.Count} old sessions from registry.");
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat($"Could not import old sessions, msg={ex.Message}");
            }
        }

        /// <summary>Generate a unique session ID to prevent collisions in the in-application data store, used when importing and merging sessions from
        /// another application or an older versin of SuperPuTTY</summary>
        /// <param name="sessionId">A string containing the sessionID of the session being imported</param>
        /// <returns>A string containing a unique sessionID</returns>
        public static string MakeUniqueSessionId(string sessionId)
        {
            string newSessionId = sessionId;

            for (int i = 1; i < 1000; i++)
            {
                SessionData sessionExisting = GetSessionById(newSessionId);
                if (sessionExisting == null)
                {
                    break;
                }                
                newSessionId = string.Format("{0}-{1}", sessionId, i);
            }

            return newSessionId;
        }

        #endregion

        #region Icons

        /// <summary>
        /// Build the session icon list at the size chosen in Options (scaled for the display): drawn templates, logos
        /// and custom icons, and old keys mapped onto them (see IconLibrary)
        /// </summary>
        /// <param name="isEnableStopImage">Also add the "stop" image, for the icon filter's "no filter" choice</param>
        /// <returns>The icons</returns>
        public static ImageList LoadImageList(bool isEnableStopImage)
        {
            ImageList imgIcons = new ImageList();
            imgIcons.ColorDepth = ColorDepth.Depth32Bit;
            int size = IconLibrary.ToPixels(IconLibrary.LogicalSize);
            imgIcons.ImageSize = new Size(size, size);
            IconLibrary.DarkBackground = Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;

            if (isEnableStopImage)
                imgIcons.Images.Add("stop", IconLibrary.RenderDrawn("stop", size));

            IconLibrary.Fill(imgIcons);

            return imgIcons;
        }

        /// <summary>Get the Icon defined for the specified session</summary>
        /// <param name="session">The session configuration data</param>
        /// <returns>The Icon configured for the session</returns>
        public static Icon GetIconForSession(SessionData session)
        {
            Icon icon = null;
            if (session != null)
            {
                string imageKey = !IconLibrary.Ensure(Images, session.ImageKey)
                    ? SessionTreeview.ImageKeySession : session.ImageKey;
                try
                {
                    // drawn at the tab's own icon size, so it's sharp rather than the list's size scaled down
                    int size = IconLibrary.TabIconPixels;
                    Bitmap bmp = IconLibrary.RenderDrawn(imageKey, size) ?? IconLibrary.Scale(Images.Images[imageKey], size);
                    icon = Icon.FromHandle(bmp.GetHicon());
                }
                catch (Exception ex)
                {
                    Log.Error("Error getting icon for image", ex);
                }
            }
            return icon;
        }

        private static void ApplyIconForWindow(ToolWindow win, SessionData session)
        {
            // drawn by TabIcons, larger and centred, rather than by DockPanelSuite
            TabIcons.Set(win, GetIconForSession(session));
        }

        #endregion

        #region Properties
        /// <summary>true if the application has not defined where the required putty program is located</summary>
        public static bool IsFirstRun {
            get
            {
                if (isFirstRun == null)
                {
                    isFirstRun = string.IsNullOrEmpty(Settings.PuttyExe);
                }
                return isFirstRun.Value;
            }
        }

        /// <summary>true if the application has not defined where the putty scp program is located</summary>
        public static bool IsScpEnabled { get { return File.Exists(SuperPuTTY.Settings.PscpExe); } }

        /// <summary>true if the application has defined where the filezilla program is located</summary>
        public static bool IsFilezillaEnabled { get { return File.Exists(SuperPuTTY.Settings.FileZillaExe); } }

        /// <summary>true if the application has defined where the winSCP program is located</summary>
        public static bool IsWinSCPEnabled { get { return File.Exists(SuperPuTTY.Settings.WinSCPExe); } }

        /// <summary>true if the application has defined where the Xorg program is located</summary>
        public static bool IsXorgEnabled { get { return File.Exists(SuperPuTTY.Settings.XorgExe); } }

        /// <summary>Returns a string containing the current version of SuperPuTTY</summary>
        public static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }

        internal static Settings Settings { get { return Settings.Default; } }
        public static frmSuperPutty MainForm { get; set; }        
        public static string LayoutsDir { get { return Path.Combine(Settings.SettingsFolder, "layouts"); } }
        public static LayoutData CurrentLayout { get; private set; }
        public static LayoutData StartingLayout { get; private set; }
        public static SessionDataStartInfo StartingSession { get; private set; }
        public static BindingList<LayoutData> Layouts { get { return layouts; } }
        public static BindingList<SessionData> Sessions { get { return sessionsList; } }
        public static CommandLineOptions CommandLine { get; private set; }
        public static ImageList Images { get; private set; }
        public static ImageList ImagesWithStop { get; private set; }
        public static GlobalWindowEvents WindowEvents { get; private set; }

        /// <summary>true of KiTTY is being used instead of putty</summary>
        public static bool IsKiTTY
        {
            get
            {
                bool isKitty = false;
                if (File.Exists(Settings.PuttyExe))
                {
                    string exe = Path.GetFileName(Settings.PuttyExe);
                    isKitty = exe != null && exe.ToLower().StartsWith("kitty");
                }
                return isKitty;
            }
        }

        /// <summary>true of PuTTY Plus is being used instead of putty</summary>
        public static bool IsPuTTYPlus
        {
            get
            {
                bool isPuttyPlus = false;
                if (File.Exists(Settings.PuttyExe))
                {
                    string exe = Path.GetFileName(Settings.PuttyExe);
                    isPuttyPlus = exe != null && exe.ToLower().StartsWith("putty-plus");
                }
                return isPuttyPlus;
            }
        }

        public static string PuTTYAppName
        {
            get
            {
                if (IsPuTTYPlus)
                    return "PuTTY Plus";

                if (IsKiTTY)
                    return "KiTTY";

                return "PuTTY";
            }
        }

        /// <summary>The path to the default AutoRestore layout configuration</summary>
        public static string AutoRestoreLayoutPath { get { return Path.Combine(Settings.SettingsFolder, LayoutData.AutoRestoreLayoutFileName); } }

        #endregion
    }

    #region SuperPuttyAction
    public enum SuperPuttyAction
    {
        CloseTab,
        NextTab,
        PrevTab,
        Options,
        FullScreen,
        OpenSession,
        SwitchSession,
        DuplicateSession,
        GotoCommandBar,
        GotoConnectionBar,
        FocusActiveSession,
        /// <summary>Open Script Editor Window</summary>
        OpenScriptEditor,
        /// <summary>Rename active tab</summary>
        RenameTab,
        RestartSession,
        ClearScrollback,
        ResetTerminal,
        CopyAllToClipboard
    }
    #endregion

    public enum InterfaceTheme
    {
        DarkTheme = 0,
        DarkBlueTheme,
        LightTheme,
        /// <summary>
        /// The original SuperPuTTY look (DockPanelSuite's VS2005 theme): angled tabs, white when selected, with one
        /// close button and the tab list at the right end of the tab strip. Light only, so it's after LightTheme and
        /// the "&lt; LightTheme means dark" checks still hold.
        /// </summary>
        ClassicTheme
    }

}
