using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace TileKeeper.Class
{
    /// <summary>
    /// Read-only update check against the public GitHub Releases API.
    /// The application never downloads or replaces its own executable; it only reports
    /// whether a newer release exists and exposes the validated GitHub release page.
    /// </summary>
    internal static class UpdateChecker
    {
        internal const string ReleasesPage = "https://github.com/ASQAL90/TileKeeper/releases/latest";
        internal const string LatestReleaseApi = "https://api.github.com/repos/ASQAL90/TileKeeper/releases/latest";

        internal sealed class ReleaseInfo
        {
            public string Version;
            public string Title;
            public string Notes;
            public string ReleaseUrl;
        }

        /// <summary>
        /// Parses the small subset of the GitHub release JSON that the UI needs.
        /// Returns false (with a user-safe error) when the payload is malformed or the
        /// release URL is not an HTTPS github.com release page.
        /// </summary>
        internal static bool TryParseLatestRelease(string json, out ReleaseInfo info, out string error)
        {
            info = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The update server returned an empty response.";
                return false;
            }

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception)
            {
                error = "The update server returned an unexpected response.";
                return false;
            }

            string tag = GetString(root, "tag_name");
            string url = GetString(root, "html_url");
            if (string.IsNullOrWhiteSpace(tag))
            {
                error = "The update information does not contain a version.";
                return false;
            }
            if (!IsAllowedReleaseUrl(url))
            {
                error = "The update information contains an unsafe release link.";
                return false;
            }

            info = new ReleaseInfo
            {
                Version = tag.Trim(),
                Title = GetString(root, "name"),
                Notes = GetString(root, "body"),
                ReleaseUrl = url
            };
            return true;
        }

        /// <summary>
        /// True only for a plain HTTPS github.com URL under this repository's releases path.
        /// This keeps a compromised or spoofed API response from sending the user elsewhere.
        /// </summary>
        internal static bool IsAllowedReleaseUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttps)
                return false;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return false;
            return uri.AbsolutePath.StartsWith("/ASQAL90/TileKeeper/releases", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Compares a release tag such as "v1.0.1" with the running product version.</summary>
        internal static bool IsNewerVersion(string currentVersion, string releaseTag)
        {
            Version current;
            Version latest;
            if (!TryParseVersion(currentVersion, out current) || !TryParseVersion(releaseTag, out latest))
                return false;
            return latest > current;
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(1);

            // Ignore pre-release/build suffixes such as "1.2.3-beta.1".
            int cut = trimmed.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
                trimmed = trimmed.Substring(0, cut);

            return Version.TryParse(trimmed, out version);
        }

        private static string GetString(Dictionary<string, object> root, string key)
        {
            object value;
            return root != null && root.TryGetValue(key, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : "";
        }
    }
}
