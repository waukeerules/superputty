using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The changes Manage Button Bars (Gui.dlgManageButtonBars) makes to the bars, kept apart from its window so they can
    /// be tested alone, as SuperXTerminal's manage.ts (its 334a0a2, 05e8376): moving and copying buttons between bars,
    /// duplicating buttons and bars, merging one bar into another, and finding the clutter an import leaves behind. An
    /// import of a bar whose name is taken arrives as "Name (2)" (ButtonBarFile.FreeName), so the same bar often ends up
    /// twice with a few buttons different; these say which buttons are the same, which only share a label, and which bars
    /// look like copies of another. Copies of a bar or a button are named as the Sessions tree names one: "Name-1",
    /// "Name-2"... Every method returns new bars and leaves the ones given alone, so the window keeps its changes until Save.
    /// </summary>
    public static class ButtonBarManage
    {
        /// <summary>A label as compared: trimmed, any case</summary>
        /// <param name="label">The label</param>
        /// <returns>The key</returns>
        private static string LabelKey(string label)
        {
            return (label ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>A string as compared, null as empty</summary>
        /// <param name="text">The text</param>
        /// <returns>The text, "" for null</returns>
        private static string Of(string text)
        {
            return text ?? string.Empty;
        }

        /// <summary>Whether two buttons are the same in every way that shows or sends: label, tooltip, colour, icon, action and text</summary>
        /// <param name="a">One button</param>
        /// <param name="b">The other</param>
        /// <returns>true if the same</returns>
        public static bool SameButton(CommandButton a, CommandButton b)
        {
            return LabelKey(a.Label) == LabelKey(b.Label) && Of(a.Tooltip) == Of(b.Tooltip)
                && string.Equals(Of(a.Color), Of(b.Color), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Of(a.ImageKey), Of(b.ImageKey), StringComparison.OrdinalIgnoreCase)
                && a.Action == b.Action && Of(a.Text) == Of(b.Text);
        }

        /// <summary>The buttons on a bar that are the same as one before them, by index: what Remove Duplicates takes off</summary>
        /// <param name="bar">The bar</param>
        /// <returns>Their indexes</returns>
        public static HashSet<int> Duplicates(ButtonBar bar)
        {
            HashSet<int> found = new HashSet<int>();
            for (int i = 0; i < bar.Buttons.Count; i++)
            {
                if (bar.Buttons.Take(i).Any(b => SameButton(b, bar.Buttons[i])))
                    found.Add(i);
            }
            return found;
        }

        /// <summary>
        /// The buttons on a bar that share their label with another button there that isn't the same (another command
        /// under the same name, as two imports of one bar leave), by index: shown marked, so the user can pick which to keep
        /// </summary>
        /// <param name="bar">The bar</param>
        /// <returns>Their indexes</returns>
        public static HashSet<int> Clashes(ButtonBar bar)
        {
            HashSet<int> found = new HashSet<int>();
            for (int i = 0; i < bar.Buttons.Count; i++)
            {
                CommandButton button = bar.Buttons[i];
                if (bar.Buttons.Where((b, j) => j != i).Any(b => LabelKey(b.Label) == LabelKey(button.Label) && !SameButton(b, button)))
                    found.Add(i);
            }
            return found;
        }

        /// <summary>A bar's name without the " (2)" an import adds when the name is taken</summary>
        /// <param name="name">The name</param>
        /// <returns>The name as it was before</returns>
        public static string BaseName(string name)
        {
            return Regex.Replace((name ?? string.Empty).Trim(), @"\s+\(\d+\)$", "");
        }

        /// <summary>Bars that look like another's copy: "Name (2)" beside "Name", for a Merge hint</summary>
        /// <param name="bars">The bars</param>
        /// <returns>Each copy's name and the name of the bar it copies</returns>
        public static List<KeyValuePair<string, string>> Copies(IList<ButtonBar> bars)
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            foreach (ButtonBar bar in bars)
            {
                string baseName = BaseName(bar.Name);
                if (baseName == bar.Name.Trim())
                    continue;
                ButtonBar original = bars.FirstOrDefault(b => b != bar && string.Equals(b.Name.Trim(), baseName, StringComparison.OrdinalIgnoreCase));
                if (original != null)
                    found.Add(new KeyValuePair<string, string>(bar.Name, original.Name));
            }
            return found;
        }

        /// <summary>A bar's index by name (any case), or -1</summary>
        /// <param name="bars">The bars</param>
        /// <param name="name">The name</param>
        /// <returns>The index</returns>
        public static int Find(IList<ButtonBar> bars, string name)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                if (string.Equals(bars[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>A deep copy of the bars, so a change never reaches the ones given</summary>
        /// <param name="bars">The bars</param>
        /// <returns>New bars with new buttons</returns>
        public static List<ButtonBar> CopyOf(IEnumerable<ButtonBar> bars)
        {
            return bars.Select(b => new ButtonBar
            {
                Name = b.Name,
                Buttons = b.Buttons.Select(x => x.Clone()).ToList(),
                TabIcons = new List<string>(b.TabIcons ?? new List<string>())
            }).ToList();
        }

        /// <summary>
        /// Move or copy buttons from one bar to the end of another. A button the other bar already has the same of isn't
        /// added twice; a move takes it off the first bar all the same, as it's on the other.
        /// </summary>
        /// <param name="bars">The bars</param>
        /// <param name="from">The bar the buttons are on</param>
        /// <param name="indexes">The buttons, by index on <paramref name="from"/></param>
        /// <param name="to">The bar they go to</param>
        /// <param name="copy">true to copy, false to move</param>
        /// <param name="skipped">How many weren't added, as the other bar has the same already</param>
        /// <returns>The bars after</returns>
        public static List<ButtonBar> Transfer(IList<ButtonBar> bars, string from, IEnumerable<int> indexes, string to, bool copy, out int skipped)
        {
            List<ButtonBar> next = CopyOf(bars);
            skipped = 0;
            int source = Find(next, from), target = Find(next, to);
            if (source < 0 || target < 0 || source == target)
                return next;
            HashSet<int> chosen = new HashSet<int>(indexes);
            for (int i = 0; i < next[source].Buttons.Count; i++)
            {
                if (!chosen.Contains(i))
                    continue;
                CommandButton button = next[source].Buttons[i];
                if (next[target].Buttons.Any(b => SameButton(b, button)))
                    skipped++;
                else
                    next[target].Buttons.Add(button.Clone());
            }
            if (!copy)
                next[source].Buttons = next[source].Buttons.Where((b, i) => !chosen.Contains(i)).ToList();
            return next;
        }

        /// <summary>
        /// Merge one bar into another: its buttons go to the end of the other, but for ones the other has already, its tab
        /// icons join the other's, and the bar goes. A button with a label already there and another command is kept, so
        /// nothing is lost; the window marks both.
        /// </summary>
        /// <param name="bars">The bars</param>
        /// <param name="from">The bar merged</param>
        /// <param name="into">The bar it's merged into</param>
        /// <param name="added">Buttons added</param>
        /// <param name="skipped">Buttons left out as the other bar has the same</param>
        /// <param name="clashing">Of those added, the ones whose label is already there with another command</param>
        /// <returns>The bars after</returns>
        public static List<ButtonBar> Merge(IList<ButtonBar> bars, string from, string into, out int added, out int skipped, out int clashing)
        {
            List<ButtonBar> next = CopyOf(bars);
            added = skipped = clashing = 0;
            int source = Find(next, from), target = Find(next, into);
            if (source < 0 || target < 0 || source == target)
                return next;
            foreach (CommandButton button in next[source].Buttons)
            {
                if (next[target].Buttons.Any(b => SameButton(b, button)))
                {
                    skipped++;
                    continue;
                }
                if (next[target].Buttons.Any(b => LabelKey(b.Label) == LabelKey(button.Label)))
                    clashing++;
                next[target].Buttons.Add(button.Clone());
                added++;
            }
            foreach (string icon in next[source].TabIcons)
            {
                if (!next[target].TabIcons.Any(t => string.Equals(t, icon, StringComparison.OrdinalIgnoreCase)))
                    next[target].TabIcons.Add(icon);
            }
            next.RemoveAt(source);
            return next;
        }

        /// <summary>
        /// A copy's name: <paramref name="name"/> with -1 added, else -2, -3... the first not taken, as the Sessions tree
        /// names a copy (SessionTreeview.UniqueName). Never the name itself: a copy beside its original always reads as one.
        /// </summary>
        /// <param name="name">The original's name</param>
        /// <param name="taken">Whether a name is taken</param>
        /// <returns>The copy's name</returns>
        public static string CopyName(string name, Func<string, bool> taken)
        {
            int n = 1;
            while (taken(name + "-" + n))
                n++;
            return name + "-" + n;
        }

        /// <summary>Copy a bar, its buttons and tab icons, placed after it as "Name-1" (or the next number free)</summary>
        /// <param name="bars">The bars</param>
        /// <param name="name">The bar to copy</param>
        /// <returns>The bars after</returns>
        public static List<ButtonBar> DuplicateBar(IList<ButtonBar> bars, string name)
        {
            List<ButtonBar> next = CopyOf(bars);
            int at = Find(next, name);
            if (at < 0)
                return next;
            ButtonBar copy = CopyOf(new[] { next[at] })[0];
            copy.Name = CopyName(next[at].Name, n => Find(next, n) >= 0);
            next.Insert(at + 1, copy);
            return next;
        }

        /// <summary>
        /// Duplicate buttons on a bar: each copy goes just after its button, labelled "Label-1" (or the next number not
        /// taken on that bar, in any case), with everything else the same, ready to change into a button of its own
        /// </summary>
        /// <param name="bars">The bars</param>
        /// <param name="name">The bar</param>
        /// <param name="indexes">The buttons, by index on the bar</param>
        /// <param name="copies">Where the copies are on their bar, by index</param>
        /// <returns>The bars after</returns>
        public static List<ButtonBar> DuplicateButtons(IList<ButtonBar> bars, string name, IEnumerable<int> indexes, out List<int> copies)
        {
            List<ButtonBar> next = CopyOf(bars);
            copies = new List<int>();
            int at = Find(next, name);
            if (at < 0)
                return next;
            ButtonBar bar = next[at];
            HashSet<int> chosen = new HashSet<int>(indexes);
            List<CommandButton> buttons = new List<CommandButton>();
            Func<string, bool> taken = label => bar.Buttons.Concat(buttons).Any(b => LabelKey(b.Label) == LabelKey(label));
            for (int i = 0; i < bar.Buttons.Count; i++)
            {
                buttons.Add(bar.Buttons[i]);
                if (!chosen.Contains(i))
                    continue;
                copies.Add(buttons.Count);
                CommandButton copy = bar.Buttons[i].Clone();
                copy.Label = CopyName((bar.Buttons[i].Label ?? string.Empty).Trim(), taken);
                buttons.Add(copy);
            }
            bar.Buttons = buttons;
            return next;
        }

        /// <summary>Move an item in a list from one place to another: a bar among bars, a button on its bar</summary>
        /// <typeparam name="T">The items' type</typeparam>
        /// <param name="items">The list</param>
        /// <param name="from">Where the item is</param>
        /// <param name="to">Where it goes</param>
        /// <returns>A new list; the same order when either place is outside it</returns>
        public static List<T> Reorder<T>(IList<T> items, int from, int to)
        {
            List<T> next = new List<T>(items);
            if (from < 0 || from >= next.Count || to < 0 || to >= next.Count)
                return next;
            T item = next[from];
            next.RemoveAt(from);
            next.Insert(to, item);
            return next;
        }
    }
}
