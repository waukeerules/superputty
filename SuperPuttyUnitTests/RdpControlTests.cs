using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// What an RDP tab does when its session ends (RdpControl.AfterEnd): the tab stays, a drop of a session that had
    /// reached the desktop is reconnected per Options > Advanced > Dropped sessions, and a failed login never is.
    /// </summary>
    [TestFixture]
    public class RdpControlTests
    {
        /// <summary>A logoff or a disconnect (from either side) waits for a click, whatever the options say</summary>
        [Test]
        public void CleanEndWaitsForAClick()
        {
            Assert.AreEqual(RdpControl.EndAction.WaitForClick, RdpControl.AfterEnd(true, true, 0, true, 6));
        }

        /// <summary>A drop of a session that was on the desktop counts down to reconnecting</summary>
        [Test]
        public void DropOfAConnectedSessionReconnects()
        {
            Assert.AreEqual(RdpControl.EndAction.Reconnect, RdpControl.AfterEnd(false, true, 0, true, 6));
        }

        /// <summary>A login that failed (never reached the desktop) isn't retried, so a bad password doesn't lock the account</summary>
        [Test]
        public void FailedLoginIsNotRetried()
        {
            Assert.AreEqual(RdpControl.EndAction.WaitForClick, RdpControl.AfterEnd(false, false, 0, true, 6));
        }

        /// <summary>A reconnect that fails (not yet back on the desktop) is tried again, up to the limit, then gives up</summary>
        [Test]
        public void FailedReconnectsTryAgainThenGiveUp()
        {
            Assert.AreEqual(RdpControl.EndAction.Reconnect, RdpControl.AfterEnd(false, false, 3, true, 6));
            Assert.AreEqual(RdpControl.EndAction.GaveUp, RdpControl.AfterEnd(false, false, 6, true, 6));
        }

        /// <summary>With reconnecting turned off in Options, a drop waits for a click</summary>
        [Test]
        public void ReconnectingOffWaitsForAClick()
        {
            Assert.AreEqual(RdpControl.EndAction.WaitForClick, RdpControl.AfterEnd(false, true, 0, false, 6));
        }

        /// <summary>Which ends are explained (ShouldReport): a failure, or a disconnect before the desktop was reached</summary>
        [Test]
        public void ShouldReportExplainsFailuresOnly()
        {
            Assert.IsFalse(RdpControl.ShouldReport(1, 0, true), "disconnected from this side");
            Assert.IsFalse(RdpControl.ShouldReport(2, 0, true), "logged off");
            Assert.IsTrue(RdpControl.ShouldReport(3, 0, false), "refused before the desktop");
            Assert.IsTrue(RdpControl.ShouldReport(264, 0, true), "network timeout");
        }
    }
}
