using System;
using System.Collections.Generic;

namespace TileKeeper.Class
{
    /// <summary>
    /// Resolves the identity keys stored in <c>ScreenN</c> to the process targets the
    /// position keeper hands to <see cref="WindowMonitor"/>. Kept free of WinForms and
    /// configuration so the matching rules can be unit-tested:
    ///
    /// <list type="number">
    /// <item>exact <see cref="Entry.Key"/> match;</item>
    /// <item>the <c>BaseName (args)</c> / <c>BaseName (no args)</c> key shape emitted by
    ///       <c>AssignIdentityKeys</c> when one exe appears twice;</item>
    /// <item>legacy config that stored the bare process name before args were split out;</item>
    /// <item>no match at all → treat the key itself as a bare process name (historic fallback).</item>
    /// </list>
    ///
    /// A position-only entry whose Enable box is off is never returned, whichever path
    /// matched it. When a mirrored row exists in both lists (same identity key), the Arrange
    /// row wins the match because its Enable box — not the Keep Running one — controls
    /// positioning. Launching entries keep their own Enable switch for the watchdog, so their
    /// flag deliberately does not affect positioning.
    /// </summary>
    public static class PositionTargets
    {
        /// <summary>One positionable program, mirroring the fields the form uses.</summary>
        public sealed class Entry
        {
            public Entry(string key, string baseName, string baseArgs,
                         bool matchLargestWindow, bool positionOnly, bool enabled, string id = null)
            {
                Key = key ?? "";
                BaseName = baseName ?? "";
                BaseArgs = baseArgs ?? "";
                MatchLargestWindow = matchLargestWindow;
                PositionOnly = positionOnly;
                Enabled = enabled;
                Id = id ?? "";
            }

            /// <summary>Persistent row identity. When a screen key is an id it resolves here first.</summary>
            public string Id { get; }

            public string Key { get; }
            public string BaseName { get; }
            public string BaseArgs { get; }
            public bool MatchLargestWindow { get; }
            public bool PositionOnly { get; }
            public bool Enabled { get; }
        }

        /// <summary>Parallel lists ready for <c>ScreenAssignment</c>.</summary>
        public sealed class Resolved
        {
            public List<string> Programs { get; } = new List<string>();
            public List<string> Args { get; } = new List<string>();
            public List<bool> MatchLargest { get; } = new List<bool>();
            public List<int> KeptIndexes { get; } = new List<int>();

            /// <summary>Persistent id of the row each key resolved to ("" for unknown legacy keys).</summary>
            public List<string> MatchedIds { get; } = new List<string>();
        }

        public static Resolved Resolve(IList<string> keys, IList<Entry> entries)
        {
            var resolved = new Resolved();
            if (keys == null)
                return resolved;

            var byId = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var byBase = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                        continue;

                    // Persistent ids are authoritative: two rows may share a name/args, but
                    // only one of them owns a given screen assignment.
                    if (!string.IsNullOrWhiteSpace(entry.Id) && !byId.ContainsKey(entry.Id))
                        byId[entry.Id] = entry;

                    if (!string.IsNullOrWhiteSpace(entry.Key))
                    {
                        // A mirrored row exists in both lists under one identity key. The
                        // Arrange row owns the positioning switch, so it must win the exact-key
                        // slot even when the Keep Running twin was inserted first.
                        if (!byKey.TryGetValue(entry.Key, out Entry existing)
                            || (!existing.PositionOnly && entry.PositionOnly))
                            byKey[entry.Key] = entry;
                    }

                    if (string.IsNullOrWhiteSpace(entry.BaseName))
                        continue;
                    if (!byBase.TryGetValue(entry.BaseName, out List<Entry> sameBase))
                        byBase[entry.BaseName] = sameBase = new List<Entry>();
                    sameBase.Add(entry);
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                Entry match = byId.TryGetValue(key, out Entry byIdentity) ? byIdentity
                                                                          : Find(byKey, byBase, key);

                // A legacy screen assignment may still carry a Keep Running id. Follow its
                // Arrange mirror so the positioning switch, not the launch switch, decides.
                if (match != null && !match.PositionOnly
                    && !string.IsNullOrWhiteSpace(match.Key)
                    && byKey.TryGetValue(match.Key, out Entry positionTwin)
                    && positionTwin.PositionOnly)
                    match = positionTwin;

                // A disabled Arrange row stays in the grid as an empty placeholder. Dropping it
                // shrinks the program count (4→3) and ComputeLayout stretched the remaining
                // tiles across the freed row; the cell must stay empty instead.
                if (match != null && match.PositionOnly && !match.Enabled)
                {
                    resolved.KeptIndexes.Add(i);
                    resolved.MatchedIds.Add(match.Id);
                    resolved.Programs.Add("");
                    resolved.Args.Add("");
                    resolved.MatchLargest.Add(false);
                    continue;
                }

                resolved.KeptIndexes.Add(i);
                resolved.MatchedIds.Add(match?.Id ?? "");
                if (match != null)
                {
                    resolved.Programs.Add(match.BaseName);
                    resolved.Args.Add(match.BaseArgs);
                    resolved.MatchLargest.Add(match.MatchLargestWindow);
                }
                else
                {
                    resolved.Programs.Add(key);
                    resolved.Args.Add("");
                    resolved.MatchLargest.Add(false);
                }
            }

            return resolved;
        }

        private static Entry Find(Dictionary<string, Entry> byKey,
                                  Dictionary<string, List<Entry>> byBase,
                                  string key)
        {
            if (byKey.TryGetValue(key, out Entry exact))
                return exact;

            // "BaseName (args)" / "BaseName (no args)" as produced by AssignIdentityKeys.
            int open = key.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && key.EndsWith(")", StringComparison.Ordinal))
            {
                string baseName = key.Substring(0, open);
                string args = key.Substring(open + 2, key.Length - open - 3);
                if (byBase.TryGetValue(baseName, out List<Entry> candidates))
                {
                    string wanted = args == "no args" ? "" : args;
                    Entry launch = null;
                    foreach (var candidate in candidates)
                    {
                        if (!string.Equals(candidate.BaseArgs, wanted, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (candidate.PositionOnly)
                            return candidate; // the Arrange switch governs positioning
                        if (launch == null)
                            launch = candidate;
                    }
                    if (launch != null)
                        return launch;
                }
            }

            // Legacy config stored the bare process name while the list now carries args
            // and therefore a disambiguated key. Prefer the Arrange row when one exists: its
            // Enable box is the positioning switch, and a disabled one must not be bypassed
            // by the mirrored Keep Running row.
            if (byBase.TryGetValue(key, out List<Entry> sameBase))
            {
                Entry launch = null;
                foreach (var candidate in sameBase)
                {
                    if (candidate.PositionOnly)
                        return candidate;
                    if (launch == null)
                        launch = candidate;
                }
                return launch;
            }

            return null;
        }
    }
}
