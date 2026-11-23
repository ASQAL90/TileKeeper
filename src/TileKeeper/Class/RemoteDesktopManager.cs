using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Net;

namespace TileKeeper.Class
{
    /// <summary>
    /// Keeps ClickOnce launches prompt-free by registering each private/intranet
    /// ClickOnce server in the per-user Internet Explorer Local Intranet Zone
    /// (SmartScreen exemption).
    ///
    /// The Intranet Zone writes go to HKCU and work for standard users too.
    ///
    /// <para>
    /// <b>Opt-in:</b> nothing is written unless the user enables
    /// <c>EnableClickOnceIntranetZone</c> in the System tab. Only private/intranet
    /// hosts are accepted; public hostnames and public IP addresses are refused so
    /// this feature cannot quietly lower SmartScreen protection for an internet
    /// server.
    /// </para>
    /// </summary>
    public static class RemoteDesktopManager
    {
        // ─────────────────────────────────────────────────────────────────────
        // SmartScreen suppression via Intranet Zone
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Registered only by the explicit opt-in setting.</summary>
        internal const string EnableSettingKey = "EnableClickOnceIntranetZone";

        /// <summary>Comma-separated hosts this application added, used for safe cleanup.</summary>
        internal const string ManagedHostsSettingKey = "IntranetZoneManagedHosts";

        private const string ZoneMapBase =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap";

        /// <summary>
        /// Extracts the hostname or IP address from a stored program-path string that
        /// represents a ClickOnce launch command (dfshim or legacy iexplore format).
        ///
        /// Returns <c>null</c> for normal executables, RDP files, or any path that does
        /// not contain an HTTP(S) URL pointing at a <c>.application</c> manifest.
        /// </summary>
        internal static string ExtractClickOnceHost(string programPath)
        {
            if (string.IsNullOrWhiteSpace(programPath))
                return null;

            int start = IndexOfUrl(programPath);
            if (start < 0)
                return null;

            string rest = programPath.Substring(start).Trim();

            // The URL is one whitespace-delimited token; stop before trailing command
            // arguments or quotes so a malformed tail cannot change the parsed host.
            int end = rest.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            string url = (end >= 0 ? rest.Substring(0, end) : rest).Trim().Trim('"').Trim('\'');

            int fragment = url.IndexOf('#');
            if (fragment >= 0)
                url = url.Substring(0, fragment);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return null;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            // Credentials embedded in a URL are never a legitimate ClickOnce launch.
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return null;

            if (string.IsNullOrEmpty(uri.Host))
                return null;

            // Only ClickOnce deployment manifests are relevant to the Intranet Zone.
            if (!uri.AbsolutePath.EndsWith(".application", StringComparison.OrdinalIgnoreCase))
                return null;

            return NormalizeHost(uri.Host);
        }

        private static int IndexOfUrl(string value)
        {
            int http = value.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
            int https = value.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (http < 0) return https;
            if (https < 0) return http;
            return Math.Min(http, https);
        }

        /// <summary>Lowercase, without brackets or a trailing root dot.</summary>
        internal static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            string value = host.Trim().TrimEnd('.');
            if (value.Length > 2 && value[0] == '[' && value[value.Length - 1] == ']')
                value = value.Substring(1, value.Length - 2);

            return value.ToLowerInvariant();
        }

