using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The line a built-in terminal tab starts with when it goes through a jump host (JumpHosts.NoticeText), as
    /// SuperXTerminal's: the target, the jump host as set and, when that's a session's name, its address, and where it
    /// came from
    /// </summary>
    [TestFixture]
    public class JumpHostNoticeTests
    {
        /// <summary>A jump host given as user@host says so once</summary>
        [Test]
        public void AddressGivenAsIs()
        {
            JumpHosts.Target target = new JumpHosts.Target { Host = "omv.example", User = "root" };
            Assert.AreEqual("Connecting to 192.168.0.50 through the jump host root@omv.example, from the session's Jump host.",
                JumpHosts.NoticeText("192.168.0.50", "root@omv.example", target, "the session's Jump host"));
        }

        /// <summary>A jump host named as a session shows that session's address too, with its port when not 22</summary>
        [Test]
        public void SessionNameWithItsAddress()
        {
            JumpHosts.Target target = new JumpHosts.Target { Host = "bastion.example", User = "me", Port = 2222 };
            Assert.AreEqual("Connecting to 10.0.0.1 through the jump host Bastions/b1 (me@bastion.example:2222), from folder \"HQ\".",
                JumpHosts.NoticeText("10.0.0.1", "Bastions/b1", target, "folder \"HQ\""));
        }

        /// <summary>No user: just the host</summary>
        [Test]
        public void NoUser()
        {
            JumpHosts.Target target = new JumpHosts.Target { Host = "jump1" };
            StringAssert.Contains("through the jump host jump1, from your local override.", JumpHosts.NoticeText("h", " jump1 ", target, "your local override"));
        }
    }
}
