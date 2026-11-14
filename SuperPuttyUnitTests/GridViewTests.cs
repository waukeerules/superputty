using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>Grid View's columns and rows (GridView.Shape), as SuperXTerminal's gridShape</summary>
    [TestFixture]
    public class GridViewTests
    {
        /// <summary>The shape for a count in an area, as "columns x rows"</summary>
        /// <param name="count">How many tiles</param>
        /// <param name="width">The area's width</param>
        /// <param name="height">The area's height</param>
        /// <returns>e.g. "3x2"</returns>
        private static string Shape(int count, double width, double height)
        {
            int columns, rows;
            GridView.Shape(count, width, height, out columns, out rows);
            return columns + "x" + rows;
        }

        /// <summary>On a wide monitor: 6 tabs are 3 x 2, 9 are 3 x 3, 2 side by side, 1 alone</summary>
        [Test]
        public void WideMonitor()
        {
            Assert.AreEqual("3x2", Shape(6, 1920, 1000));
            Assert.AreEqual("3x3", Shape(9, 1920, 1000));
            Assert.AreEqual("2x1", Shape(2, 1920, 1000));
            Assert.AreEqual("1x1", Shape(1, 1920, 1000));
        }

        /// <summary>A tall area gets more rows than columns</summary>
        [Test]
        public void TallArea()
        {
            Assert.AreEqual("1x4", Shape(4, 800, 1600));
        }

        /// <summary>Without a size yet, as square as can be</summary>
        [Test]
        public void NoSize()
        {
            Assert.AreEqual("3x2", Shape(5, 0, 0));
        }
    }
}
