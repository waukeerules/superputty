using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The quick connect bar's Host box as a list of hosts (QuickHosts), ported from SuperXTerminal's quickHosts tests,
    /// and each host read with its own login, port or protocol (HostConnectionString)
    /// </summary>
    [TestFixture]
    public class QuickHostsTests
    {
        /// <summary>Split at commas, semicolons, spaces and new lines</summary>
        [Test]
        public void SplitAtSeparators()
        {
            CollectionAssert.AreEqual(new[] { "10.0.0.1", "10.0.0.2", "sw1.lab.example", "10.0.0.3", "10.0.0.4" },
                QuickHosts.Split("10.0.0.1, 10.0.0.2;sw1.lab.example\n10.0.0.3  10.0.0.4"));
        }

        /// <summary>Each keeps its own user, port and connection type</summary>
        [Test]
        public void KeepUserPortAndType()
        {
            CollectionAssert.AreEqual(new[] { "admin@10.0.0.1:2222", "telnet://10.0.0.2", "[fe80::1]:22" },
                QuickHosts.Split("admin@10.0.0.1:2222, telnet://10.0.0.2, [fe80::1]:22"));
        }

        /// <summary>A number alone after a host is still its port, as the box has always taken "host port"</summary>
        [Test]
        public void SpaceThenPortIsThatHostsPort()
        {
            CollectionAssert.AreEqual(new[] { "10.0.0.1 2222" }, QuickHosts.Split("10.0.0.1 2222"));
            CollectionAssert.AreEqual(new[] { "10.0.0.1 2222", "10.0.0.2" }, QuickHosts.Split("10.0.0.1 2222, 10.0.0.2"));
            // a host that already has its port doesn't take another
            CollectionAssert.AreEqual(new[] { "10.0.0.1:22", "2222" }, QuickHosts.Split("10.0.0.1:22 2222"));
        }

        /// <summary>A host typed twice, in any case, opens one tab; nothing typed is none; one host is that host</summary>
        [Test]
        public void OnceEachAndEmpty()
        {
            CollectionAssert.AreEqual(new[] { "sw1", "sw2" }, QuickHosts.Split("sw1, SW1, sw2, sw1"));
            CollectionAssert.IsEmpty(QuickHosts.Split(" , ;\n"));
            CollectionAssert.IsEmpty(QuickHosts.Split(null));
            CollectionAssert.AreEqual(new[] { "192.168.0.5" }, QuickHosts.Split("  192.168.0.5 "));
        }

        /// <summary>A pasted column goes in with commas between; a paste of one line is left to the box</summary>
        [Test]
        public void Pasted()
        {
            Assert.AreEqual("10.0.0.1, 10.0.0.2, 10.0.0.3", QuickHosts.Pasted("10.0.0.1\r\n10.0.0.2\n\n10.0.0.3\n"));
            Assert.IsNull(QuickHosts.Pasted("10.0.0.1, 10.0.0.2"));
        }

        /// <summary>Each host's own login, port and protocol, as quick connect opens it</summary>
        [Test]
        public void ReadEachHost()
        {
            HostConnectionString admin = new HostConnectionString("admin@10.0.0.7:2222");
            Assert.AreEqual("admin", admin.User);
            Assert.AreEqual("10.0.0.7", admin.Host);
            Assert.AreEqual(2222, admin.Port);
            HostConnectionString telnet = new HostConnectionString("telnet://root@sw1:23");
            Assert.AreEqual(ConnectionProtocol.Telnet, telnet.Protocol);
            Assert.AreEqual("root", telnet.User);
            Assert.AreEqual("sw1", telnet.Host);
            HostConnectionString v6 = new HostConnectionString("[fe80::1]:22");
            Assert.AreEqual("fe80::1", v6.Host);
            Assert.AreEqual(22, v6.Port);
            HostConnectionString plain = new HostConnectionString("10.0.0.1 2222");
            Assert.IsNull(plain.User);
            Assert.AreEqual("10.0.0.1", plain.Host);
            Assert.AreEqual(2222, plain.Port);
        }
    }
}