        /// <summary>
        /// True when <paramref name="host"/> belongs to a private/intranet network:
        /// loopback, RFC1918, link-local, IPv6 ULA/link-local, a single-label NetBIOS
        /// name, or a reserved private suffix such as <c>.local</c>/<c>.lan</c>.
        /// </summary>
        internal static bool IsPrivateOrIntranetHost(string host)
        {
            string value = NormalizeHost(host);
            if (value.Length == 0)
                return false;

            if (IPAddress.TryParse(value, out IPAddress address))
                return IsPrivateAddress(address);

            // Single-label hostname (no dot) is a NetBIOS/intranet name, never a public FQDN.
            if (value.IndexOf('.') < 0)
                return IsValidDnsLabel(value);

            string[] privateSuffixes =
            {
                ".local", ".lan", ".internal", ".intranet", ".corp",
                ".home.arpa", ".localdomain"
            };

            foreach (string suffix in privateSuffixes)
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    value.Length > suffix.Length)
                    return true;
            }

            return false;
        }

        private static bool IsValidDnsLabel(string label)
        {
            if (label.Length == 0 || label.Length > 63)
                return false;
            if (label[0] == '-' || label[label.Length - 1] == '-')
                return false;

            foreach (char c in label)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-'))
                    return false;
            }

            return true;
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || b[0] == 127;
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.Equals(IPAddress.IPv6Loopback))
                    return true;

                byte[] b = address.GetAddressBytes();
                // fc00::/7 (unique local) and fe80::/10 (link-local).
                return (b[0] & 0xFE) == 0xFC
                    || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
            }

            return false;
        }

        /// <summary>
        /// Adds <paramref name="host"/> to the Windows Local Intranet Zone so that
        /// ClickOnce apps served from it are not flagged by SmartScreen.
        ///
        /// Only private/intranet hosts are accepted. Returns <c>true</c> when the host
        /// was registered (or was already registered), <c>false</c> when it was refused
        /// or the write failed.
        ///
        /// <para>
        /// <b>Registry hive — HKCU, not HKLM:</b> writing to <c>HKCU</c> (current user)
        /// requires no Administrator privileges and takes effect for this user only.
        /// </para>
        /// </summary>
        public static bool AddToIntranetZone(string host)
        {
            string value = NormalizeHost(host);
            if (!IsPrivateOrIntranetHost(value))
            {
                Console.WriteLine($"Refused non-private host '{host}' (Intranet Zone unchanged).");
                return false;
            }

            try
            {
                if (IPAddress.TryParse(value, out IPAddress address))
                    AddIpToIntranetZone(address, ZoneMapBase);
                else
                    AddHostnameToIntranetZone(value, ZoneMapBase);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding '{value}' to Intranet Zone: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes the Intranet Zone entry for <paramref name="host"/> if this application
        /// could have created it. Only private/intranet hosts are touched, so a public
        /// domain entry that the user added manually is never deleted.
        /// Returns <c>true</c> when an entry was removed.
        /// </summary>
        public static bool RemoveFromIntranetZone(string host)
        {
            string value = NormalizeHost(host);
            if (!IsPrivateOrIntranetHost(value))
                return false;

            try
            {
                if (IPAddress.TryParse(value, out IPAddress address))
                    return RemoveIpFromIntranetZone(address);

                return RemoveHostnameFromIntranetZone(value, ZoneMapBase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing '{value}' from Intranet Zone: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes every given host. Returns the number of entries actually deleted.
        /// </summary>
        public static int RemoveFromIntranetZone(IEnumerable<string> hosts)
        {
            if (hosts == null)
                return 0;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int removed = 0;

            foreach (string host in hosts)
            {
                string value = NormalizeHost(host);
                if (value.Length == 0 || !seen.Add(value))
                    continue;
                if (RemoveFromIntranetZone(value))
                    removed++;
            }

            return removed;
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
                if (domainKey == null)
                    throw new InvalidOperationException("Registry key could not be created.");

                domainKey.SetValue("http", 1, RegistryValueKind.DWord); // 1 = Local Intranet
                domainKey.SetValue("https", 1, RegistryValueKind.DWord);
            }

            Console.WriteLine($"Added hostname '{host}' to Intranet Zone (HKCU).");
        }

        /// <summary>
        /// Registers an IP address under <c>HKCU\...\ZoneMap\Ranges</c> as Intranet (zone 1).
        /// Skips the write if the IP is already present in any existing Range sub-key.
        /// </summary>
        private static void AddIpToIntranetZone(IPAddress ip, string zoneMapBase)
        {
            string rangesPath = zoneMapBase + @"\Ranges";

            using (RegistryKey ranges = Registry.CurrentUser.OpenSubKey(rangesPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(rangesPath))
            {
                if (ranges == null)
                    throw new InvalidOperationException("ZoneMap Ranges key could not be created.");

                // Check whether this IP is already registered to avoid duplicate entries.
                foreach (string subKeyName in ranges.GetSubKeyNames())
                {
                    using (RegistryKey existing = ranges.OpenSubKey(subKeyName))
                    {
                        if (TryParseRangeValue(existing, out IPAddress existingIp) &&
                            existingIp.Equals(ip))
                        {
                            Console.WriteLine($"IP '{ip}' is already in Intranet Zone (HKCU).");
                            return;
                        }
                    }
                }

                // Find the next available RangeN name.
                int n = 1;
                while (ranges.OpenSubKey($"Range{n}") != null)
                    n++;

                using (RegistryKey rangeKey = ranges.CreateSubKey($"Range{n}"))
                {
                    if (rangeKey == null)
                        throw new InvalidOperationException("ZoneMap range key could not be created.");

                    rangeKey.SetValue(":Range", ip.ToString(), RegistryValueKind.String);
                    rangeKey.SetValue("http", 1, RegistryValueKind.DWord);
                    rangeKey.SetValue("https", 1, RegistryValueKind.DWord);
                }
            }

            Console.WriteLine($"Added IP '{ip}' to Intranet Zone (HKCU).");
        }

        private static bool RemoveHostnameFromIntranetZone(string host, string zoneMapBase)
        {
            string path = zoneMapBase + @"\Domains\" + host;

            using (RegistryKey existing = Registry.CurrentUser.OpenSubKey(path))
            {
                if (existing == null)
                    return false;
            }

            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            Console.WriteLine($"Removed hostname '{host}' from Intranet Zone (HKCU).");
            return true;
        }

        private static bool RemoveIpFromIntranetZone(IPAddress ip)
        {
            string rangesPath = ZoneMapBase + @"\Ranges";

            using (RegistryKey ranges = Registry.CurrentUser.OpenSubKey(rangesPath, writable: true))
            {
                if (ranges == null)
                    return false;

                var matches = new List<string>();
                foreach (string subKeyName in ranges.GetSubKeyNames())
                {
                    using (RegistryKey existing = ranges.OpenSubKey(subKeyName))
                    {
                        if (TryParseRangeValue(existing, out IPAddress existingIp) &&
                            existingIp.Equals(ip))
                            matches.Add(subKeyName);
                    }
                }

                foreach (string name in matches)
                    ranges.DeleteSubKeyTree(name, throwOnMissingSubKey: false);

                if (matches.Count == 0)
                    return false;

                Console.WriteLine($"Removed IP '{ip}' from Intranet Zone (HKCU).");
                return true;
            }
        }

        private static bool TryParseRangeValue(RegistryKey key, out IPAddress address)
        {
            address = null;
            if (key == null)
                return false;

            string raw = key.GetValue(":Range") as string;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            return IPAddress.TryParse(raw.Trim(), out address);
        }
    }
}
