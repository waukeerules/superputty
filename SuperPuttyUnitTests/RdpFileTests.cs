using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// .rdp files (RdpFile): the one Connect external writes for mstsc with the session's options, and sessions read
    /// from one for File > Import > From .rdp files, as SuperXTerminal's external.rs and importing/rdp.rs
    /// </summary>
    [TestFixture]
    public class RdpFileTests
    {
        /// <summary>The session's address (port only when not 3389), username and options become .rdp settings</summary>
        [Test]
        public void WritesOptionsAsRdpSettings()
        {
            SessionData session = new SessionData { Host = "10.0.0.5", Port = 3390, Username = "me", Proto = ConnectionProtocol.RDP };
            RdpOptions options = RdpOptions.Parse("Display=Fixed;Width=1280;Height=800;Clipboard=0;Gateway=gw.example.com;ColorDepth=16");
            string file = RdpFile.Write(session, options);
            foreach (string line in new[] { "full address:s:10.0.0.5:3390", "username:s:me", "screen mode id:i:1", "desktopwidth:i:1280",
                "desktopheight:i:800", "redirectclipboard:i:0", "gatewayhostname:s:gw.example.com", "gatewayusagemethod:i:1", "session bpp:i:16" })
            {
                StringAssert.Contains(line + "\r\n", file, line);
            }
        }

        /// <summary>Remote Desktop's own port is left out of the address, and a fitted display asks for dynamic resolution</summary>
        [Test]
        public void DefaultPortAndFittedDisplay()
        {
            string file = RdpFile.Write(new SessionData { Host = "server1", Port = 3389 }, RdpOptions.Defaults());
            StringAssert.StartsWith("full address:s:server1\r\n", file);
            StringAssert.Contains("dynamic resolution:i:1", file);
        }

        /// <summary>A session read from a file: named as the file, host and port from full address, username</summary>
        [Test]
        public void ReadsAddressPortAndUsername()
        {
            SessionData s = RdpFile.Read(@"C:\x\Jump Box.rdp", "\uFEFFscreen mode id:i:2\r\nfull address:s:jump.example.com:3390\r\nusername:s:CORP\\me\r\n");
            Assert.AreEqual("Jump Box", s.SessionName);
            Assert.AreEqual("jump.example.com", s.Host);
            Assert.AreEqual(3390, s.Port);
            Assert.AreEqual(@"CORP\me", s.Username);
            Assert.AreEqual(ConnectionProtocol.RDP, s.Proto);
        }

        /// <summary>No port means Remote Desktop's; IPv6 in brackets with a port, or bare, is read as such</summary>
        [Test]
        public void ReadsDefaultPortAndIPv6()
        {
            Assert.AreEqual(3389, RdpFile.Read("a.rdp", "full address:s:host1").Port);
            SessionData v6 = RdpFile.Read("b.rdp", "full address:s:[fe80::1]:3391");
            Assert.AreEqual("fe80::1", v6.Host);
            Assert.AreEqual(3391, v6.Port);
            SessionData bare = RdpFile.Read("c.rdp", "full address:s:fe80::2");
            Assert.AreEqual("fe80::2", bare.Host);
            Assert.AreEqual(3389, bare.Port);
        }

        /// <summary>A file without an address gives no session</summary>
        [Test]
        public void NoAddressNoSession()
        {
            Assert.IsNull(RdpFile.Read("d.rdp", "username:s:me"));
        }
    }
}
