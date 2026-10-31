using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Grey hint text shown in an empty text box, such as "Type to search..." or "e.g. 192.168.0.5", using the
    /// Windows edit control's cue banner (EM_SETCUEBANNER), so it disappears as soon as something is typed and is
    /// never part of the box's Text. Used by SessionTreeview's search box and dlgEditSession's fields.
    /// </summary>
    public static class HintText
    {
        /// <summary>Win32 message that sets an edit control's cue banner</summary>
        private const int EM_SETCUEBANNER = 0x1501;

        /// <summary>Each text box's current hint, so it can be set again when the box's window is recreated</summary>
        private static readonly ConditionalWeakTable<TextBox, string[]> hints = new ConditionalWeakTable<TextBox, string[]>();

        /// <summary>Send a message with a string parameter to a window</summary>
        /// <param name="hWnd">The edit control</param>
        /// <param name="msg">The message</param>
        /// <param name="wParam">For EM_SETCUEBANNER, 1 to keep showing the hint while the box has focus</param>
        /// <param name="lParam">The hint</param>
        /// <returns>1 if the hint was set</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>
        /// Show a hint in a text box while it's empty, including while it has focus, until something is typed.
        /// Calling it again replaces the hint.
        /// </summary>
        /// <param name="textBox">A single-line text box; Windows doesn't show hints in multi-line ones</param>
        /// <param name="hint">The hint, e.g. "e.g. 192.168.0.5"</param>
        public static void Set(TextBox textBox, string hint)
        {
            string[] current;
            if (hints.TryGetValue(textBox, out current))
            {
                current[0] = hint;
            }
            else
            {
                hints.Add(textBox, new[] { hint });
                // a recreated window (e.g. after a style change) loses its hint
                textBox.HandleCreated += (sender, e) => Apply(textBox);
            }
            if (textBox.IsHandleCreated)
            {
                Apply(textBox);
            }
        }

        /// <summary>Send a text box its current hint</summary>
        /// <param name="textBox">The text box; its window must exist</param>
        private static void Apply(TextBox textBox)
        {
            string[] hint;
            if (hints.TryGetValue(textBox, out hint))
            {
                SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, hint[0]);
            }
        }
    }
}
