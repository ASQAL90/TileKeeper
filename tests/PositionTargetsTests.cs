using System.Collections.Generic;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// The position keeper must resolve a screen's stored key back to the right process even
    /// when the key predates the current list (bare exe name) or two shortcuts share one exe.
    /// A disabled Arrange row must never be positioned, while a launch row's Enable switch
    /// (which belongs to the relaunch watchdog) must not suppress positioning.
    /// </summary>
    [TestFixture]
    public class PositionTargetsTests
    {
        private static PositionTargets.Entry Launch(string name, string args, bool enabled = true) =>
            new PositionTargets.Entry(name, name, args, false, false, enabled);

        private static PositionTargets.Entry Arrange(string name, string args, bool enabled = true,
                                                     bool matchLargest = false) =>
            new PositionTargets.Entry(
                args.Length > 0 ? $"{name} ({args})" : name, name, args, matchLargest, true, enabled);

        [Test]
        public void Resolve_ExactKey_ReturnsProcessAndFlags()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Launch("notepad.exe", ""),
                Arrange("viewer.exe", "192.0.2.10:5900", matchLargest: true),
            };

            var resolved = PositionTargets.Resolve(
                new[] { "viewer.exe (192.0.2.10:5900)" }, entries);

            Assert.That(resolved.KeptIndexes, Is.EqualTo(new[] { 0 }));
            Assert.That(resolved.Programs, Is.EqualTo(new[] { "viewer.exe" }));
            Assert.That(resolved.Args, Is.EqualTo(new[] { "192.0.2.10:5900" }));
            Assert.That(resolved.MatchLargest, Is.EqualTo(new[] { true }));
        }

        [Test]
        public void Resolve_LegacyBareName_StillFindsRowThatNowHasArgs()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Launch("viewer.exe", "192.0.2.10:5900"),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "viewer.exe" }));
            Assert.That(resolved.Args, Is.EqualTo(new[] { "192.0.2.10:5900" }),
                "old config stored the bare process name; args must still be applied");
        }

        [Test]
        public void Resolve_DuplicateExe_MatchesDisambiguatedKey()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Launch("viewer.exe", "192.0.2.10:5900"),
                Launch("viewer.exe", "192.0.2.11:5900"),
            };

            var resolved = PositionTargets.Resolve(
                new[] { "viewer.exe (192.0.2.11:5900)" }, entries);

            Assert.That(resolved.Args, Is.EqualTo(new[] { "192.0.2.11:5900" }));
        }

        [Test]
        public void Resolve_NoArgsShape_MatchesArgsLessEntry()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Launch("app.exe", ""),
                Launch("app.exe", "secondary"),
            };

            var resolved = PositionTargets.Resolve(new[] { "app.exe (no args)" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "app.exe" }));
            Assert.That(resolved.Args, Is.EqualTo(new[] { "" }));
        }

        [Test]
        public void Resolve_DisabledArrangeRow_IsSkipped()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Arrange("viewer.exe", "192.0.2.10:5900", enabled: false),
                Launch("notepad.exe", ""),
            };

            var resolved = PositionTargets.Resolve(
                new[] { "viewer.exe (192.0.2.10:5900)", "notepad.exe" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "notepad.exe" }));
            Assert.That(resolved.KeptIndexes, Is.EqualTo(new[] { 1 }),
                "slot indexes must still refer to the original key positions");
        }

        [Test]
        public void Resolve_DisabledArrangeRow_CannotSlipThroughLegacyMatch()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Arrange("viewer.exe", "192.0.2.10:5900", enabled: false),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe" }, entries);

            Assert.That(resolved.Programs, Is.Empty,
                "a stale bare-name key must not resurrect a disabled Arrange row");
            Assert.That(resolved.KeptIndexes, Is.Empty);
        }

        [Test]
        public void Resolve_DisabledLaunchRow_StillGetsPositioned()
        {
            var entries = new List<PositionTargets.Entry>
            {
                Launch("notepad.exe", "", enabled: false),
            };

            var resolved = PositionTargets.Resolve(new[] { "notepad.exe" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "notepad.exe" }),
                "Enable on a Keep Running row only controls relaunch, never positioning");
        }

        [Test]
        public void Resolve_UnknownKey_FallsBackToBareProcessName()
        {
            var resolved = PositionTargets.Resolve(
                new[] { "legacy.exe" }, new List<PositionTargets.Entry>());

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "legacy.exe" }));
            Assert.That(resolved.Args, Is.EqualTo(new[] { "" }));
            Assert.That(resolved.MatchLargest, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void Resolve_EmptyKeys_ReturnsEmptyLists()
        {
            var resolved = PositionTargets.Resolve(null, new List<PositionTargets.Entry>());

            Assert.That(resolved.Programs, Is.Empty);
            Assert.That(resolved.Args, Is.Empty);
            Assert.That(resolved.MatchLargest, Is.Empty);
            Assert.That(resolved.KeptIndexes, Is.Empty);
        }
    }
}
