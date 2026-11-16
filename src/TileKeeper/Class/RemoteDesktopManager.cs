using Microsoft.Win32;
using System;
using System.Net;

namespace TileKeeper.Class
{
    /// <summary>
    /// Keeps ClickOnce launches prompt-free by registering each ClickOnce server in
    /// the per-user Internet Explorer Local Intranet Zone (SmartScreen exemption).
    ///
    /// The Intranet Zone writes go to HKCU and work for standard users too.
    /// </summary>
    public class RemoteDesktopManager
    {
        // ─────────────────────────────────────────────────────────────────────
        // SmartScreen suppression via Intranet Zone
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts the hostname or IP address from a stored program-path string that
        /// represents a ClickOnce launch command (dfshim or legacy iexplore format).
        ///
        /// Returns <c>null</c> for normal executables, RDP files, or any path that does
        /// not contain an HTTP URL pointing at a <c>.application</c> manifest.
        ///
        /// Examples:
        /// <code>
        /// "rundll32.exe dfshim.dll,ShOpenVerbApplication http://10.0.0.1/App/App.application"
        ///     → "10.0.0.1"
        ///
        /// "C:\...\iexplore.exe \"http://myserver/App/App.application\""
        ///     → "myserver"
        ///
        /// "C:\Apps\MyApp.exe"
        ///     → null
        /// </code>
        /// </summary>
        internal static string ExtractClickOnceHost(string programPath)
        {
            if (string.IsNullOrEmpty(programPath))
                return null;

            int httpIndex = programPath.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (httpIndex < 0)
                return null;

            // Strip trailing quotes, whitespace, and fragment identifiers
            string url = programPath.Substring(httpIndex).Trim().Trim('"').Split('#')[0].Trim();

            if (url.IndexOf(".application", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return null;

            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        /// <summary>
        /// Adds <paramref name="host"/> to the Windows Local Intranet Zone so that
        /// ClickOnce apps served from it are not flagged by Windows Defender SmartScreen.
        ///
        /// <para>
        /// <b>Why this works:</b> SmartScreen applies to executables downloaded from the
        /// Internet zone (Zone 3). Content from the Local Intranet zone (Zone 1) is
        /// exempt. Adding the ClickOnce server to the Intranet zone removes the Zone 3
        /// mark from downloaded app files, so SmartScreen never prompts.
        /// </para>
        ///
        /// <para>
        /// <b>Registry hive — HKCU, not HKLM:</b> Writing to <c>HKCU</c> (current user)
        /// requires no Administrator privileges and takes effect for this user only, which
        /// is exactly what is needed. <c>HKLM</c> (machine-wide) would need elevation and
        /// would break when the app runs as a standard user.
        /// </para>
        ///
        /// <para>
        /// <b>Registry structure (under HKCU):</b>
        /// <list type="bullet">
        ///   <item>
        ///     Hostname → <c>HKCU\...\ZoneMap\Domains\{host}</c> with <c>http = 1</c> and <c>https = 1</c>.
        ///   </item>
        ///   <item>
        ///     IP address → <c>HKCU\...\ZoneMap\Ranges\Range{N}</c> with <c>:Range = {ip}</c>,
        ///     <c>http = 1</c>, and <c>https = 1</c>. A new Range sub-key is created only if
        ///     the IP is not already registered.
        ///   </item>
        /// </list>
        /// </para>
        ///
        /// Works for both standard users and administrators — no elevation required.
        /// </summary>
        public static void AddToIntranetZone(string host)
        {
            if (string.IsNullOrEmpty(host))
                return;

            // HKCU — no admin privilege required; per-user zone trust is sufficient
            const string zoneMapBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap";

            try
            {
                if (IPAddress.TryParse(host, out _))
                    AddIpToIntranetZone(host, zoneMapBase);
                else
                    AddHostnameToIntranetZone(host, zoneMapBase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding '{host}' to Intranet Zone: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a hostname under <c>HKCU\...\ZoneMap\Domains</c> as Intranet (zone 1).
        /// Idempotent — overwrites if the key already exists.
        /// </summary>
        private static void AddHostnameToIntranetZone(string host, string zoneMapBase)
        {
            using (RegistryKey domainKey = Registry.CurrentUser.CreateSubKey(
                zoneMapBase + @"\Domains\" + host))
            {
                domainKey.SetValue("http",  1, RegistryValueKind.DWord); // 1 = Local Intranet
                domainKey.SetValue("https", 1, RegistryValueKind.DWord);
            }

            Console.WriteLine($"Added hostname '{host}' to Intranet Zone (HKCU).");
        }

        /// <summary>
        /// Registers an IP address under <c>HKCU\...\ZoneMap\Ranges</c> as Intranet (zone 1).
        /// Skips the write if the IP is already present in any existing Range sub-key.
        /// </summary>
        private static void AddIpToIntranetZone(string ip, string zoneMapBase)
        {
            string rangesPath = zoneMapBase + @"\Ranges";

            using (RegistryKey ranges = Registry.CurrentUser.OpenSubKey(rangesPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(rangesPath))
            {
                // Check whether this IP is already registered to avoid duplicate entries
                foreach (string subKeyName in ranges.GetSubKeyNames())
                {
                    using (RegistryKey existing = ranges.OpenSubKey(subKeyName))
                    {
                        if (existing?.GetValue(":Range") as string == ip)
                        {
                            Console.WriteLine($"IP '{ip}' is already in Intranet Zone (HKCU).");
                            return;
                        }
                    }
                }

                // Find the next available RangeN name
                int n = 1;
                while (ranges.OpenSubKey($"Range{n}") != null)
                    n++;

                using (RegistryKey rangeKey = ranges.CreateSubKey($"Range{n}"))
                {
                    rangeKey.SetValue(":Range", ip, RegistryValueKind.String);
                    rangeKey.SetValue("http",   1,  RegistryValueKind.DWord);
                    rangeKey.SetValue("https",  1,  RegistryValueKind.DWord);
                }
            }

            Console.WriteLine($"Added IP '{ip}' to Intranet Zone (HKCU).");
        }
    }
}
