using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Button bars as a JSON file, so they can be shared: the button bar's "Export Button Bar..." writes the bar shown,
    /// and "Import Button Bar..." on someone else's strip adds the bars in the file (Gui.ButtonBarStrip). It holds each
    /// bar's name, the tab icons it's shown for, and its buttons with their label, tooltip, colour or icon, action and
    /// text. {key} placeholders and {prompt:}/{secret:} prompts are kept as they are, to be filled in by whoever uses them.
    /// </summary>
    public static class ButtonBarFile
    {
        /// <summary>The value of the file's "format" field, so another JSON file isn't taken for one</summary>
        public const string Format = "superxputty-button-bars";

        /// <summary>The file's text for some bars</summary>
        /// <param name="bars">The bars, in order</param>
        /// <returns>Indented JSON, starting with the format marker</returns>
        public static string ToJson(IEnumerable<ButtonBar> bars)
        {
            JObject json = new JObject
            {
                ["format"] = Format,
                ["version"] = 1,
                ["bars"] = new JArray(bars.Select(bar =>
                {
                    JObject b = new JObject { ["name"] = bar.Name ?? string.Empty };
                    if (bar.TabIcons != null && bar.TabIcons.Count > 0)
                    {
                        b["tabIcons"] = new JArray(bar.TabIcons);
                    }
                    b["buttons"] = new JArray(bar.Buttons.Select(button =>
                    {
                        JObject o = new JObject { ["label"] = button.Label ?? string.Empty };
                        if (!string.IsNullOrEmpty(button.Tooltip)) o["tooltip"] = button.Tooltip;
                        if (!string.IsNullOrEmpty(button.Color)) o["color"] = button.Color;
                        if (!string.IsNullOrEmpty(button.ImageKey)) o["icon"] = button.ImageKey;
                        o["action"] = button.Action.ToString();
                        o["text"] = button.Text ?? string.Empty;
                        return o;
                    }));
                    return b;
                }))
            };
            return json.ToString(Formatting.Indented);
        }

        /// <summary>Read a file's text</summary>
        /// <param name="text">The file's text</param>
        /// <param name="error">Why it can't be used, for the user; null when it can</param>
        /// <returns>The bars in it, or null</returns>
        public static List<ButtonBar> FromJson(string text, out string error)
        {
            JObject json;
            try
            {
                json = JObject.Parse(text ?? string.Empty);
            }
            catch (JsonException)
            {
                error = "This isn't a JSON file.";
                return null;
            }
            if ((string)json["format"] != Format)
            {
                error = "This isn't a button bar file exported from SuperXPuTTY.";
                return null;
            }
            List<ButtonBar> bars = new List<ButtonBar>();
            foreach (JObject b in (json["bars"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string name = ((string)b["name"] ?? string.Empty).Trim();
                if (name.Length == 0)
                    continue;
                ButtonBar bar = new ButtonBar
                {
                    Name = name,
                    TabIcons = (b["tabIcons"] as JArray ?? new JArray()).Select(t => (string)t).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
                };
                foreach (JObject o in (b["buttons"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    CommandButtonAction action;
                    bar.Buttons.Add(new CommandButton
                    {
                        Label = (string)o["label"] ?? string.Empty,
                        Tooltip = NullIfEmpty((string)o["tooltip"]),
                        Color = NullIfEmpty((string)o["color"]),
                        ImageKey = NullIfEmpty((string)o["icon"]),
                        // an action this version doesn't know is typed as text, the harmless choice
                        Action = Enum.TryParse((string)o["action"], true, out action) && Enum.IsDefined(typeof(CommandButtonAction), action) ? action : CommandButtonAction.SendString,
                        Text = (string)o["text"] ?? string.Empty
                    });
                }
                bars.Add(bar);
            }
            error = bars.Count == 0 ? "The file has no button bars in it." : null;
            return error == null ? bars : null;
        }

        /// <summary>A name not used by any of the bars: the name itself, or "Name (2)", "Name (3)", ...</summary>
        /// <param name="name">The name wanted</param>
        /// <param name="existing">The bars there are</param>
        /// <returns>A free name, compared case-insensitively as bar names are</returns>
        public static string FreeName(string name, IEnumerable<ButtonBar> existing)
        {
            HashSet<string> taken = new HashSet<string>(existing.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(name))
                return name;
            for (int n = 2; ; n++)
            {
                string candidate = string.Format("{0} ({1})", name, n);
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        /// <summary>Null for an empty string, so optional fields stay unset</summary>
        /// <param name="value">The value</param>
        /// <returns>The value, or null</returns>
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
