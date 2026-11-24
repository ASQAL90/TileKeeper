using System.Collections.Generic;

namespace TileKeeper.Class
{
    /// <summary>
    /// Selects the rows the Keep Running watchdog may relaunch. The Enable checkbox is a
    /// per-row switch for relaunching only — positioning has its own flag — so a disabled
    /// row is skipped here and nowhere else. Rows without a name or path are dropped too:
    /// <c>BuildFullPath</c> returns an empty command for them and they would only burn a
    /// cycle. Kept free of WinForms/config so the semantics can be unit-tested.
    /// </summary>
    public static class LaunchTargets
    {
        public sealed class Entry
        {
            public Entry(string name, string path, bool enabled)
            {
                Name = name ?? "";
                Path = path ?? "";
                Enabled = enabled;
            }

            public string Name { get; }
            public string Path { get; }
            public bool Enabled { get; }
        }

        /// <summary>Enabled, complete rows in their original order.</summary>
        public static List<Entry> Filter(IEnumerable<Entry> rows)
        {
            var result = new List<Entry>();
            if (rows == null)
                return result;

            foreach (var row in rows)
            {
                if (row == null || !row.Enabled)
                    continue;
                if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Path))
                    continue;
                result.Add(row);
            }

            return result;
        }
    }
}
