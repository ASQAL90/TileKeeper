using System;
using System.Collections.Generic;
using System.Linq;

namespace TileKeeper.Class
{
    /// <summary>
    /// One row of either program list. <see cref="Id"/> is a persistent identity created once
    /// when the row first appears; it is saved in app.config and used by Screen1-4, slot pins and
    /// Remove. <see cref="Key"/> is only a human/legacy process identity kept for the position
    /// resolver and for migrating configs written before ids existed — it is deliberately never
    /// used to store screen assignments, because it changes when duplicate rows are added/removed.
    /// </summary>
    public sealed class ProgramEntry
    {
        public string Id;
        public string Name;
        public string Path;
        public string BaseName;
        public string BaseArgs;
        public string Key;
        public bool MatchLargestWindow;
        public bool PositionOnly;
        public bool Enabled;

        public ProgramEntry(string name, string path, bool matchLargestWindow = false,
                            bool positionOnly = false, bool enabled = true, string id = null)
        {
            Name = name ?? "";
            Path = path ?? "";
            Id = string.IsNullOrWhiteSpace(id) ? NewId() : id;
            ParseNameParts(Name, out BaseName, out BaseArgs);
            Key = BaseName;
            MatchLargestWindow = matchLargestWindow;
            PositionOnly = positionOnly;
            Enabled = enabled;
        }

        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>Splits a raw Programs-tab "Name" (e.g. "app.exe arg1") into exe base and args.</summary>
        public static void ParseNameParts(string raw, out string baseName, out string baseArgs)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                baseName = "";
                baseArgs = "";
                return;
            }

            int sp = raw.IndexOf(' ');
            baseName = System.IO.Path.GetFileNameWithoutExtension(sp < 0 ? raw : raw.Substring(0, sp));
            baseArgs = sp < 0 ? "" : raw.Substring(sp + 1).Trim();
        }
    }

    /// <summary>
    /// The in-memory identity catalog. Both UI lists and the position keeper refer to rows by
    /// <see cref="ProgramEntry.Id"/>. Re-keying is done once over the whole catalog so a row's
    /// display/legacy key never depends on which list happens to be read at the time.
    /// </summary>
    public sealed class ProgramCatalog
    {
        private readonly List<ProgramEntry> _programs = new List<ProgramEntry>();
        private readonly List<ProgramEntry> _positionOnly = new List<ProgramEntry>();

        public IList<ProgramEntry> Programs => _programs;
        public IList<ProgramEntry> PositionOnly => _positionOnly;
        public IEnumerable<ProgramEntry> All => _programs.Concat(_positionOnly);

        public void Clear()
        {
            _programs.Clear();
            _positionOnly.Clear();
        }

        public ProgramEntry Add(string name, string path, bool positionOnly,
                                bool matchLargestWindow, bool enabled, string id = null)
        {
            var entry = new ProgramEntry(name, path, matchLargestWindow, positionOnly, enabled, id);
            (positionOnly ? _positionOnly : _programs).Add(entry);
            return entry;
        }

        public ProgramEntry GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public void RemoveIds(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? new string[0], StringComparer.OrdinalIgnoreCase);
            _programs.RemoveAll(e => set.Contains(e.Id));
            _positionOnly.RemoveAll(e => set.Contains(e.Id));
        }

        /// <summary>Computes legacy display/process keys once over the combined catalog.</summary>
        public void Rekey()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in All)
                if (!string.IsNullOrWhiteSpace(e.BaseName))
                    counts[e.BaseName] = counts.TryGetValue(e.BaseName, out int c) ? c + 1 : 1;

            foreach (var e in All)
            {
                if (string.IsNullOrWhiteSpace(e.BaseName))
                    continue;
                e.Key = counts[e.BaseName] > 1
                    ? $"{e.BaseName} ({(e.BaseArgs.Length > 0 ? e.BaseArgs : "no args")})"
                    : e.BaseName;
            }
        }

        public static string[] SplitCsv(string csv) =>
            (csv ?? "").Split(',')
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToArray();

        public static string JoinIds(IEnumerable<ProgramEntry> entries) =>
            string.Join(",", (entries ?? Enumerable.Empty<ProgramEntry>()).Select(e => e.Id));

        /// <summary>
        /// Translates legacy ScreenN tokens (bare name, "Base (args)" key, or already an id)
        /// into persistent ids. Unknown tokens are preserved so a stale assignment is never
        /// silently dropped. Slots stay positionally parallel.
        /// </summary>
        public static List<string> MigrateTokens(IList<string> tokens,
                                                 IList<PositionTargets.Entry> targets,
                                                 ISet<string> knownIds)
        {
            var result = new List<string>();
            foreach (string token in tokens ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(token) && knownIds != null && knownIds.Contains(token))
                {
                    result.Add(token);
                    continue;
                }

                string id = "";
                var resolved = PositionTargets.Resolve(new[] { token ?? "" }, targets);
                if (resolved.MatchedIds.Count > 0)
                    id = resolved.MatchedIds[0];
                result.Add(string.IsNullOrWhiteSpace(id) ? token : id);
            }
            return result;
        }

        /// <summary>
        /// Rewrites tokens through <paramref name="remap"/> while preserving order, duplicates
        /// and every token that has no mapping. Used once when a screen assignment still points
        /// at a Keep Running row id and must follow that row's Arrange mirror instead.
        /// </summary>
        public static List<string> RemapTokens(IList<string> tokens, IDictionary<string, string> remap)
        {
            var result = new List<string>();
            foreach (string token in tokens ?? new string[0])
            {
                string mapped;
                result.Add(!string.IsNullOrWhiteSpace(token) && remap != null
                           && remap.TryGetValue(token, out mapped)
                           && !string.IsNullOrWhiteSpace(mapped)
                    ? mapped
                    : token);
            }
            return result;
        }

        /// <summary>Removes ids from a screen token list, keeping slot values positionally aligned.</summary>
        public static void RemoveIdsFromTokens(IList<string> tokens, ISet<string> removedIds,
                                               IList<int> slots,
                                               out List<string> keptTokens, out List<int> keptSlots)
        {
            keptTokens = new List<string>();
            keptSlots = new List<int>();
            for (int i = 0; i < (tokens?.Count ?? 0); i++)
            {
                string token = tokens[i];
                if (removedIds != null && removedIds.Contains(token))
                    continue;
                keptTokens.Add(token);
                keptSlots.Add(slots != null && i < slots.Count ? slots[i] : -1);
            }
        }
    }
}
