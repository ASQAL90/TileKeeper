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
        public void Resolve_MirroredRows_ArrangeSwitchStopsPositioning()
        {
            // Mirror-on-add gives both rows the same identity key. The Arrange row owns the
            // positioning switch, so disabling it must win over the enabled Keep Running twin.
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, false, true),
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, true, false),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe (host:1)" }, entries);

            Assert.That(resolved.Programs, Is.Empty);
            Assert.That(resolved.KeptIndexes, Is.Empty);
        }

        [Test]
        public void Resolve_MirroredRows_ArrangeSwitchEnablesPositioning()
        {
            // Disabling relaunch on the Keep Running row must not disturb positioning.
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, false, false),
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", true, true, true),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe (host:1)" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "viewer.exe" }));
            Assert.That(resolved.MatchLargest, Is.EqualTo(new[] { true }),
                "the Arrange row's Match largest setting applies");
        }

        [Test]
        public void Resolve_LegacyKeyWithMirroredRows_UsesArrangeSwitch()
        {
            // Both rows already carry the current identity key; only the stored config key
            // is old (bare process name). The disabled Arrange row must still win.
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, false, true),
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, true, false),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe" }, entries);

            Assert.That(resolved.Programs, Is.Empty,
                "a stale bare-name key must still honour the disabled Arrange row");
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
        public void Resolve_BareKeyMatchingLaunchRow_IsNotHijackedByArrangeRow()
        {
            // A launch row with no args has the bare name as its identity key. That exact
            // match is a different identity from "viewer.exe (host:1)", so it keeps its own
            // (launch-row) positioning even when the args-carrying Arrange row is disabled.
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe", "viewer.exe", "", false, false, false),
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, true, false),
            };

            var resolved = PositionTargets.Resolve(new[] { "viewer.exe" }, entries);

            Assert.That(resolved.Programs, Is.EqualTo(new[] { "viewer.exe" }));
            Assert.That(resolved.Args, Is.EqualTo(new[] { "" }));
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
        public void BuildRemovalAliases_BareKey_IncludesLegacyBareName()
        {
            var aliases = ProgramCatalog.BuildRemovalAliases("viewer.exe", "viewer.exe", "viewer.exe", "");

            Assert.That(aliases, Does.Contain("viewer.exe"));
            Assert.That(aliases, Does.Contain("viewer.exe (no args)"));
        }

        [Test]
        public void BuildRemovalAliases_DisambiguatedKey_DoesNotIncludeBareName()
        {
            var aliases = ProgramCatalog.BuildRemovalAliases(
                "viewer.exe (host:1)", "viewer.exe (host:1)", "viewer.exe", "host:1");

            Assert.That(aliases, Does.Contain("viewer.exe (host:1)"));
            Assert.That(aliases, Does.Not.Contain("viewer.exe"));
        }

        [Test]
        public void BuildRemovalAliases_QuotedArguments_KeepExactText()
        {
            var aliases = ProgramCatalog.BuildRemovalAliases(
                "app.exe (\"C:\\Program Files\\x\")", "app.exe (\"C:\\Program Files\\x\")",
                "app.exe", "\"C:\\Program Files\\x\"");

            Assert.That(aliases, Does.Contain("app.exe (\"C:\\Program Files\\x\")"));
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

        [Test]
        public void Resolve_ById_DistinguishesRowsWithIdenticalNames()
        {
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, false, true, "id-a"),
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, true, true, "id-b"),
            };

            var resolved = PositionTargets.Resolve(new[] { "id-b" }, entries);

            Assert.That(resolved.MatchedIds, Is.EqualTo(new[] { "id-b" }));
            Assert.That(resolved.Programs, Is.EqualTo(new[] { "viewer.exe" }));
        }

        [Test]
        public void Catalog_SurvivingDuplicateKeepsItsIdAfterRemoval()
        {
            var catalog = new ProgramCatalog();
            var first = catalog.Add("viewer.exe host:1", "", false, false, true);
            var second = catalog.Add("viewer.exe host:1", "", true, false, true);
            catalog.Rekey();
            string removedId = first.Id;
            string survivingId = second.Id;

            catalog.RemoveIds(new[] { removedId });

            Assert.That(catalog.GetById(removedId), Is.Null);
            Assert.That(catalog.GetById(survivingId), Is.SameAs(second));
            Assert.That(second.Id, Is.EqualTo(survivingId));
        }

        [Test]
        public void MigrateTokens_LegacyKeyToId_PreservesUnknownTokenAndExistingId()
        {
            var entries = new List<PositionTargets.Entry>
            {
                new PositionTargets.Entry("viewer.exe (host:1)", "viewer.exe", "host:1", false, false, true, "id-a"),
                new PositionTargets.Entry("viewer.exe (host:2)", "viewer.exe", "host:2", false, true, true, "id-b"),
            };
            var known = new HashSet<string> { "id-a", "id-b" };

            var migrated = ProgramCatalog.MigrateTokens(
                new[] { "viewer.exe (host:1)", "legacy.exe", "id-b" }, entries, known);

            Assert.That(migrated, Is.EqualTo(new[] { "id-a", "legacy.exe", "id-b" }));
        }

        [Test]
        public void RemoveIdsFromTokens_ClearsOnlyRemovedRowAndKeepsSlotsAligned()
        {
            ProgramCatalog.RemoveIdsFromTokens(
                new[] { "id-a", "legacy.exe", "id-b" },
                new HashSet<string> { "id-a" },
                new[] { 0, 1, 2 },
                out List<string> kept, out List<int> slots);

            Assert.That(kept, Is.EqualTo(new[] { "legacy.exe", "id-b" }));
            Assert.That(slots, Is.EqualTo(new[] { 1, 2 }));
        }
    }
}
