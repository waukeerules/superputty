namespace SuperPutty.Utils
{
    /// <summary>
    /// An <see cref="IEmbeddedApplication"/> that can receive typed text and keys from Send Commands, the button
    /// bar and SPSL scripts. Terminal tabs receive them as window messages instead (<see cref="CommandData.SendToTerminal"/>);
    /// an embedded application has no such window. Implemented by <see cref="Gui.RdpControl"/>; called through
    /// ctlPuttyPanel.SendCommand.
    /// </summary>
    public interface ICommandReceiver
    {
        /// <summary>Type the command's text and keys, then wait for its delay. Safe to call from any thread.</summary>
        /// <param name="command">The text, keys and delay to send</param>
        void SendCommand(CommandData command);
    }
}
