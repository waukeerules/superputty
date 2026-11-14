using System;
using System.Text;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Gui;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// When a tab counts as connected rather than connecting (ConnectionProgress), and which state a session open in
    /// several tabs shows in the Sessions tree (IconLibrary.Better), as SuperXTerminal's PageHost and TabArea tests
    /// </summary>
    [TestFixture]
    public class ConnectionProgressTests
    {
        /// <summary>ConnectionProgress.HasText on a string's UTF-8 bytes</summary>
        /// <param name="text">The output</param>
        /// <returns>Whether it has text</returns>
        private static bool HasText(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return ConnectionProgress.HasText(bytes, bytes.Length);
        }

        /// <summary>What the pseudo console prints the moment plink starts is only codes, so the tab stays connecting</summary>
        [Test]
        public void PseudoConsoleStartUpIsNotText()
        {
            Assert.IsFalse(HasText("\x1b[?9001h\x1b[?1004h\x1b[6n\x1b]0;C:\\Program Files\\PuTTY\\plink.exe\x07\x1b[?25l\x1b[2J\x1b[m\x1b[H\x1b[?25h"));
            Assert.IsFalse(HasText("\r\n   \r\n\t"));
            Assert.IsFalse(HasText(""));
        }

        /// <summary>A prompt counts, coloured or not, and so does an error message</summary>
        [Test]
        public void PromptsAreText()
        {
            Assert.IsTrue(HasText("login as: "));
            Assert.IsTrue(HasText("\x1b[1;32muser@host\x1b[0m:~$ "));
            Assert.IsTrue(HasText("\x1b[?25lFATAL ERROR: Network error: Connection timed out"));
            Assert.IsTrue(HasText("Пароль: "));
        }

        /// <summary>PuTTY's session log header doesn't count; what follows it does</summary>
        [Test]
        public void PuttyLogHeaderIsNotText()
        {
            string header = "=~=~=~=~=~=~=~=~=~=~=~= PuTTY log 2026.11.14 10:00:00 =~=~=~=~=~=~=~=~=~=~=~=\r\n";
            Assert.IsFalse(HasText(header));
            Assert.IsTrue(HasText(header + "Welcome to Ubuntu"));
        }

        /// <summary>
        /// A PuTTY session log as PuTTY wrote it (raw session to a test server): the header and the server's codes only
        /// is still connecting; its prompt after that is the far end answering
        /// </summary>
        [Test]
        public void PuttyLogAsWritten()
        {
            string log = "=~=~=~=~=~=~=~=~=~=~=~= PuTTY log 2026.11.14 14:38:56 =~=~=~=~=~=~=~=~=~=~=~=\r\n\x1b[?25l\x1b[2J\x1b[H\x1b]0;title\x07\r\n";
            Assert.IsFalse(HasText(log));
            Assert.IsTrue(HasText(log + "login: "));
        }

        /// <summary>Connected once a second has passed since the first text, not before, and never without text</summary>
        [Test]
        public void AnsweredAfterASecond()
        {
            DateTime seen = new DateTime(2026, 11, 14, 10, 0, 0);
            Assert.IsFalse(ConnectionProgress.IsAnswered(null, seen.AddMinutes(5)));
            Assert.IsFalse(ConnectionProgress.IsAnswered(seen, seen.AddMilliseconds(999)));
            Assert.IsTrue(ConnectionProgress.IsAnswered(seen, seen.AddSeconds(1)));
        }

        /// <summary>Remote protocols are tracked; local shells, serial lines and VNC are not</summary>
        [Test]
        public void TrackedProtocols()
        {
            foreach (ConnectionProtocol p in new[] { ConnectionProtocol.SSH, ConnectionProtocol.Telnet, ConnectionProtocol.Rlogin, ConnectionProtocol.Raw, ConnectionProtocol.RDP })
                Assert.IsTrue(ConnectionProgress.IsTracked(p), p.ToString());
            foreach (ConnectionProtocol p in new[] { ConnectionProtocol.Serial, ConnectionProtocol.WINCMD, ConnectionProtocol.PS, ConnectionProtocol.WSL, ConnectionProtocol.VNC })
                Assert.IsFalse(ConnectionProgress.IsTracked(p), p.ToString());
        }

        /// <summary>A session open in several tabs shows the best: connected over connecting over dropped</summary>
        [Test]
        public void TreeShowsTheBestTab()
        {
            Assert.AreEqual(IconLibrary.OpenState.Connected, IconLibrary.Better(IconLibrary.OpenState.Connecting, IconLibrary.OpenState.Connected));
            Assert.AreEqual(IconLibrary.OpenState.Connecting, IconLibrary.Better(IconLibrary.OpenState.Disconnected, IconLibrary.OpenState.Connecting));
            Assert.AreEqual(IconLibrary.OpenState.Connecting, IconLibrary.Better(IconLibrary.OpenState.Connecting, IconLibrary.OpenState.Disconnected));
            Assert.AreEqual(IconLibrary.OpenState.Connected, IconLibrary.Better(IconLibrary.OpenState.Connected, IconLibrary.OpenState.Disconnected));
        }
    }
}
