# Working on this repository

Instructions for AI coding agents (Claude, ChatGPT, Grok, Copilot, ...) and for the humans reviewing their work.

This is a fork of [SilverGreen93/superputty](https://github.com/SilverGreen93/superputty): a C# WinForms (.NET Framework 4.8) app that hosts PuTTY and other terminals in tabs. Changes may be offered upstream, so they must be easy to review and explain.

## Document every change

This applies to code added or changed from now on. Don't go back and document untouched existing code.

### New files

Every new `.cs` file needs:

- **An XML doc comment on the class** explaining what the class is for, why it exists and how it fits in: which code calls it, and which code it calls.
- **An XML doc comment on every method, property, constructor and event, including private ones.** Use `<summary>` to say what it does and why; `<param>` for each parameter; `<returns>` for anything that isn't `void`; `<exception>` for anything it deliberately throws.
- **Comments inside methods only where the code alone doesn't explain *why***: a workaround, a timing dependency, a Win32 quirk. Don't narrate what each line does.

```csharp
/// <summary>
/// Replaces {key} placeholders in session fields with the current user's values,
/// so one Sessions.xml can be shared by many users. Used by PuttyStartInfo and
/// SuperPuTTY.OpenProtoSession; values come from EnvironmentVariablesManager.
/// </summary>
public static class SessionVariables
{
    /// <summary>Replace each {key} with its value in a single pass; undefined keys are left as they are</summary>
    /// <param name="input">Text that may contain placeholders, e.g. "{user}@{domain}"</param>
    /// <param name="variables">The user's values, keyed case-insensitively</param>
    /// <returns>The text with every defined placeholder replaced</returns>
    public static string Substitute(string input, IDictionary<string, string> variables)
```

### Changes to existing files

- **Each new or rewritten method** in an existing file gets the same XML doc comment as above.
- **A small change inside an existing method** gets a short comment only if the reason isn't obvious. Say why the code is that way, not "changed by X".
- **Match the surrounding file's style**: brace placement, `this.` usage, naming.
- **Don't reformat, re-indent or reorder code you aren't changing.** Unrelated churn buries the real change in review and makes merging upstream harder.

### FORK_CHANGES.md

[FORK_CHANGES.md](FORK_CHANGES.md) records how this fork differs from upstream. Update it in the same commit whenever a change:

- adds or changes a feature or user-visible behavior,
- fixes a bug (include the symptom, the root cause and the fix), or
- adds, removes or renames a file, or changes a file's role.

Keep its file tables, known issues and manual test steps accurate. If a change makes something there untrue, fix it.

### Commit messages

- **First line:** what changed, in under ~72 characters.
- **Body:** why it changed, and for bug fixes the root cause.
- **Separate commits:** a bug fix that could go upstream on its own gets its own commit, separate from feature work.

## Build and test

```bash
MSBuild.exe SuperPutty.sln /restore /p:Configuration=Debug /v:minimal
```

- Build the `.sln`, not `SuperPutty\SuperPutty.csproj`, so the installer is built too.
- The output is `bin\Debug\SuperXPuTTY.exe`. If SuperPuTTY is running from there, the build fails to copy the exe; close it first.
- The log goes to `%TEMP%\SuperPuTTY.log`. `ApplicationPanel` logs every `MoveWindow` with the panel size, which is useful for window and sizing bugs.
- `SuperPuttyUnitTests` uses an old NUnit 2.5 GUI runner, and most of its tests need a live SCP server. For new logic, keep it in small static methods that can be checked directly, and list the manual test steps in FORK_CHANGES.md.
- Say what you actually verified (built, ran, checked in the UI) and what you didn't.

## Architecture

**Stack:** C# WinForms on .NET Framework 4.8, built with Visual Studio 2022. The assembly is named `SuperXPuTTY`. NuGet packages:

- DockPanelSuite 3.1.1 and DockPanelSuite.ThemeVS2015 3.1.1, for Visual Studio-style docking
- log4net 3.1.0
- Microsoft.VisualBasic 10.3.0
- Newtonsoft.Json 13.0.3 (fork only, used for the placeholder values)

PuTTY, or [PuTTY Plus](https://github.com/SilverGreen93/putty), is an external runtime requirement.

### How a session gets on screen

```
SessionTreeview / toolbar / Quick Selector / layout restore / command line
  → SuperPuTTY.OpenProtoSession(SessionData)         checks placeholders, creates the tab
      → new ctlPuttyPanel(session)                   the tab; builds PuttyStartInfo
          → PuttyStartInfo / *StartInfo              executable + arguments per protocol, placeholders resolved
          → ApplicationPanel (inner Panel)           starts the process and reparents its window
      → panel.Show(DockPanel, LastDockstate)         docks and lays out the tab
      → panel.AppPanel.StartApplication()            Process.Start → SetParent → MoveWindow to the tab's size
```

SCP sessions go through `SuperPuTTY.OpenScpSession` → `PscpBrowserPanel` instead.

### Key types

| Type | File | Role |
|---|---|---|
| `SuperPuTTY` | `SuperPuTTY.cs` | Static app core: settings, the session list, loading and saving `Sessions.xml`, layouts, and opening sessions |
| `frmSuperPutty` | `frmSuperPutty.cs` | Main form: the `DockPanel`, menus, the quick-connect and send-command toolbars, the status bar, and theme selection |
| `ToolWindow` → `ToolWindowDocument` | `ToolWindow.cs` | Base classes for everything docked. `ToolWindow : DockContent` is for tool panes; `ToolWindowDocument` is for document tabs |
| `ctlPuttyPanel` | `ctlPuttyPanel.cs` | The tab for **every** terminal protocol: PuTTY, RDP, VNC, Mintty, Cygterm, cmd, PowerShell and WSL. It holds one `ApplicationPanel`. `GetPersistString` and `FromPersistString` save and restore it in layouts |
| `ApplicationPanel` | `ctlApplicationPanel.cs` | A plain `Panel`, not a tab. It starts the external process, captures its window (`SetParent`, `MoveWindow`) and keeps it sized and focused. All the Win32 embedding code is here |
| `SessionData` | `Data/SessionData.cs` | One saved session. Its setters raise change events that lead to `Sessions.xml` being saved |
| `PuttyStartInfo` + `*StartInfo` | `Utils/` | Build the command line for each protocol (`RDPStartinfo`, `VNCStartinfo`, `WSLStartInfo`, ...) |
| `SessionVariables`, `EnvironmentVariablesManager` | `Utils/` | Fork feature: `{key}` placeholders and the per-user values file |
| `RdpControl`, `IEmbeddedApplication`, `ICommandReceiver` | `Gui/`, `Utils/` | Fork feature: RDP tabs host the Remote Desktop ActiveX control (late bound, no interop assemblies) inside the `ApplicationPanel` instead of capturing mstsc. Options are the `Rdp*` settings (Tools → Options → RDP). To type into any tab, call `ctlPuttyPanel.SendCommand`, never `CommandData.SendToTerminal` with a window handle, so RDP tabs work too |
| `SessionLog`, `ctlPuttyPanel.RunScript` / `WaitForOutput` | `Utils/`, top level | Fork feature: every PuTTY session logs its output (`-sessionlog`, kept or temporary), which is how SuperPuTTY sees output; SPSL's `WAITFOR` reads it. Run scripts with `ctlPuttyPanel.RunScript`, not `SPSL.BeginExecuteScript`, so `WAITFOR` and RDP tabs work |
| `IconLibrary`, `IconTemplates`, `BrandLogos`, `CustomIcons`, `dlgIconPicker` | `Gui/` | Fork feature: session icons are drawn at the size shown (Options → Icon size). A key is a name plus an optional colour (`switch#1E63B4`, `custom:juniper#C62828`). Templates and logos are vector drawings, custom icons are one-colour PNGs in `<SettingsFolder>\icons`, and old PNG keys map onto them; theme icon folders aren't read. Look up keys with `IconLibrary.Ensure(SuperPuTTY.Images, key)` rather than `Images.ContainsKey`, so coloured keys are drawn on first use. Set document tab icons with `TabIcons.Set`, not the `Icon` property: DockPanelSuite would draw them at a fixed 16 px |
| `PuttyWindow`, `ctlPuttyPanel.CheckConnection` | `Utils/`, top level | Fork feature: dropped sessions keep their tab, get a badge and reconnect, by finding PuTTY's "Fatal Error" box and sending its Restart Session command. A PuTTY process exiting still closes the tab |
| `ButtonBarStrip`, `ButtonBarStore`, `SendStringParser` | `Gui/`, `Utils/` | Fork feature: the button bar above the status bar. Buttons are stored in `ButtonBars.xml` in SettingsFolder; sending uses `CommandData.SendToTerminal`, like the Send Commands toolbar and SPSL |
| `SessionTreeview`, `LayoutsList`, `SessionDetail`, log viewers | top level | Tool windows (`ToolWindow`), shown and hidden through `SingletonToolWindowHelper<T>` |
| Dialogs | `dlg*.cs`, `Gui/`, `EnvironmentVariablesForm.cs` | Plain `Form`s with designer files |

### Folders

| Folder | Contents |
|---|---|
| `Data/` | Session, layout, history and shortcut data, and importers for PuTTY, MobaXterm and RDP sessions |
| `Utils/` | Command-line builders, `NativeMethods` (Win32), settings provider, single-instance, hotkeys, focus helpers |
| `Gui/` | Reusable UI: Quick Selector, the shortcut editor, `DpiUtils`, image picker |
| `Scp/` | The pscp file browser and file transfers (MVP: View, Presenter, ViewModel) |
| `Scripting/` | SPSL, the session scripting language (`Spsl.cs` plus one class per command) |
| `DarkModeForms/` | Copied in from [BlueMystical/Dark-Mode-Forms](https://github.com/BlueMystical/Dark-Mode-Forms): `DarkModeCS`, `Messenger` (themed message boxes), `Flat*` controls |
| `Models/` | Fork additions (`EnvironmentVariable`) |
| `Resources/third-party/` | Licences for bundled third-party data (Simple Icons, CC0) |

### Settings and files

- **`SuperXPuTTY.settings`** holds per-user settings, through `PortableSettingsProvider`. It's stored in `Documents\SuperXPuTTY\`, or next to the exe in a portable install. Access it in code through `SuperPuTTY.Settings`.
- **`SettingsFolder`** is configurable, and may be a shared drive. It holds `Sessions.xml` and `layouts\`.
- **`EnvironmentVariables.json`** is per-user, next to `SuperXPuTTY.settings`.

### UI conventions

- **Message boxes:** use `Messenger.MessageBox` rather than `MessageBox.Show`, so they follow the theme.
- **Themes:** test UI changes in both the light and dark themes. The `DockPanel` theme (`VS2015*Theme`) is set in `frmSuperPutty`; individual forms apply `DarkModeCS`.
- **Docking:** for docking changes, test saving and restoring a layout, floating windows, and multiple monitors. `ApplicationPanel.MoveWindow` has a fix specific to a second screen.
- **High DPI:** use `DpiUtils` (see `frmSuperPutty.FixDpiScalingIssues`).
- **Logging:** use log4net, through a `private static readonly ILog Log = LogManager.GetLogger(typeof(...))` field in each class.

### References

- Upstream: <https://github.com/SilverGreen93/superputty>. The original SuperPuTTY wiki: <https://github.com/jimradford/superputty/wiki/Documentation>
- DockPanel Suite: <https://docs.dockpanelsuite.com/>
- Upstream records user-facing changes in `ReleaseNotes.txt`. This fork records its changes in `FORK_CHANGES.md` and leaves `ReleaseNotes.txt` as upstream's.

## Things that bite in this codebase

- **Mixed line endings.** Some files are CRLF and some LF; some are mixed. Preserve each file's existing endings. A script that reads in text mode and writes back can turn a 3-line change into a whole-file diff, so check `git diff --stat` before committing.
- **WinForms designer files** (`*.Designer.cs`): add controls the way the designer would: declare the field, construct it in `InitializeComponent`, set its properties, add it to its parent's `Controls`, and wire its events.
- **Dark mode.** New dialogs apply `DarkModeCS` the same way the existing dialogs do (`if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme) new DarkModeCS(this) { ... }`). It recolors controls during the form's `Load` event, so read themed colors in `OnLoad`, not in the constructor.
- **Stored sessions.** Never modify a `SessionData` taken from the session tree just to launch it. Its setters raise change events, and the change is saved to `Sessions.xml`, which may be shared by a team. Work on `session.Clone()`, as `SessionVariables.Resolve` does.
- **Session placeholders** (`{key}`) are resolved only in `PuttyStartInfo` and `SuperPuTTY.OpenScpSession`, and checked in `SuperPuTTY.OpenProtoSession`. New launch paths go through those, not their own substitution.
- **Starting PuTTY.** `SuperPuTTY.OpenProtoSession` starts the hosted application after `panel.Show()`, so it starts at the tab's real size. Don't move the start back into `OnVisibleChanged` or a constructor.
- **Secrets.** Never log passwords, `-pw` arguments, placeholder values or the text being typed into a session (it can contain a secret `{key}`). Secret Environment Variables are DPAPI-encrypted in `EnvironmentVariables.json`; show them masked (`SessionVariables.Load(true)`).
