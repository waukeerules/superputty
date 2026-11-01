using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using log4net;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The files and the WebView2 environment behind the Highlighted Output panel (Gui.HighlightedOutputPanel). The
    /// page and its default rules and themes are in the "web\output" folder beside the exe. Users add their own
    /// *.rules.json and *.theme.json files to a "highlighting" folder in the SettingsFolder (shared with the team) or in
    /// their own settings folder; a rule set with the same id, or a theme with the same name, replaces the one before it.
    /// The format is described in docs/MonacoTerminal.md and in the README written to the folder.
    /// </summary>
    public static class OutputHighlighting
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(OutputHighlighting));

        /// <summary>The host name the page is served under (mapped to <see cref="WebFolder"/>; never a real site)</summary>
        public const string HostName = "superputty-output.local";

        /// <summary>The page's address</summary>
        public const string PageUrl = "https://" + HostName + "/output/index.html";

        /// <summary>The shared WebView2 environment, so every panel uses one browser process; created on first use</summary>
        private static Task<CoreWebView2Environment> environment;

        /// <summary>The "web" folder beside the exe, holding output\index.html, its scripts and the default rules</summary>
        public static string WebFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web"); }
        }

        /// <summary>The "highlighting" folder in the SettingsFolder, which may be shared with a team; null when no SettingsFolder is set</summary>
        public static string SharedFolder
        {
            get
            {
                string settings = SuperPuTTY.Settings.SettingsFolder;
                return string.IsNullOrWhiteSpace(settings) ? null : Path.Combine(settings, "highlighting");
            }
        }

        /// <summary>The "highlighting" folder beside this user's SuperXPuTTY.settings; its files win over the shared ones</summary>
        public static string UserFolder
        {
            get { return Path.Combine(SuperPuTTY.UserSettingsFolder, "highlighting"); }
        }

        /// <summary>
        /// Where WebView2 keeps its profile (cache, the page's remembered choices). Its default, beside the exe, isn't
        /// writable in Program Files.
        /// </summary>
        public static string UserDataFolder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperXPuTTY", "WebView2");
            }
        }

        /// <summary>The installed WebView2 runtime's version, or null when it isn't installed</summary>
        /// <returns>e.g. "156.0.4314.58", or null</returns>
        public static string RuntimeVersion()
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not check for the WebView2 runtime", ex);
                return null;
            }
        }

        /// <summary>The shared WebView2 environment, created the first time it's needed</summary>
        /// <returns>A task giving the environment</returns>
        public static Task<CoreWebView2Environment> GetEnvironment()
        {
            if (environment == null || environment.IsFaulted)
            {
                environment = CoreWebView2Environment.CreateAsync(null, UserDataFolder);
            }
            return environment;
        }

        /// <summary>
        /// Read the rule sets and themes: the defaults beside the exe, then the shared folder, then the user's folder
        /// </summary>
        /// <param name="ruleSets">The rule sets, in order, one per id</param>
        /// <param name="themes">The themes, in order, one per name</param>
        /// <returns>Problems to show the user (files that couldn't be read); empty when all were fine</returns>
        public static List<string> Load(out JArray ruleSets, out JArray themes)
        {
            List<string> problems = new List<string>();
            ruleSets = new JArray();
            themes = new JArray();
            string output = Path.Combine(WebFolder, "output");
            string[][] folders =
            {
                new[] { Path.Combine(output, "rules"), Path.Combine(output, "themes") },
                new[] { SharedFolder, SharedFolder },
                new[] { UserFolder, UserFolder },
            };
            foreach (string[] pair in folders)
            {
                foreach (JObject item in ReadAll(pair[0], "*.rules.json", problems))
                {
                    Merge(ruleSets, item, "id");
                }
                foreach (JObject item in ReadAll(pair[1], "*.theme.json", problems))
                {
                    Merge(themes, item, "name");
                }
            }
            return problems;
        }

        /// <summary>Read every matching JSON file in a folder; files that aren't a JSON object are reported and skipped</summary>
        /// <param name="folder">The folder; may be null or missing</param>
        /// <param name="pattern">e.g. "*.rules.json"</param>
        /// <param name="problems">Gets a line for each file that couldn't be read</param>
        /// <returns>The files' contents, in name order</returns>
        private static List<JObject> ReadAll(string folder, string pattern, List<string> problems)
        {
            List<JObject> items = new List<JObject>();
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return items;
                string[] files = Directory.GetFiles(folder, pattern);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        items.Add(JObject.Parse(File.ReadAllText(file)));
                    }
                    catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        problems.Add(Path.GetFileName(file) + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problems.Add(folder + ": " + ex.Message);
            }
            return items;
        }

        /// <summary>
        /// Add an item to a list, replacing the earlier one with the same key (case-insensitive), so later folders
        /// override earlier ones. Items without the key are ignored.
        /// </summary>
        /// <param name="list">The rule sets or themes so far</param>
        /// <param name="item">The new item</param>
        /// <param name="key">"id" for rule sets, "name" for themes</param>
        public static void Merge(JArray list, JObject item, string key)
        {
            string value = (string)item[key];
            if (string.IsNullOrWhiteSpace(value))
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals((string)list[i][key], value, StringComparison.OrdinalIgnoreCase))
                {
                    list[i] = item;
                    return;
                }
            }
            list.Add(item);
        }

        /// <summary>
        /// The folder "Rules Folder" opens: the shared one when there's a SettingsFolder and it can be written, else the
        /// user's own. Creates it, with a README explaining the files, the first time.
        /// </summary>
        /// <returns>The folder's path</returns>
        public static string PrepareFolder()
        {
            foreach (string folder in new[] { SharedFolder, UserFolder })
            {
                if (folder == null)
                    continue;
                try
                {
                    Directory.CreateDirectory(folder);
                    string readme = Path.Combine(folder, "README.txt");
                    if (!File.Exists(readme))
                    {
                        File.WriteAllText(readme, Readme);
                    }
                    return folder;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warn("Could not prepare the highlighting folder " + folder, ex);
                }
            }
            return UserFolder;
        }

        /// <summary>The README written to a new highlighting folder</summary>
        private const string Readme =
