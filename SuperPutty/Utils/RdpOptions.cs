using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The options the built-in RDP client (Gui.RdpControl) connects with. Global defaults come from Tools -> Options ->
    /// RDP (the Rdp* settings); a session can override any of them, stored in SessionData.RdpOptions as
    /// "Key=Value;Key=Value" with only the overridden keys. A null property means "not overridden" in a session's
    /// overrides; in <see cref="EffectiveFor"/>'s result every property has a value.
    /// </summary>
    public class RdpOptions
    {
        /// <summary>"FitTab" or "Fixed"</summary>
        public string DisplayMode { get; set; }

        /// <summary>Desktop width for "Fixed"</summary>
        public int? FixedWidth { get; set; }

        /// <summary>Desktop height for "Fixed"</summary>
        public int? FixedHeight { get; set; }

        /// <summary>15, 16, 24 or 32</summary>
        public int? ColorDepth { get; set; }

        /// <summary>Scale the remote desktop to fit</summary>
        public bool? SmartSizing { get; set; }

        /// <summary>Share the clipboard with the remote</summary>
        public bool? RedirectClipboard { get; set; }

        /// <summary>Share local drives with the remote</summary>
        public bool? RedirectDrives { get; set; }

        /// <summary>Share local printers with the remote</summary>
        public bool? RedirectPrinters { get; set; }

        /// <summary>Share smart cards with the remote</summary>
        public bool? RedirectSmartCards { get; set; }

        /// <summary>Share the microphone with the remote</summary>
        public bool? RedirectMicrophone { get; set; }

        /// <summary>0 play on this computer, 1 play on the remote, 2 don't play</summary>
        public int? AudioMode { get; set; }

        /// <summary>Windows key combinations: 0 on this computer, 1 on the remote, 2 only in full screen</summary>
        public int? KeyboardHookMode { get; set; }

        /// <summary>0 connect without warning, 1 don't connect, 2 warn, when the server can't be authenticated</summary>
        public int? AuthenticationLevel { get; set; }

        /// <summary>RD Gateway host; in a session's overrides, "" means "no gateway" (overriding a global one)</summary>
        public string GatewayHost { get; set; }

        /// <summary>Connect to the admin (console) session</summary>
        public bool? ConnectToConsole { get; set; }

        /// <summary>Whether this is a session's overrides with nothing overridden</summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(this.Format()); }
        }

        /// <summary>The global defaults from Tools -> Options -> RDP, with every option set</summary>
        /// <returns>The defaults</returns>
        public static RdpOptions Defaults()
        {
            Properties.Settings s = SuperPuTTY.Settings;
            return new RdpOptions
            {
                DisplayMode = string.Equals(s.RdpDisplayMode, "Fixed", StringComparison.OrdinalIgnoreCase) ? "Fixed" : "FitTab",
                FixedWidth = s.RdpFixedWidth,
                FixedHeight = s.RdpFixedHeight,
                ColorDepth = s.RdpColorDepth,
                SmartSizing = s.RdpSmartSizing,
                RedirectClipboard = s.RdpRedirectClipboard,
                RedirectDrives = s.RdpRedirectDrives,
                RedirectPrinters = s.RdpRedirectPrinters,
                RedirectSmartCards = s.RdpRedirectSmartCards,
                RedirectMicrophone = s.RdpRedirectMicrophone,
                AudioMode = s.RdpAudioMode,
                KeyboardHookMode = s.RdpKeyboardHookMode,
                AuthenticationLevel = s.RdpAuthenticationLevel,
                GatewayHost = s.RdpGatewayHost ?? string.Empty,
                ConnectToConsole = s.RdpConnectToConsole
            };
        }

        /// <summary>The options a session connects with: its overrides on top of the global defaults</summary>
        /// <param name="session">The session</param>
        /// <returns>Options with every property set</returns>
        public static RdpOptions EffectiveFor(SessionData session)
        {
            return Defaults().Overlay(Parse(session.RdpOptions));
        }

        /// <summary>A copy of these options with every property that <paramref name="overrides"/> sets replaced</summary>
        /// <param name="overrides">A session's overrides</param>
        /// <returns>A new <see cref="RdpOptions"/></returns>
        public RdpOptions Overlay(RdpOptions overrides)
        {
            return new RdpOptions
            {
                DisplayMode = overrides.DisplayMode ?? this.DisplayMode,
                FixedWidth = overrides.FixedWidth ?? this.FixedWidth,
                FixedHeight = overrides.FixedHeight ?? this.FixedHeight,
                ColorDepth = overrides.ColorDepth ?? this.ColorDepth,
                SmartSizing = overrides.SmartSizing ?? this.SmartSizing,
                RedirectClipboard = overrides.RedirectClipboard ?? this.RedirectClipboard,
                RedirectDrives = overrides.RedirectDrives ?? this.RedirectDrives,
                RedirectPrinters = overrides.RedirectPrinters ?? this.RedirectPrinters,
                RedirectSmartCards = overrides.RedirectSmartCards ?? this.RedirectSmartCards,
                RedirectMicrophone = overrides.RedirectMicrophone ?? this.RedirectMicrophone,
                AudioMode = overrides.AudioMode ?? this.AudioMode,
                KeyboardHookMode = overrides.KeyboardHookMode ?? this.KeyboardHookMode,
                AuthenticationLevel = overrides.AuthenticationLevel ?? this.AuthenticationLevel,
                GatewayHost = overrides.GatewayHost ?? this.GatewayHost,
                ConnectToConsole = overrides.ConnectToConsole ?? this.ConnectToConsole
            };
        }

        /// <summary>Read a session's overrides from "Key=Value;Key=Value"; unknown keys and bad values are ignored</summary>
        /// <param name="text">The stored text; null or empty for no overrides</param>
        /// <returns>The overrides; properties not mentioned are null</returns>
        public static RdpOptions Parse(string text)
        {
            RdpOptions options = new RdpOptions();
            foreach (string pair in (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = pair.Substring(0, equals).Trim();
                string value = pair.Substring(equals + 1).Trim();
                switch (key)
                {
                    case "Display": options.DisplayMode = value == "Fixed" ? "Fixed" : "FitTab"; break;
                    case "Width": options.FixedWidth = ParseInt(value); break;
                    case "Height": options.FixedHeight = ParseInt(value); break;
                    case "ColorDepth": options.ColorDepth = ParseInt(value); break;
                    case "SmartSizing": options.SmartSizing = ParseBool(value); break;
                    case "Clipboard": options.RedirectClipboard = ParseBool(value); break;
                    case "Drives": options.RedirectDrives = ParseBool(value); break;
                    case "Printers": options.RedirectPrinters = ParseBool(value); break;
                    case "SmartCards": options.RedirectSmartCards = ParseBool(value); break;
                    case "Microphone": options.RedirectMicrophone = ParseBool(value); break;
                    case "Audio": options.AudioMode = ParseInt(value); break;
                    case "Keyboard": options.KeyboardHookMode = ParseInt(value); break;
                    case "Authentication": options.AuthenticationLevel = ParseInt(value); break;
                    case "Gateway": options.GatewayHost = value; break;
                    case "Console": options.ConnectToConsole = ParseBool(value); break;
                }
            }
            return options;
        }

        /// <summary>Write the properties that are set as "Key=Value;Key=Value", for SessionData.RdpOptions</summary>
        /// <returns>The text; empty when nothing is set</returns>
        public string Format()
        {
            List<string> pairs = new List<string>();
            Action<string, object> add = (key, value) =>
            {
                if (value != null)
                {
                    string text = value is bool ? ((bool)value ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture);
                    // ; and = separate the pairs, so they can't be part of a value
                    pairs.Add(key + "=" + text.Replace(";", "").Replace("=", ""));
                }
            };
            add("Display", this.DisplayMode);
            add("Width", this.FixedWidth);
            add("Height", this.FixedHeight);
            add("ColorDepth", this.ColorDepth);
            add("SmartSizing", this.SmartSizing);
            add("Clipboard", this.RedirectClipboard);
            add("Drives", this.RedirectDrives);
            add("Printers", this.RedirectPrinters);
            add("SmartCards", this.RedirectSmartCards);
            add("Microphone", this.RedirectMicrophone);
            add("Audio", this.AudioMode);
            add("Keyboard", this.KeyboardHookMode);
            add("Authentication", this.AuthenticationLevel);
            add("Gateway", this.GatewayHost);
            add("Console", this.ConnectToConsole);
            return string.Join(";", pairs);
        }

        /// <summary>Parse a whole number, or null if it isn't one</summary>
        private static int? ParseInt(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : (int?)null;
        }

        /// <summary>Parse 1/0 or true/false, or null if it's neither</summary>
        private static bool? ParseBool(string value)
        {
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
    }
}
