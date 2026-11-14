using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The changes Manage Button Bars makes (ButtonBarManage), ported from SuperXTerminal's manage tests: moving and
    /// copying buttons without doubling them, merging a bar imported as "Name (2)" into the one it copies, spotting
    /// duplicate buttons and labels used for two commands, and copies named Name-1
    /// </summary>
    [TestFixture]
    public class ButtonBarManageTests
    {
        /// <summary>A button sending its label and a line break, unless given other text</summary>
        /// <param name="label">Its label</param>
        /// <param name="text">What it sends; null for its label and a line break</param>
        /// <returns>The button</returns>
        private static CommandButton Button(string label, string text = null)
        {
            return new CommandButton { Label = label, Text = text ?? label + "\\n", Action = CommandButtonAction.SendString };
        }

        /// <summary>A bar with these buttons</summary>
        /// <param name="name">Its name</param>
        /// <param name="buttons">Its buttons</param>
        /// <returns>The bar</returns>
        private static ButtonBar Bar(string name, params CommandButton[] buttons)
        {
            return new ButtonBar { Name = name, Buttons = buttons.ToList() };
        }

        /// <summary>A bar's button labels, joined with "|"</summary>
        /// <param name="bars">The bars</param>
        /// <param name="name">The bar's name</param>
        /// <returns>The labels</returns>
        private static string Labels(List<ButtonBar> bars, string name)
        {
            return string.Join("|", bars[ButtonBarManage.Find(bars, name)].Buttons.Select(b => b.Label));
        }

        /// <summary>The usual two bars</summary>
        /// <returns>Cisco with three buttons, Linux with one</returns>
        private static List<ButtonBar> Bars()
        {
            return new List<ButtonBar> { Bar("Cisco", Button("show run"), Button("show ver"), Button("wr")), Bar("Linux", Button("ls")) };
        }

        /// <summary>Moves the buttons chosen to the end of the other bar, leaving the bars given alone</summary>
        [Test]
        public void Moves()
        {
            List<ButtonBar> bars = Bars();
            int skipped;
            List<ButtonBar> next = ButtonBarManage.Transfer(bars, "Cisco", new[] { 0, 2 }, "Linux", false, out skipped);
            Assert.AreEqual("show ver", Labels(next, "Cisco"));
            Assert.AreEqual("ls|show run|wr", Labels(next, "Linux"));
            Assert.AreEqual(0, skipped);
            Assert.AreEqual("show run|show ver|wr", Labels(bars, "Cisco"), "the bars given are left alone");
        }

        /// <summary>Copies them, leaving the first bar as it was</summary>
        [Test]
        public void Copies()
        {
            int skipped;
            List<ButtonBar> next = ButtonBarManage.Transfer(Bars(), "Cisco", new[] { 1 }, "Linux", true, out skipped);
            Assert.AreEqual("show run|show ver|wr", Labels(next, "Cisco"));
            Assert.AreEqual("ls|show ver", Labels(next, "Linux"));
        }

        /// <summary>A button the other bar has the same of isn't added again, and a move still takes it off</summary>
        [Test]
        public void NotAddedTwice()
        {
            List<ButtonBar> bars = new List<ButtonBar> { Bars()[0], Bar("Linux", Button("ls"), Button("wr")) };
            int skipped;
            List<ButtonBar> copied = ButtonBarManage.Transfer(bars, "Cisco", new[] { 2 }, "Linux", true, out skipped);
            Assert.AreEqual("ls|wr", Labels(copied, "Linux"));
            Assert.AreEqual(1, skipped);
            List<ButtonBar> moved = ButtonBarManage.Transfer(bars, "Cisco", new[] { 2 }, "Linux", false, out skipped);
            Assert.AreEqual("show run|show ver", Labels(moved, "Cisco"));
            Assert.AreEqual("ls|wr", Labels(moved, "Linux"));
        }

        /// <summary>Merging adds the buttons it hasn't got, joins the tab icons and takes the bar away</summary>
        [Test]
        public void Merges()
        {
            ButtonBar original = Bar("Cisco", Button("show run"), Button("wr"));
            original.TabIcons.Add("switch");
            ButtonBar imported = Bar("Cisco (2)", Button("show run"), Button("show ip int br"), Button("wr", "write memory\\n"));
            imported.TabIcons.AddRange(new[] { "router", "Switch" });
            int added, skipped, clashing;
            List<ButtonBar> next = ButtonBarManage.Merge(new List<ButtonBar> { original, imported }, "Cisco (2)", "Cisco", out added, out skipped, out clashing);
            Assert.AreEqual(1, next.Count);
            Assert.AreEqual("show run|wr|show ip int br|wr", Labels(next, "Cisco"));
            CollectionAssert.AreEqual(new[] { "switch", "router" }, next[0].TabIcons);
            Assert.AreEqual(2, added);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(1, clashing);
        }

        /// <summary>A button the same as one before it is a duplicate; one with another command only clashes</summary>
        [Test]
        public void DuplicatesAndClashes()
        {
            ButtonBar bar = Bar("Cisco", Button("wr"), Button("show run"), Button("WR", "wr\\n"), Button("wr", "write memory\\n"));
            CollectionAssert.AreEqual(new[] { 2 }, ButtonBarManage.Duplicates(bar).ToArray());
            CollectionAssert.AreEquivalent(new[] { 0, 2, 3 }, ButtonBarManage.Clashes(bar));
        }

        /// <summary>A bar named as an import names a clash is taken for a copy of the other</summary>
        [Test]
        public void ImportCopies()
        {
            List<ButtonBar> bars = new List<ButtonBar> { Bar("Cisco"), Bar("Cisco (2)"), Bar("Linux (3)"), Bar("Juniper") };
            List<KeyValuePair<string, string>> found = ButtonBarManage.Copies(bars);
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Cisco (2)", found[0].Key);
            Assert.AreEqual("Cisco", found[0].Value);
        }

        /// <summary>A bar's copy goes after it as Name-1, or the next number not taken</summary>
        [Test]
        public void CopiesABar()
        {
            List<ButtonBar> bars = new List<ButtonBar> { Bar("Cisco", Button("wr")), Bar("Cisco-1"), Bar("Linux") };
            List<ButtonBar> next = ButtonBarManage.DuplicateBar(bars, "Cisco");
            CollectionAssert.AreEqual(new[] { "Cisco", "Cisco-2", "Cisco-1", "Linux" }, next.Select(b => b.Name).ToArray());
            Assert.AreEqual("wr", Labels(next, "Cisco-2"));
        }

        /// <summary>Duplicated buttons go just after themselves as Label-1, then -2 beside the original</summary>
        [Test]
        public void DuplicatesButtons()
        {
            List<int> copies;
            List<ButtonBar> next = ButtonBarManage.DuplicateButtons(Bars(), "Cisco", new[] { 0, 2 }, out copies);
            Assert.AreEqual("show run|show run-1|show ver|wr|wr-1", Labels(next, "Cisco"));
            CollectionAssert.AreEqual(new[] { 1, 4 }, copies);
            List<ButtonBar> again = ButtonBarManage.DuplicateButtons(next, "Cisco", new[] { 0 }, out copies);
            Assert.AreEqual("show run|show run-2|show run-1|show ver|wr|wr-1", Labels(again, "Cisco"));
        }

        /// <summary>An item goes somewhere else in its list; a place outside it changes nothing</summary>
        [Test]
        public void Reorders()
        {
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, ButtonBarManage.Reorder(new[] { "a", "b", "c" }, 0, 2));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ButtonBarManage.Reorder(new[] { "a", "b", "c" }, 2, 5));
        }
    }
}
