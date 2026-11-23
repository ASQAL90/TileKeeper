using System;
using System.Collections.Generic;

namespace TileKeeper.Class
{
    /// <summary>
    /// Parses and writes the comma-separated enabled flags stored next to a program list.
    /// A missing or short list defaults to enabled, so configs written by older versions
    /// keep working without a migration step.
    /// </summary>
    internal static class ProgramEnableFlags
    {
        /// <summary>Returns one flag per row; missing entries and non-zero values are enabled.</summary>
        public static bool[] Parse(string csv, int count)
        {
            var result = new bool[Math.Max(0, count)];
            if (string.IsNullOrWhiteSpace(csv))
            {
                for (int i = 0; i < result.Length; i++)
                    result[i] = true;
                return result;
            }

            string[] parts = csv.Split(',');
            for (int i = 0; i < result.Length; i++)
            {
                if (i >= parts.Length)
                {
                    result[i] = true;
                    continue;
                }

                string value = (parts[i] ?? "").Trim();
                result[i] = value.Length == 0
                    || value == "1"
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("on", StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        /// <summary>Serializes flags as "1"/"0" so the config stays human-readable.</summary>
        public static string Serialize(IEnumerable<bool> flags)
        {
            var parts = new List<string>();
            if (flags != null)
                foreach (bool enabled in flags)
                    parts.Add(enabled ? "1" : "0");
            return string.Join(",", parts);
        }
    }
}
