using System.Collections.Generic;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Characterization tests for the screen tiling layout rules that both the grid map
    /// (GridMapPanel) and the window repositioner (WindowMonitor) paint with.
    /// Auto shapes: 1 → full cell, 2 → halves, 3 → wide top + 2 below, 4 → 2x2, 5/6 → 2x3,
    /// 7/8 → 2x4, 9+ → nearest square with the last row stretched.
    /// </summary>
    [TestFixture]
    public class WindowMonitorLayoutTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(5, 5)]
        [TestCase(6, 6)]
        [TestCase(7, 7)]
        [TestCase(8, 8)]
        [TestCase(9, 9)]
        public void ComputeLayout_Automatic_SizeMatchesProgramCount(int n, int expectedCells)
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(n, 0, 0);

            Assert.That(layout.Length, Is.EqualTo(expectedCells));
        }

        [Test]
        public void ComputeLayout_SingleProgram_IsOneFullCell()
        {
            var cell = WindowMonitor.ComputeLayout(1, 0, 0)[0];

            Assert.That(cell.X, Is.EqualTo(0.0));
            Assert.That(cell.Y, Is.EqualTo(0.0));
            Assert.That(cell.W, Is.EqualTo(1.0));
            Assert.That(cell.H, Is.EqualTo(1.0));
        }

        [Test]
        public void ComputeLayout_TwoPrograms_AreSideBySideHalves()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(2, 0, 0);

            Assert.That(layout[0].W, Is.EqualTo(0.5));
            Assert.That(layout[1].X, Is.EqualTo(0.5));
            Assert.That(layout[1].W, Is.EqualTo(0.5));
            Assert.That(layout[0].H, Is.EqualTo(1.0));
        }

        [Test]
        public void ComputeLayout_ThreePrograms_HaveWideTopRow()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(3, 0, 0);

            Assert.That(layout[0].W, Is.EqualTo(1.0));
            Assert.That(layout[0].H, Is.EqualTo(0.5));
            Assert.That(layout[1].Y, Is.EqualTo(0.5));
            Assert.That(layout[1].W, Is.EqualTo(0.5));
            Assert.That(layout[2].X, Is.EqualTo(0.5));
        }

        [Test]
        public void ComputeLayout_FivePrograms_StretchFinalPartialRow()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(5, 0, 0);

            Assert.That(layout.Length, Is.EqualTo(5));
            Assert.That(layout[0].W, Is.EqualTo(1.0 / 3.0).Within(1e-9));
            Assert.That(layout[3].W, Is.EqualTo(0.5));   // second row: only 2 cells, stretched
        }

        [Test]
        public void ComputeLayout_ExplicitRowsAndCols_AreRespected()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(4, 1, 4);

            Assert.That(layout.Length, Is.EqualTo(4));
            Assert.That(layout[0].H, Is.EqualTo(1.0));
            foreach (var cell in layout)
                Assert.That(cell.W, Is.EqualTo(0.25).Within(1e-9));
        }

        [Test]
        public void ComputeLayout_ColumnsOnly_InferRows()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(5, 0, 3);

            Assert.That(layout.Length, Is.EqualTo(5));
            Assert.That(layout[0].H, Is.EqualTo(0.5));   // 2 rows inferred
        }

        [Test]
        public void ComputeLayout_RowsOnly_InferColumns()
        {
            WindowMonitor.CellRect[] layout = WindowMonitor.ComputeLayout(6, 2, 0);

            Assert.That(layout.Length, Is.EqualTo(6));
            Assert.That(layout[0].H, Is.EqualTo(0.5));
        }

        [Test]
        public void ResolveLayout_NullSlots_AutoFillsInListOrder()
        {
            WindowMonitor.ResolveLayout(
                new List<string> { "a", "b", "c" }, 0, 0, null, out _, out int[] cells);

            Assert.That(cells, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void ResolveLayout_SlotPinning_RespectsExplicitCells()
        {
            WindowMonitor.ResolveLayout(
                new List<string> { "a", "b", "c" }, 3, 1, new[] { 2, -1, -1 }, out _, out int[] cells);

            Assert.That(cells, Is.EqualTo(new[] { 2, 0, 1 }));
        }

        [Test]
        public void ResolveLayout_ConflictingSlots_FirstKeepsCellOthersAutoFill()
        {
            WindowMonitor.ResolveLayout(
                new List<string> { "a", "b", "c" }, 3, 1, new[] { 0, 0, -1 }, out _, out int[] cells);

            Assert.That(cells, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void ResolveLayout_OutOfRangeSlot_IsAutoFilled()
        {
            WindowMonitor.ResolveLayout(
                new List<string> { "a", "b", "c" }, 3, 1, new[] { 99, -1, -1 }, out _, out int[] cells);

            Assert.That(cells, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void ResolveLayout_LayoutSmallerThanList_WrapsWithModulo()
        {
            WindowMonitor.ResolveLayout(
                new List<string> { "a", "b", "c", "d" }, 1, 2, new int[4], out var layout, out int[] cells);

            Assert.That(layout.Length, Is.EqualTo(2));
            Assert.That(cells, Is.EqualTo(new[] { 0, 1, 0, 1 }));
        }

        [Test]
        public void ResolveLayout_EmptyList_ProducesSingleCellLayoutAndNoCells()
        {
            WindowMonitor.ResolveLayout(new List<string>(), 0, 0, new int[0], out var layout, out int[] cells);

            Assert.That(layout.Length, Is.EqualTo(1));
            Assert.That(cells.Length, Is.EqualTo(0));
        }
    }
}
