using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Characterization tests for .rdp (Remote Desktop) program rows.
    ///
    /// mstsc.exe takes the .rdp path as its single argument, and the stored program "Name"
    /// doubles as the identity that keeps concurrent mstsc sessions apart (the Position tab
    /// shows one mstsc row per .rdp file, and Auto Position filters each session by the
    /// command-line tail stored in Name). The path therefore has to be quoted, or any .rdp
    /// living under a path with spaces (e.g. "C:\Remote Sessions\Desktop\...") launches
    /// as several broken arguments.
    /// </summary>
    [TestFixture]
    public class RdpLaunchTests
    {
        private const string RdpPath = @"C:\Remote Sessions\Desktop\192.0.2.66.rdp";

        [Test]
        public void ParseNameParts_RdpName_YieldsMstscAndQuotedPathAsArgs()
        {
            TileKeeperForm.ParseNameParts(
                "mstsc.exe \"" + RdpPath + "\"", out string baseName, out string baseArgs);

            Assert.That(baseName, Is.EqualTo("mstsc"));
            Assert.That(baseArgs, Is.EqualTo("\"" + RdpPath + "\""));
        }

        [Test]
        public void BuildFullPath_LegacyRdpRow_QuotesThePathStoredInPathColumn()
        {
            string full = TileKeeperForm.BuildFullPath("mstsc.exe", RdpPath);

            Assert.That(full, Is.EqualTo("mstsc.exe \"" + RdpPath + "\""));
        }

        [Test]
        public void BuildFullPath_NewRdpRow_EmbedsQuotedPathExactlyOnce()
        {
            string name = "mstsc.exe \"" + RdpPath + "\"";

            string full = TileKeeperForm.BuildFullPath(name, RdpPath);

            Assert.That(full, Is.EqualTo("mstsc.exe \"" + RdpPath + "\""));
        }

        /// <summary>
        /// The per-file identity only holds if the stored args really do select one mstsc
        /// session and reject its siblings — otherwise Auto Position would tile the wrong
        /// remote session (or the same one twice) when several .rdp windows are open.
        /// </summary>
        [Test]
        public void CommandLineMatching_DistinguishesConcurrentRdpSessions()
        {
            const string session66 = "\"C:\\Windows\\system32\\mstsc.exe\" \"C:\\Remote Sessions\\Desktop\\192.0.2.66.rdp\"";
            const string session72 = "\"C:\\Windows\\system32\\mstsc.exe\" \"C:\\Remote Sessions\\Desktop\\192.0.2.72.rdp\"";
            string args66 = "\"C:\\Remote Sessions\\Desktop\\192.0.2.66.rdp\"";
            string args72 = "\"C:\\Remote Sessions\\Desktop\\192.0.2.72.rdp\"";

            Assert.That(WindowMonitor.CommandLineMatchesArgs(session66, args66), Is.True);
            Assert.That(WindowMonitor.CommandLineMatchesArgs(session72, args72), Is.True);
            Assert.That(WindowMonitor.CommandLineMatchesArgs(session66, args72), Is.False);
            Assert.That(WindowMonitor.CommandLineMatchesArgs(session72, args66), Is.False);
        }
    }
}
