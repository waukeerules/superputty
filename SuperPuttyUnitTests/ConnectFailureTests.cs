using System.Text;
using NUnit.Framework;
using SuperPutty.Utils;
using Kind = SuperPutty.Utils.ConnectFailure.FailureKind;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// Reading why a connection failed from plink's or PuTTY's last words, whether reconnecting by itself can help, and
    /// the delay before each attempt (ConnectFailure), as SuperXTerminal's sshFailure tests. Messages are plink 0.85's.
    /// </summary>
    [TestFixture]
    public class ConnectFailureTests
    {
        /// <summary>A refused password isn't retried by itself, so it can't lock the account</summary>
        [Test]
        public void RefusedLoginIsNotRetried()
        {
            ConnectFailure f = ConnectFailure.Read("login as: admin\r\nadmin@10.0.0.5's password:\r\nAccess denied\r\nadmin@10.0.0.5's password:\r\nAccess denied\r\nFATAL ERROR: Remote side unexpectedly closed network connection\r\n");
            Assert.AreEqual(Kind.Login, f.Kind);
            Assert.IsFalse(f.Reconnects);
            StringAssert.Contains("can't lock the account", f.Describe("10.0.0.5 port 22"));
            Assert.AreEqual(Kind.Login, ConnectFailure.Read("FATAL ERROR: No supported authentication methods available (server sent: publickey)\r\n").Kind);
        }

        /// <summary>The session's own "Access denied" (a router's, or one long before the end) isn't plink's</summary>
        [Test]
        public void SessionsOwnAccessDeniedIsADrop()
        {
            Assert.AreEqual(Kind.None, ConnectFailure.Read("sw1#show run\r\n% Access denied\r\nsw1#\r\nFATAL ERROR: Remote side unexpectedly closed network connection\r\n").Kind);
            string earlier = "Access denied\r\n" + new StringBuilder().Insert(0, "line\r\n", 20) + "FATAL ERROR: Network error: Software caused connection abort\r\n";
            ConnectFailure f = ConnectFailure.Read(earlier);
            Assert.AreEqual(Kind.None, f.Kind);
            Assert.IsTrue(f.Reconnects);
            Assert.IsNull(f.Describe("h port 22"));
        }

        /// <summary>Too many login attempts is read before the refused login a server may send with it</summary>
        [Test]
        public void TooManyAttempts()
        {
            ConnectFailure f = ConnectFailure.Read("Access denied\r\nFATAL ERROR: Server sent disconnect message\r\ntype 2 (protocol error):\r\n\"Too many authentication failures\"\r\n");
            Assert.AreEqual(Kind.TooManyAttempts, f.Kind);
            Assert.IsFalse(f.Reconnects);
        }

        /// <summary>An unknown host, and a host key that wasn't accepted, aren't retried</summary>
        [Test]
        public void UnknownHostAndHostKey()
        {
            Assert.AreEqual(Kind.UnknownHost, ConnectFailure.Read("Unable to open connection:\r\nHost does not exist").Kind);
            ConnectFailure key = ConnectFailure.Read("The host key is not cached for this server:\r\n  10.0.0.9 (port 22)\r\nStore key in cache? (y/n, Return cancels connection, i for more info) \r\nConnection abandoned.\r\n");
            Assert.AreEqual(Kind.HostKey, key.Kind);
            Assert.IsFalse(key.Reconnects);
        }

        /// <summary>No algorithm in common says which kind and what the device offers</summary>
        [Test]
        public void NoAlgorithmInCommon()
        {
            ConnectFailure f = ConnectFailure.Read("FATAL ERROR: Couldn't agree a host key algorithm (available: ssh-rsa,ssh-dss)\r\n");
            Assert.AreEqual(Kind.Negotiate, f.Kind);
            Assert.AreEqual("host key", f.Detail);
            Assert.AreEqual("ssh-rsa,ssh-dss", f.Offer);
            Assert.IsFalse(f.Reconnects);
            Assert.AreEqual("key exchange", ConnectFailure.Read("FATAL ERROR: Couldn't agree a key exchange algorithm (available: diffie-hellman-group1-sha1)").Detail);
        }

        /// <summary>Nothing answering, no route or a timeout reconnect, with a hint</summary>
        [Test]
        public void NetworkFailuresReconnect()
        {
            ConnectFailure refused = ConnectFailure.Read("FATAL ERROR: Network error: Connection refused\r\n");
            Assert.AreEqual(Kind.Refused, refused.Kind);
            Assert.IsTrue(refused.Reconnects);
            StringAssert.Contains("10.0.0.9 port 2222", refused.Describe("10.0.0.9 port 2222"));
            Assert.AreEqual(Kind.Unreachable, ConnectFailure.Read("FATAL ERROR: Network error: Network is unreachable").Kind);
            Assert.AreEqual(Kind.TimedOut, ConnectFailure.Read("FATAL ERROR: Network error: Connection timed out").Kind);
            Assert.IsTrue(ConnectFailure.Read("FATAL ERROR: Network error: Connection timed out").Reconnects);
        }

        /// <summary>The words are found in the pseudo console's output once its codes are taken out (ConnectionProgress.PlainText)</summary>
        [Test]
        public void ReadFromPseudoConsoleOutput()
        {
            byte[] output = Encoding.UTF8.GetBytes("\x1b[?25l\x1b[2J\x1b[m\x1b[Hadmin@10.0.0.5's password:\x1b[2;1HAccess denied\x1b[?25h\r\n");
            string text = ConnectionProgress.PlainText(output, output.Length);
            Assert.AreEqual(Kind.Login, ConnectFailure.Read(text).Kind);
        }

        /// <summary>Delays double from the one chosen, up to two minutes; one chosen above two minutes stays as chosen</summary>
        [Test]
        public void DelayDoublesToTwoMinutes()
        {
            int[] delays = new int[6];
            for (int n = 0; n < 6; n++)
                delays[n] = ConnectFailure.ReconnectDelay(10, n);
            CollectionAssert.AreEqual(new[] { 10, 20, 40, 80, 120, 120 }, delays);
            Assert.AreEqual(300, ConnectFailure.ReconnectDelay(300, 3));
            Assert.AreEqual(1, ConnectFailure.ReconnectDelay(0, 0));
        }
    }
}
