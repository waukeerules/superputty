/*
 * Copyright (c) 2009 Jim Radford http://www.jimradford.com
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

using DarkModeForms;
using log4net;
using Microsoft.VisualBasic;
using SuperPutty.Data;
using SuperPutty.Gui;
using SuperPutty.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using static System.Collections.Specialized.BitVector32;


namespace SuperPutty
{
    public delegate void SelectionChangedHandler(SessionData Session);

    public partial class SessionTreeview : ToolWindow, IComparer
    {
        public event SelectionChangedHandler SelectionChanged;
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionTreeview));

        private static int MaxSessionsToOpen = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.MaxSessionsToOpen"] ?? "10");

        public const string SessionIdDelim = "/";
        public const string ImageKeySession = "computer";
        public const string ImageKeyFolder = "folder";

        /// <summary>The icons of the Favorites, Recent and Private sessions groups</summary>
        public const string ImageKeyFavorites = "star", ImageKeyRecent = "clock", ImageKeyPrivate = "lock";

        /// <summary>The tree's top-level nodes, in the order they're shown; each one's Tag</summary>
        enum RootKind { Favorites, Recent, Shared, Private, Database }

        /// <summary>
        /// The Tag of an entry in Favorites or Recent: a shortcut to a session, which stays in its own folder. Not a
        /// SessionData, so the folder and session code (rename, delete, drag) leaves these entries alone.
        /// </summary>
        class SessionShortcut
        {
            /// <summary>The session it opens</summary>
            public SessionData Session;

            /// <summary>Which group it's in</summary>
            public RootKind Group;

            /// <summary>Its place in the group's list; Recent is shown in this order, most recent first</summary>
            public int Order;
        }

        private DockPanel m_DockPanel;
        private bool isRenamingNode;
        TreeNode nodeRoot;
        ImageList imgIcons = new ImageList();
        Func<SessionData, bool> filter;

        /// <summary>The Favorites and Recent groups (null while empty or hidden), and the Private sessions root</summary>
        TreeNode nodeFavorites, nodeRecent, nodePrivate;

        /// <summary>The "Database sessions" root, for sessions from the Postgres table; null while that's turned off</summary>
        TreeNode nodeDatabase;

        /// <summary>The Database sessions root's menu: new session and folder, Refresh and Database settings</summary>
        ContextMenuStrip contextMenuStripDatabaseRoot;

        /// <summary>A database session to select once the table has been read again after adding or changing it</summary>
        string pendingSelectId;

        /// <summary>The sessions under one root (Saved, Private or Database sessions), by folder path ("" at the top)</summary>
        class FolderIndex
        {
            /// <summary>Every session, for whole-folder actions on database folders not opened yet</summary>
            public readonly Dictionary<string, List<SessionData>> All = new Dictionary<string, List<SessionData>>();

            /// <summary>The sessions the search shows; folders are filled from it when opened</summary>
            public readonly Dictionary<string, List<SessionData>> Shown = new Dictionary<string, List<SessionData>>();

            /// <summary>The subfolders of each folder that hold something the search shows</summary>
            public readonly Dictionary<string, SortedSet<string>> Subfolders = new Dictionary<string, SortedSet<string>>();
        }

        /// <summary>The index for each root; rebuilt from the session list when <see cref="indexDirty"/></summary>
        Dictionary<RootKind, FolderIndex> folderIndex = new Dictionary<RootKind, FolderIndex>();

        /// <summary>Whether sessions were added, removed or moved since the index was built</summary>
        bool indexDirty = true;

        /// <summary>Folders open under each root before a reload, reopened when it's filled again</summary>
        Dictionary<RootKind, List<string>> reopenAfterReload;

        /// <summary>Folders with more children than this are filled with sorting off and sorted once</summary>
        const int SortOnceAbove = 200;

        /// <summary>The name of the placeholder child that marks a database folder as not filled in yet</summary>
        const string LazyPlaceholder = "\u0001lazy";

        /// <summary>Menus built in code: for Favorites and Recent entries, and for the Recent group</summary>
        ContextMenuStrip contextMenuStripShortcut, contextMenuStripRecentGroup;

        /// <summary>Menu items built in code: add to / remove from Favorites, and move between Saved and Private sessions</summary>
        ToolStripMenuItem favoriteToolStripMenuItem, moveSessionToolStripMenuItem, moveFolderToolStripMenuItem;

        /// <summary>The Colour submenus of a session's and a folder's menu (environment colours, filled as they open)</summary>
        ToolStripMenuItem colorSessionToolStripMenuItem, colorFolderToolStripMenuItem;

        /// <summary>A folder's "Edit sessions...": change fields on all its sessions at once</summary>
        ToolStripMenuItem editSessionsToolStripMenuItem;

        /// <summary>A folder's "Folder settings...": its colour and the defaults its sessions inherit</summary>
        ToolStripMenuItem folderSettingsToolStripMenuItem;

        /// <summary>A folder's "Copy to" submenu (other roots)</summary>
        ToolStripMenuItem copyFolderToolStripMenuItem;

        /// <summary>Items of <see cref="contextMenuStripShortcut"/> whose text or visibility depends on the entry</summary>
        ToolStripMenuItem shortcutFavoriteToolStripMenuItem, shortcutRemoveToolStripMenuItem;

        /// <summary>Whether a redraw of Favorites and Recent is already queued</summary>
        bool shortcutRebuildPending;

        /// <summary>The toolbar under the search box: new session, new folder, expand all, collapse all, sort</summary>
        ToolStrip toolStripTree;

        /// <summary>The toolbar's sort button, whose icon shows the current order</summary>
        ToolStripButton sortToolStripButton;

        /// <summary>Sort names Z to A: chosen with the toolbar's sort button, and not saved, so A to Z returns on restart</summary>
        bool sortDescending;

        /// <summary>The sessions with a tab open, by session ID, and whether they're connected; drawn as a dot on their icons</summary>
        Dictionary<string, IconLibrary.OpenState> openStates = new Dictionary<string, IconLibrary.OpenState>();

        /// <summary>Whether a refresh of the open-tab dots is already queued</summary>
        bool openStateRefreshPending;

        public SessionData SelectedSession
        {
            get
            {
                if (treeView1.SelectedNode != null && treeView1.SelectedNode.Tag is SessionData)
                {
                    return treeView1.SelectedNode.Tag as SessionData;
                }
                // a Favorites or Recent entry stands for its session
                if (treeView1.SelectedNode != null && treeView1.SelectedNode.Tag is SessionShortcut)
                {
                    return ((SessionShortcut)treeView1.SelectedNode.Tag).Session;
                }
                return null;
            }
        }

        /// <summary>
        /// Instantiate the treeview containing the sessions
        /// </summary>
        /// <param name="dockPanel">The DockPanel container</param>
        /// <remarks>Having the dockpanel container is necessary to allow us to
        /// dock any terminal or file transfer sessions from within the treeview class</remarks>
        public SessionTreeview(DockPanel dockPanel)
        {
            m_DockPanel = dockPanel;
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            InitializeComponent();
            this.treeView1.TreeViewNodeSorter = this;
            this.treeView1.HideSelection = false;
            if (SuperPuTTY.Images != null)
            {
                this.treeView1.ImageList = SuperPuTTY.Images;
            }
            this.BuildShortcutMenus();
            this.BuildToolbar();
            this.ApplySettings();

            // populate sessions in the treeview from the registry
            this.LoadSessions();
            this.ExpandInitialTree();
            SuperPuTTY.Sessions.ListChanged += new ListChangedEventHandler(Sessions_ListChanged);
            SuperPuTTY.Settings.SettingsSaving += new SettingsSavingEventHandler(Settings_SettingsSaving);
            SessionShortcuts.Changed += SessionShortcuts_Changed;
            this.treeView1.BeforeExpand += this.treeView1_BeforeExpand;
            if (m_DockPanel != null)
            {
                m_DockPanel.ContentAdded += DockPanel_ContentChanged;
                m_DockPanel.ContentRemoved += DockPanel_ContentChanged;
            }
            ctlPuttyPanel.ConnectionChanged += PuttyPanel_ConnectionChanged;
            SuperPuTTY.DatabaseSessionsReplaced += DatabaseSessions_Replaced;
            DatabaseSessions.StatusChanged += DatabaseSessions_StatusChanged;
            EnvironmentColors.Changed += EnvironmentColors_Changed;
            // tabs may already be open, e.g. when the Sessions panel is closed and shown again
            this.RefreshOpenStates();
        }

        void ExpandInitialTree()
        {
            if (SuperPuTTY.Settings.ExpandSessionsTreeOnStartup)
            {
                ExpandFully(nodeRoot);
                ExpandFully(nodePrivate);
                this.treeView1.SelectedNode = this.DefaultRoot;
            }
            else
            {
                // start with semi-collapsed view
                foreach (TreeNode root in new[] { this.nodeRoot, this.nodePrivate })
                {
                    root.Expand();
                    foreach (TreeNode node in root.Nodes)
                    {
                        if (!IsSessionNode(node))
                        {
                            node.Collapse();
                        }
                    }
                }
            }
            this.nodeFavorites?.Expand();
            this.nodeRecent?.Expand();
            this.nodeDatabase?.Expand();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                // Assign custom renderer for context menus
                contextMenuStripFolder.Renderer = new MyRenderer(new CustomColorTable(new OSThemeColors()), false)
                {
                    MyColors = DarkModeCS.GetSystemColors(null, 0)
                };
                contextMenuStripAddTreeItem.Renderer = new MyRenderer(new CustomColorTable(new OSThemeColors()), false)
                {
                    MyColors = DarkModeCS.GetSystemColors(null, 0)
                };
                foreach (ToolStrip menu in new ToolStrip[] { this.contextMenuStripShortcut, this.contextMenuStripRecentGroup, this.contextMenuStripDatabaseRoot, this.toolStripTree })
                {
                    menu.Renderer = new MyRenderer(new CustomColorTable(new OSThemeColors()), false)
                    {
                        MyColors = DarkModeCS.GetSystemColors(null, 0)
                    };
                }
            }
        }

        void Settings_SettingsSaving(object sender, CancelEventArgs e)
        {
            this.ApplySettings();
        }

        void ApplySettings()
        {
            this.treeView1.ShowLines = SuperPuTTY.Settings.SessionsTreeShowLines;
            this.treeView1.Font = SuperPuTTY.Settings.SessionsTreeFont;
            this.panelSearch.Visible = SuperPuTTY.Settings.SessionsShowSearch;
            // without "Filter while typing", the search only runs on Enter (or the search button), so say so
            HintText.Set(this.txtSearch, SuperPuTTY.Settings.FilterSessionsOnChange ? "Type to search..." : "Search... (press Enter)");
            this.ApplySearchBarSize();
            this.BuildShortcutGroups();
            this.ResortNodes();
        }

        /// <summary>
        /// Size the search box and its buttons for the icon size chosen in Options, as the toolbars are: a larger font,
        /// and the buttons drawn as icons at that height instead of the old 16 px pictures
        /// </summary>
        void ApplySearchBarSize()
        {
            this.txtSearch.Font = new Font("Segoe UI", IconLibrary.TabFontSize);
            int height = this.txtSearch.PreferredHeight;
            int glyph = Math.Max(16, height - 8);
            foreach (KeyValuePair<Button, string> button in new[]
            {
                new KeyValuePair<Button, string>(this.btnSearch, "search"),
                new KeyValuePair<Button, string>(this.btnClear, "clear")
            })
            {
                Image old = button.Key.Image;
                button.Key.AutoSize = false;
                button.Key.Size = new Size(height + 2, height);
                button.Key.Image = IconLibrary.RenderDrawn(button.Value, glyph);
                old?.Dispose();
            }
            this.panelSearch.Height = height + this.panelSearch.Padding.Vertical;
            this.ApplyToolbarIcons();
            this.txtSearch.Location = new Point(0, this.panelSearch.Padding.Top);
            this.txtSearch.Width = this.panelSearch.ClientSize.Width - this.btnSearch.Width - this.btnClear.Width - this.panelSearch.Padding.Right - 1;
        }

        protected override void OnClosed(EventArgs e)
        {
            SuperPuTTY.Sessions.ListChanged -= new ListChangedEventHandler(Sessions_ListChanged);
            SuperPuTTY.Settings.SettingsSaving -= new SettingsSavingEventHandler(Settings_SettingsSaving);
            SessionShortcuts.Changed -= SessionShortcuts_Changed;
            if (m_DockPanel != null)
            {
                m_DockPanel.ContentAdded -= DockPanel_ContentChanged;
                m_DockPanel.ContentRemoved -= DockPanel_ContentChanged;
            }
            ctlPuttyPanel.ConnectionChanged -= PuttyPanel_ConnectionChanged;
            SuperPuTTY.DatabaseSessionsReplaced -= DatabaseSessions_Replaced;
            DatabaseSessions.StatusChanged -= DatabaseSessions_StatusChanged;
            EnvironmentColors.Changed -= EnvironmentColors_Changed;
            base.OnClosed(e);
        }

        /// <summary>
        /// Load the sessions from the registry and populate the treeview control
        /// </summary>
        /// <summary>
        /// Where new sessions go when no folder is chosen (and from Favorites or Recent): Saved sessions, or Private
        /// sessions when Saved sessions are turned off in Options
        /// </summary>
        TreeNode DefaultRoot
        {
            get { return SuperPuTTY.Settings.UseSessionsXml ? this.nodeRoot : this.nodePrivate; }
        }

        void LoadSessions()
        {
            treeView1.Nodes.Clear();
            this.nodeFavorites = null;
            this.nodeRecent = null;

            // with Saved sessions turned off in Options the root exists but isn't shown, so code that refers to it
            // still works and it simply stays empty
            this.nodeRoot = SuperPuTTY.Settings.UseSessionsXml
                ? treeView1.Nodes.Add("root", "Saved sessions", ImageKeyFolder, ImageKeyFolder)
                : new TreeNode("Saved sessions", 0, 0) { Name = "root", ImageKey = ImageKeyFolder, SelectedImageKey = ImageKeyFolder };
            this.nodeRoot.Tag = RootKind.Shared;
            this.nodeRoot.ContextMenuStrip = this.contextMenuStripFolder;

            string privateKey = IsValidImage(ImageKeyPrivate) ? ImageKeyPrivate : ImageKeyFolder;
            this.nodePrivate = treeView1.Nodes.Add("private", "Private sessions", privateKey, privateKey);
            this.nodePrivate.Tag = RootKind.Private;
            this.nodePrivate.ContextMenuStrip = this.contextMenuStripFolder;
            this.nodePrivate.ToolTipText = "Only you see these: they're kept beside your settings, not in the shared Sessions.xml";

            this.nodeDatabase = null;
            this.EnsureDatabaseRoot();

            // every root is filled on demand: only its top level now, each folder when it's first opened
            this.indexDirty = true;
            this.FillRoot(this.nodeRoot, null);
            this.FillRoot(this.nodePrivate, null);
            this.FillRoot(this.nodeDatabase, null);
            BuildShortcutGroups();
            ResortNodes();
        }

        /// <summary>
        /// Show a session just added to the list (a new session, a copy, an import). Folders are filled on demand, so
        /// it's only added if its folder is already filled; otherwise it shows when the folder is opened. Folders
        /// missing on the way are made (and are filled: they hold only this session).
        /// </summary>
        /// <param name="session">The session</param>
        private void TryAddSessionNode(SessionData session)
        {
            this.indexDirty = true;
            if (session.IsDatabase)
            {
                // Database sessions is redrawn after each read (DatabaseSessions_Replaced)
                return;
            }
            if (this.filter != null && !this.filter(session))
                return;
            TreeNode nodeParent = session.IsPrivate ? this.nodePrivate : this.nodeRoot;
            string folder = session.SessionId == null ? null : SessionData.GetSessionParentId(session.SessionId);
            foreach (string part in (folder ?? string.Empty).Split(SessionIdDelim.ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
            {
                if (nodeParent == null || IsUnfilled(nodeParent))
                    return;
                nodeParent = nodeParent.Nodes[part] ?? AddFolderNode(nodeParent, part);
            }
            if (nodeParent != null && !IsUnfilled(nodeParent))
            {
                AddSessionNode(nodeParent, session, true);
            }
        }

        public void SessionPropertyChanged(SessionData Session, String PropertyName)
        {
            if (Session == null)
                return;

            if (PropertyName == "Color")
            {
                this.ColorNode(FindSessionNode(Session.SessionId));
            }

            if (PropertyName == "SessionName" || PropertyName == "ImageKey")
            {
                TreeNode Node = FindSessionNode(Session.SessionId);
                if (Node == null)
                {
                    // It is possible that the session id was changed before the
                    // session name. In this case, we check to see if we
                    // can find a node with the old session id that is also associated
                    // to the session data.
                    Node = FindSessionNode(Session.OldSessionId);
                    if (Node == null || Node.Tag != Session)
                        return;
                }

                Node.Text = Session.SessionName;
                Node.Name = Session.SessionName;
                string imageKey = SessionImageKey(Session);
                Node.ImageKey = imageKey;
                Node.SelectedImageKey = imageKey;
                ScheduleShortcutRebuild();

                bool IsSelectedSession = treeView1.SelectedNode == Node;
                ResortNodes();
                if (IsSelectedSession)
                {
                    // Re-selecting the node since it will be unselected when sorting.
                    treeView1.SelectedNode = Node;
                }
            }
            else if (PropertyName == "SessionId")
            {
                TreeNode Node = FindSessionNode(Session.OldSessionId);
                if (Node == null)
                {
                    // It is possible that the session name was changed before the
                    // session id. In this case, we check to see if we
                    // can find a node with the current session id that is also associated
                    // to the session data.
                    Node = FindSessionNode(Session.SessionId);
                    if (Node == null || Node.Tag != Session)
                        return;
                }

                try
                {
                    this.isRenamingNode = true;
                    SuperPuTTY.RemoveSession(Session.OldSessionId);
                    SuperPuTTY.AddSession(Session);
                }
                finally
                {
                    this.isRenamingNode = false;
                }
                // a renamed or moved favourite stays a favourite
                SessionShortcuts.Rename(Session.OldSessionId, Session.SessionId);
                this.indexDirty = true;
            }
        }

        public void SessionPropertyChanging(SessionData Session, String AttributeName, Object NewValue, ref bool CancelChange)
        {
            if (Session == null)
                return;

            if (AttributeName == "SessionName")
            {
                TreeNode Node = FindSessionNode(Session.SessionId);
                if (Node == null)
                    return;

                String Error = "";
                bool IsValid = ValidateSessionNameChange(Node.Parent, Node, NewValue as String, out Error);
                if (!IsValid)
                {
                    CancelChange = true;
                    Messenger.MessageBox(Error);
                }
            }
        }

        void Sessions_ListChanged(object sender, ListChangedEventArgs e)
        {
            this.indexDirty = true;
            if (isRenamingNode)
            {
                return;
            }
            BindingList<SessionData> sessions = (BindingList<SessionData>) sender;
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                SessionData session = sessions[e.NewIndex];
                TryAddSessionNode(session);
                if (SessionShortcuts.IsFavorite(session.SessionId) || SessionShortcuts.Recent.Contains(session.SessionId))
                {
                    ScheduleShortcutRebuild();
                }
            }
            else if (e.ListChangedType == ListChangedType.Reset)
            {
                // the list is being reloaded (File > Reload sessions): empty Saved and Private sessions, ignore the
                // sessions added one by one, and fill them once the reload is done, reopening the open folders
                if (this.reopenAfterReload == null && !this.IsHandleCreated)
                {
                    // not shown yet: empty the roots and let the sessions be added one by one, as they come
                    this.nodeRoot.Nodes.Clear();
                    this.nodePrivate.Nodes.Clear();
                }
                else if (this.reopenAfterReload == null)
                {
                    this.reopenAfterReload = new Dictionary<RootKind, List<string>>();
                    foreach (TreeNode root in new[] { this.nodeRoot, this.nodePrivate })
                    {
                        List<string> open = new List<string>();
                        this.CollectOpenFolders(root, open);
                        this.reopenAfterReload[(RootKind)root.Tag] = open;
                        root.Nodes.Clear();
                        root.Nodes.Add(new TreeNode { Name = LazyPlaceholder });
                    }
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        Dictionary<RootKind, List<string>> reopen = this.reopenAfterReload;
                        this.reopenAfterReload = null;
                        this.FillRoot(this.nodeRoot, reopen[RootKind.Shared]);
                        this.FillRoot(this.nodePrivate, reopen[RootKind.Private]);
                        this.RefreshOpenStates();
                    });
                }
                ScheduleShortcutRebuild();
            }
            // @TODO: implement more later, note delete will be tricky...need a copy of the list
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (this.SelectionChanged != null)
            {
                this.SelectionChanged(this.SelectedSession);
            }
        }

        /// <summary>
        /// Opens the selected session when the node is double clicked in the treeview
        /// </summary>
        /// <param name="sender">The treeview control that was double clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // e is null if this method is called from connectToolStripMenuItem_Click
            TreeNode node = e != null ? e.Node : treeView1.SelectedNode;

            if (IsSessionNode(node) && node == treeView1.SelectedNode)
            {
                SessionData sessionData = (SessionData)node.Tag;
                SuperPuTTY.OpenProtoSession(sessionData);
            }
            else if (IsShortcutNode(node) && node == treeView1.SelectedNode)
            {
                SuperPuTTY.OpenProtoSession(((SessionShortcut)node.Tag).Session);
            }
        }


        /// <summary>
        /// Create/Update a session entry
        /// </summary>
        /// <param name="sender">The toolstripmenuitem control that was clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void CreateOrEditSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem))
            {
                return;
            }

            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;

            if (menuItem == newSessionToolStripMenuItem)
            {
                // create new session
                CreateOrEditSession(0, null);
            }
            else if (menuItem == createLikeToolStripMenuItem)
            {
                // copy as
                SessionData session = (SessionData)((SessionData)treeView1.SelectedNode.Tag).Clone();
                CreateOrEditSession(1, session);
            }
            else
            {
                // edit, session node selected
                // We make a clone of the session since we do not want to directly edit the real object.
                SessionData session = (SessionData)((SessionData)treeView1.SelectedNode.Tag).Clone();
                CreateOrEditSession(2, session);
            }
        }


        public void CreateOrEditSession(int mode, SessionData existingSession)
        { 
            SessionData session = existingSession;
            TreeNode node = null;
            TreeNode nodeRef = this.DefaultRoot;
            string title = null;

            // Favorites and Recent hold shortcuts, not sessions: new sessions from there go in Saved sessions (or
            // Private sessions, when Saved sessions are turned off)
            if (treeView1.SelectedNode == null || IsQuickNode(treeView1.SelectedNode))
            {
                treeView1.SelectedNode = this.DefaultRoot;
            }
            if (mode == 2 && DatabaseLocked(treeView1.SelectedNode))
            {
                // the table is read-only for this connection or login, or can't be reached
                return;
            }

            bool isFolderNode = IsFolderNode(treeView1.SelectedNode);

            if (mode == 0)
            {
                // create new session
                session = new SessionData();
                nodeRef = isFolderNode ? treeView1.SelectedNode : treeView1.SelectedNode.Parent;
                title = "Create new session";
            }
            else if (mode == 1)
            {
                // copy as
                if (session == null)
                {
                    Log.DebugFormat("session null");
                    return;
                }
                session.SessionId = SuperPuTTY.MakeUniqueSessionId(session.SessionId);
                session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                nodeRef = isFolderNode ? treeView1.SelectedNode : treeView1.SelectedNode.Parent;
                title = "Create new session based on " + session.OldName;
            }
            else if (mode == 2)
            {
                if (session == null)
                {
                    Log.DebugFormat("session null");
                    return;
                }
                // edit, session node selected
                node = treeView1.SelectedNode;
                nodeRef = node.Parent;
                title = "Edit session: " + session.SessionName;
                if (session.DatabaseLocked)
                {
                    // locked in the database: it opens, but isn't edited here
                    return;
                }
            }

            if (node == null && DatabaseLocked(nodeRef))
            {
                // the table can't be written: new sessions, and copies of database sessions, go in Saved (or Private) sessions
                nodeRef = this.DefaultRoot;
                if (session.IsDatabase)
                {
                    session.SessionId = SuperPuTTY.MakeUniqueSessionId(session.SessionName);
                    session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                }
            }
            if (node == null)
            {
                session.DatabaseId = null;
                // the folder it goes in is filled first, so the new session shows there and is selected
                this.EnsureFolderFilled(nodeRef);
            }

            dlgEditSession form = new dlgEditSession(session, treeView1.ImageList) {Text = title};
            if (node != null && session.IsDatabase)
            {
                // fields the database's locked_fields names are shown but can't be changed
                form.LockedFields = session.DatabaseLockedFields;
            }
            form.SessionNameValidator += delegate(string txt, out string error)
            {
                bool IsValid = ValidateSessionNameChange(nodeRef, node, txt, out error);
                return IsValid;
            };
            
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                if (IsDatabaseNode(nodeRef))
                {
                    // database sessions are written to the table, then read back
                    this.SaveToDatabase(node, nodeRef, session);
                    return;
                }
                /* "node" will only be assigned if we're editing an existing session entry */
                if (node == null)
                {
                    session.IsPrivate = IsPrivateNode(nodeRef);
                    // get the path up to the ref (parent) node
                    if (nodeRef.Parent != null)
                    {
                        UpdateSessionId(nodeRef, session);
                        session.SessionId = SessionData.CombineSessionIds(session.SessionId, session.SessionName);
                    }
                    SuperPuTTY.AddSession(session);

                    // find new node and select it
                    TreeNode nodeNew = nodeRef.Nodes[session.SessionName];
                    if (nodeNew != null)
                    {
                        treeView1.SelectedNode = nodeNew;
                    }
                }
                else
                {

                    SessionData RealSession = (SessionData)treeView1.SelectedNode.Tag;
                    RealSession.CopyFrom(session);
                    RealSession.SessionName = session.SessionName;
                    treeView1.SelectedNode = node;
                }

                //treeView1.ExpandAll();
                SuperPuTTY.SaveSessions();
            }
            
        }

        private bool ValidateSessionNameChange(TreeNode ParentNode, TreeNode Node, String NewName, out String Error)
        {
            bool IsEdit = Node != null;
            Error = String.Empty;
            bool IsDupeNode;
            if (IsEdit)
                IsDupeNode = ParentNode.Nodes.ContainsKey(NewName) && ParentNode.Nodes[NewName] != Node;
            else
                IsDupeNode = ParentNode.Nodes.ContainsKey(NewName);

            string newId = SessionData.CombineSessionIds(FolderPath(ParentNode), NewName);
            SessionData sameId = string.IsNullOrEmpty(NewName) ? null : SuperPuTTY.GetSessionById(newId);
            if (IsDupeNode)
            {
                Error = "Session with same name exists";
            }
            else if (sameId != null && (Node == null || sameId != Node.Tag))
            {
                // IDs must be unique across Saved and Private sessions, which can have the same folders
                Error = "A session with this name is already in the same folder of " + (sameId.IsPrivate ? "Private sessions" : "Saved sessions");
            }
            else if (NewName.Contains(SessionIdDelim))
            {
                Error = "Invalid character ( " + SessionIdDelim + " ) in name";
            }
            else if (string.IsNullOrEmpty(NewName) || NewName.Trim() == String.Empty)
            {
                Error = "Empty name";
            }
            return string.IsNullOrEmpty(Error);
        }

        /// <summary>
        /// Forces a node to be selected when right clicked, this assures the context menu will be operating
        /// on the proper session entry.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = treeView1.GetNodeAt(e.X, e.Y);
            }          
        }

        /// <summary>
        /// Delete a session entry from the treeview and the registry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            if (DatabaseLocked(treeView1.SelectedNode) || session.DatabaseLocked)
            {
                return;
            }
            if (Messenger.MessageBox("Are you sure you want to delete session \"" + session.SessionName + "\"?", "Delete session", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (session.IsDatabase)
                {
                    if (this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Delete, Session = session }))
                    {
                        SessionShortcuts.Forget(session.SessionId);
                    }
                    return;
                }
                //session.RegistryRemove(session.SessionName);
                treeView1.SelectedNode.Remove();
                SuperPuTTY.RemoveSession(session.SessionId);
                SuperPuTTY.SaveSessions();
                SessionShortcuts.Forget(session.SessionId);
                //m_SessionsById.Remove(session.SessionId);
            }
        }

        /// <summary>
        /// Open a directory listing on the selected nodes host to allow dropping files
        /// for drag + drop copy.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void fileBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            SuperPuTTY.OpenScpSession(session);
        }

        /// <summary>
        /// Shortcut for double clicking an entries node.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1_NodeMouseDoubleClick(null, null);
        }

        /// <summary>
        /// Open putty with args but as external process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectExternalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (IsSessionNode(node))
            {
                SessionData sessionData = (SessionData)node.Tag;
                if (!SessionVariables.EnsureDefined(sessionData, this))
                {
                    return;
                }
                PuttyStartInfo startInfo = new PuttyStartInfo(sessionData);
                startInfo.StartStandalone();
            }
        }

        private void connectInNewSuperPuTTYToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (IsSessionNode(node))
            {
                SuperPuTTY.LoadSessionInNewInstance(((SessionData)node.Tag).SessionId);
            }
        }

        private void newFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            // a database folder not opened yet is filled first, or the new folder would go when it's opened
            this.EnsureFolderFilled(node);
            if (node != null)
            {
                dlgRenameItem dialog = new dlgRenameItem
                {
                    Text = "New Folder",
                    ItemName = "New Folder",
                    DetailName = "",
                    ItemNameValidator = delegate(string txt, out string error)
                    {
                        error = String.Empty;
                        if (node.Nodes.ContainsKey(txt))
                        {
                            error = "Node with same name exists";
                        }
                        else if (txt.Contains(SessionIdDelim))
                        {
                            error = "Invalid character ( " + SessionIdDelim + " ) in name";
                        }
                        else if (string.IsNullOrEmpty(txt) || txt.Trim() == String.Empty)
                        {
                            error = "Empty folder name";
                        }

                        return string.IsNullOrEmpty(error);
                    }
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFolderNode(node, dialog.ItemName);
                }
            }
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                dlgRenameItem dialog = new dlgRenameItem
                {
                    Text = "Rename Folder",
                    ItemName = node.Text,
                    DetailName = "",
                    ItemNameValidator = delegate(string txt, out string error)
                    {
                        error = String.Empty;
                        if (node.Parent.Nodes.ContainsKey(txt) && txt != node.Text)
                        {
                            error = "Node with same name exists";
                        }
                        else if (txt.Contains(SessionIdDelim))
                        {
                            error = "Invalid character ( " + SessionIdDelim + " ) in name";
                        }
                        return string.IsNullOrEmpty(error);
                    }
                };
                if (dialog.ShowDialog(this) == DialogResult.OK && node.Text != dialog.ItemName && IsDatabaseNode(node))
                {
                    // the sessions in it, at every level, move to the new path in the table
                    string from = DatabaseFolder(node);
                    string to = SessionData.CombineSessionIds(NullIfEmpty(DatabaseFolder(node.Parent)), dialog.ItemName);
                    List<SessionData> moved = new List<SessionData>();
                    GetAllSessions(node, moved);
                    SessionData locked = moved.FirstOrDefault(m => m.IsDatabaseFieldLocked("folder"));
                    if (locked != null)
                    {
                        Messenger.MessageBox("\"" + locked.SessionName + "\" in this folder is locked in the database, so the folder can't be renamed here.",
                            "Rename folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (moved.Count == 0)
                    {
                        // a folder made here and still empty exists only in the tree
                        node.Text = dialog.ItemName;
                        node.Name = dialog.ItemName;
                        ResortNodes();
                    }
                    else if (this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.RenameFolder, From = from, To = to }))
                    {
                        foreach (SessionData session in moved)
                        {
                            SessionShortcuts.Rename(session.SessionId, DatabaseSessions.IdPrefix + SessionIdDelim + to + session.SessionId.Substring((DatabaseSessions.IdPrefix + SessionIdDelim + from).Length));
                        }
                    }
                }
                else if (dialog.DialogResult == DialogResult.OK && node.Text != dialog.ItemName)
                {
                    // the new path is given to every session below by walking the nodes, so they all have to be there
                    this.FillSubtree(node);
                    string oldPath = NodePath(node);
                    node.Text = dialog.ItemName;
                    node.Name = dialog.ItemName;
                    UpdateSessionId(node);
                    SuperPuTTY.SaveSessions();
                    // its colour (and its subfolders') goes with it
                    FolderSettings.Store store = StoreOf(node).Value;
                    FolderSettings.Move(store, oldPath, store, NodePath(node));
                    ResortNodes();
                }
            }
        }

        private void removeFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                if (node.Nodes.Count > 0)
                {
                    List<SessionData> sessions = new List<SessionData>();
                    GetAllSessions(node, sessions);
                    if (DialogResult.Yes == Messenger.MessageBox(
                        "Are you sure you want to remove folder \"" + node.Text + "\" and its " + sessions.Count + " sessions?",
                        "Remove folder", 
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question))
                    {
                        if (IsDatabaseNode(node))
                        {
                            string folder = DatabaseFolder(node);
                            SessionData locked = sessions.FirstOrDefault(m => m.DatabaseLocked);
                            if (locked != null)
                            {
                                Messenger.MessageBox("\"" + locked.SessionName + "\" in this folder is locked in the database, so the folder can't be removed here.",
                                    "Remove folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }
                            if (this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.DeleteFolder, From = folder }))
                            {
                                sessions.ForEach(session => SessionShortcuts.Forget(session.SessionId));
                            }
                            return;
                        }
                        foreach (SessionData session in sessions)
                        {
                            SuperPuTTY.RemoveSession(session.SessionId);
                            SessionShortcuts.Forget(session.SessionId);
                        }
                        FolderSettings.Remove(StoreOf(node).Value, NodePath(node));
                        node.Remove();
                        SuperPuTTY.ReportStatus("Removed Folder, {0} and {1} sessions", node.Text, sessions.Count);
                        SuperPuTTY.SaveSessions();
                    }
                }
                else
                {
                    FolderSettings.Store? store = StoreOf(node);
                    if (store != null)
                    {
                        FolderSettings.Remove(store.Value, NodePath(node));
                    }
                    node.Remove();
                    SuperPuTTY.ReportStatus("Removed Folder, {0}", node.Text);
                }
            }
        }

        private void connectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null && !IsSessionNode(node))
            {
                List<SessionData> sessions = new List<SessionData>();
                GetAllSessions(node, sessions);
                Log.InfoFormat("Found {0} sessions", sessions.Count);

                if (sessions.Count > MaxSessionsToOpen)
                {
                    if (DialogResult.Cancel == Messenger.MessageBox(
                        "Open All " + sessions.Count + " sessions?", 
                        "WARNING", 
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                    {
                        // bug out...too many sessions to open
                        return;
                    }
                }
                foreach (SessionData session in sessions)
                {
                    SuperPuTTY.OpenProtoSession(session);
                }
            }
        }

        #region Environment colours

        /// <summary>The folder settings store of a node's root</summary>
        /// <param name="node">A folder, session or root</param>
        /// <returns>Shared, Private or Database; null under Favorites and Recent</returns>
        FolderSettings.Store? StoreOf(TreeNode node)
        {
            TreeNode root = RootOf(node);
            return root == null ? (FolderSettings.Store?)null : root == this.nodeRoot ? FolderSettings.Store.Shared
                : root == this.nodePrivate ? FolderSettings.Store.Private : root == this.nodeDatabase ? FolderSettings.Store.Database
                : (FolderSettings.Store?)null;
        }

        /// <summary>
        /// Show a node's environment colour as its text colour: a session's own colour, or a folder's own setting or
        /// a rule matching its name. Sessions that only inherit a colour aren't coloured here; their tabs are.
        /// </summary>
        /// <param name="node">A session or folder node</param>
        void ColorNode(TreeNode node)
        {
            if (node == null || node.Parent == null || IsQuickNode(node))
                return;
            Color? color;
            if (IsSessionNode(node))
            {
                color = EnvironmentColors.Parse(((SessionData)node.Tag).Color);
            }
            else
            {
                FolderSettings.Store? store = StoreOf(node);
                color = store == null ? null : EnvironmentColors.ForFolder(store.Value, NodePath(node));
            }
            Color text = color == null ? Color.Empty : EnvironmentColors.ForText(color.Value, this.treeView1.BackColor);
            if (node.ForeColor != text)
            {
                node.ForeColor = text;
            }
        }

        /// <summary>Colour every node in the tree again (only folders already filled have nodes)</summary>
        /// <param name="nodes">The nodes to start from</param>
        void ColorNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                this.ColorNode(node);
                if (node.Nodes.Count > 0)
                {
                    this.ColorNodes(node.Nodes);
                }
            }
        }

        /// <summary>A rule, a folder's or a session's colour may have changed: colour the tree again</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        void EnvironmentColors_Changed(object sender, EventArgs e)
        {
            if (!this.IsDisposed)
            {
                this.ColorNodes(this.treeView1.Nodes);
            }
        }

        /// <summary>Fill a session's Colour menu</summary>
        /// <param name="session">The selected session</param>
        void FillSessionColorMenu(SessionData session)
        {
            if (session == null)
                return;
            EnvironmentColors.FillMenu(this.colorSessionToolStripMenuItem, session.Color, "None (use the folder's or a rule's)", color => this.SetSessionColor(session, color));
        }

        /// <summary>Fill a folder's Colour menu</summary>
        /// <param name="node">The selected folder</param>
        void FillFolderColorMenu(TreeNode node)
        {
            FolderSettings.Store? store = StoreOf(node);
            if (node == null || store == null)
                return;
            FolderSetting setting = FolderSettings.Get(store.Value, NodePath(node));
            EnvironmentColors.FillMenu(this.colorFolderToolStripMenuItem, setting?.Color, "None (use a parent's or a rule's)", color => this.SetFolderColor(node, color));
        }

        /// <summary>Set or clear a session's own colour, saving it to Sessions.xml or to the table</summary>
        /// <param name="session">The session</param>
        /// <param name="color">"#RRGGBB", or "" for none</param>
        void SetSessionColor(SessionData session, string color)
        {
            if (session.IsDatabase)
            {
                if (!DatabaseSessions.HasColorColumn)
                {
                    Messenger.MessageBox("The database table has no color column yet. Run Create / upgrade schema in Tools > Database sessions.",
                        "Colour", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SessionData changed = (SessionData)session.Clone();
                changed.Color = color;
                this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Update, Session = changed, Folder = DatabaseSessions.FolderOf(session.SessionId) });
                return;
            }
            session.Color = color;
            SuperPuTTY.SaveSessions();
            EnvironmentColors.RaiseChanged();
        }

        /// <summary>Set or clear a folder's colour: in Folders.xml or PrivateFolders.xml, or in the table's folder settings</summary>
        /// <param name="node">The folder</param>
        /// <param name="color">"#RRGGBB", or "" for none</param>
        void SetFolderColor(TreeNode node, string color)
        {
            FolderSetting setting = FolderSettings.Get(StoreOf(node).Value, NodePath(node)) ?? new FolderSetting { Path = NodePath(node) };
            setting.Color = color;
            this.SaveFolderSetting(node, setting);
        }

        /// <summary>Open a folder's settings (colour and the defaults its sessions inherit) and save them</summary>
        /// <param name="node">The folder</param>
        void EditFolderSettings(TreeNode node)
        {
            FolderSettings.Store? store = StoreOf(node);
            if (node == null || store == null || node.Parent == null)
                return;
            using (dlgFolderSettings dialog = new dlgFolderSettings(NodePath(node), FolderSettings.Get(store.Value, NodePath(node))))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.SaveFolderSetting(node, dialog.Setting);
                }
            }
        }

        /// <summary>
        /// Save a folder's settings: in Folders.xml or PrivateFolders.xml, or in the table's folder settings (which needs
        /// it online), then read back
        /// </summary>
        /// <param name="node">The folder</param>
        /// <param name="setting">Its settings; empty ones remove them</param>
        void SaveFolderSetting(TreeNode node, FolderSetting setting)
        {
            FolderSettings.Store store = StoreOf(node).Value;
            setting.Path = NodePath(node);
            if (store != FolderSettings.Store.Database)
            {
                FolderSettings.Set(store, setting);
                return;
            }
            if (DatabaseSessions.State != DatabaseSessions.SyncState.Online)
            {
                Messenger.MessageBox("Folder settings in Database sessions are saved in the database, which can't be reached now.",
                    "Folder settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                DatabaseSessions.SetFolder(DatabaseSessions.FromSettings(), setting);
                DatabaseSessions.BeginSync();
            }
            catch (InvalidOperationException ex)
            {
                Messenger.MessageBox("Couldn't save the folder's settings: " + ex.Message, "Folder settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        /// <summary>Export a folder's sessions, with those in its subfolders, to a CSV file</summary>
        /// <param name="node">The folder, or a root such as Saved sessions</param>
        private void ExportFolderToCsv(TreeNode node)
        {
            if (node == null || IsSessionNode(node))
                return;
            List<SessionData> sessions = new List<SessionData>();
            GetAllSessions(node, sessions);
            SessionCsv.ExportWithDialog(this, sessions, node.Text + ".csv");
        }

        private void contextMenuStripFolder_Opening(object sender, CancelEventArgs e)
        {
            bool isRootNode = this.treeView1.SelectedNode != null && this.treeView1.SelectedNode.Parent != null;
            this.renameToolStripMenuItem.Enabled = isRootNode;
            // TODO: handle removing folder and nodes in it recursively
            this.removeFolderToolStripMenuItem.Enabled = isRootNode;// && this.treeView1.SelectedNode.Nodes.Count == 0;
            this.moveFolderToolStripMenuItem.Visible = isRootNode;
            this.moveFolderToolStripMenuItem.Text = MoveMenuText(this.treeView1.SelectedNode);
            // folders under Database sessions mirror the table: changed only when it can be written, and not moved yet
            bool locked = DatabaseLocked(this.treeView1.SelectedNode);
            this.newSessionToolStripMenuItem.Enabled = !locked;
            this.newFolderToolStripMenuItem.Enabled = !locked;
            this.renameToolStripMenuItem.Enabled &= !locked;
            this.removeFolderToolStripMenuItem.Enabled &= !locked;
            this.copyFolderToolStripMenuItem.Visible = isRootNode;
            // roots aren't coloured: a colour belongs to a folder and what's under it
            this.colorFolderToolStripMenuItem.Visible = isRootNode;
            this.colorFolderToolStripMenuItem.Enabled = !locked;
            this.editSessionsToolStripMenuItem.Enabled = !locked;
            this.folderSettingsToolStripMenuItem.Visible = isRootNode;
            this.folderSettingsToolStripMenuItem.Enabled = !locked;
        }

        /// <summary>Change fields on all the sessions in a folder (and its subfolders) at once</summary>
        /// <param name="node">The folder or root</param>
        private void BulkEditFolder(TreeNode node)
        {
            if (node == null || IsSessionNode(node))
                return;
            List<SessionData> sessions = new List<SessionData>();
            GetAllSessions(node, sessions);
            if (sessions.Count == 0)
            {
                Messenger.MessageBox("\"" + node.Text + "\" has no sessions.", "Edit sessions", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (dlgBulkEdit dialog = new dlgBulkEdit(node.Text, sessions))
            {
                dialog.ShowDialog(this);
            }
        }

        private void contextMenuStripAddTreeItem_Opening(object sender, CancelEventArgs e)
        {
            // disable file transfers if pscp isn't configured.
            fileBrowserToolStripMenuItem.Enabled = SuperPuTTY.IsScpEnabled;
            fileZillaToolStripMenuItem.Enabled = SuperPuTTY.IsFilezillaEnabled;
            winSCPToolStripMenuItem.Enabled = SuperPuTTY.IsWinSCPEnabled;
            fileZillaToolStripMenuItem.Visible = SuperPuTTY.IsFilezillaEnabled;
            winSCPToolStripMenuItem.Visible = SuperPuTTY.IsWinSCPEnabled;

            connectInNewSuperPuTTYToolStripMenuItem.Enabled = !SuperPuTTY.Settings.SingleInstanceMode;

            SessionData session = this.SelectedSession;
            this.favoriteToolStripMenuItem.Text = session != null && SessionShortcuts.IsFavorite(session.SessionId)
                ? "Remove from Favorites" : "Add to Favorites";
            this.moveSessionToolStripMenuItem.Text = MoveMenuText(this.treeView1.SelectedNode);
            // database sessions are edited and deleted only when the table can be written, and not moved yet
            bool locked = DatabaseLocked(this.treeView1.SelectedNode) || (session != null && session.DatabaseLocked);
            this.settingsToolStripMenuItem.Enabled = !locked;
            this.deleteToolStripMenuItem.Enabled = !locked;
            this.colorSessionToolStripMenuItem.Enabled = !locked && (session == null || !session.IsDatabaseFieldLocked("color"));
        }

        /// <summary>The text of the menu item that moves a node to another root (now a submenu listing them)</summary>
        /// <param name="node">The session or folder</param>
        /// <returns>"Move to"</returns>
        string MoveMenuText(TreeNode node)
        {
            return "Move to";
        }

        #region Copying and moving between roots

        /// <summary>Which root a node is under</summary>
        /// <param name="node">A node</param>
        /// <returns>Its root's kind</returns>
        RootKind KindOf(TreeNode node)
        {
            object tag = RootOf(node)?.Tag;
            return tag is RootKind ? (RootKind)tag : RootKind.Shared;
        }

        /// <summary>A root's name, as in the tree</summary>
        /// <param name="kind">Shared, Private or Database</param>
        /// <returns>e.g. "Saved sessions"</returns>
        static string RootName(RootKind kind)
        {
            return kind == RootKind.Private ? "Private sessions" : kind == RootKind.Database ? "Database sessions" : "Saved sessions";
        }

        /// <summary>A Move to or Copy to submenu, filled with the other roots as it opens</summary>
        /// <param name="text">"Move to" or "Copy to"</param>
        /// <param name="move">Move rather than copy</param>
        /// <returns>The menu item</returns>
        ToolStripMenuItem TransferMenu(string text, bool move)
        {
            ToolStripMenuItem menu = new ToolStripMenuItem(text);
            menu.DropDownItems.Add("-");
            menu.DropDownOpening += (s, e) => this.FillTransferMenu(menu, this.treeView1.SelectedNode, move);
            return menu;
        }

        /// <summary>
        /// Fill a Copy to or Move to submenu with the other roots: Saved, Private and (when it's on) Database sessions.
        /// Database sessions can be chosen only while the table can be written, and moving out of it likewise.
        /// </summary>
        /// <param name="menu">The submenu</param>
        /// <param name="node">The session or folder</param>
        /// <param name="move">Move rather than copy</param>
        void FillTransferMenu(ToolStripMenuItem menu, TreeNode node, bool move)
        {
            menu.DropDownItems.Clear();
            if (node == null)
                return;
            RootKind from = KindOf(node);
            foreach (RootKind kind in new[] { RootKind.Shared, RootKind.Private, RootKind.Database })
            {
                if (kind == from || (kind == RootKind.Database && this.nodeDatabase == null) || (kind == RootKind.Shared && !SuperPuTTY.Settings.UseSessionsXml))
                    continue;
                RootKind target = kind;
                bool enabled = (target != RootKind.Database || DatabaseSessions.Writable) && !(move && from == RootKind.Database && DatabaseLocked(node));
                menu.DropDownItems.Add(new ToolStripMenuItem(RootName(target), null, (s, e) => this.TransferToRoot(node, target, move)) { Enabled = enabled });
            }
        }

        /// <summary>Copy or move a session or folder to the same folder path in another root</summary>
        /// <param name="node">The session or folder</param>
        /// <param name="kind">The root</param>
        /// <param name="move">Move rather than copy</param>
        void TransferToRoot(TreeNode node, RootKind kind, bool move)
        {
            if (move && !IsDatabaseNode(node) && kind != RootKind.Database)
            {
                // between Saved and Private sessions: the node itself moves, as before
                this.MoveToOtherRoot(node);
                return;
            }
            this.Transfer(node, kind, NodePath(node.Parent), move);
        }

        /// <summary>A copy of a session for another root: without the database's id, version, locks and owner</summary>
        /// <param name="session">The session</param>
        /// <returns>The copy</returns>
        static SessionData PlainCopy(SessionData session)
        {
            SessionData copy = (SessionData)session.Clone();
            copy.DatabaseId = null;
            copy.DatabaseVersion = null;
            copy.DatabaseLocked = false;
            copy.DatabaseLockedFields = null;
            copy.DatabaseManagedBy = null;
            copy.DatabaseUpdatedBy = null;
            copy.DatabasePending = false;
            copy.DatabasePendingNote = null;
            copy.IsPrivate = false;
            return copy;
        }

        /// <summary>
        /// Copy or move a session, or a folder with everything under it, into a folder of another root, keeping the
        /// subfolders. Into Database sessions they're inserted as one batch; out of it they're added to Sessions.xml or
        /// PrivateSessions.xml, and for a move then deleted from the table as one batch. Sessions whose name is
        /// taken there, or that the table refuses (a lock, a name clash), are left where they were and listed.
        /// </summary>
        /// <param name="payload">The session or folder</param>
        /// <param name="kind">The root to copy or move into</param>
        /// <param name="targetFolder">The folder path there, "" for the top</param>
        /// <param name="move">Remove the originals once copied</param>
        void Transfer(TreeNode payload, RootKind kind, string targetFolder, bool move)
        {
            this.FillSubtree(payload);
            List<SessionData> sessions = new List<SessionData>();
            if (IsSessionNode(payload))
                sessions.Add((SessionData)payload.Tag);
            else
                GetAllSessions(payload, sessions);
            if (sessions.Count == 0)
            {
                Messenger.MessageBox("\"" + payload.Text + "\" has no sessions to copy.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool fromDatabase = IsDatabaseNode(payload);
            string verb = move ? "Move" : "Copy";
            if (move && fromDatabase && DialogResult.Yes != Messenger.MessageBox(
                string.Format("Move {0} session{1} out of Database sessions into {2}?\n\nThey're deleted from the table, for everyone who uses it.",
                    sessions.Count, sessions.Count == 1 ? "" : "s", RootName(kind)), verb, MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                return;
            if (kind == RootKind.Database && !DatabaseSessions.Writable)
            {
                Messenger.MessageBox("Database sessions can't be written now (read-only, no permission, or not read yet).", verb, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // each session keeps its place below the payload: its folder relative to the payload's parent
            string basePath = NodePath(payload.Parent);
            Func<SessionData, string> newFolder = s =>
            {
                string folder = FolderOf(s);
                string relative = basePath.Length == 0 ? folder : folder.Length <= basePath.Length ? string.Empty : folder.Substring(basePath.Length + 1);
                return SessionData.CombineSessionIds(NullIfEmpty(targetFolder), NullIfEmpty(relative)) ?? string.Empty;
            };
            Func<SessionData, string> nameOf = s => SessionData.GetSessionNameFromId(fromDatabase ? DatabaseSessions.TreePath(s.SessionId) : s.SessionId);
            List<string> problems = new List<string>();
            List<KeyValuePair<SessionData, string>> arrived = new List<KeyValuePair<SessionData, string>>();

            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                if (kind == RootKind.Database)
                {
                    List<DatabaseOutbox.PendingChange> inserts = new List<DatabaseOutbox.PendingChange>();
                    foreach (SessionData session in sessions)
                    {
                        SessionData copy = PlainCopy(session);
                        copy.SessionName = nameOf(session);
                        inserts.Add(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Insert, Session = copy, Folder = newFolder(session) });
                    }
                    List<string> errors;
                    HashSet<int> refused;
                    DatabaseSessions.SubmitBatch(inserts, out errors, out refused);
                    problems.AddRange(errors);
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        if (!refused.Contains(i))
                            arrived.Add(new KeyValuePair<SessionData, string>(sessions[i], DatabaseSessions.MakeSessionId(inserts[i].Folder, inserts[i].Session.SessionName)));
                    }
                    if (move)
                    {
                        // out of Saved or Private sessions: what arrived (or is queued) leaves here
                        foreach (KeyValuePair<SessionData, string> moved in arrived)
                        {
                            this.FindSessionNode(moved.Key.SessionId)?.Remove();
                            SuperPuTTY.RemoveSession(moved.Key.SessionId);
                            SessionShortcuts.Rename(moved.Key.SessionId, moved.Value);
                        }
                        SuperPuTTY.SaveSessions();
                    }
                }
                else
                {
                    foreach (SessionData session in sessions)
                    {
                        string newId = SessionData.CombineSessionIds(NullIfEmpty(newFolder(session)), nameOf(session));
                        SessionData existing = SuperPuTTY.GetSessionById(newId);
                        if (existing != null)
                        {
                            problems.Add(newId + ": already in " + RootName(existing.IsPrivate ? RootKind.Private : RootKind.Shared));
                            continue;
                        }
                        SessionData copy = PlainCopy(session);
                        copy.SessionId = newId;
                        copy.SessionName = nameOf(session);
                        copy.IsPrivate = kind == RootKind.Private;
                        SuperPuTTY.AddSession(copy);
                        arrived.Add(new KeyValuePair<SessionData, string>(session, newId));
                    }
                    SuperPuTTY.SaveSessions();
                    if (move && fromDatabase && arrived.Count > 0)
                    {
                        // out of the table: locked rows can't be deleted from here, so they stay there too
                        List<KeyValuePair<SessionData, string>> deletable = arrived.Where(a => !a.Key.DatabaseLocked).ToList();
                        problems.AddRange(arrived.Where(a => a.Key.DatabaseLocked).Select(a => a.Key.SessionName + ": locked in the database, so it was copied and stays there too"));
                        List<DatabaseOutbox.PendingChange> deletes = deletable.Select(a => new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Delete, Session = a.Key }).ToList();
                        List<string> errors;
                        HashSet<int> refused;
                        DatabaseSessions.SubmitBatch(deletes, out errors, out refused);
                        problems.AddRange(errors.Select(err => err + " (copied, but it stays in the database too)"));
                        for (int i = 0; i < deletable.Count; i++)
                        {
                            if (!refused.Contains(i))
                                SessionShortcuts.Rename(deletable[i].Key.SessionId, deletable[i].Value);
                        }
                    }
                }
            }
            finally
            {
                Cursor.Current = previous;
            }

            string done = string.Format("{0} {1} session{2} to {3}", move ? "Moved" : "Copied", arrived.Count, arrived.Count == 1 ? "" : "s", RootName(kind));
            Log.InfoFormat("{0}; {1} left where they were", done, problems.Count);
            SuperPuTTY.ReportStatus(done + (problems.Count > 0 ? string.Format(" ({0} left where they were)", problems.Count) : ""));
            if (problems.Count > 0)
            {
                Messenger.MessageBox(done + ". These were left where they were:\n\n" + string.Join("\n", problems.Take(15))
                    + (problems.Count > 15 ? "\n... and " + (problems.Count - 15) + " more" : ""), verb, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion

        #region Database sessions

        /// <summary>Add or remove the Database sessions root to match Options, and show its status</summary>
        void EnsureDatabaseRoot()
        {
            if (!DatabaseSessions.Enabled)
            {
                this.nodeDatabase?.Remove();
                this.nodeDatabase = null;
                return;
            }
            if (this.nodeDatabase == null)
            {
                string key = IsValidImage("database") ? "database" : ImageKeyFolder;
                this.nodeDatabase = this.treeView1.Nodes.Add("database", "Database sessions", key, key);
                this.nodeDatabase.Tag = RootKind.Database;
                this.nodeDatabase.ContextMenuStrip = this.contextMenuStripDatabaseRoot;
            }
            this.ShowDatabaseStatus();
        }

        /// <summary>Show the database status in the root's title and tooltip, e.g. "Database sessions (offline, cached 09:41)"</summary>
        void ShowDatabaseStatus()
        {
            if (this.nodeDatabase == null)
                return;
            this.nodeDatabase.Text = "Database sessions (" + DatabaseSessions.StatusText() + ")";
            string tip = "From " + DatabaseSessions.FromSettings();
            if (DatabaseSessions.SyncedAt.HasValue)
                tip += Environment.NewLine + "Read at " + DatabaseSessions.SyncedAt.Value.ToString("g");
            if (DatabaseSessions.State == DatabaseSessions.SyncState.Offline)
                tip += Environment.NewLine + "Can't reach it: " + DatabaseSessions.LastError + Environment.NewLine
                    + "Showing the cached copy; it's tried again every few minutes, or right-click -> Refresh"
                    + (DatabaseSessions.Writable ? Environment.NewLine + "Changes made now are kept here and sent when it's back" : "");
            if (DatabaseSessions.PendingCount + DatabaseSessions.FailedCount > 0)
                tip += Environment.NewLine + string.Format("{0} change(s) waiting, {1} not saved: right-click -> Pending changes",
                    DatabaseSessions.PendingCount, DatabaseSessions.FailedCount);
            if (DatabaseSessions.NeedsUpgrade)
                tip += Environment.NewLine + "The table has no lock columns yet: Database settings -> Create / upgrade schema";
            this.nodeDatabase.ToolTipText = tip;
        }

        /// <summary>Redraw the root's title when the read starts, progresses, finishes or fails</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        void DatabaseSessions_StatusChanged(object sender, EventArgs e)
        {
            this.EnsureDatabaseRoot();
        }

        /// <summary>
        /// Redraw Database sessions after its sessions were swapped for a fresh read. The nodes are added with sorting
        /// off and sorted once, since adding thousands one at a time into a sorted tree is slow.
        /// </summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        void DatabaseSessions_Replaced(object sender, EventArgs e)
        {
            this.EnsureDatabaseRoot();
            if (this.nodeDatabase == null)
            {
                this.ScheduleShortcutRebuild();
                return;
            }
            this.indexDirty = true;
            this.FillRoot(this.nodeDatabase, null);
            this.ScheduleShortcutRebuild();
            this.RefreshOpenStates();
            if (this.pendingSelectId != null)
            {
                // the session just added or changed
                TreeNode added = FindSessionNode(this.pendingSelectId);
                this.pendingSelectId = null;
                if (added != null)
                {
                    this.treeView1.SelectedNode = added;
                    added.EnsureVisible();
                }
            }
        }

        /// <summary>
        /// Add a database session to the table, or save changes to one, then read the table again; the session is
        /// selected once it's back. A failure (offline, a name clash, someone else's change) is shown, and the tree
        /// is left as it was.
        /// </summary>
        /// <param name="node">The session's node when editing; null when adding</param>
        /// <param name="nodeRef">The folder it goes in</param>
        /// <param name="session">The session as filled in</param>
        void SaveToDatabase(TreeNode node, TreeNode nodeRef, SessionData session)
        {
            string folder = DatabaseFolder(nodeRef);
            string newId = DatabaseSessions.MakeSessionId(folder, session.SessionName);
            SessionData real = node?.Tag as SessionData;
            if (real != null)
            {
                // the row it updates, and the version it was read at
                session.DatabaseId = real.DatabaseId;
                session.DatabaseVersion = real.DatabaseVersion;
            }
            // selected once it's shown: at once if it's queued, after the read if it's saved
            this.pendingSelectId = newId;
            bool saved = this.WriteDatabase(new DatabaseOutbox.PendingChange
            {
                Kind = real == null ? DatabaseOutbox.ChangeKind.Insert : DatabaseOutbox.ChangeKind.Update,
                Session = session,
                Folder = folder
            });
            if (!saved)
            {
                this.pendingSelectId = null;
            }
            else if (real != null)
            {
                // a renamed favourite stays a favourite
                SessionShortcuts.Rename(real.SessionId, newId);
            }
        }

        /// <summary>
        /// Make a change to the table, with a wait cursor: saved at once when it can be reached, otherwise queued and
        /// shown as pending until it's sent. A refusal (a lock, a name clash, someone else's change) is shown.
        /// </summary>
        /// <param name="change">The change</param>
        /// <returns>true if it was saved or queued</returns>
        bool WriteDatabase(DatabaseOutbox.PendingChange change)
        {
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            DatabaseSessions.SubmitResult result;
            string error;
            try
            {
                result = DatabaseSessions.Submit(change, out error);
            }
            finally
            {
                Cursor.Current = previous;
            }
            if (result == DatabaseSessions.SubmitResult.Failed)
            {
                Messenger.MessageBox("Couldn't save to the database: " + error, "Database sessions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (result == DatabaseSessions.SubmitResult.Queued)
            {
                SuperPuTTY.ReportStatus("The database can't be reached: the change is kept here and sent when it's back ({0})", change);
            }
            return true;
        }

        /// <summary>List the changes waiting for the database, to retry, overwrite or discard them</summary>
        void ShowPendingChanges()
        {
            using (dlgPendingChanges dialog = new dlgPendingChanges())
            {
                dialog.ShowDialog(this);
            }
        }

        /// <summary>
        /// Whether a database session or folder can be dropped into a folder of Database sessions: the table can be
        /// written, the target is a folder (or the root) other than its own, not inside the dragged folder, and
        /// nothing of the same name is there
        /// </summary>
        /// <param name="payload">The session or folder being dragged</param>
        /// <param name="target">The folder under the mouse</param>
        /// <returns>true if it can be moved there</returns>
        bool CanMoveInDatabase(TreeNode payload, TreeNode target)
        {
            this.EnsureFolderFilled(target);
            if (DatabaseLocked(target) || !IsFolderNode(target) || target == payload || target == payload.Parent
                || target.Nodes.ContainsKey(payload.Text))
                return false;
            for (TreeNode node = target; node != null; node = node.Parent)
            {
                if (node == payload)
                {
                    // a folder can't go inside itself
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Move a database session, or a folder and everything under it, into another folder of Database sessions:
        /// a session is saved with its new folder, a folder is renamed to its new path. Saved at once, or queued while
        /// offline; refused if a session involved has its folder locked. An empty folder made in the tree just moves.
        /// </summary>
        /// <param name="payload">The session or folder</param>
        /// <param name="target">The folder it's dropped on</param>
        void MoveInDatabase(TreeNode payload, TreeNode target)
        {
            string toFolder = DatabaseFolder(target);
            List<SessionData> sessions = new List<SessionData>();
            if (IsSessionNode(payload))
                sessions.Add((SessionData)payload.Tag);
            else
                GetAllSessions(payload, sessions);
            SessionData locked = sessions.FirstOrDefault(s => s.IsDatabaseFieldLocked("folder"));
            if (locked != null)
            {
                Messenger.MessageBox("\"" + locked.SessionName + "\" is locked in the database, so it can't be moved here.",
                    "Move", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (IsSessionNode(payload))
            {
                SessionData real = (SessionData)payload.Tag;
                SessionData moved = (SessionData)real.Clone();
                string newId = DatabaseSessions.MakeSessionId(toFolder, real.SessionName);
                this.pendingSelectId = newId;
                if (this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.Update, Session = moved, Folder = toFolder }))
                    SessionShortcuts.Rename(real.SessionId, newId);
                else
                    this.pendingSelectId = null;
                return;
            }

            if (sessions.Count == 0)
            {
                // a folder made here and still empty exists only in the tree
                TreeNode copy = (TreeNode)payload.Clone();
                payload.Remove();
                target.Nodes.Add(copy);
                target.Expand();
                return;
            }
            string from = DatabaseFolder(payload);
            string to = SessionData.CombineSessionIds(NullIfEmpty(toFolder), payload.Text);
            if (this.WriteDatabase(new DatabaseOutbox.PendingChange { Kind = DatabaseOutbox.ChangeKind.RenameFolder, From = from, To = to }))
            {
                string oldPrefix = DatabaseSessions.IdPrefix + SessionIdDelim + from;
                foreach (SessionData session in sessions)
                {
                    SessionShortcuts.Rename(session.SessionId, DatabaseSessions.IdPrefix + SessionIdDelim + to + session.SessionId.Substring(oldPrefix.Length));
                }
            }
        }

        /// <summary>The root node for a kind</summary>
        /// <param name="kind">Shared, Private or Database</param>
        /// <returns>Its node, or null (Database sessions while it's off)</returns>
        TreeNode RootNode(RootKind kind)
        {
            return kind == RootKind.Shared ? this.nodeRoot : kind == RootKind.Private ? this.nodePrivate
                : kind == RootKind.Database ? this.nodeDatabase : null;
        }

        /// <summary>A session's folder within its root</summary>
        /// <param name="session">The session</param>
        /// <returns>e.g. "Lab/Core", or "" at the top</returns>
        static string FolderOf(SessionData session)
        {
            return session.IsDatabase ? DatabaseSessions.FolderOf(session.SessionId)
                : SessionData.GetSessionParentId(session.SessionId) ?? string.Empty;
        }

        /// <summary>A folder node's path within its root, as <see cref="FolderOf"/> gives it</summary>
        /// <param name="node">A folder, or a root</param>
        /// <returns>e.g. "Lab/Core", or "" for a root</returns>
        string NodePath(TreeNode node)
        {
            return IsDatabaseNode(node) ? DatabaseFolder(node) : FolderPath(node) ?? string.Empty;
        }

        /// <summary>The index for a root, rebuilt first if sessions changed since it was built</summary>
        /// <param name="kind">The root</param>
        /// <returns>Its index</returns>
        FolderIndex IndexFor(RootKind kind)
        {
            if (this.indexDirty)
            {
                this.BuildIndex();
            }
            FolderIndex index;
            if (!this.folderIndex.TryGetValue(kind, out index))
                this.folderIndex[kind] = index = new FolderIndex();
            return index;
        }

        /// <summary>
        /// Index every session by root and folder, for filling folders on demand: all of them, and those the search
        /// shows with every folder on the way to them
        /// </summary>
        void BuildIndex()
        {
            this.folderIndex = new Dictionary<RootKind, FolderIndex>
            {
                { RootKind.Shared, new FolderIndex() },
                { RootKind.Private, new FolderIndex() },
                { RootKind.Database, new FolderIndex() }
            };
            foreach (SessionData session in SuperPuTTY.GetAllSessions())
            {
                FolderIndex index = this.folderIndex[session.IsDatabase ? RootKind.Database : session.IsPrivate ? RootKind.Private : RootKind.Shared];
                string folder = FolderOf(session);
                AddToIndex(index.All, folder, session);
                if (this.filter == null || this.filter(session))
                {
                    AddToIndex(index.Shown, folder, session);
                    // every folder on the way down shows, so the session can be reached
                    for (string child = folder; child.Length > 0; )
                    {
                        string parent = SessionData.GetSessionParentId(child) ?? string.Empty;
                        SortedSet<string> subfolders;
                        if (!index.Subfolders.TryGetValue(parent, out subfolders))
                            index.Subfolders[parent] = subfolders = new SortedSet<string>(StringComparer.Ordinal);
                        if (!subfolders.Add(SessionData.GetSessionNameFromId(child)))
                            break;
                        child = parent;
                    }
                }
            }
            this.indexDirty = false;
        }

        /// <summary>
        /// Put a root's top level in the tree; each folder is filled the first time it's opened
        /// (<see cref="FillFolder"/>). With 10,000 sessions, creating every node up front kept the window busy for
        /// seconds: almost all of it inside the Windows tree control. Folders that were open are opened again.
        /// </summary>
        /// <param name="root">Saved, Private or Database sessions; nothing happens for null</param>
        /// <param name="reopen">Folder paths to open again; null to reopen those open now</param>
        void FillRoot(TreeNode root, List<string> reopen)
        {
            if (root == null)
                return;
            if (reopen == null)
            {
                reopen = new List<string>();
                this.CollectOpenFolders(root, reopen);
            }
            bool firstFill = root.Nodes.Count == 0;
            bool expanded = root.IsExpanded;
            this.treeView1.BeginUpdate();
            try
            {
                this.FillFolder(root);
                if (expanded || firstFill)
                {
                    root.Expand();
                }
                foreach (string path in reopen)
                {
                    this.FindFolder(root, path)?.Expand();
                }
            }
            finally
            {
                this.treeView1.EndUpdate();
            }
        }

        /// <summary>Add a session to a by-folder index</summary>
        /// <param name="index">The index</param>
        /// <param name="folder">Its folder path</param>
        /// <param name="session">The session</param>
        static void AddToIndex(Dictionary<string, List<SessionData>> index, string folder, SessionData session)
        {
            List<SessionData> list;
            if (!index.TryGetValue(folder, out list))
                index[folder] = list = new List<SessionData>();
            list.Add(session);
        }

        /// <summary>The paths of the open folders under a root or folder, parents before children</summary>
        /// <param name="node">The root or a folder</param>
        /// <param name="open">Collects the paths</param>
        void CollectOpenFolders(TreeNode node, List<string> open)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (child.IsExpanded && IsFolderNode(child) && child.Name != LazyPlaceholder)
                {
                    open.Add(NodePath(child));
                    this.CollectOpenFolders(child, open);
                }
            }
        }

        /// <summary>Whether a folder is still only a placeholder, waiting to be filled</summary>
        /// <param name="node">The folder</param>
        /// <returns>true until it's filled</returns>
        static bool IsUnfilled(TreeNode node)
        {
            return node != null && node.Nodes.Count == 1 && node.Nodes[0].Name == LazyPlaceholder;
        }

        /// <summary>
        /// Put a folder's subfolders and sessions in the tree, from the index. Subfolders get a placeholder child, so
        /// they show as openable, and are filled when opened (<see cref="treeView1_BeforeExpand"/>). A big folder is
        /// filled with sorting off and sorted once, as sorting each node into place is slow.
        /// </summary>
        /// <param name="node">The folder, or a root</param>
        void FillFolder(TreeNode node)
        {
            RootKind kind = (RootKind)RootOf(node).Tag;
            FolderIndex index = this.IndexFor(kind);
            string path = NodePath(node);
            node.Nodes.Clear();
            SortedSet<string> subfolders;
            List<SessionData> sessions;
            index.Subfolders.TryGetValue(path, out subfolders);
            index.Shown.TryGetValue(path, out sessions);
            bool sortOnce = (subfolders?.Count ?? 0) + (sessions?.Count ?? 0) > SortOnceAbove && this.treeView1.TreeViewNodeSorter != null;
            if (sortOnce)
            {
                this.treeView1.TreeViewNodeSorter = null;
            }
            if (subfolders != null)
            {
                foreach (string name in subfolders)
                {
                    // directly, not AddFolderNode, which reports every folder in the status bar
                    TreeNode folder = node.Nodes.Add(name, name, ImageKeyFolder, ImageKeyFolder);
                    folder.ContextMenuStrip = this.contextMenuStripFolder;
                    folder.Nodes.Add(new TreeNode { Name = LazyPlaceholder });
                    this.ColorNode(folder);
                }
            }
            if (sessions != null)
            {
                foreach (SessionData session in sessions)
                {
                    this.AddSessionNode(node, session, true);
                }
            }
            if (sortOnce)
            {
                this.ResortNodes();
            }
        }

        /// <summary>Fill a folder, and every folder below it, so its whole subtree is in the tree (before walking it)</summary>
        /// <param name="node">The folder or root; nothing happens for a session or null</param>
        void FillSubtree(TreeNode node)
        {
            if (node == null || IsSessionNode(node) || IsQuickNode(node))
                return;
            this.EnsureFolderFilled(node);
            foreach (TreeNode child in node.Nodes.Cast<TreeNode>().ToList())
            {
                if (!IsSessionNode(child) && child.Name != LazyPlaceholder)
                    this.FillSubtree(child);
            }
        }

        /// <summary>
        /// Expand a node and everything under it (or the whole tree) with drawing paused throughout. Database folders
        /// fill as they open; without the pause, each one redrew the tree, and expanding 900 folders took over a minute.
        /// </summary>
        /// <param name="node">The node, or null for the whole tree</param>
        void ExpandFully(TreeNode node)
        {
            this.treeView1.BeginUpdate();
            try
            {
                if (node == null)
                    this.treeView1.ExpandAll();
                else
                    node.ExpandAll();
            }
            finally
            {
                this.treeView1.EndUpdate();
            }
        }

        /// <summary>Fill a folder that's still only a placeholder</summary>
        /// <param name="node">The folder</param>
        void EnsureFolderFilled(TreeNode node)
        {
            if (IsUnfilled(node))
            {
                this.treeView1.BeginUpdate();
                try
                {
                    this.FillFolder(node);
                }
                finally
                {
                    this.treeView1.EndUpdate();
                }
            }
        }

        /// <summary>Fill a folder as it's opened</summary>
        /// <param name="sender">The tree</param>
        /// <param name="e">The folder being opened</param>
        void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            this.EnsureFolderFilled(e.Node);
        }

        /// <summary>A folder's node under a root, filling the folders on the way</summary>
        /// <param name="root">The root</param>
        /// <param name="path">e.g. "Region 1/Iowa"</param>
        /// <returns>The node, or null if it isn't shown</returns>
        TreeNode FindFolder(TreeNode root, string path)
        {
            TreeNode node = root;
            foreach (string part in path.Split(SessionIdDelim.ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
            {
                this.EnsureFolderFilled(node);
                node = node?.Nodes[part];
                if (node == null || !IsFolderNode(node))
                    return null;
            }
            return node;
        }

        /// <summary>Open the database settings; they take effect when it's closed with OK</summary>
        void ShowDatabaseSettings()
        {
            using (dlgDatabaseSettings dialog = new dlgDatabaseSettings())
            {
                dialog.ShowDialog(this);
            }
        }

        #endregion

        #region Open tabs

        /// <summary>
        /// A session's icon as the tree shows it: its own icon (or the default), with a green dot while a tab for it is
        /// open and connected, or a red one while its tabs have dropped
        /// </summary>
        /// <param name="session">The session</param>
        /// <returns>The image key</returns>
        string SessionImageKey(SessionData session)
        {
            string baseKey = IsValidImage(session.ImageKey) ? session.ImageKey : ImageKeySession;
            IconLibrary.OpenState state;
            if (session.SessionId == null || !this.openStates.TryGetValue(session.SessionId, out state))
                return baseKey;
            return IconLibrary.EnsureWithState(this.treeView1.ImageList, baseKey, state);
        }

        /// <summary>Update the dots when a tab opens or closes</summary>
        /// <param name="sender">The dock panel</param>
        /// <param name="e">The tab</param>
        void DockPanel_ContentChanged(object sender, DockContentEventArgs e)
        {
            if (e.Content is ctlPuttyPanel)
            {
                this.ScheduleOpenStateRefresh();
            }
        }

        /// <summary>Update the dots when a tab's session drops or comes back</summary>
        /// <param name="sender">The tab</param>
        /// <param name="e">Not used</param>
        void PuttyPanel_ConnectionChanged(object sender, EventArgs e)
        {
            this.ScheduleOpenStateRefresh();
        }

        /// <summary>
        /// Refresh the dots once the current change is done: a closing tab is only gone from the dock panel afterwards,
        /// and a layout opens many tabs in a row
        /// </summary>
        void ScheduleOpenStateRefresh()
        {
            if (!this.IsHandleCreated)
            {
                this.RefreshOpenStates();
                return;
            }
            if (!this.openStateRefreshPending)
            {
                this.openStateRefreshPending = true;
                this.BeginInvoke((MethodInvoker)delegate
                {
                    this.openStateRefreshPending = false;
                    this.RefreshOpenStates();
                });
            }
        }

        /// <summary>
        /// Work out which sessions have a tab open, and whether any of each one's tabs is connected, then give every
        /// session node and Favorites/Recent entry the matching icon
        /// </summary>
        void RefreshOpenStates()
        {
            Dictionary<string, IconLibrary.OpenState> states = new Dictionary<string, IconLibrary.OpenState>();
            if (m_DockPanel != null)
            {
                foreach (ctlPuttyPanel panel in m_DockPanel.Contents.OfType<ctlPuttyPanel>())
                {
                    if (panel.IsDisposed || panel.Session == null || panel.Session.SessionId == null)
                        continue;
                    IconLibrary.OpenState state = panel.IsDisconnected ? IconLibrary.OpenState.Disconnected : IconLibrary.OpenState.Connected;
                    IconLibrary.OpenState existing;
                    // one connected tab is enough for the session to show as connected
                    if (!states.TryGetValue(panel.Session.SessionId, out existing) || state == IconLibrary.OpenState.Connected)
                    {
                        states[panel.Session.SessionId] = state;
                    }
                }
            }
            this.openStates = states;
            if (this.treeView1.ImageList == null)
                return;
            foreach (TreeNode root in this.treeView1.Nodes)
            {
                this.ApplyOpenStates(root);
            }
        }

        /// <summary>Give the session nodes and entries under a node the icons for their current open state</summary>
        /// <param name="node">A root or folder</param>
        void ApplyOpenStates(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
            {
                SessionData session = child.Tag as SessionData ?? (child.Tag as SessionShortcut)?.Session;
                if (session != null)
                {
                    string key = SessionImageKey(session);
                    if (child.ImageKey != key)
                    {
                        child.ImageKey = key;
                        child.SelectedImageKey = key;
                    }
                }
                else
                {
                    this.ApplyOpenStates(child);
                }
            }
        }

        #endregion

        #region Toolbar

        /// <summary>
        /// Build the toolbar under the search box, like mRemoteNG's: new session, new folder, expand all, collapse all
        /// and sort. It's docked between the search box and the tree.
        /// </summary>
        void BuildToolbar()
        {
            this.toolStripTree = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                CanOverflow = false,
                Padding = new Padding(2, 1, 2, 1)
            };
            this.sortToolStripButton = NewToolbarButton("", (s, e) => this.ToggleSortOrder());
            this.toolStripTree.Items.AddRange(new ToolStripItem[]
            {
                NewToolbarButton("New session (in the selected folder)", (s, e) => this.NewSessionFromToolbar()),
                NewToolbarButton("New folder (in the selected folder)", (s, e) => this.NewFolderFromToolbar()),
                new ToolStripSeparator(),
                NewToolbarButton("Expand all", (s, e) => this.ExpandFully(null)),
                NewToolbarButton("Collapse all", (s, e) => this.CollapseAll()),
                new ToolStripSeparator(),
                this.sortToolStripButton
            });
            this.Controls.Add(this.toolStripTree);
            // dock order: the search box at the very top, then this toolbar, then the tree filling the rest
            this.Controls.SetChildIndex(this.toolStripTree, this.Controls.GetChildIndex(this.panelSearch));
        }

        /// <summary>A toolbar button showing only its icon, set later by <see cref="ApplyToolbarIcons"/></summary>
        /// <param name="toolTip">What it does</param>
        /// <param name="click">What to do when it's clicked</param>
        /// <returns>The button</returns>
        ToolStripButton NewToolbarButton(string toolTip, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                ToolTipText = toolTip
            };
            button.Click += click;
            return button;
        }

        /// <summary>Draw the toolbar's icons at the icon size chosen in Options, as the main toolbars are</summary>
        void ApplyToolbarIcons()
        {
            int size = IconLibrary.ToPixels(IconLibrary.LogicalSize);
            this.toolStripTree.ImageScalingSize = new Size(size, size);
            string[] keys = { "newsession", "newfolder", null, "expandall", "collapseall", null, this.sortDescending ? "sortza" : "sortaz" };
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] != null)
                {
                    Image old = this.toolStripTree.Items[i].Image;
                    this.toolStripTree.Items[i].Image = IconLibrary.RenderDrawn(keys[i], size);
                    old?.Dispose();
                }
            }
            this.sortToolStripButton.ToolTipText = this.sortDescending
                ? "Sorted Z to A: click for A to Z"
                : "Sorted A to Z: click for Z to A (until SuperPuTTY restarts)";
        }

        /// <summary>The folder the toolbar's New buttons add to: the selected folder, or the selected session's folder</summary>
        /// <returns>A folder or root; Saved sessions when Favorites or Recent is selected</returns>
        TreeNode SelectedFolder()
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node == null || IsQuickNode(node) || DatabaseLocked(node))
                return this.DefaultRoot;
            return IsSessionNode(node) ? node.Parent : node;
        }

        /// <summary>Create a session in the selected folder, as its menu's "New session..." does</summary>
        void NewSessionFromToolbar()
        {
            this.treeView1.SelectedNode = this.SelectedFolder();
            this.CreateOrEditSession(0, null);
        }

        /// <summary>Create a folder in the selected folder, as its menu's "New folder" does</summary>
        void NewFolderFromToolbar()
        {
            this.treeView1.SelectedNode = this.SelectedFolder();
            this.newFolderToolStripMenuItem_Click(this, EventArgs.Empty);
        }

        /// <summary>
        /// Collapse everything, Favorites and Recent included, leaving Saved sessions and Private sessions open so
        /// their top-level folders show
        /// </summary>
        void CollapseAll()
        {
            this.treeView1.CollapseAll();
            this.nodeRoot.Expand();
            this.nodePrivate.Expand();
        }

        /// <summary>Switch between A to Z and Z to A; folders stay first if Options says so</summary>
        void ToggleSortOrder()
        {
            this.sortDescending = !this.sortDescending;
            TreeNode selected = this.treeView1.SelectedNode;
            this.ResortNodes();
            this.treeView1.SelectedNode = selected;
            this.ApplyToolbarIcons();
        }

        #endregion

        #region Favorites, Recent and Private sessions

        /// <summary>
        /// Build the menus and menu items for Favorites, Recent and moving between Saved and Private sessions. They're
        /// made here rather than in the designer file, beside the code that uses them.
        /// </summary>
        void BuildShortcutMenus()
        {
            this.favoriteToolStripMenuItem = new ToolStripMenuItem("Add to Favorites", null, (s, e) => this.ToggleFavorite(this.SelectedSession));
            this.contextMenuStripAddTreeItem.Items.Insert(this.contextMenuStripAddTreeItem.Items.IndexOf(this.toolStripSeparator1), this.favoriteToolStripMenuItem);
            // Move to and Copy to: submenus of the other roots (Saved, Private, Database sessions), filled as they open;
            // a placeholder item so the submenu arrow shows before then
            this.moveSessionToolStripMenuItem = this.TransferMenu("Move to", true);
            this.contextMenuStripAddTreeItem.Items.Insert(this.contextMenuStripAddTreeItem.Items.IndexOf(this.deleteToolStripMenuItem) + 1, this.moveSessionToolStripMenuItem);
            this.contextMenuStripAddTreeItem.Items.Insert(this.contextMenuStripAddTreeItem.Items.IndexOf(this.moveSessionToolStripMenuItem) + 1, this.TransferMenu("Copy to", false));
            this.moveFolderToolStripMenuItem = this.TransferMenu("Move to", true);
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.renameToolStripMenuItem) + 1, this.moveFolderToolStripMenuItem);
            this.copyFolderToolStripMenuItem = this.TransferMenu("Copy to", false);
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.moveFolderToolStripMenuItem) + 1, this.copyFolderToolStripMenuItem);
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.connectAllToolStripMenuItem) + 1,
                new ToolStripMenuItem("Export to CSV...", null, (s, e) => this.ExportFolderToCsv(this.treeView1.SelectedNode)));
            // environment colours; a placeholder item so the submenu arrow shows before it's filled
            this.colorFolderToolStripMenuItem = new ToolStripMenuItem("Colour");
            this.colorFolderToolStripMenuItem.DropDownItems.Add("-");
            this.colorFolderToolStripMenuItem.DropDownOpening += (s, e) => this.FillFolderColorMenu(this.treeView1.SelectedNode);
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.renameToolStripMenuItem) + 1, this.colorFolderToolStripMenuItem);
            this.editSessionsToolStripMenuItem = new ToolStripMenuItem("Edit sessions...", null, (s, e) => this.BulkEditFolder(this.treeView1.SelectedNode));
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.colorFolderToolStripMenuItem) + 1, this.editSessionsToolStripMenuItem);
            this.folderSettingsToolStripMenuItem = new ToolStripMenuItem("Folder settings...", null, (s, e) => this.EditFolderSettings(this.treeView1.SelectedNode));
            this.contextMenuStripFolder.Items.Insert(this.contextMenuStripFolder.Items.IndexOf(this.colorFolderToolStripMenuItem), this.folderSettingsToolStripMenuItem);
            this.colorSessionToolStripMenuItem = new ToolStripMenuItem("Colour");
            this.colorSessionToolStripMenuItem.DropDownItems.Add("-");
            this.colorSessionToolStripMenuItem.DropDownOpening += (s, e) => this.FillSessionColorMenu(this.SelectedSession);
            this.contextMenuStripAddTreeItem.Items.Insert(this.contextMenuStripAddTreeItem.Items.IndexOf(this.favoriteToolStripMenuItem) + 1, this.colorSessionToolStripMenuItem);

            ToolStripMenuItem connect = new ToolStripMenuItem("Connect", null, (s, e) => this.treeView1_NodeMouseDoubleClick(null, null));
            connect.Font = this.connectToolStripMenuItem.Font;
            this.shortcutFavoriteToolStripMenuItem = new ToolStripMenuItem("Add to Favorites", null, (s, e) => this.ToggleFavorite(this.SelectedSession));
            this.shortcutRemoveToolStripMenuItem = new ToolStripMenuItem("Remove from Recent", null, (s, e) => this.RemoveShortcut(this.treeView1.SelectedNode));
            this.contextMenuStripShortcut = new ContextMenuStrip { ShowImageMargin = false };
            this.contextMenuStripShortcut.Items.AddRange(new ToolStripItem[]
            {
                connect,
                new ToolStripMenuItem("Show in Sessions tree", null, (s, e) => this.ShowInTree(this.SelectedSession)),
                new ToolStripSeparator(),
                this.shortcutFavoriteToolStripMenuItem,
                this.shortcutRemoveToolStripMenuItem
            });
            this.contextMenuStripShortcut.Opening += this.contextMenuStripShortcut_Opening;

            this.contextMenuStripRecentGroup = new ContextMenuStrip { ShowImageMargin = false };
            this.contextMenuStripRecentGroup.Items.Add("Clear Recent", null, (s, e) => SessionShortcuts.ClearRecent());

            this.contextMenuStripDatabaseRoot = new ContextMenuStrip { ShowImageMargin = false };
            ToolStripMenuItem databaseNewSession = new ToolStripMenuItem("New session...", null, (s, e) => this.CreateOrEditSession(0, null));
            ToolStripMenuItem databaseNewFolder = new ToolStripMenuItem("New folder", null, (s, e) => this.newFolderToolStripMenuItem_Click(this, EventArgs.Empty));
            ToolStripMenuItem databasePending = new ToolStripMenuItem("Pending changes...", null, (s, e) => this.ShowPendingChanges());
            this.contextMenuStripDatabaseRoot.Opening += (s, e) =>
            {
                databaseNewSession.Enabled = DatabaseSessions.Writable;
                databaseNewFolder.Enabled = DatabaseSessions.Writable;
                int waiting = DatabaseSessions.PendingCount + DatabaseSessions.FailedCount;
                databasePending.Visible = waiting > 0;
                databasePending.Text = string.Format("Pending changes ({0})...", waiting);
            };
            this.contextMenuStripDatabaseRoot.Items.AddRange(new ToolStripItem[]
            {
                databaseNewSession,
                databaseNewFolder,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Refresh", null, (s, e) => DatabaseSessions.BeginSync()),
                databasePending,
                new ToolStripMenuItem("Database settings...", null, (s, e) => this.ShowDatabaseSettings()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Expand all", null, (s, e) => this.ExpandFully(this.nodeDatabase)),
                new ToolStripMenuItem("Collapse all", null, (s, e) => { this.nodeDatabase?.Collapse(); this.nodeDatabase?.Expand(); })
            });
        }

        /// <summary>Set the Favorites and Recent entry menu for the entry right-clicked</summary>
        /// <param name="sender">The menu</param>
        /// <param name="e">Not used</param>
        void contextMenuStripShortcut_Opening(object sender, CancelEventArgs e)
        {
            SessionShortcut shortcut = this.treeView1.SelectedNode?.Tag as SessionShortcut;
            if (shortcut == null)
            {
                e.Cancel = true;
                return;
            }
            bool favorite = SessionShortcuts.IsFavorite(shortcut.Session.SessionId);
            // in Favorites, "Remove from Favorites" is the remove item, so the toggle would only repeat it
            this.shortcutFavoriteToolStripMenuItem.Visible = shortcut.Group == RootKind.Recent;
            this.shortcutFavoriteToolStripMenuItem.Text = favorite ? "Remove from Favorites" : "Add to Favorites";
            this.shortcutRemoveToolStripMenuItem.Text = shortcut.Group == RootKind.Favorites ? "Remove from Favorites" : "Remove from Recent";
        }

        /// <summary>Add a session to Favorites, or remove it if it's there</summary>
        /// <param name="session">The session; nothing happens if null</param>
        void ToggleFavorite(SessionData session)
        {
            if (session == null)
                return;
            if (SessionShortcuts.IsFavorite(session.SessionId))
                SessionShortcuts.RemoveFavorite(session.SessionId);
            else
                SessionShortcuts.AddFavorite(session.SessionId);
        }

        /// <summary>Remove a Favorites or Recent entry from its group; the session itself stays</summary>
        /// <param name="node">The entry</param>
        void RemoveShortcut(TreeNode node)
        {
            SessionShortcut shortcut = node?.Tag as SessionShortcut;
            if (shortcut == null)
                return;
            if (shortcut.Group == RootKind.Favorites)
                SessionShortcuts.RemoveFavorite(shortcut.Session.SessionId);
            else
                SessionShortcuts.RemoveRecent(shortcut.Session.SessionId);
        }

        /// <summary>Select a session's own node, in Saved or Private sessions, opening its folders</summary>
        /// <param name="session">The session</param>
        void ShowInTree(SessionData session)
        {
            TreeNode node = session == null ? null : FindSessionNode(session.SessionId);
            if (node != null)
            {
                this.treeView1.SelectedNode = node;
                node.EnsureVisible();
                this.treeView1.Focus();
            }
        }

        /// <summary>Redraw Favorites and Recent when their lists change</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        void SessionShortcuts_Changed(object sender, EventArgs e)
        {
            ScheduleShortcutRebuild();
        }

        /// <summary>
        /// Redraw Favorites and Recent once the current change is done. Several changes in a row (a reload adds
        /// sessions one at a time) make one redraw.
        /// </summary>
        void ScheduleShortcutRebuild()
        {
            if (!this.IsHandleCreated)
            {
                // not shown yet: LoadSessions draws them when it is
                return;
            }
            if (!this.shortcutRebuildPending)
            {
                this.shortcutRebuildPending = true;
                this.BeginInvoke((MethodInvoker)delegate
                {
                    this.shortcutRebuildPending = false;
                    this.BuildShortcutGroups();
                });
            }
        }

        /// <summary>
        /// Fill the Favorites and Recent groups from <see cref="SessionShortcuts"/>, showing each only when it has a
        /// session to show (and Recent only when Options says so). The search filter applies to them too.
        /// </summary>
        void BuildShortcutGroups()
        {
            if (this.nodeRoot == null)
                return;
            SessionShortcut selected = this.treeView1.SelectedNode?.Tag as SessionShortcut;
            this.nodeFavorites = BuildShortcutGroup(this.nodeFavorites, RootKind.Favorites, "Favorites", ImageKeyFavorites,
                SessionShortcuts.Favorites, true);
            this.nodeRecent = BuildShortcutGroup(this.nodeRecent, RootKind.Recent, "Recent", ImageKeyRecent,
                SessionShortcuts.Recent, SuperPuTTY.Settings.SessionsShowRecent);

            // rebuilding drops the selection; keep it on the same entry (e.g. one just opened from Recent)
            if (selected != null)
            {
                TreeNode group = selected.Group == RootKind.Favorites ? this.nodeFavorites : this.nodeRecent;
                TreeNode same = group?.Nodes.Cast<TreeNode>().FirstOrDefault(n => ((SessionShortcut)n.Tag).Session == selected.Session);
                if (same != null)
                {
                    this.treeView1.SelectedNode = same;
                }
            }
        }

        /// <summary>Fill one of the Favorites and Recent groups, adding or removing its node as needed</summary>
        /// <param name="group">The group's node, or null if it isn't shown</param>
        /// <param name="kind">Which group</param>
        /// <param name="text">Its title</param>
        /// <param name="imageKey">Its icon</param>
        /// <param name="ids">The session IDs it lists, in order; IDs of sessions that no longer exist are skipped</param>
        /// <param name="show">false to hide the group</param>
        /// <returns>The group's node, or null if it's hidden</returns>
        TreeNode BuildShortcutGroup(TreeNode group, RootKind kind, string text, string imageKey, IList<string> ids, bool show)
        {
            List<SessionData> sessions = !show ? new List<SessionData>() : ids
                .Select(SuperPuTTY.GetSessionById)
                .Where(session => session != null && (this.filter == null || this.filter(session)))
                .ToList();
            if (sessions.Count == 0)
            {
                group?.Remove();
                return null;
            }

            bool expand = group == null || group.IsExpanded;
            if (group == null)
            {
                string key = IsValidImage(imageKey) ? imageKey : ImageKeyFolder;
                group = this.treeView1.Nodes.Add(kind.ToString(), text, key, key);
                group.Tag = kind;
                group.ContextMenuStrip = kind == RootKind.Recent ? this.contextMenuStripRecentGroup : null;
            }
            group.Nodes.Clear();
            for (int i = 0; i < sessions.Count; i++)
            {
                SessionData session = sessions[i];
                string key = SessionImageKey(session);
                TreeNode node = group.Nodes.Add(session.SessionId, session.SessionName, key, key);
                node.Tag = new SessionShortcut { Session = session, Group = kind, Order = i };
                node.ContextMenuStrip = this.contextMenuStripShortcut;
                string folder = SessionData.GetSessionParentId(TreeId(session.SessionId));
                node.ToolTipText = (session.IsDatabase ? "Database sessions" : session.IsPrivate ? "Private sessions" : "Saved sessions") + (folder == null ? "" : "/" + folder)
                    + Environment.NewLine + session.ToString();
            }
            if (expand)
            {
                group.Expand();
            }
            return group;
        }

        /// <summary>
        /// Move a session or folder between Saved sessions and Private sessions, into the same folder path, creating
        /// folders as needed; then save both files
        /// </summary>
        /// <param name="node">The session or folder</param>
        void MoveToOtherRoot(TreeNode node)
        {
            if (node == null || node.Parent == null || IsQuickNode(node) || IsDatabaseNode(node))
                return;
            TreeNode otherRoot = IsPrivateNode(node) ? this.nodeRoot : this.nodePrivate;
            TreeNode target = FindOrCreateParentNode(otherRoot, SessionData.CombineSessionIds(FolderPath(node.Parent), node.Text));
            if (target.Nodes.ContainsKey(node.Text))
            {
                Messenger.MessageBox("\"" + node.Text + "\" is already in " + otherRoot.Text + ". Rename one of them first.",
                    "Move", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TreeNode moved = this.MoveNode(node, target);
            if (moved != null)
            {
                SuperPuTTY.SaveSessions();
                this.treeView1.SelectedNode = moved;
                moved.EnsureVisible();
                SuperPuTTY.ReportStatus("Moved {0} to {1}", moved.Text, otherRoot.Text);
            }
        }

        /// <summary>
        /// Move a session or folder node into a folder, in either root: its sessions get IDs for their new place,
        /// and become private or shared to match the root. Doesn't save.
        /// </summary>
        /// <param name="nodePayload">The session or folder to move</param>
        /// <param name="target">The folder or root to move it into</param>
        /// <returns>The node in its new place, or null if a session there would have the same ID as one elsewhere</returns>
        TreeNode MoveNode(TreeNode nodePayload, TreeNode target)
        {
            // the move walks the nodes (copying them, giving sessions new IDs), so every level has to be there
            this.FillSubtree(nodePayload);
            this.EnsureFolderFilled(target);
            string conflict = FindMoveConflict(nodePayload, target);
            if (conflict != null)
            {
                Messenger.MessageBox("There is already a session " + conflict + ". Rename one of them first.",
                    "Move", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            TreeNode nodeNew = (TreeNode)nodePayload.Clone();
            // If node was expanded before, ensure new node is also expanded when moved
            if (nodePayload.IsExpanded)
            {
                nodeNew.Expand();
            }
            FolderSettings.Store? fromStore = IsSessionNode(nodePayload) ? null : StoreOf(nodePayload);
            string fromPath = fromStore == null ? null : NodePath(nodePayload);
            nodePayload.Remove();
            target.Nodes.Add(nodeNew);
            if (fromStore != null)
            {
                // a folder's colour (and its subfolders') goes with it, into the other file if it changed root
                FolderSettings.Move(fromStore.Value, fromPath, StoreOf(nodeNew).Value, NodePath(nodeNew));
            }

            List<SessionData> sessions = new List<SessionData>();
            if (IsSessionNode(nodeNew))
                sessions.Add((SessionData)nodeNew.Tag);
            else
                GetAllSessions(nodeNew, sessions);
            bool isPrivate = IsPrivateNode(target);
            foreach (SessionData session in sessions)
            {
                session.IsPrivate = isPrivate;
            }

            if (IsSessionNode(nodeNew))
            {
                UpdateSessionId(nodeNew, (SessionData)nodeNew.Tag);
            }
            else
            {
                // every level below, not just the folder's own sessions, so sessions in subfolders follow too
                UpdateSessionId(nodeNew);
            }
            target.Expand();
            return nodeNew;
        }

        /// <summary>The ID a moved session would get that another session already has, if any</summary>
        /// <param name="nodePayload">The session or folder being moved</param>
        /// <param name="target">Where it's going</param>
        /// <returns>The clashing ID, or null if there's none</returns>
        string FindMoveConflict(TreeNode nodePayload, TreeNode target)
        {
            List<SessionData> sessions = new List<SessionData>();
            if (IsSessionNode(nodePayload))
                sessions.Add((SessionData)nodePayload.Tag);
            else
                GetAllSessions(nodePayload, sessions);
            string oldPrefix = FolderPath(nodePayload.Parent);
            string newPrefix = FolderPath(target);
            foreach (SessionData session in sessions)
            {
                string relative = oldPrefix == null ? session.SessionId : session.SessionId.Substring(oldPrefix.Length + 1);
                string newId = SessionData.CombineSessionIds(newPrefix, relative);
                SessionData existing = SuperPuTTY.GetSessionById(newId);
                if (existing != null && !sessions.Contains(existing))
                {
                    return newId;
                }
            }
            return null;
        }

        #endregion

        #region Node helpers

        TreeNode AddSessionNode(TreeNode parentNode, SessionData session, bool isInitializing)
        {
            TreeNode addedNode = null;
            if (parentNode.Nodes.ContainsKey(session.SessionName))
            {
                SuperPuTTY.ReportStatus("Node with the same name exists.  New node ({0}) NOT added", session.SessionName);
            }
            else
            {
                addedNode = parentNode.Nodes.Add(session.SessionName, session.SessionName, ImageKeySession, ImageKeySession);
                addedNode.Tag = session;
                addedNode.ContextMenuStrip = this.contextMenuStripAddTreeItem;
                addedNode.ToolTipText = session.ToString();
                if (session.DatabasePending)
                {
                    // not in the table yet: queued while offline, or refused
                    addedNode.NodeFont = new Font(this.treeView1.Font, FontStyle.Italic);
                    addedNode.ToolTipText = session.DatabasePendingNote + Environment.NewLine + addedNode.ToolTipText;
                }
                this.ColorNode(addedNode);
                if (!string.IsNullOrEmpty(session.DatabaseUpdatedBy))
                {
                    addedNode.ToolTipText = "Last changed by " + session.DatabaseUpdatedBy + Environment.NewLine + addedNode.ToolTipText;
                }
                if (!string.IsNullOrEmpty(session.DatabaseManagedBy))
                {
                    addedNode.ToolTipText = "Managed by " + session.DatabaseManagedBy + Environment.NewLine + addedNode.ToolTipText;
                }
                if (session.DatabaseLocked)
                {
                    addedNode.ToolTipText = "Locked in the database: opens, but isn't edited here" + Environment.NewLine + addedNode.ToolTipText;
                }
                else if (!string.IsNullOrEmpty(session.DatabaseLockedFields))
                {
                    addedNode.ToolTipText = "Locked in the database: " + session.DatabaseLockedFields.Replace(",", ", ") + Environment.NewLine + addedNode.ToolTipText;
                }

                // Override with custom icon if valid, and a dot if the session has a tab open
                string imageKey = SessionImageKey(session);
                if (imageKey != ImageKeySession)
                {
                    addedNode.ImageKey = imageKey;
                    addedNode.SelectedImageKey = imageKey;
                }
            }

            // once per session however often the tree is rebuilt (each search does): adding again made the handler lists grow
            session.OnPropertyChanged -= SessionPropertyChanged;
            session.OnPropertyChanged += SessionPropertyChanged;
            session.OnPropertyChanging -= SessionPropertyChanging;
            session.OnPropertyChanging += SessionPropertyChanging;

            return addedNode;
        }

        TreeNode AddFolderNode(TreeNode parentNode, String nodeName)
        {
            TreeNode nodeNew = null;
            if (parentNode.Nodes.ContainsKey(nodeName))
            {
                SuperPuTTY.ReportStatus("Node with the same name exists.  New node ({0}) NOT added", nodeName);
            }
            else
            {
                SuperPuTTY.ReportStatus("Adding new folder, {1}.  parent={0}", parentNode.Text, nodeName);
                nodeNew = parentNode.Nodes.Add(nodeName, nodeName, ImageKeyFolder, ImageKeyFolder);
                nodeNew.ContextMenuStrip = this.contextMenuStripFolder;
                this.ColorNode(nodeNew);
            }
            return nodeNew;
        }

        bool IsSessionNode(TreeNode node)
        {
            return node != null && node.Tag is SessionData;
        }

        /// <summary>Whether a node is a folder or one of the Saved and Private sessions roots</summary>
        /// <param name="node">The node</param>
        /// <returns>false for sessions, the Favorites and Recent groups and their entries</returns>
        bool IsFolderNode(TreeNode node)
        {
            return !IsSessionNode(node) && !IsQuickNode(node);
        }

        /// <summary>Whether a node is an entry in Favorites or Recent</summary>
        /// <param name="node">The node</param>
        /// <returns>true for shortcut entries</returns>
        bool IsShortcutNode(TreeNode node)
        {
            return node != null && node.Tag is SessionShortcut;
        }

        /// <summary>Whether a node is the Favorites or Recent group, or an entry in one</summary>
        /// <param name="node">The node</param>
        /// <returns>true for those groups and their entries</returns>
        bool IsQuickNode(TreeNode node)
        {
            if (node == null)
                return false;
            TreeNode root = RootOf(node);
            return root == this.nodeFavorites || root == this.nodeRecent || IsShortcutNode(node);
        }

        /// <summary>The top-level node a node is under</summary>
        /// <param name="node">The node</param>
        /// <returns>Its root, or the node itself if it's a root</returns>
        TreeNode RootOf(TreeNode node)
        {
            while (node != null && node.Parent != null)
            {
                node = node.Parent;
            }
            return node;
        }

        /// <summary>Whether a node is in Private sessions (or is that root)</summary>
        /// <param name="node">The node</param>
        /// <returns>true under Private sessions</returns>
        bool IsPrivateNode(TreeNode node)
        {
            return node != null && RootOf(node) == this.nodePrivate;
        }

        /// <summary>Whether a node is in Database sessions (or is that root)</summary>
        /// <param name="node">The node</param>
        /// <returns>true under Database sessions</returns>
        bool IsDatabaseNode(TreeNode node)
        {
            return node != null && this.nodeDatabase != null && RootOf(node) == this.nodeDatabase;
        }

        /// <summary>
        /// Whether a node is in Database sessions while the table can't be written (read-only, no permission, offline,
        /// or not read yet): its sessions and folders can then be opened but not added, changed or removed
        /// </summary>
        /// <param name="node">The node</param>
        /// <returns>true if it's a database node that can't be changed now</returns>
        bool DatabaseLocked(TreeNode node)
        {
            return IsDatabaseNode(node) && !DatabaseSessions.Writable;
        }

        /// <summary>A folder's path in the table's folder column</summary>
        /// <param name="folder">A folder under Database sessions, or that root</param>
        /// <returns>e.g. "Lab/Core", or "" for the root</returns>
        string DatabaseFolder(TreeNode folder)
        {
            string path = TreeId(FolderPath(folder));
            return path == null || path == DatabaseSessions.IdPrefix ? string.Empty : path;
        }

        /// <summary>Null for an empty string</summary>
        /// <param name="text">The text</param>
        /// <returns>The text, or null</returns>
        static string NullIfEmpty(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>A session ID as a path under its root: database sessions' IDs lose their "@db/" prefix</summary>
        /// <param name="sessionId">The ID, e.g. "@db/Lab/sw1"</param>
        /// <returns>e.g. "Lab/sw1"</returns>
        static string TreeId(string sessionId)
        {
            string prefix = DatabaseSessions.IdPrefix + SessionIdDelim;
            return sessionId != null && sessionId.StartsWith(prefix, StringComparison.Ordinal) ? sessionId.Substring(prefix.Length) : sessionId;
        }

        /// <summary>A folder's path as used in session IDs, e.g. "Lab/Core"</summary>
        /// <param name="folder">The folder, or a root</param>
        /// <returns>The path, or null for a root</returns>
        string FolderPath(TreeNode folder)
        {
            List<string> names = new List<string>();
            for (TreeNode node = folder; node != null && node.Parent != null; node = node.Parent)
            {
                names.Add(node.Text);
            }
            names.Reverse();
            if (folder != null && RootOf(folder) == this.nodeDatabase && this.nodeDatabase != null)
            {
                names.Insert(0, DatabaseSessions.IdPrefix);
            }
            return names.Count == 0 ? null : string.Join(SessionIdDelim, names);
        }

        private void UpdateSessionId(TreeNode parentNode)
        {
            foreach (TreeNode node in parentNode.Nodes)
            {
                if (IsSessionNode(node))
                {
                    UpdateSessionId(node, (SessionData)node.Tag);
                }
                else
                {
                    UpdateSessionId(node);
                }
            }
        }
        private void UpdateSessionId(TreeNode addedNode, SessionData session)
        {
            // set session id as node path
            List<string> parentNodeNames = new List<string>();
            for (TreeNode node = addedNode; node.Parent != null; node = node.Parent)
            {
                parentNodeNames.Add(node.Text);
            }
            parentNodeNames.Reverse();
            String sessionId = String.Join(SessionIdDelim, parentNodeNames.ToArray());
            //Log.InfoFormat("sessionId={0}", sessionId);
            //session.SessionId = sessionId;
            if (session != null) session.SessionId = sessionId;
            //SuperPuTTY.SaveSessions();
            //session.SaveToRegistry();
        }

        /// <summary>The folder a session ID belongs in, under a root, creating folders that don't exist yet</summary>
        /// <param name="root">Saved sessions or Private sessions</param>
        /// <param name="sessionId">The session ID, e.g. "Lab/Core/sw1"</param>
        /// <returns>The folder, e.g. Lab/Core; the root for an ID without a folder</returns>
        TreeNode FindOrCreateParentNode(TreeNode root, string sessionId)
        {
            Log.DebugFormat("Finding Parent Node for sessionId ({0})", sessionId);
            TreeNode nodeParent = root;

            string[] parts = sessionId.Split(SessionIdDelim.ToCharArray());
            if (parts.Length > 1)
            {
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string part = parts[i];
                    // folders are filled on demand: fill this one before looking in it
                    this.EnsureFolderFilled(nodeParent);
                    TreeNode node = nodeParent.Nodes[part];
                    if (node == null)
                    {
                        nodeParent = this.AddFolderNode(nodeParent, part);
                    }
                    else if (IsFolderNode(node))
                    {
                        nodeParent = node;
                    }
                }
            }

            Log.DebugFormat("Returning node ({0})", nodeParent.Text);
            return nodeParent;
        }

        /// <summary>A session's node, in Saved sessions or Private sessions</summary>
        /// <param name="SessionId">The session's ID</param>
        /// <returns>The node, or null if it isn't shown</returns>
        TreeNode FindSessionNode(String SessionId)
        {
            if (SessionId != null && SessionId.StartsWith(DatabaseSessions.IdPrefix + SessionIdDelim, StringComparison.Ordinal))
            {
                return FindSessionNode(this.nodeDatabase, TreeId(SessionId));
            }
            return FindSessionNode(this.nodeRoot, SessionId) ?? FindSessionNode(this.nodePrivate, SessionId);
        }

        /// <summary>A session's node under one root</summary>
        /// <param name="root">Saved sessions or Private sessions</param>
        /// <param name="SessionId">The session's ID</param>
        /// <returns>The node, or null if it isn't there</returns>
        TreeNode FindSessionNode(TreeNode root, String SessionId)
        {
            Log.DebugFormat("Finding Node for sessionId ({0})", SessionId);
            TreeNode CurrentNode = root;
            TreeNode NodeToReturn = null;
            if (root == null || SessionId == null)
            {
                return null;
            }

            string[] Parts = SessionId.Split(SessionIdDelim.ToCharArray());
            if (Parts.Length > 0)
            {
                for (int i = 0; i < Parts.Length; i++)
                {
                    string Part = Parts[i];
                    // database folders are filled as they're needed
                    this.EnsureFolderFilled(CurrentNode);
                    CurrentNode = CurrentNode.Nodes[Part];
                    if (CurrentNode == null)
                    {
                        return null;
                    }
                    else if (i == Parts.Length - 1 && !IsFolderNode(CurrentNode))
                    {
                        NodeToReturn = CurrentNode;
                    }
                }
            }

            Log.DebugFormat("Returning node ({0})", NodeToReturn != null ? NodeToReturn.Text : "null");
            return NodeToReturn;
        }

        /// <summary>Sort the tree's nodes (the TreeView's node sorter); see <see cref="CompareNodes"/></summary>
        /// <param name="x">One node</param>
        /// <param name="y">The other</param>
        /// <returns>Negative if x goes first, positive if y does, 0 if equal</returns>
        public int Compare(object x, object y)
        {
            TreeNode tx = x as TreeNode;
            TreeNode ty = y as TreeNode;
            // the top level keeps a fixed order: Favorites, Recent, Saved sessions, Private sessions
            if (tx.Tag is RootKind && ty.Tag is RootKind)
            {
                return ((RootKind)tx.Tag).CompareTo((RootKind)ty.Tag);
            }
            // Recent is most recent first, not by name
            SessionShortcut sx = tx.Tag as SessionShortcut, sy = ty.Tag as SessionShortcut;
            if (sx != null && sy != null && sx.Group == RootKind.Recent)
            {
                return sx.Order.CompareTo(sy.Order);
            }
            bool foldersFirst = SuperPuTTY.Settings.SessiontreeShowFoldersFirst;
            int order = CompareNodes(tx.Text, IsFolderNode(tx), ty.Text, IsFolderNode(ty), foldersFirst);
            // Z to A reverses names only; folders stay before sessions
            bool byKind = foldersFirst && IsFolderNode(tx) != IsFolderNode(ty);
            return this.sortDescending && !byKind ? -order : order;
        }

        /// <summary>
        /// Order two tree nodes: by name, with folders before sessions when "Show folders first" is on. It must give
        /// the opposite answer when the nodes are swapped; upstream only put a folder first when it came first in the
        /// comparison, so the order depended on where nodes happened to be, and every re-sort (e.g. after saving
        /// Options) could move folders around, as if the option had flipped.
        /// </summary>
        /// <param name="xText">The first node's name</param>
        /// <param name="xIsFolder">Whether the first node is a folder</param>
        /// <param name="yText">The second node's name</param>
        /// <param name="yIsFolder">Whether the second node is a folder</param>
        /// <param name="foldersFirst">Whether folders go before sessions</param>
        /// <returns>Negative if the first goes first, positive if the second does, 0 if equal</returns>
        public static int CompareNodes(string xText, bool xIsFolder, string yText, bool yIsFolder, bool foldersFirst)
        {
            if (foldersFirst && xIsFolder != yIsFolder)
            {
                return xIsFolder ? -1 : 1;
            }
            return string.Compare(xText, yText);
        }

        void ResortNodes()
        {
            this.treeView1.TreeViewNodeSorter = null;
            this.treeView1.TreeViewNodeSorter = this;
        }


        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
              TreeNode node = this.treeView1.SelectedNode;
              if (node != null)
              {
                  this.ExpandFully(node);
              }
        }

        private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                node.Collapse();
                if (node == this.nodeRoot)
                {
                    nodeRoot.Expand();
                }
            }
        }

        void GetAllSessions(TreeNode nodeFolder, List<SessionData> sessions)
        {
            if (nodeFolder != null && IsDatabaseNode(nodeFolder) && !IsSessionNode(nodeFolder))
            {
                // database folders may not be filled in yet: take their sessions from the index
                string path = DatabaseFolder(nodeFolder);
                foreach (KeyValuePair<string, List<SessionData>> folder in this.IndexFor(RootKind.Database).All)
                {
                    if (path.Length == 0 || folder.Key == path || folder.Key.StartsWith(path + SessionIdDelim, StringComparison.Ordinal))
                        sessions.AddRange(folder.Value);
                }
                return;
            }
            // other folders are walked node by node (moves rely on that), so fill them first
            this.FillSubtree(nodeFolder);
            if (nodeFolder != null)
            {
                foreach (TreeNode child in nodeFolder.Nodes)
                {
                    if (IsSessionNode(child))
                    {
                        SessionData session = (SessionData) child.Tag;
                        sessions.Add(session);
                    }
                    else
                    {
                        GetAllSessions(child, sessions);
                    }
                }
            }
        }

        void GetAllNodes(TreeNode node, List<TreeNode> nodes)
        {
            if (node != null)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    if (child.Nodes.Count == 0)
                    {
                        nodes.Add(child);
                    }
                    else
                    {
                        GetAllNodes(child, nodes);
                    }
                }
            }
        }
        #endregion

        #region Drag Drop

        private void treeView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Get the tree
            TreeView tree = (TreeView)sender;

            // Get the node underneath the mouse.
            TreeNode node = e.Item as TreeNode;

            // Start the drag-and-drop operation with a cloned copy of the node.
            //if (node != null && IsSessionNode(node))
            // the top-level nodes and the Favorites and Recent entries stay where they are; in Database sessions a
            // folder can be dragged only when the table can be written (a session also onto Favorites)
            if (node != null && node.Parent != null && !IsQuickNode(node) && (!IsDatabaseNode(node) || IsSessionNode(node) || !DatabaseLocked(node)))
            {
                this.treeView1.DoDragDrop(node, DragDropEffects.Copy);
            }
        }

        private void treeView1_DragOver(object sender, DragEventArgs e)
        {
            // Get the tree.
            TreeView tree = (TreeView)sender;

            // Drag and drop denied by default.
            e.Effect = DragDropEffects.None;

            // Is it a valid format?
            TreeNode nodePayload = (TreeNode) e.Data.GetData(typeof(TreeNode));
            if (nodePayload != null)
            {
                // Get the screen point.
                Point pt = new Point(e.X, e.Y);

                // Convert to a point in the TreeView's coordinate system.
                pt = tree.PointToClient(pt);

                TreeNode node = tree.GetNodeAt(pt);
                // a session dropped on Favorites becomes a favourite
                if (node != null && RootOf(node) == this.nodeFavorites && IsSessionNode(nodePayload))
                {
                    e.Effect = DragDropEffects.Copy;
                }
                // within Database sessions, sessions and folders move to another folder there
                else if (node != null && IsDatabaseNode(node) && IsDatabaseNode(nodePayload))
                {
                    tree.SelectedNode = node;
                    if (CanMoveInDatabase(nodePayload, node))
                    {
                        e.Effect = DragDropEffects.Copy;
                    }
                }
                // into or out of Database sessions: copied (moving is on the menu, as it deletes from one side)
                else if (node != null && IsDatabaseNode(node) != IsDatabaseNode(nodePayload) && IsFolderNode(node) && !IsQuickNode(node)
                    && !(IsDatabaseNode(node) && DatabaseLocked(node)))
                {
                    tree.SelectedNode = node;
                    e.Effect = DragDropEffects.Copy;
                }
                // Is the mouse over a valid node?
                else if (node != null && node != nodePayload && nodePayload.Nodes.Find(node.Text, true).Length == 0)
                {
                    tree.SelectedNode = node;
                    // folder that is not the same parent and new node name is not already present
                    if (IsFolderNode(node) && node != nodePayload.Parent && !node.Nodes.ContainsKey(nodePayload.Text)
                        && !IsDatabaseNode(node) && !IsDatabaseNode(nodePayload))
                    {
                        e.Effect = DragDropEffects.Copy;
                    }
                }
            }
        }

        private void treeView1_DragDrop(object sender, DragEventArgs e)
        {
            // Get the tree.
            TreeView tree = (TreeView)sender;

            // Get the screen point.
            Point pt = new Point(e.X, e.Y);

            // Convert to a point in the TreeView's coordinate system.
            pt = tree.PointToClient(pt);

            // Get the node underneath the mouse.
            TreeNode node = tree.GetNodeAt(pt);

            TreeNode nodeDropped = (TreeNode)e.Data.GetData(typeof(TreeNode));
            if (node != null && RootOf(node) == this.nodeFavorites && IsSessionNode(nodeDropped))
            {
                SessionShortcuts.AddFavorite(((SessionData)nodeDropped.Tag).SessionId);
            }
            else if (node != null && IsDatabaseNode(node) && IsDatabaseNode(nodeDropped))
            {
                if (CanMoveInDatabase(nodeDropped, node))
                {
                    this.MoveInDatabase(nodeDropped, node);
                }
            }
            else if (node != null && IsDatabaseNode(node) != IsDatabaseNode(nodeDropped) && IsFolderNode(node) && !IsQuickNode(node))
            {
                this.Transfer(nodeDropped, KindOf(node), NodePath(node), false);
            }
            else if (node != null && IsFolderNode(node) && !IsDatabaseNode(node) && !IsDatabaseNode(nodeDropped))
            {
                Log.DebugFormat("Drag drop");

                // into another folder or root; dropping into Private sessions makes the sessions private
                if (this.MoveNode(nodeDropped, node) == null)
                {
                    return;
                }

                // auto save settings...use timer to prevent excessive saves while dragging and dropping nodes
                timerDelayedSave.Stop();
                timerDelayedSave.Start();
                //SuperPuTTY.SaveSessions();
            }
        }

        #endregion

        public void resetFoldersChildrenPaths(TreeNode nodePayload)
        {
            // Reset folders children nodes sessionId (path)
            foreach (TreeNode node in nodePayload.Nodes)
            {
                UpdateSessionId(node, (SessionData)node.Tag);
            }
        }

        private void timerDelayedSave_Tick(object sender, EventArgs e)
        {
            // stop timer
            timerDelayedSave.Stop();

            // do save
            SuperPuTTY.SaveSessions();
            SuperPuTTY.ReportStatus("Saved Sessions after Drag-Drop @ {0}", DateTime.Now);
        }

        #region Icon
        bool IsValidImage(string imageKey)
        {
            bool valid = false;
            if (!string.IsNullOrEmpty(imageKey))
            {
                // coloured icons ("switch#1E63B4") are drawn into the list on first use
                valid = IconLibrary.Ensure(this.treeView1.ImageList, imageKey);
                if (!valid)
                {
                    Log.WarnFormat("Missing icon, {0}", imageKey);
                }
            }
            return valid;
        }


        #endregion

        #region Search
        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    if (SuperPuTTY.Settings.FilterSessionsOnChange)
                    {
                        treeView1.Focus();
                    }
                    else
                    {
                        this.ApplySearch(this.txtSearch.Text);
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    this.ClearSearch();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            this.ApplySearch(this.txtSearch.Text);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            this.ClearSearch();
        }

        private void ClearSearch()
        {
            this.txtSearch.Text = "";
            this.ApplySearch("");
        }
        private void ApplySearch(string txt)
        {
            Log.InfoFormat("Applying Search: txt={0}.", txt);

            // define filter
            SearchFilter searchFilter = new SearchFilter(SuperPuTTY.Settings.SessionsSearchMode, txt, GetSearchFieldsSetting());
            this.filter = searchFilter.IsMatch;

            // reload
            this.LoadSessions();

            // if "clear" show init state otherwise expand all to show all matches
            if (string.IsNullOrEmpty(txt))
            {
                this.ExpandInitialTree();
            }
            else
            {
                this.ExpandFully(null);
            }
        }

        public enum SearchMode
        {
            CaseSensitive, CaseInSensitive, Regex
        }

        /// <summary>
        /// The session fields the search box looks in. Chosen with "Search in..." in Tools -> Options and stored
        /// in Settings.SessionsSearchFields as the flag names, e.g. "Name, Host" or "All". The password is
        /// never searched.
        /// </summary>
        [Flags]
        public enum SearchFields
        {
            /// <summary>Nothing; treated as <see cref="All"/></summary>
            None = 0,
            /// <summary>The session name</summary>
            Name = 1,
            /// <summary>The folder path the session is in</summary>
            Folder = 2,
            /// <summary>Host name or address</summary>
            Host = 4,
            /// <summary>Login username</summary>
            Username = 8,
            /// <summary>TCP port</summary>
            Port = 16,
            /// <summary>Connection type, e.g. SSH or RDP</summary>
            Protocol = 32,
            /// <summary>PuTTY saved-session profile</summary>
            PuttyProfile = 64,
            /// <summary>Extra arguments, with any -pw password removed</summary>
            ExtraArgs = 128,
            /// <summary>The session's notes</summary>
            Notes = 256,
            /// <summary>The SPSL script file path</summary>
            SpslScript = 512,
            /// <summary>Every field</summary>
            All = Name | Folder | Host | Username | Port | Protocol | PuttyProfile | ExtraArgs | Notes | SpslScript
        }

        /// <summary>Each searchable field and its label, in the order shown in Tools -> Options</summary>
        public static readonly KeyValuePair<SearchFields, string>[] SearchFieldLabels =
        {
            new KeyValuePair<SearchFields, string>(SearchFields.Name, "Session name"),
            new KeyValuePair<SearchFields, string>(SearchFields.Folder, "Folder"),
            new KeyValuePair<SearchFields, string>(SearchFields.Host, "Host address"),
            new KeyValuePair<SearchFields, string>(SearchFields.Username, "Login username"),
            new KeyValuePair<SearchFields, string>(SearchFields.Port, "TCP port"),
            new KeyValuePair<SearchFields, string>(SearchFields.Protocol, "Connection type"),
            new KeyValuePair<SearchFields, string>(SearchFields.PuttyProfile, "PuTTY session profile"),
            new KeyValuePair<SearchFields, string>(SearchFields.ExtraArgs, "Extra arguments"),
            new KeyValuePair<SearchFields, string>(SearchFields.Notes, "Notes"),
            new KeyValuePair<SearchFields, string>(SearchFields.SpslScript, "SPSL script file")
        };

        /// <summary>The fields to search, from Settings.SessionsSearchFields; all fields if the setting is empty or invalid</summary>
        /// <returns>The fields to search; never <see cref="SearchFields.None"/></returns>
        public static SearchFields GetSearchFieldsSetting()
        {
            SearchFields fields = FormUtils.SafeParseEnum(SuperPuTTY.Settings.SessionsSearchFields ?? string.Empty, true, SearchFields.All);
            return fields == SearchFields.None ? SearchFields.All : fields;
        }

        /// <summary>Decides whether a session matches the search box text, in the chosen <see cref="SearchMode"/> and <see cref="SearchFields"/></summary>
        public class SearchFilter
        {
            /// <summary>Create a filter</summary>
            /// <param name="mode">The <see cref="SearchMode"/> name from Settings.SessionsSearchMode</param>
            /// <param name="filter">The text typed in the search box</param>
            /// <param name="fields">The session fields to look in</param>
            public SearchFilter(string mode, string filter, SearchFields fields)
            {
                this.Mode = FormUtils.SafeParseEnum(mode, true, SearchMode.CaseSensitive);
                this.Filter = filter;
                this.Fields = fields;
                if (this.Mode == SearchMode.Regex)
                {
                    try
                    {
                        this.Regex = new Regex(filter);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not parse pattern: " + filter, ex);
                    }
                }
            }
            /// <summary>Whether any of the chosen fields of <paramref name="s"/> matches the search text</summary>
            /// <param name="s">The session to test</param>
            /// <returns>true if the session should be shown; always true for empty text or an invalid regex</returns>
            public bool IsMatch(SessionData s)
            {
                if (string.IsNullOrEmpty(this.Filter) || (this.Mode == SearchMode.Regex && this.Regex == null))
                {
                    return true;
                }
                return GetSearchValues(s, this.Fields).Any(this.IsMatch);
            }

            /// <summary>Whether one field value matches the search text</summary>
            /// <param name="value">The field value; not null</param>
            /// <returns>true if it matches in the current <see cref="Mode"/></returns>
            private bool IsMatch(string value)
            {
                switch (this.Mode)
                {
                    case SearchMode.CaseInSensitive:
                        return value.IndexOf(this.Filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    case SearchMode.Regex:
                        return this.Regex.IsMatch(value);
                    default:
                        return value.Contains(this.Filter);
                }
            }

            /// <summary>The non-empty values of the chosen fields of a session, as text</summary>
            /// <param name="s">The session</param>
            /// <param name="fields">Which fields to return</param>
            /// <returns>The field values to search</returns>
            public static IEnumerable<string> GetSearchValues(SessionData s, SearchFields fields)
            {
                List<string> values = new List<string>();
                if (fields.HasFlag(SearchFields.Name)) values.Add(s.SessionName);
                if (fields.HasFlag(SearchFields.Folder)) values.Add(SessionData.GetSessionParentId(s.SessionId));
                if (fields.HasFlag(SearchFields.Host)) values.Add(s.Host);
                if (fields.HasFlag(SearchFields.Username)) values.Add(s.Username);
                if (fields.HasFlag(SearchFields.Port)) values.Add(s.Port.ToString());
                if (fields.HasFlag(SearchFields.Protocol)) values.Add(s.Proto.ToString());
                if (fields.HasFlag(SearchFields.PuttyProfile)) values.Add(s.PuttySession);
                // never let a search reveal a password stored in the extra arguments
                if (fields.HasFlag(SearchFields.ExtraArgs)) values.Add(CommandLineOptions.replacePassword(s.ExtraArgs, ""));
                if (fields.HasFlag(SearchFields.Notes)) values.Add(s.Note);
                if (fields.HasFlag(SearchFields.SpslScript)) values.Add(s.SPSLFileName);
                return values.Where(v => !string.IsNullOrEmpty(v));
            }

            public SearchMode Mode { get; set; }
            public string Filter { get; set; }

            /// <summary>The session fields searched</summary>
            public SearchFields Fields { get; set; }
            public Regex Regex { get; set; }
        }
        #endregion

        #region Key Handling
        private void treeView1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13 && IsShortcutNode(this.treeView1.SelectedNode))
            {
                treeView1_NodeMouseDoubleClick(null, null);
                e.Handled = true;
            }
            else if (e.KeyChar == (char)13 && IsSessionNode(this.treeView1.SelectedNode))
            {
                if (Control.ModifierKeys == Keys.None)
                {
                    treeView1_NodeMouseDoubleClick(null, null);
                    e.Handled = true;
                }
                else if (Control.ModifierKeys == Keys.Shift && !DatabaseLocked(this.treeView1.SelectedNode))
                {
                    CreateOrEditSessionToolStripMenuItem_Click(this.settingsToolStripMenuItem, e);
                    e.Handled = true;
                }
            }
        }

        private void treeView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && (treeView1.SelectedNode == null || treeView1.SelectedNode.Parent == null))
            {
                // the top-level nodes can't be deleted
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && DatabaseLocked(treeView1.SelectedNode))
            {
                // the table can't be written
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && IsShortcutNode(treeView1.SelectedNode))
            {
                // Delete on a Favorites or Recent entry removes the entry, not the session
                RemoveShortcut(treeView1.SelectedNode);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                if (IsFolderNode(treeView1.SelectedNode))
                {
                    removeFolderToolStripMenuItem_Click(removeFolderToolStripMenuItem, e);
                }
                else
                {
                    deleteToolStripMenuItem_Click(deleteToolStripMenuItem, e);
                }
                e.Handled = true;
            }
        }
        #endregion

        private void winSCPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            ExternalApplications.openWinSCP(session);
        }

        private void fileZillaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            ExternalApplications.openFileZilla(session); 
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (SuperPuTTY.Settings.FilterSessionsOnChange)
            {
                this.ApplySearch(this.txtSearch.Text);
            }
        }

        private void copyHostNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session;
            session = (SessionData)treeView1.SelectedNode.Tag;
            if (session.Port != 0)
            {
                Clipboard.SetText(session.Host + " " + session.Port);
            }
            else
            {
                Clipboard.SetText(session.Host);
            }
        }
    }

}
