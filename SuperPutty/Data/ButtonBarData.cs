using System.Collections.Generic;
using System.Xml.Serialization;

namespace SuperPutty.Data
{
    /// <summary>What a <see cref="CommandButton"/> does when clicked</summary>
    public enum CommandButtonAction
    {
        /// <summary>Type the button's text into the terminal, with escape codes such as \r for Enter (see SendStringParser)</summary>
        SendString,

        /// <summary>Run the button's text as an SPSL script (see SuperPuTTY.Scripting.SPSL)</summary>
        SpslScript
    }

    /// <summary>
    /// One button on a <see cref="ButtonBar"/>: a label and the text or script it sends to terminal sessions.
    /// Shown by <see cref="Gui.ButtonBarStrip"/>, edited in <see cref="Gui.dlgEditCommandButton"/>,
    /// and saved to ButtonBars.xml by <see cref="Utils.ButtonBarStore"/>.
    /// </summary>
    public class CommandButton
    {
        /// <summary>The text shown on the button</summary>
        [XmlAttribute]
        public string Label { get; set; }

        /// <summary>Optional tooltip; when empty the button's command text is shown instead</summary>
        [XmlAttribute]
        public string Tooltip { get; set; }

        /// <summary>
        /// Optional color of the circle drawn before the label, as HTML ("#3FB950") or a color name.
        /// Ignored when <see cref="ImageKey"/> is set.
        /// </summary>
        [XmlAttribute]
        public string Color { get; set; }

        /// <summary>Optional key of a session icon (SuperPuTTY.Images) shown before the label, instead of a circle</summary>
        [XmlAttribute]
        public string ImageKey { get; set; }

        /// <summary>Whether <see cref="Text"/> is a string to type or an SPSL script</summary>
        [XmlAttribute]
        public CommandButtonAction Action { get; set; }

        /// <summary>
        /// The string to send (with escape codes) or the SPSL script. May contain {key} placeholders,
        /// which are replaced with the user's values from Tools -> Environment Variables when clicked.
        /// </summary>
        public string Text { get; set; }

        /// <summary>Create a copy of this button, so an edit dialog can work on it without touching the original until OK</summary>
        /// <returns>A new <see cref="CommandButton"/> with the same values</returns>
        public CommandButton Clone()
        {
            return (CommandButton)this.MemberwiseClone();
        }
    }

    /// <summary>A named set of <see cref="CommandButton"/>s; the user switches between bars with the dropdown on the strip</summary>
    public class ButtonBar
    {
        /// <summary>The bar's name, unique within <see cref="ButtonBarList"/> (compared case-insensitively)</summary>
        [XmlAttribute]
        public string Name { get; set; }

        /// <summary>The buttons, in the order they are shown left to right</summary>
        [XmlArrayItem("Button")]
        public List<CommandButton> Buttons { get; set; } = new List<CommandButton>();

        /// <summary>
        /// Tab icons (SuperPuTTY.Images keys, e.g. "windows") this bar is shown for: when a tab with one of these icons
        /// becomes active, the button bar switches to this bar
        /// </summary>
        [XmlArrayItem("Icon")]
        public List<string> TabIcons { get; set; } = new List<string>();

        /// <summary>XmlSerializer: only write the icon list when there is one</summary>
        /// <returns>true to write <see cref="TabIcons"/></returns>
        public bool ShouldSerializeTabIcons()
        {
            return this.TabIcons != null && this.TabIcons.Count > 0;
        }
    }

    /// <summary>The root of ButtonBars.xml: every button bar, in the order shown in the dropdown</summary>
    [XmlRoot("ButtonBars")]
    public class ButtonBarList
    {
        /// <summary>All button bars</summary>
        [XmlElement("ButtonBar")]
        public List<ButtonBar> Bars { get; set; } = new List<ButtonBar>();
    }
}