@"SuperPuTTY highlighting rules and themes (Highlighted Output panel)

Put *.rules.json and *.theme.json files here, then press Reload Rules in the panel. The defaults are in
web\output\rules and web\output\themes beside SuperXPuTTY.exe; copy one here to change it. A rule set with the
same ""id"", or a theme with the same ""name"", replaces the default. Files in the highlighting folder beside your
SuperXPuTTY.settings win over files in the SettingsFolder's highlighting folder, which a team can share.

Rule set:
{
  ""id"": ""mycisco"",
  ""name"": ""My Cisco"",
  ""include"": [""cisco""],                      // rule sets whose rules come first
  ""detect"": [""^[A-Za-z0-9_.-]+[>#]""],         // for Auto: a line matching one of these picks this set
  ""rules"": [
    { ""match"": ""\\bconnected\\b"", ""style"": ""good"" },
    { ""match"": ""^\\S+.*\\bup\\s+up\\s*$"", ""scope"": ""line"", ""style"": ""good"" },
    { ""match"": ""\\bfailed\\b"", ""style"": ""bad"", ""ignoreCase"": true }
  ]
}
""match"" is a JavaScript regular expression (backslashes doubled in JSON). ""scope"": ""line"" colours the whole
line; otherwise only the matched text. A matched text's colour wins over its line's.

Theme:
{
  ""name"": ""My Dark"",
  ""base"": ""vs-dark"",                          // or ""vs"" for a light theme
  ""colors"": { ""background"": ""#101010"", ""foreground"": ""#d0d0d0"", ""selection"": ""#264f78"" },
  ""font"": { ""family"": ""Cascadia Mono"", ""size"": 14 },
  ""styles"": {
    ""good"": { ""color"": ""#4ec97a"" },
    ""bad"": { ""color"": ""#f14c4c"", ""bold"": true, ""background"": ""#3a1010"" }
  }
}
Styles used by the default rules: good, bad, warning, info, address, mac, prompt, keyword, comment.
// comments like the ones above are allowed.
";
    }
}
