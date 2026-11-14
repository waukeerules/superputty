using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The keys an RDP tab's menu sends into the remote desktop (Send Key), as SuperXTerminal's RDP_KEYS: the ones that
    /// can't be typed into a Remote Desktop window from here, because Windows or SuperXPuTTY takes them first (Ctrl+Alt+Del,
    /// the Windows key, Alt+Tab). They're sent as fixed PC scan codes, not through the local keyboard layout, so they're
    /// the same keys whatever layout is in use. Used by ctlPuttyPanel's menu and RdpControl.SendKeyCombination.
    /// </summary>
    public static class RdpKeys
    {
        /// <summary>Flag on a scan code for an extended key (prefixed with E0 on a real keyboard), as the control's SendKeys takes it</summary>
        public const int ExtendedKeyFlag = 0x100;

        /// <summary>The menu's keys, in order: label, then the keys pressed together (held in order, released in reverse)</summary>
        public static readonly IList<KeyValuePair<string, Keys[]>> MenuKeys = new List<KeyValuePair<string, Keys[]>>
        {
            new KeyValuePair<string, Keys[]>("Ctrl+Alt+Delete", new[] { Keys.LControlKey, Keys.LMenu, Keys.Delete }),
            new KeyValuePair<string, Keys[]>("Ctrl+Shift+Esc  (Task Manager)", new[] { Keys.LControlKey, Keys.LShiftKey, Keys.Escape }),
            new KeyValuePair<string, Keys[]>("Windows key  (Start)", new[] { Keys.LWin }),
            new KeyValuePair<string, Keys[]>("Windows+R  (Run)", new[] { Keys.LWin, Keys.R }),
            new KeyValuePair<string, Keys[]>("Windows+E  (File Explorer)", new[] { Keys.LWin, Keys.E }),
            new KeyValuePair<string, Keys[]>("Windows+D  (show the desktop)", new[] { Keys.LWin, Keys.D }),
            new KeyValuePair<string, Keys[]>("Windows+L  (lock)", new[] { Keys.LWin, Keys.L }),
            new KeyValuePair<string, Keys[]>("Alt+Tab", new[] { Keys.LMenu, Keys.Tab }),
            new KeyValuePair<string, Keys[]>("Alt+F4  (close the window)", new[] { Keys.LMenu, Keys.F4 }),
            new KeyValuePair<string, Keys[]>("Print Screen", new[] { Keys.PrintScreen })
        };

        /// <summary>PC (set 1) scan codes of the keys in <see cref="MenuKeys"/>, extended ones with <see cref="ExtendedKeyFlag"/></summary>
        private static readonly Dictionary<Keys, int> ScanCodes = new Dictionary<Keys, int>
        {
            { Keys.LControlKey, 0x1D },
            { Keys.LMenu, 0x38 },
            { Keys.LShiftKey, 0x2A },
            { Keys.Delete, 0x53 | ExtendedKeyFlag },
            { Keys.Escape, 0x01 },
            { Keys.LWin, 0x5B | ExtendedKeyFlag },
            { Keys.RWin, 0x5C | ExtendedKeyFlag },
            { Keys.R, 0x13 },
            { Keys.E, 0x12 },
            { Keys.D, 0x20 },
            { Keys.L, 0x26 },
            { Keys.Tab, 0x0F },
            { Keys.F4, 0x3E },
            { Keys.PrintScreen, 0x37 | ExtendedKeyFlag }
        };

        /// <summary>A key's scan code, as <see cref="Events"/> sends it; 0 for a key not in the table</summary>
        /// <param name="key">The key</param>
        /// <returns>Its scan code, with <see cref="ExtendedKeyFlag"/> for an extended key</returns>
        public static int ScanCodeOf(Keys key)
        {
            int code;
            return ScanCodes.TryGetValue(key, out code) ? code : 0;
        }

        /// <summary>
        /// The key events for pressing keys together: each pressed in order, then released in reverse, as the control's
        /// SendKeys takes them
        /// </summary>
        /// <param name="keys">The keys, e.g. Ctrl, Alt, Delete</param>
        /// <param name="scanCodes">Each event's scan code</param>
        /// <param name="keyUps">Each event's direction (VARIANT_BOOL: 0 down, -1 up)</param>
        /// <exception cref="ArgumentException">A key that isn't one of <see cref="MenuKeys"/>' keys</exception>
        public static void Events(Keys[] keys, out int[] scanCodes, out short[] keyUps)
        {
            List<int> codes = new List<int>();
            List<short> ups = new List<short>();
            foreach (Keys key in keys)
            {
                int code;
                if (!ScanCodes.TryGetValue(key, out code))
                    throw new ArgumentException("No scan code for " + key, nameof(keys));
                codes.Add(code);
                ups.Add(0);
            }
            for (int i = keys.Length - 1; i >= 0; i--)
            {
                codes.Add(ScanCodes[keys[i]]);
                ups.Add(-1);
            }
            scanCodes = codes.ToArray();
            keyUps = ups.ToArray();
        }
    }
}
