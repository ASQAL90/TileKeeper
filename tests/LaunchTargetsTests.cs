using System.Collections.Generic;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// The relaunch watchdog must read the Enable checkbox, not stale config: only enabled rows
    /// with a usable name and path are candidates, and list order is preserved.
    /// </summary>
    [TestFixture]
    public class LaunchTargetsTests
    {
        [Test]
        public void Filter_KeepsEnabledRowsInOrder()
        {
            var rows = new List<LaunchTargets.Entry>
            {
                new LaunchTargets.Entry("a.exe", @"C:\Apps\a", true),
                new LaunchTargets.Entry("b.exe", @"C:\Apps\b", false),
                new LaunchTargets.Entry("c.exe", @"C:\Apps\c", true),
            };

            var targets = LaunchTargets.Filter(rows);

            Assert.That(targets.Count, Is.EqualTo(2));
            Assert.That(targets[0].Name, Is.EqualTo("a.exe"));
            Assert.That(targets[1].Name, Is.EqualTo("c.exe"));
        }

        [Test]
        public void Filter_DropsRowsWithoutNameOrPath()
        {
            var rows = new List<LaunchTargets.Entry>
            {
                new LaunchTargets.Entry("", @"C:\Apps\a", true),
                new LaunchTargets.Entry("b.exe", "", true),
                new LaunchTargets.Entry("   ", @"C:\Apps\c", true),
                new LaunchTargets.Entry("d.exe", @"C:\Apps\d", true),
            };

            var targets = LaunchTargets.Filter(rows);

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].Name, Is.EqualTo("d.exe"));
        }

        [Test]
        public void Filter_NullList_ReturnsEmpty()
        {
            Assert.That(LaunchTargets.Filter(null), Is.Empty);
        }

        [Test]
        public void Filter_DisabledRow_IsNeverRelaunched()
        {
            var rows = new List<LaunchTargets.Entry>
            {
                new LaunchTargets.Entry("notepad.exe", @"C:\Apps", false),
            };

            Assert.That(LaunchTargets.Filter(rows), Is.Empty);
        }

        [Test]
        public void Filter_AllEnabled_KeepsEveryCompleteRow()
        {
            var rows = new List<LaunchTargets.Entry>
            {
                new LaunchTargets.Entry("a.exe", @"C:\A", true),
                new LaunchTargets.Entry("b.exe", @"C:\B", true),
            };

            Assert.That(LaunchTargets.Filter(rows).Count, Is.EqualTo(2));
        }
    }
}
