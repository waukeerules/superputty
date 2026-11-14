using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using log4net;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Grid View, as SuperXTerminal's (its 03768a5, f1185e4, 28955cc, bde855e): every document tab in a split of its own,
    /// tiled row by row in the order they show, to watch many sessions at once (term mon on a few switches, installs on
    /// several servers) without making the grid split by split. The columns and rows are the ones that give each tile
    /// the most room for a terminal-shaped area (<see cref="Shape"/>). While it's on, the grid is made again as tabs open
    /// and close; turned off, the splits from before come back (<see cref="Restore"/>), or with Join All Tabs every
    /// tab joins one group. The tabs only move between DockPanelSuite panes, so their sessions carry on. frmSuperPutty
    /// attaches it to the dock panel and offers it in the View menu (Ctrl+Shift+G); ctlPuttyPanel's menu and the
    /// Sessions tree's Connect ... in a Grid turn it on too. Whether it's on is the GridView setting, so it lasts.
    /// </summary>
    public static class GridView
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(GridView));

        /// <summary>How much wider than high a tile should be: an 80 x 24 terminal is about 1.6 times as wide as high</summary>
        public const double TileAspect = 1.6;

        /// <summary>How long after the last tab opened or closed the grid is made again, so many opening at once tile once</summary>
        private const int RetileDelayMs = 300;

        /// <summary>The main window's dock panel; null until <see cref="Attach"/></summary>
        private static DockPanel dock;

        /// <summary>Makes the grid again a moment after tabs open or close</summary>
        private static Timer retileTimer;

        /// <summary>Set while the grid is being made, so the moves it makes don't ask for another</summary>
        private static bool tiling;

        /// <summary>The splits as they were when Grid View went on, for <see cref="Restore"/>; null when there are none to restore</summary>
        private static List<Group> before;

        /// <summary>One split (DockPanelSuite pane) as it was before the grid: its tabs in order, the one in front, and where it was</summary>
        private class Group
        {
            /// <summary>Its tabs, in their order</summary>
            public List<IDockContent> Contents = new List<IDockContent>();

            /// <summary>The tab in front</summary>
            public IDockContent Active;

            /// <summary>Where it was, in the dock panel's coordinates</summary>
            public Rectangle Bounds;

            /// <summary>The pane it's been put back as, while restoring</summary>
            public DockPane Pane;
        }

        /// <summary>Raised when Grid View goes on or off, so the View menu and tab menus can tick it</summary>
        public static event EventHandler Changed;

        /// <summary>Whether Grid View is on</summary>
        public static bool IsOn
        {
            get { return SuperPuTTY.Settings.GridView; }
        }

        /// <summary>
        /// Follow the main window's dock panel: as tabs open and close in Grid View, the grid is made again. Grid View
        /// left on when SuperXPuTTY last closed tiles the tabs as they open.
        /// </summary>
        /// <param name="panel">The main window's dock panel</param>
        public static void Attach(DockPanel panel)
        {
            dock = panel;
            retileTimer = new Timer { Interval = RetileDelayMs };
            retileTimer.Tick += (s, e) =>
            {
                retileTimer.Stop();
                if (IsOn)
                {
                    Tile();
                }
            };
            panel.ContentAdded += (s, e) => ScheduleTile();
            panel.ContentRemoved += (s, e) => ScheduleTile();
        }

        /// <summary>Make the grid again a moment from now, once the tabs opening or closing have settled</summary>
        public static void ScheduleTile()
        {
            if (!IsOn || tiling || retileTimer == null)
                return;
            retileTimer.Stop();
            retileTimer.Start();
        }

        /// <summary>
        /// Turn Grid View on or off. On, the splits as they are now are noted and the tabs are tiled; off, the splits
        /// from before come back
        /// </summary>
        /// <param name="on">true to tile the tabs</param>
        public static void Set(bool on)
        {
            if (dock == null || on == IsOn)
                return;
            SuperPuTTY.Settings.GridView = on;
            SuperPuTTY.Settings.Save();
            if (on)
            {
                before = Snapshot();
                Tile();
            }
            else
            {
                Restore();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Join every tab into one group, the active tab in front; Grid View goes off and the splits from before are forgotten</summary>
        public static void JoinAll()
        {
            if (dock == null)
                return;
            bool wasOn = IsOn;
            SuperPuTTY.Settings.GridView = false;
            SuperPuTTY.Settings.Save();
            before = null;
            IDockContent active = dock.ActiveDocument;
            Join(Documents());
            (active as DockContent)?.Activate();
            if (wasOn)
            {
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The columns and rows for so many tiles: the shape that gives each tile the most room for a terminal-shaped
        /// area (<see cref="TileAspect"/>). 6 tabs on a wide monitor are 3 x 2, 9 are 3 x 3, a tall area gets more rows.
        /// </summary>
        /// <param name="count">How many tiles</param>
        /// <param name="width">The area's width</param>
        /// <param name="height">The area's height</param>
        /// <param name="columns">The columns</param>
        /// <param name="rows">The rows</param>
        public static void Shape(int count, double width, double height, out int columns, out int rows)
        {
            int n = Math.Max(1, count);
            if (!(width > 0 && height > 0))
            {
                columns = (int)Math.Ceiling(Math.Sqrt(n));
                rows = (int)Math.Ceiling(n / (double)columns);
                return;
            }
            columns = 1;
            rows = n;
            double best = -1;
            for (int c = 1; c <= n; c++)
            {
                int r = (int)Math.Ceiling(n / (double)c);
                double fits = Math.Min(width / c / TileAspect, height / r);
                if (fits > best)
                {
                    best = fits;
                    columns = c;
                    rows = r;
                }
            }
        }

        /// <summary>
        /// The tabs in a grid's order, row by row as Grid View reads: Close Others to the Right goes by it, since each tile
        /// is a group of one (ctlPuttyPanel)
        /// </summary>
        /// <returns>The document tabs, in the order they show</returns>
        public static List<IDockContent> Documents()
        {
            if (dock == null)
                return new List<IDockContent>();
            List<IDockContent> documents = new List<IDockContent>();
            foreach (DockPane pane in DocumentPanes())
            {
                documents.AddRange(pane.Contents.Where(c => c.DockHandler.DockState == DockState.Document));
            }
            return documents;
        }

        /// <summary>The panes holding document tabs, top to bottom, then left to right, as they show</summary>
        /// <returns>The panes</returns>
        private static List<DockPane> DocumentPanes()
        {
            return dock.Panes
                .Where(p => p.DockState == DockState.Document && !p.IsHidden && p.Contents.Count > 0)
                .OrderBy(p => BoundsOf(p).Top / 8)
                .ThenBy(p => BoundsOf(p).Left)
                .ToList();
        }

        /// <summary>A pane's place in the dock panel's coordinates</summary>
        /// <param name="pane">The pane</param>
        /// <returns>Its bounds</returns>
        private static Rectangle BoundsOf(DockPane pane)
        {
            return pane.IsHandleCreated && dock.IsHandleCreated ? dock.RectangleToClient(pane.RectangleToScreen(pane.ClientRectangle)) : pane.Bounds;
        }

        /// <summary>The splits as they are now, for putting back when Grid View goes off</summary>
        /// <returns>Each split's tabs, tab in front and place</returns>
        private static List<Group> Snapshot()
        {
            return DocumentPanes().Select(pane => new Group
            {
                Contents = pane.Contents.Where(c => c.DockHandler.DockState == DockState.Document).ToList(),
                Active = pane.ActiveContent,
                Bounds = BoundsOf(pane)
            }).ToList();
        }

        /// <summary>Put every tab in one pane (the first tab's), keeping their order</summary>
        /// <param name="documents">The tabs</param>
        /// <returns>The pane they're in; null when there are none</returns>
        private static DockPane Join(List<IDockContent> documents)
        {
            if (documents.Count == 0)
                return null;
            tiling = true;
            try
            {
                DockPane target = documents[0].DockHandler.Pane;
                foreach (IDockContent content in documents.Skip(1))
                {
                    if (content.DockHandler.Pane != target)
                    {
                        content.DockHandler.Show(target, null);
                    }
                }
                return target;
            }
            finally
            {
                tiling = false;
            }
        }

        /// <summary>
        /// Tile the tabs: rows are split off the first tab's pane top to bottom, then each row is split into its columns,
        /// each tile an even share. The tab in front stays in front.
        /// </summary>
        public static void Tile()
        {
            if (dock == null)
                return;
            List<IDockContent> documents = Documents();
            if (documents.Count < 2)
                return;
            IDockContent active = dock.ActiveDocument;
            Rectangle area = dock.DocumentWindowBounds;
            int columns, rows;
            Shape(documents.Count, area.Width, area.Height, out columns, out rows);
            Log.InfoFormat("Grid View: {0} tabs in {1} x {2}", documents.Count, columns, rows);
            dock.SuspendLayout(true);
            DockPane all = Join(documents);
            tiling = true;
            try
            {
                // the rows: each first tab split off the row above, taking an even share of what's left
                DockPane[] rowPanes = new DockPane[rows];
                rowPanes[0] = all;
                for (int r = 1; r < rows && r * columns < documents.Count; r++)
                {
                    int left = rows - r + 1;
                    documents[r * columns].DockHandler.Show(rowPanes[r - 1], DockAlignment.Bottom, (left - 1) / (double)left);
                    rowPanes[r] = documents[r * columns].DockHandler.Pane;
                }
                // then each row's columns, split off the tile to their left
                for (int r = 0; r < rows && r * columns < documents.Count; r++)
                {
                    int inRow = Math.Min(columns, documents.Count - r * columns);
                    DockPane previous = rowPanes[r];
                    for (int c = 1; c < inRow; c++)
                    {
                        int left = inRow - c + 1;
                        IDockContent content = documents[r * columns + c];
                        content.DockHandler.Show(previous, DockAlignment.Right, (left - 1) / (double)left);
                        previous = content.DockHandler.Pane;
                    }
                }
                // each tile shows its own tab
                foreach (IDockContent content in documents)
                {
                    content.DockHandler.Pane.ActiveContent = content;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Grid View: could not tile the tabs", ex);
            }
            finally
            {
                tiling = false;
                dock.ResumeLayout(true, true);
            }
            (active as DockContent)?.Activate();
        }

        /// <summary>
        /// Grid View went off: put back the splits from before it went on, each beside the one it was next to, about
        /// its size, with its tabs in their order and the one that was in front. A tab closed in the grid is left out;
        /// one opened there joins the split that was in front. Without a note of them, every tab joins one group.
        /// </summary>
        private static void Restore()
        {
            List<IDockContent> documents = Documents();
            List<Group> groups = before;
            before = null;
            IDockContent active = dock.ActiveDocument;
            if (groups == null || groups.Count == 0)
            {
                Join(documents);
                (active as DockContent)?.Activate();
                return;
            }
            // tabs closed in the grid are left out, and splits left with none
            foreach (Group group in groups)
            {
                group.Contents = group.Contents.Where(documents.Contains).ToList();
            }
            groups = groups.Where(g => g.Contents.Count > 0).ToList();
            if (groups.Count == 0)
            {
                Join(documents);
                return;
            }
            // tabs opened in the grid join the split that was in front
            Group front = groups.FirstOrDefault(g => g.Contents.Contains(active)) ?? groups[0];
            front.Contents.AddRange(documents.Where(d => !groups.Any(g => g.Contents.Contains(d))));

            dock.SuspendLayout(true);
            DockPane first = Join(documents);
            tiling = true;
            try
            {
                // the first split stays where the tabs are now; the rest are made beside the split they were next to
                groups[0].Pane = first;
                for (int i = 1; i < groups.Count; i++)
                {
                    Group group = groups[i];
                    Group beside = groups.Take(i).OrderBy(g => Gap(g.Bounds, group.Bounds)).First();
                    bool right = group.Bounds.Left >= beside.Bounds.Right - 8;
                    double share = right
                        ? group.Bounds.Width / (double)Math.Max(1, beside.Bounds.Width + group.Bounds.Width)
                        : group.Bounds.Height / (double)Math.Max(1, beside.Bounds.Height + group.Bounds.Height);
                    group.Contents[0].DockHandler.Show(beside.Pane, right ? DockAlignment.Right : DockAlignment.Bottom, Math.Min(0.9, Math.Max(0.1, share)));
                    group.Pane = group.Contents[0].DockHandler.Pane;
                }
                // each split's tabs in their order, and the one that was in front
                foreach (Group group in groups)
                {
                    foreach (IDockContent content in group.Contents)
                    {
                        if (content.DockHandler.Pane != group.Pane)
                        {
                            content.DockHandler.Show(group.Pane, null);
                        }
                    }
                    if (group.Active != null && group.Contents.Contains(group.Active))
                    {
                        group.Pane.ActiveContent = group.Active;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Grid View: could not put the splits back", ex);
            }
            finally
            {
                tiling = false;
                dock.ResumeLayout(true, true);
            }
            (active as DockContent)?.Activate();
        }

        /// <summary>How far apart two splits were: 0 when they touch, so a split goes back beside the one it was next to</summary>
        /// <param name="a">One split's place</param>
        /// <param name="b">The other's</param>
        /// <returns>The gap in pixels, plus how far off they are along the edge they share</returns>
        private static int Gap(Rectangle a, Rectangle b)
        {
            int horizontal = Math.Max(0, Math.Max(b.Left - a.Right, a.Left - b.Right));
            int vertical = Math.Max(0, Math.Max(b.Top - a.Bottom, a.Top - b.Bottom));
            int along = b.Left >= a.Right - 8 ? Math.Abs(a.Top - b.Top) : Math.Abs(a.Left - b.Left);
            return horizontal + vertical + along;
        }

        /// <summary>
        /// Connect ... in a Grid (the Sessions tree): Grid View goes on and the sessions open, tiled once they're all in
        /// </summary>
        /// <param name="open">Opens the sessions</param>
        public static void OpenInGrid(Action open)
        {
            if (!IsOn)
            {
                Set(true);
            }
            open();
            ScheduleTile();
        }
    }
}
