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
        /// <summary>prompt a user for input</summary>
        /// <param name="arg">The pre-parsed string to send</param>
        /// <returns>A string containing commands to send with variables replaced with a carriage return sent at the end</returns>
        internal static CommandData PromptHandler(string arg)
        {
            // the same prompt as PWDPROMPT, with the typing shown: Visual Basic's InputBox can't follow the dark theme.
            // Cancel sends an empty line, as InputBox did.
            string result = string.Empty;
            using (frmPrivatePrompt prompt = new frmPrivatePrompt(arg, false))
            {
                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    result = prompt.GetResult();
                }
            }
            CommandData data = new CommandData(result, new KeyEventArgs(Keys.Enter), TimeSpan.FromMilliseconds(50));
            return data;
        }
    }
}
