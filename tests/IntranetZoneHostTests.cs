using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Characterization tests for the opt-in SmartScreen/Intranet Zone exemption.
    /// Extraction must be strict (HTTP(S) + .application only) and the host gate must
    /// accept private/intranet addresses while refusing public ones.
    /// </summary>
    [TestFixture]
    public class IntranetZoneHostTests
    {
        [TestCase(
            "rundll32.exe dfshim.dll,ShOpenVerbApplication http://10.0.0.1/App/App.application",
            "10.0.0.1")]
        [TestCase(
            "C:\\Windows\\iexplore.exe \"https://myserver/App/App.application\"",
            "myserver")]
        [TestCase(
            "rundll32.exe dfshim.dll,ShOpenVerbApplication https://Server.Corp/App/App.application#frag",
            "server.corp")]
        [TestCase(
            "rundll32.exe dfshim.dll,ShOpenVerbApplication http://[fe80::1]/App/App.application",
            "fe80::1")]
        public void ExtractClickOnceHost_AcceptsStrictApplicationUrls(string command, string expected)
        {
            Assert.That(RemoteDesktopManager.ExtractClickOnceHost(command), Is.EqualTo(expected));
        }

        [TestCase("C:\\Apps\\MyApp.exe")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("mstsc.exe C:\\Sessions\\server.rdp")]
        [TestCase("rundll32.exe dfshim.dll,ShOpenVerbApplication http://10.0.0.1/App/setup.exe")]
        [TestCase("rundll32.exe dfshim.dll,ShOpenVerbApplication ftp://10.0.0.1/App/App.application")]
        [TestCase("rundll32.exe dfshim.dll,ShOpenVerbApplication http://user:pass@10.0.0.1/App/App.application")]
        public void ExtractClickOnceHost_RejectsNonClickOnceOrUnsafeInput(string command)
        {
            Assert.That(RemoteDesktopManager.ExtractClickOnceHost(command), Is.Null);
        }

        [TestCase("10.1.2.3", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.255", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("192.168.1.10", true)]
        [TestCase("169.254.10.10", true)]
        [TestCase("127.0.0.1", true)]
        [TestCase("8.8.8.8", false)]
        [TestCase("::1", true)]
        [TestCase("fc00::1", true)]
        [TestCase("fd12:3456::1", true)]
        [TestCase("fe80::1", true)]
        [TestCase("2001:db8::1", false)]
        [TestCase("server", true)]
        [TestCase("server.local", true)]
        [TestCase("sub.example.local", true)]
        [TestCase("printer.corp", true)]
        [TestCase("example.com", false)]
        [TestCase("sub.example.com", false)]
        [TestCase("-bad", false)]
        [TestCase("", false)]
        public void IsPrivateOrIntranetHost_OnlyAllowsPrivateNamesAndAddresses(string host, bool expected)
        {
            Assert.That(RemoteDesktopManager.IsPrivateOrIntranetHost(host), Is.EqualTo(expected));
        }

        [TestCase("[fe80::1]", "fe80::1")]
        [TestCase("Server.Corp.", "server.corp")]
        [TestCase("  10.0.0.1  ", "10.0.0.1")]
        public void NormalizeHost_TrimsBracketsDotAndCase(string host, string expected)
        {
            Assert.That(RemoteDesktopManager.NormalizeHost(host), Is.EqualTo(expected));
        }
    }
}
