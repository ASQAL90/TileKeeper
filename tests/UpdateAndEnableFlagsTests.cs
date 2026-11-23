using System;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Characterization tests for the per-row Enable flags and the read-only GitHub
    /// release check. The updater must never accept an arbitrary link from an API response.
    /// </summary>
    [TestFixture]
    public class UpdateAndEnableFlagsTests
    {
        [Test]
        public void EnableFlags_MissingConfig_DefaultsEverythingToEnabled()
        {
            bool[] flags = ProgramEnableFlags.Parse(null, 3);
            Assert.That(flags, Is.EqualTo(new[] { true, true, true }));
        }

        [Test]
        public void EnableFlags_ParsesZerosAndShortLists()
        {
            bool[] flags = ProgramEnableFlags.Parse("1,0,false", 4);
            Assert.That(flags, Is.EqualTo(new[] { true, false, false, true }));
        }

        [Test]
        public void EnableFlags_RoundTripsThroughSerialize()
        {
            bool[] flags = { true, false, true };
            string csv = ProgramEnableFlags.Serialize(flags);
            Assert.That(csv, Is.EqualTo("1,0,1"));
            Assert.That(ProgramEnableFlags.Parse(csv, flags.Length), Is.EqualTo(flags));
        }

        [Test]
        public void TryParseLatestRelease_ReadsVersionAndReleasePage()
        {
            const string json = "{\"tag_name\":\"v1.2.3\"," +
                                "\"name\":\"TileKeeper 1.2.3\"," +
                                "\"body\":\"Release notes here\"," +
                                "\"html_url\":\"https://github.com/ASQAL90/TileKeeper/releases/tag/v1.2.3\"}";

            Assert.That(UpdateChecker.TryParseLatestRelease(json, out UpdateChecker.ReleaseInfo info, out string error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(info.Version, Is.EqualTo("v1.2.3"));
            Assert.That(info.Notes, Is.EqualTo("Release notes here"));
            Assert.That(info.ReleaseUrl, Is.EqualTo("https://github.com/ASQAL90/TileKeeper/releases/tag/v1.2.3"));
        }

        [Test]
        public void TryParseLatestRelease_RejectsUnsafeOrIncompletePayload()
        {
            Assert.That(UpdateChecker.TryParseLatestRelease("", out _, out string error), Is.False);
            Assert.That(error, Does.Contain("empty"));

            string noTag = "{\"html_url\":\"https://github.com/ASQAL90/TileKeeper/releases/tag/v1\"}";
            Assert.That(UpdateChecker.TryParseLatestRelease(noTag, out _, out error), Is.False);
            Assert.That(error, Does.Contain("version"));

            string badUrl = "{\"tag_name\":\"v1\",\"html_url\":\"https://evil.example.com/asqal90/tilekeeper\"}";
            Assert.That(UpdateChecker.TryParseLatestRelease(badUrl, out _, out error), Is.False);
            Assert.That(error, Does.Contain("unsafe"));
        }

        [TestCase("https://github.com/ASQAL90/TileKeeper/releases/latest", true)]
        [TestCase("https://github.com/ASQAL90/TileKeeper/releases/tag/v1.0.0", true)]
        [TestCase("http://github.com/ASQAL90/TileKeeper/releases/latest", false)]
        [TestCase("https://evil.example.com/ASQAL90/TileKeeper/releases/latest", false)]
        [TestCase("https://github.com.evil.example/ASQAL90/TileKeeper/releases/latest", false)]
        [TestCase("https://github.com/ASQAL90/TileKeeper/issues/1", false)]
        [TestCase("", false)]
        public void IsAllowedReleaseUrl_OnlyAcceptsHttpsGithubReleases(string url, bool expected)
        {
            Assert.That(UpdateChecker.IsAllowedReleaseUrl(url), Is.EqualTo(expected));
        }

        [TestCase("1.0.0.1", "v1.0.1", true)]
        [TestCase("1.0.0.1", "v1.0.0.1", false)]
        [TestCase("1.2.0.0", "v1.1.9", false)]
        [TestCase("1.2.0.0", "v1.2.0-beta.1", false)]
        public void IsNewerVersion_ComparesReleaseTagsWithProductVersion(string current, string tag, bool expected)
        {
            Assert.That(UpdateChecker.IsNewerVersion(current, tag), Is.EqualTo(expected));
        }
    }
}
