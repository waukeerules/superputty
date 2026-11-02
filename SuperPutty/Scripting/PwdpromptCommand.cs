/*
 * https://github.com/jimradford/superputty/blob/master/License.txt
 */

using System;
using System.Windows.Forms;
using SuperPutty;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    public static partial class Commands
    {
        
        /// <summary>
        /// Prompt the user for hidden input (a password) and type it with Enter. Cancel stops the script, rather than
        /// typing anything: the word "Cancelled" used to be sent, as if it were the password.
        /// </summary>
        /// <param name="arg">The pre-parsed string to send</param>
        /// <returns>A string containing commands to send with variables replaced with a carriage return sent at the end</returns>
        /// <exception cref="OperationCanceledException">The prompt was cancelled; ctlPuttyPanel.RunScriptLines stops the script</exception>
        internal static CommandData PrivatePromptHandler(string arg)
        {
            String result;
            using (frmPrivatePrompt testDialog = new frmPrivatePrompt("Insert your password"))
            {
                if (testDialog.ShowDialog() != DialogResult.OK)
                {
                    throw new OperationCanceledException("you cancelled the password prompt");
                }
                result = testDialog.GetResult();
            }

            CommandData data = new CommandData(result, new KeyEventArgs(Keys.Enter), TimeSpan.FromMilliseconds(50));
            return data;
        }
    }
}
