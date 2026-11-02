# Fork changes: Session Placeholders ("Environment Variables")

Fork: <https://github.com/waukeerules/superputty>
Upstream: <https://github.com/SilverGreen93/superputty>
Base: upstream `master` @ `639ef12` (v2.1.1.0). The fork is up to date with upstream, with the feature commits on top.

## Summary

This adds a user-defined key/value store (**Tools → Environment Variables...**). Saved sessions can then contain `{key}` placeholders that SuperPuTTY replaces with the current user's values when the session is launched.

```
Login username:  {vaultuser}@root@{domain}@192.168.0.1@{psmpserver}#policy
Login username:  {user}@{domain}
```

With `user = jdoe` and `domain = corp.example.com` defined locally, `{user}@{domain}` launches as `jdoe@corp.example.com`.

## Why

1. **A shared `Sessions.xml`.** A team can point at one sessions file on a network drive. Each engineer keeps their own values (username, domain, vault account) locally, so nobody has to run a script to generate a personal copy of the sessions file.
2. **CyberArk PSM / SSH-proxy login strings.** PSM expects usernames like `vaultuser@targetuser@targethost@psmpserver`. The target part changes for every device, so a PuTTY saved session can't hold it. Placeholders let a session template carry the fixed parts while the per-user parts come from local values.
3. **Easier session creation.** You duplicate an existing session, keep the placeholders, and change only the session name and host.

The key *names* must match across the team, but each person's *values* can differ.

## How it works

- **Placeholders** are `{key}`, where the key is made of letters, digits and `_`. Keys are case-insensitive, so `{User}` and `{user}` are the same.
- **Where:** Host, Login username, Password and Extra arguments.
- **When:** at launch, through every path: double-click, Connect, Connect All, the quick-connect toolbar, Quick Selector, Restart, Duplicate, layouts, command line, Connect External and SCP. The values are replaced on a **copy** of the session, so the stored session, and `Sessions.xml`, always keeps its `{placeholders}`.
- **Missing values:** if Host, Login username or Extra arguments uses a key with no value, the launch stops with a message naming the missing keys and an offer to open **Tools → Environment Variables...**. After you define them and click OK, the session launches. This prevents sending a literal `{domain}` to a CyberArk proxy, where repeated failed logins can lock the account. The password isn't checked, since a real password may contain braces.
- **Preview:** in the Edit Session dialog, a line under Login username shows what the session will connect as, e.g. *Connects as jdoe@corp.example.com to psmp.corp*. Any field that uses an undefined key turns red, and the line reads *Undefined: {domain}. Click here to define them.* Clicking it opens the Environment Variables dialog, and the preview updates when it closes.

## What changed vs. upstream

### New files

| File | Purpose |
|---|---|
| `SuperPutty/Utils/SessionVariables.cs` | All the placeholder logic in one place: `Load`, `Substitute` (one regex pass), `FindMissing`, `Resolve` (returns a substituted copy) and `EnsureDefined` (the missing-key prompt) |
| `SuperPutty/Models/EnvironmentVariable.cs` | `EnvironmentVariable` (`Key`, `Value`) and `EnvironmentVariables` (a `BindingList` container) |
| `SuperPutty/Utils/EnvironmentVariablesManager.cs` | Loads and saves `EnvironmentVariables.json` in the user's own settings folder (see Storage) |
| `SuperPutty/EnvironmentVariablesForm.cs` / `.Designer.cs` / `.resx` | A dialog with a Key/Value grid and Add/OK/Cancel buttons. It rejects empty, duplicate (case-insensitive) or invalid keys, drops blank rows on save, and follows the dark theme like the other dialogs |

### Modified files

Each upstream file changes only by a few lines:

| File | Change |
|---|---|
| `Utils/PuttyStartInfo.cs` | `session = SessionVariables.Resolve(session);` at the top of the constructor. This covers PuTTY, RDP, VNC, Mintty, Cygterm, cmd, PowerShell and WSL |
| `SuperPuTTY.cs` | `OpenProtoSession` and `OpenScpSession` call `EnsureDefined` first. SCP passes a resolved copy to pscp |
| `SessionTreeview.cs` | **Connect External** calls `EnsureDefined` |
| `dlgEditSession.cs` / `.Designer.cs` | The placeholder preview line and red highlighting. A tooltip on Login username: "Enter username or placeholder (e.g., {user})" |
| `frmSuperPutty.cs` / `.Designer.cs` | The **Tools → Environment Variables...** menu item |
| `SuperPutty.csproj` | Adds the new files, a `Newtonsoft.Json` 13.0.3 package reference and a `System.Xml.Linq` reference |

### Storage

`EnvironmentVariables.json` sits next to the user's `SuperXPuTTY.settings`, normally `Documents\SuperXPuTTY\`, or the exe folder for a portable install. It deliberately does **not** go in the configurable settings folder that holds `Sessions.xml`, because that folder may be the shared network drive, and then everyone would share one set of values. Earlier builds stored the file in the settings folder; if a user has no file yet, it's copied from there once.

```json
{
  "Variables": [
    { "Key": "user",   "Value": "jdoe" },
    { "Key": "domain", "Value": "corp.example.com" }
  ]
}
```

## Fixed during review

- **The user's values were saved into `Sessions.xml`.** Substitution modified the live session object, so the next save wrote the values over the placeholders. Substitution now happens on a copy.
- **Some launch paths skipped substitution** (Quick Selector, Restart, Duplicate, layouts, command line, SCP, Connect External). Substitution now happens in `PuttyStartInfo` and `OpenScpSession`, which every path goes through.
- **The values file was shared** along with `Sessions.xml`. It's now per-user.
- **Undefined keys were sent as literal `{key}`.** The launch now stops and explains what's missing.
- **Passwords in logs.** The extra logging (including the full PuTTY command line with `-pw`) is gone; upstream's masked argument log remains.
- **`%VAR%` expansion in arguments was dropped.** Upstream's `PuttyStartInfo` is back, so it works again.
- **Each launch set process environment variables** for every key (`PATH` would have overwritten SuperPuTTY's own). Removed.
- **Duplicated code and unrelated reformatting** in `frmSuperPutty.cs`, `SessionTreeview.cs`, `PuttyStartInfo.cs` and `Settings.cs` were reverted to upstream, which keeps the diff small for review.
- **Substitution depended on key order and was case-sensitive** while the dialog's duplicate check was not. It's now one case-insensitive regex pass.

## Other fix: first tab opens with a squished terminal

This is a long-standing upstream bug, not part of the placeholder feature. It's a separate commit, so it can go upstream on its own.

**Symptom:** the first session opened after starting SuperPuTTY shows its login banner wrapped at about 24 columns. Every later session is fine.

**Cause:** `ctlPuttyPanel.CreatePanel()` sets `ApplicationName` and then calls `Controls.Add(AppPanel)`. Adding a control to a hidden parent raises `OnVisibleChanged`, and `ApplicationPanel` starts PuTTY there, while the tab is still being constructed at its placeholder size. From the second tab on, the tab area already exists, so PuTTY is moved to full size within milliseconds. For the first tab, the tab area is created and laid out only at the end of `Show()`, after `FocusActiveDocument` → `SetFocusToChildApplication` retries for about 0.4 s. By then PuTTY has connected with a tiny terminal and the server's banner has wrapped. The log shows it as `MoveWindow [OnVisChanged] w=198, h=78`, then `[OnResize] w=1656, h=875` about 0.5 s later.

**Fix:**
- `ApplicationPanel` gets a `StartOnVisible` property (default `true`, the old behavior) and a public `StartApplication()`. The startup code moved there unchanged.
- `ctlPuttyPanel` sets `StartOnVisible = false` before `Controls.Add`.
- `SuperPuTTY.OpenProtoSession` calls `StartApplication()` right after `panel.Show()`. Every tab goes through it, including layout restore. PuTTY's window exists as soon as `Show()` returns, so the SPSL script handle code is unchanged.

The focus retry loop also returns immediately now, since PuTTY isn't running yet while `Show()` is in progress.

**Follow-up fix:** `ChildWindowFocusHelper` kept a dictionary keyed by each tab's window handle, filled when the tab is added. Since PuTTY now starts after that, every new tab was added with handle `0`, and the second one threw `ArgumentException: An item with the same key has already been added` from `DockPanel_ContentAdded`. The helper now keeps a list of the tabs and compares their current handles when it needs to, in `ContainsChild`. That also stays correct for RDP, whose window handle can change after capture.

## Button bar

A row of one-click buttons above the status bar. Show or hide it with **View → Toolbars → Button Bar**. The Send Commands toolbar is unchanged and still works alongside it.

**Using it:**
- **Click** a button to send it to the active tab. **Shift+click** sends it to every tab selected in the Send Commands toolbar's "Select documents" picker, filtered by its icon group the same way.
- **Right-click** the bar to add, edit, delete or move buttons, and to create, rename or delete bars. The dropdown on the left switches bars.
- Each button either sends a string or runs an SPSL script.
  - **Send string:** escape codes `\r` Enter, `\n` line feed, `\t` Tab, `\b` Backspace, `\e` Escape, `\p` 1-second pause, `\v` paste clipboard, `\\` backslash. A line break typed in the editor also means Enter.
  - **SPSL script:** the existing scripting language (`SENDLINE`, `SENDKEY`, `SLEEP`, ...).
- `{key}` placeholders work in both and are filled in from **Tools → Environment Variables**. A button that uses an undefined key stops and offers to open that dialog, the same way a session does.
- **Icons:** a button can show a colored circle (eight presets or any custom color), or one of SuperPuTTY's session icons. The circle is stored as `Color="#3FB950"` and the icon as `ImageKey`.
- **Focus:** one click is enough even while you're typing in a terminal, and the keyboard goes back to the active tab afterwards. A ToolStrip normally uses the first click on an inactive window only to activate it (`WM_MOUSEACTIVATE` → `MA_ACTIVATEANDEAT`); the bar answers `MA_ACTIVATE` instead, then calls `FocusActiveDocument`.
- **Tooltips show above the bar.** Before the fix, a button's tooltip could flash on and off for as long as the pointer stayed on it. ToolStrip puts tooltips below the pointer. The bar is at the bottom of the screen, so Windows moved the two-line tooltip up over the pointer; when the tooltip took the mouse (it did with a terminal tab open), the strip saw the pointer leave the button and hid it, then showed it again after the hover time, about every 0.7 s. The bar now turns off ToolStrip's tooltips and shows its own (`ButtonBarStrip.ShowTip`) after the hover time, just above the button, so it's never under the pointer. Checked in the full app, with the real pointer held on a button: before, with a cmd tab open, the tooltip changed 18 times in 6 s and the window under the pointer was the tooltip; after, with and without a tab, it showed once above the bar and stayed for its 5 s.

**Sharing as a file:** right-click the bar → **Export Button Bar...** saves the bar shown to a JSON file (`Utils/ButtonBarFile.cs`): its name, the tab icons it's shown for, and each button's label, tooltip, colour or icon, action and text. **Import Button Bar...** adds the bars in such a file and switches to it; one named like a bar already here comes in as "Name (2)", so nothing is overwritten. `{key}` placeholders and `{prompt:}`/`{secret:}` prompts go in the file as they are, never their values. Another JSON file, or one without bars, is refused; an action this version doesn't know is imported as Send string.

**Storage:** `ButtonBars.xml` sits in the settings folder next to `Sessions.xml`, so a team that shares that folder shares the buttons; placeholders keep per-user values out of the file. Which bar is showing, and whether the bar is visible, are per-user settings (`ActiveButtonBar`, `ShowButtonBar`). The file is reloaded whenever the right-click menu opens, so edits apply to the latest version, and it's saved through a temporary file so a failed write can't leave a half-written file. If two people save at the same moment, the last one wins.

**Files:**

| File | Purpose |
|---|---|
| `Data/ButtonBarData.cs` | New. `CommandButton`, `ButtonBar` and `ButtonBarList`, the XML model |
| `Utils/ButtonBarStore.cs` | New. Loads and saves `ButtonBars.xml` |
| `Utils/SendStringParser.cs` | New. Turns a send string with escape codes into `CommandData` steps for the existing `SendToTerminal` |
| `Gui/ButtonBarStrip.cs` | New. The toolbar: dropdown, buttons, right-click menu (with Export and Import Button Bar), and sending on background threads |
| `Utils/ButtonBarFile.cs` | New. Button bars as JSON: `ToJson`, `FromJson`, `FreeName` |
| `Gui/dlgEditCommandButton.cs` / `.Designer.cs` | New. The button editor dialog, with dark mode |
| `frmSuperPutty.cs` / `.Designer.cs` | Hosts the bar, adds the menu item, and hides it in full screen. The Send Commands target filter is moved, unchanged, into `GetSelectedPuttyPanels()` so both toolbars use it |
| `Utils/SessionVariables.cs` | `EnsureDefined` also checks arbitrary text, for buttons |
| `Properties/Settings.*`, `App.config` | `ShowButtonBar` (default on) and `ActiveButtonBar` |

**Export and Import checked** (`ButtonBarFile`, 8 checks, all passed): the user's two bars (6 buttons with send strings, an SPSL script, colours and an icon; and a bar with a tab icon) came back field for field from their exported text, and ButtonBars.xml was left as it was; a taken name became "Name (2)"; another JSON file, text that isn't JSON and a file without bars were refused; an unknown action became Send string and an unnamed bar was skipped. **Not checked:** the two menu items in the running app.

**Not done yet:** keyboard shortcuts for buttons, a default bar per session, and other button actions (launch an application, menu functions).

## Session search across all fields

Upstream's search box above the session tree only looked at the session name and host, and Regex mode only at the name. It now looks at every field by default: session name, folder, host, login username, port, connection type, PuTTY session profile, extra arguments, notes and SPSL script file.

- **Choosing fields:** **Tools → Options → GUI → Session tree → Search in...** opens a checklist. It stays open while you tick fields and has an "All fields" entry; at least one field always stays ticked. The choice is a per-user setting, `SessionsSearchFields`, stored as flag names such as `Name, Host` or `All` (the default).
- **Search modes:** case-sensitive, case-insensitive and Regex all apply to every chosen field.
- **Passwords:** never searched. A `-pw` password in the extra arguments is removed before matching.
- **Placeholders:** the stored text is searched, so `{user}` finds sessions that use that placeholder.
- **Bug fixed:** a session with no host no longer crashes the search (`s.Host.ToLower()` threw on null).

**Files:** `SessionTreeview.cs` (the `SearchFields` flags, field labels and `SearchFilter`), `dlgFindPutty.cs` / `.Designer.cs` (the "Search in..." button and checklist), and `Properties/Settings.*` and `App.config` (`SessionsSearchFields`).

## Other fix: session icons missing depending on how SuperPuTTY is started

> **Superseded:** SuperPuTTY no longer reads `themes\default\icons` folders at all; see "Custom icons, and no more theme icon folders" below. This section records the earlier fix.

Upstream loads icons from `themes\default\icons` under the **current directory**, the folder the process was started from, rather than where the exe is. Started from a shortcut with another "Start in" folder, a file association, the command line or a script, SuperPuTTY found no icons. The log showed `theme directory not found`, and sessions fell back to the plain computer icon.

`SuperPuTTY.GetThemeIconFolders` now checks these locations in order and merges every one that exists. An earlier folder wins when two have an icon with the same name.

1. `<SettingsFolder>\themes\default\icons`: put custom `.png`, `.jpg` or `.bmp` icons here. If the settings folder is shared, the whole team gets them, and they can replace a built-in icon of the same name.
2. The folder `SuperXPuTTY.exe` is in.
3. The current directory (upstream's behavior).
4. `Program Files (x86)` and `Program Files`, under both `SuperXPuTTY` and `SuperPuTTY`.

The log lists each folder used and how many icons came from it.

## Other fix: cmd, PowerShell and WSL open in their own window on Windows 11

**Symptom:** on Windows 11, double-clicking a PowerShell (or cmd, or WSL) session opens it in a separate Windows Terminal window instead of a tab. The log shows `Unable to get handle for process on first try. Polling 10 s for handle.`, and the session takes 10 seconds to give up.

**Cause:** Windows 11's default terminal setting ("Let Windows decide", or Windows Terminal) hands console programs to Windows Terminal. The started `powershell.exe` never owns a window; the window that appears is Windows Terminal's (`CASCADIA_HOSTING_WINDOW_CLASS`), owned by a different process, so there's nothing SuperPuTTY can embed.

**Fix:**
- `PuttyStartInfo.UsesConsoleHost` starts these three protocols as `conhost.exe "<program>" <args>` on Windows 11 and later (build 22000+, read from the registry). Running a program under `conhost.exe` explicitly skips the hand-off and gives a classic console window (`ConsoleWindowClass`). Windows 10 keeps upstream's behavior.
- Windows reports a console window as belonging to the program inside it, not to `conhost`. `ApplicationPanel.WaitForTargetProcess`, upstream's hook for programs that start other programs, now follows `conhost` to its child (`FindChildProcess`, using WMI) and captures that, exactly as on Windows 10.
- The project now references `System.Management`.

**Checked:** `PuttyStartInfo` produces `conhost.exe "...\powershell.exe"` for PS and `conhost.exe "...\cmd.exe"` for WINCMD, and leaves SSH unchanged. Starting `conhost` with PowerShell the way `ApplicationPanel` does, `FindChildProcess` found `powershell` in about 230 ms, and its window was a `ConsoleWindowClass`. Started directly, cmd's only window was Windows Terminal's.

## RDP sessions fit the tab (built-in Remote Desktop client)

**Before:** RDP tabs started `mstsc -v:host /span` and captured its window. `/span` makes the remote desktop as large as all monitors combined, so a tab only showed its top-left part. mstsc can't fix this from outside: in full-screen mode, the only mode SuperPuTTY can embed, it ignores the requested size and uses the monitor's resolution. Smart sizing and "size to the window" only work in windowed mode, and a windowed mstsc doesn't stay embedded. Both were tried.

**Now**, RDP tabs host Microsoft's Remote Desktop ActiveX control (`mstscax.dll`, part of Windows) directly, as Remote Desktop Connection Manager (RDCMan) does:
- **Sized to the tab:** the remote desktop is set to the tab's size when connecting.
- **Resizing:** when the tab is resized (after a 0.5 s pause), the remote resolution changes to match (`UpdateSessionDisplaySettings`, Windows 8.1 / Server 2012 R2 and later). Older remotes such as Windows 7 are scaled to fit instead (`SmartSizing`).
- **Login:** the session's host, port and login username are used. Windows asks for credentials as mstsc does (CredSSP), unless the session has a password. A server whose identity can't be verified gets mstsc's usual warning.
- **When the session ends** (disconnect, log off) the tab closes. If it can't connect, Windows' own error text is shown first, e.g. "Remote Desktop can't connect to the remote computer...".
- **Turning it off:** **Tools → Options → General → Built-in client**, next to the RDP client path (setting `RdpUseEmbeddedClient`, on by default). It's also skipped when the RDP client is FreeRDP (`wfreerdp.exe`), or when the control isn't installed. Untick it to go back to starting the RDP client program.
- **Not supported with the built-in client:** the session's extra arguments are mstsc command-line switches, so they aren't used.

**RDP options** (Tools → Options → RDP; they apply to all RDP sessions using the built-in client, when they connect):

| Option | Choices (default first) | Control setting |
|---|---|---|
| Desktop size | Fit the tab, or a fixed width × height (1920×1080) | `DesktopWidth`/`DesktopHeight`; the remote only follows resizing in "fit the tab" |
| Colour depth | 32, 24, 16 or 15-bit | `ColorDepth` |
| Scale to fit | On / off | `AdvancedSettings9.SmartSizing` |
| Use on remote | Clipboard (on), drives, printers, smart cards, microphone | `RedirectClipboard`, `RedirectDrives`, `RedirectPrinters`, `RedirectSmartCards`, `AudioCaptureRedirectionMode` |
| Remote audio | Play on this computer, play on the remote, don't play | `AudioRedirectionMode` |
| Windows key combos | Only when full screen (mstsc's default), on this computer, on the remote | `SecuredSettings3.KeyboardHookMode` |
| Server authentication | Warn me, connect without warning, don't connect | `AuthenticationLevel` |
| RD Gateway | Host name, or empty to connect directly | `TransportSettings4.GatewayHostname`, used directly, credentials negotiated |
| Admin (console) session | Off / on | `ConnectToAdministerServer` |

They're per-user settings named `Rdp*` (e.g. `RdpDisplayMode`, `RdpGatewayHost`); `dlgFindPutty.LoadRdpOptions` / `SaveRdpOptions` map the page to them.

**Per-session RDP options:** in Edit Session, **RDP options...** (next to Extra arguments, enabled for RDP sessions) overrides any of the options above for that session. Each dropdown starts with "Default (...)" showing the global value. Checkboxes are three-state: filled = Default, ticked = on, empty = off. An empty gateway box means Default, and **Don't use a gateway** overrides a global gateway. The button reads **RDP options ✓** when the session overrides something.
- **Storage:** only the overridden options, in the session's `RdpOptions` attribute in `Sessions.xml`, e.g. `RdpOptions="Display=Fixed;Width=1280;Height=720;Drives=1"`. Sessions without overrides don't get the attribute (`SessionData.ShouldSerializeRdpOptions`), so existing sessions are saved exactly as before. In a shared `Sessions.xml` the overrides are shared too, which suits them, since they describe the server.
- **Connecting:** `RdpControl` connects with `RdpOptions.EffectiveFor(session)`, the global options with the session's overrides on top, fixed when the tab opens.
- **Code:** `Utils/RdpOptions.cs` (parse, format, defaults and overlay); `Gui/dlgRdpSessionOptions.cs` / `.Designer.cs`; `SessionData.RdpOptions`; `dlgEditSession.buttonRdpOptions_Click`. The Extra arguments box is narrower to make room for the button.
- **Checked against the compiled assembly:**
  - The text round-trips; unknown keys and bad values are ignored; `Gateway=` is kept as "no gateway".
  - Overrides win over the defaults, and everything else falls back.
  - A session without overrides has no `RdpOptions` attribute; one with overrides round-trips through XML and through Duplicate.
  - The dialog shows the stored overrides, with untouched options in the Default state, and reads them back unchanged. All Default gives an empty string.

**Send Commands, the button bar and SPSL in RDP tabs:** these used to type by posting `WM_CHAR` to a terminal's window, which an RDP tab doesn't have, so they skipped RDP tabs ("No active session to send to"). Now:
- `ctlPuttyPanel.SendCommand(CommandData)` sends to either kind of tab: window messages for a terminal, keystrokes for an embedded application that implements `ICommandReceiver` (new, `Utils/ICommandReceiver.cs`). `CanReceiveCommands` tells the senders which tabs qualify.
- `RdpControl` implements it with the control's `SendKeys`. It's on `IMsRdpClientNonScriptable`, which is IUnknown-only, so it's declared by hand with every method before `SendKeys` in vtable order. Text is turned into scan codes with the local keyboard layout (`VkKeyScan`, `MapVirtualKey`), wrapped in Shift/Ctrl/Alt as needed; keys like Enter or Ctrl+C come from `CommandData.KeyData`, and extended keys (arrows, Home, Delete, ...) get the extended flag. The call is made on the UI thread, where the control lives, and failures (e.g. while reconnecting) are logged rather than thrown. Keystrokes are only sent after login.
- The Send Commands toolbar and the button bar (both send strings and SPSL scripts) send through `ctlPuttyPanel.SendCommand`. Button bar SPSL scripts are run line by line with `SPSL.TryParseScriptLine`, as `SPSL.BeginExecuteScript` does, but sent through the tab.
- The remote should use the same keyboard layout as this computer. Characters the local layout can't type are skipped and logged.

**Checked outside the app (options and keys):** with test values set in memory, `Start()` applied every option, read back from the control: colour depth 24, smart sizing, clipboard and drives, audio "don't play", Windows keys "on the remote", authentication "warn", and the gateway with its usage, profile and credential modes. The hand-declared `SendKeys` returned a clean `E_FAIL` on an unconnected control, as expected. The scan codes are `a` = 1E, `A` = Shift 2A + 1E, Enter = 1C, Ctrl+C = 1D + 2E, and Left = 4B with the extended flag. Typing into a logged-in session is tested by hand.

**How it fits in:**
- **`Gui/RdpControl.cs` (new):** an `AxHost` for `MsRdpClient9NotSafeForScripting` (CLSID `8b918b82-7985-4c24-89df-c33ad2bbfbcd`). It's late bound through `dynamic`, so no generated interop assemblies are needed. Only the two events used (`OnLoginComplete`, `OnDisconnected`) are declared, in a hand-written `IMsTscAxEvents` dispinterface.
- **`Utils/IEmbeddedApplication.cs` (new):** `Start`, `FocusApplication` and an `Ended` event, for anything hosted as a control rather than a captured process.
- **`ctlApplicationPanel.cs`:** with `EmbeddedApplicationFactory` set, `StartApplication` adds the control and fills the tab with it instead of starting a process. `Ended` closes the tab like a process exit, and focus goes to the control. RDP tabs are still `ctlPuttyPanel`s, so layouts, closing, icons and restart work as before.
- **`ctlPuttyPanel.cs`:** sets the factory for RDP sessions when `RdpControl.ShouldEmbed` says so; placeholders are resolved first.
- **`Utils/RDPStartinfo.cs`:** mstsc (when the built-in client is off) gets upstream's arguments again. FreeRDP gets `/size:` set to the tab's size instead of a fixed 1920x1080, through `ApplicationPanel.ApplicationParametersForSize`.
- **`SuperPutty.csproj`:** references `Microsoft.CSharp`, which `dynamic` needs; it's part of .NET Framework.

**Checked outside the app:** `RdpControl` was hosted on a hidden form and connected to `127.0.0.1` port 1 (closed). The control was created; the server, port, username, desktop size (the control's 884x601), smart sizing and CredSSP were all accepted; `Connect()` started connecting. The disconnect event (reason 516) arrived through the hand-declared event interface, and Windows' error text was read. A real login, resizing while connected, and the tab itself are tested by hand.

## Secret values in Environment Variables

A variable can be marked **Secret** (new column in Tools → Environment Variables), e.g. a vault password used in a `{key}` placeholder.

- **Storage:** a secret is written to `EnvironmentVariables.json` only as `ProtectedValue`, encrypted with Windows DPAPI for the current user (with extra entropy, `SuperPuTTY.EnvironmentVariables`). Only that Windows user on that computer can decrypt it. Ordinary variables are written as before. This is done with Json.NET `ShouldSerializeValue` / `ShouldSerializeProtectedValue` in `Models/EnvironmentVariable.cs`; `EnvironmentVariablesManager` encrypts on save and decrypts on load.
- **On screen:** secret values show as dots in the dialog and are edited in a password box. The Edit Session preview shows them as dots (`SessionVariables.Load(maskSecrets: true)`).
- **A value that can't be decrypted** (the file was copied from another user or computer) loads as empty, with a warning in the log, and has to be entered again.
- **Logs:** `CommandData.SendToTerminal` (upstream) logged every command's full text; it now logs only the length, since a button or script can type a secret.
- **Checked against the compiled assembly:** a secret serializes with no plain text in the JSON, decrypts back to the original, and a corrupted value comes back empty rather than throwing.

## Button bars that follow the tab's icon

Each button bar can be linked to tab icons: right-click the bar → **Show for Tab Icons...**, then tick one or more session icons (e.g. `windows`, `windows_xp`). The list stays open while you tick. When a tab becomes active, the bar switches to the first bar linked to that tab's icon. Sessions without an icon count as the default `computer` icon. When no bar is linked to the icon, it shows the bar you last picked from the dropdown; picking a bar by hand shows it straight away and makes it that fallback.

- **Storage:** the links are stored with each bar in `ButtonBars.xml` (`<TabIcons><Icon>windows</Icon></TabIcons>`, written only when set), so a shared file shares them.
- **Code:** `ButtonBar.TabIcons` in `Data/ButtonBarData.cs`. In `ButtonBarStrip`, `ShowBarForTabIcon` is called from `frmSuperPutty.dockPanel1_ActiveDocumentChanged`, `PickBarForTabIcon` chooses the bar, and `ShowTabIconMenu` is the icon checklist.

## Session logs and SPSL WAITFOR

**Session logs:** every PuTTY session (SSH, Telnet, Rlogin, Raw, Serial) is started with `-sessionlog "<file>" -logoverwrite`, which needs PuTTY 0.75 or later. The file name has the session name and start time, so PuTTY never asks to overwrite.
- With **Tools → Options → Advanced → Session logs → Keep a log of every PuTTY session** on, logs are kept in the chosen folder (default `Documents\SuperXPuTTY\logs`, deliberately not the possibly shared settings folder).
- Otherwise each session writes a temporary log in `%TEMP%\SuperPuTTY\logs`. It's deleted 5 s after its tab closes, and leftovers older than a day are purged at startup.
- It's the only way SuperPuTTY can see a session's output, since PuTTY draws the terminal itself. The log contains what the terminal shows; typed passwords aren't echoed, so they aren't in it.
- **Code:** `Utils/SessionLog.cs`; `PuttyStartInfo.SessionLogPath`; `ctlPuttyPanel.SessionLogPath`; settings `SessionLogEnabled` and `SessionLogFolder`.

**`WAITFOR` (SPSL):** `WAITFOR Password:` waits until that text appears in the session's output, up to 30 s; `WAITFOR /t:10 Password:` sets the timeout in seconds. In a session's own login script, the first `WAITFOR` looks at everything since the session started. In a button-bar script it looks at the **current line** and anything after it: a prompt already on screen matches, but older output, such as the login prompt of a session that's already logged in, is ignored. After each command sent, it looks only at what comes next, and the next `WAITFOR` starts after the match. If the text doesn't appear in time, the script **stops**, so nothing is typed into the wrong prompt, and the status bar says why. Matching is exact and case-sensitive; trailing spaces in the text are dropped, as SPSL trims every line.

```
#!/bin/spsl
WAITFOR login as:
SENDLINE {user}
WAITFOR password:
SENDLINE {vault_pw}
WAITFOR #
SENDLINE terminal length 0
```

- **Progress and Stop:** while scripts run, the status bar shows an indicator, e.g. `▶ Login_Test (r1): line 2/3 · WAITFOR password: · 8 s`, or `▶ 3 scripts running`. Click it to see each script with a **Stop** entry, plus **Stop all** when several are running. A stop takes effect at once, even during a `WAITFOR` or a `SLEEP` (the runner does `SLEEP` itself so it can be interrupted), and nothing more is typed. The status bar then says "stopped by you at line N of M". Closing a tab stops its scripts. The indicator shows the command word, and `WAITFOR`'s text, but never what's being typed, which may be a secret. Code: `Utils/ScriptRun.cs` (progress and cancellation); `ctlPuttyPanel.RunScriptLines`; `frmSuperPutty.UpdateScriptsIndicator`, polled every 100 ms; `toolStripDropDownScripts` in the status bar.
- **Spinners:** while a script runs, the button-bar button that started it shows a spinning arc in place of its icon, and so does the tab it runs in. They go back to normal when it ends, is stopped or fails. The tab spinner needs Options → GUI → **Show tab icons**. It's drawn as a white badge with the blue arc, because the selected tab is painted in the theme's accent blue, which hid a bare blue arc. `Gui/Spinner.cs` draws 12 frames once, as bitmaps for buttons and 16x16 icons for tabs, and reuses them, so animating creates no new bitmaps or handles. `ScriptRun.Source` records which button started a run. Buttons are matched by label and text, since the bars reload whenever their menu opens. `ButtonBarStrip.UpdateRunningButtons` and `ctlPuttyPanel.ShowScriptRunning` are advanced by the same 100 ms timer.
- **Where it works:** `ctlPuttyPanel.RunScript` runs scripts line by line on a background thread, stopping on a timeout or an error. It's used for a session's own SPSL script when the session opens (in place of `SPSL.BeginExecuteScript`) and for button-bar scripts. Through Send Commands' script editor, `WAITFOR` waits but can't stop the script.
- **Where it doesn't:** RDP, VNC, cmd and other non-PuTTY tabs have no session log, so `WAITFOR` there logs a warning and counts as a timeout.
- **Code:** `Scripting/WaitForCommand.cs`; `CommandData.WaitFor` / `WaitTimeout`; `ctlPuttyPanel.WaitForOutput`, `SendCommand` and `RunScript`.
- **Checked against the compiled assembly (button scripts):** on a log that looks like a logged-in session, `WAITFOR login as:` ignored the old prompt and `WAITFOR $` matched the prompt on the last line. On a fresh session at `login as: `, it matched.
- **Checked against the compiled assembly:** the parsing (default, `/t:5`, missing text) and the PuTTY arguments, with the temp log path. On a real `ctlPuttyPanel`, with a test thread writing to its log as PuTTY would, the wait found text already there at once, found text written 1.5 s later at 1.53 s, and timed out on text already matched.

## Activity and silence alerts

Right-click a tab → **Watch for Activity**, or **Watch for Silence** → 10 seconds / 30 seconds / 1 minute / 5 minutes / Off. These are per tab and aren't saved.

- **Activity:** new output in the tab while you aren't looking at it.
- **Silence:** no output for the chosen time, after the tab has printed something. It fires once per quiet spell and re-arms when output resumes.
- **Signals:** an alert puts a badge on the tab's icon (an orange dot for activity, grey pause bars for silence, on a white disc so it shows on the selected tab too) and a message in the status bar. If SuperPuTTY isn't in front, the taskbar button also flashes until it is.
- **Clearing:** opening the tab, with SuperPuTTY in front, clears the badge.
- **Badge priority:** a running script's spinner wins over the badges (`ctlPuttyPanel.UpdateTabIcon`).
- **"Looking at the tab"** means it's the active tab and SuperPuTTY is in front. While you type in a terminal, Windows reports the embedded PuTTY window as the foreground window, so that counts too (`frmSuperPutty.IsInFront`).
- **How it works:** the session log's size is checked about once a second, so it needs a session log and is only available for PuTTY sessions.
- **Code:** `ctlPuttyPanel.CheckOutputAlerts` / `SetOutputAlert`, `Gui/TabBadges.cs`, and the 100 ms script timer in `frmSuperPutty`, which checks alerts on every 10th tick.

## Search a tab's output

Right-click a tab → **Search Output...** opens a window (one per tab, closed with the tab) that searches everything the tab has printed, from its session log; PuTTY itself has no scrollback search.

- **Options:** match case, and regex. An invalid pattern shows the error instead of searching.
- **Results:** the matching lines are listed with their line numbers, up to 5,000. Double-click a line to copy it; **Open log** opens the whole log.
- **Cleanup:** colour and cursor codes left in the log (with or without the escape character, e.g. `[01;34m`) are removed. Window-title sequences (`]0;user@host: ~`) are removed only when they still end with BEL or ESC \. Without that ending there's no telling where the title stops, so it's kept rather than risk hiding the prompt and command after it (`SessionLog.CleanForDisplay`).
- **Code:** `Gui/dlgSearchOutput.cs` / `.Designer.cs`; `dlgSearchOutput.MakeMatcher`.

**Checked against the compiled assembly:**
- Cleanup: colour codes, terminated titles and carriage returns are removed; an unterminated title keeps the command after it.
- Matching: plain text, match case and regex work, and an invalid regex is reported.
- Alerts on a real tab object: output while unseen gives Activity; looking at the tab clears it; output while looking gives no alert; 1 s of quiet with a 1 s silence watch gives Silence, once, and again after the next output and quiet spell.

## Dropped sessions stay open and reconnect

When a PuTTY session drops (network error, the device reloads, the server times out an idle login), the tab now stays open with its scrollback, gets a red-cross badge, and reconnects in the same window.

- **Detecting a drop:** PuTTY shows a "PuTTY Fatal Error" box with the reason (e.g. "Remote side unexpectedly closed network connection"). About once a second SuperPuTTY looks for that box among the windows of each tab's PuTTY process.
- **Badge and status:** the tab gets a red-cross badge, which beats the activity and silence badges; only a running script's spinner beats it. The status bar shows the reason, and if SuperPuTTY isn't in front, its taskbar button flashes.
- **Reconnecting automatically** (Tools → Options → Advanced → **Dropped sessions**; on by default, every 10 s, up to 6 times):
  - SuperPuTTY closes PuTTY's error box and, after the delay, sends PuTTY's own **Restart Session**. That reconnects in the same window and appends to the same session log, so Search Output and alerts carry on.
  - A failed attempt shows a new error box, which counts as an attempt; after the last one it gives up and says so.
  - The session counts as back once it prints something again (e.g. `login as:`), or after 15 s active with no output. The badge then clears and the status bar says "reconnected".
- **Reconnecting by hand:** right-click the tab → **Reconnect** (bold, at the top, shown only while the session is down). It closes PuTTY's error box if it's up, then restarts. **Stop Reconnecting** cancels a scheduled automatic attempt.
- **Auto-reconnect off:** PuTTY's error box stays up as before, and the tab still gets the badge.
- **Sessions that end without an error,** e.g. a non-zero exit status, leave PuTTY open as "(inactive)". They get the badge, but aren't reconnected automatically.
- **Unchanged:**
  - A clean exit (typing `exit`) still closes the tab.
  - This relies on the PuTTY session's "Close window on exit" being "Only on clean exit", PuTTY's default. With "Always", a drop closes the tab as before.
  - Raw-protocol sessions also close on a reset, because PuTTY exits.
  - The session's SPSL login script isn't re-run after a reconnect.
- **RDP tabs:** the Remote Desktop control's own auto-reconnect (`EnableAutoReconnect`, with its "reconnecting" overlay) follows the same checkbox. If it gives up, the tab closes as before.
- **Code:** `Utils/PuttyWindow.cs` finds, reads and closes the error box, reads the "(inactive)" title, and sends Restart Session. `ctlPuttyPanel.CheckConnection` / `Reconnect` and the connection state machine are called from the timer in `frmSuperPutty`. Also `TabBadges.Disconnected`, and the settings `AutoReconnect`, `AutoReconnectDelaySeconds` and `AutoReconnectAttempts`.

**Checked against PuTTY 0.85 and the compiled assembly:**
- On a telnet session whose connection was reset, `PuttyWindow` found the error box and read "Network error: Software caused connection abort".
- Closing the box left the window "(inactive)". Restart Session reconnected the same window; the session log was appended to, not overwritten; and the window was active again.
- An idle SSH login to 192.168.3.2 was dropped by the server after 2 minutes with "Remote side unexpectedly closed network connection".
- A restart with nothing listening showed a new error box ("Connection refused").

## Classic tab look

Tools → Options → GUI → Interface theme has a fourth choice, **Classic**: the tab look of the original [jimradford/superputty](https://github.com/jimradford/superputty).
- **Look:** angled tabs, white when selected and shaded when not, with one close button and the tab-list drop-down at the right end of the tab strip instead of an X on every tab. It's DockPanelSuite's VS2005 theme (the `DockPanelSuite.ThemeVS2005` 3.1.1 package, matching DockPanelSuite 3.1.1).
- **Why:** upstream moved to the VS2015 themes to get dark mode, which changed the tabs.
- **Light only:** with Classic, dialogs stay light like the Light theme. The existing `InterfaceTheme < LightTheme` dark-mode checks still hold because `ClassicTheme` comes after `LightTheme`.
- **Applying it:** like the other themes, it applies after a restart.
- **Options page:** the theme labels were shortened (Dark / Dark blue / Light / Classic) to fit four.
- **Installer:** `WeifenLuo.WinFormsUI.Docking.ThemeVS2005.dll` is added to `Product.wxs`.
- **Code:** `InterfaceTheme.ClassicTheme` (`SuperPuTTY.cs`), the theme switch in `frmSuperPutty`'s constructor, `optClassicTheme` in `dlgFindPutty`.

## Current tab marked in the tab list, softer Dark tab colours

- **Tab list:** the drop-down arrow at the right end of a tab strip lists every tab, but DockPanelSuite never showed which one is current, so with many same-named tabs there was no telling. The current tab is now ticked and bold, in every theme. DockPanelSuite fills that menu just before showing it, tagging each item with its tab, so SuperPuTTY marks the matching item in the menu's `Opening` event. The menu is a private field of each theme's tab strip, found by its type. Panes are re-checked when content is added and when the active document changes, so split-off and floating panes get it too.
- **Dark theme colours:** the selected tab is now a muted slate blue (#3C4A5C) instead of bright blue (#007ACC). Hovered tabs and the close-button highlights are muted to match. Dark blue and Light are unchanged.
- **Readable check marks in dark menus:** both renderers involved build on `ToolStripProfessionalRenderer`: DockPanelSuite's Visual Studio renderer for the tab list, and DarkModeCS's renderer for the tab's right-click menu (e.g. "Accept commands"). That renderer draws Windows' black check glyph, which almost vanished on the dark check box. `Gui/MenuCheckMarks.cs` handles each renderer's `RenderItemCheck` event, which is raised after the glyph is drawn, and paints a light tick over it. It does this only when the check box colour is dark, so light themes are untouched. It's applied to each tab's menu and to the tab-list menus.
- **Code:** `Gui/DocumentTabs.cs` (`MarkCurrentTabInLists`, `SoftenDarkTheme`), `Gui/MenuCheckMarks.cs`, called from `frmSuperPutty` (`SoftDarkTheme`, `DockPanel_ContentAdded`, `dockPanel1_ActiveDocumentChanged`).

**Checked against the compiled assembly:** with four same-named tabs in a real DockPanel, opening the tab list through DockPanelSuite's own button handler ticked and bolded only the active tab, in both the Classic (VS2005) and Dark themes. After switching tabs, the mark moved. The Dark palette's selected tab came out as #3C4A5C. The rendered menus were also checked: with DockPanelSuite's dark renderer, the check box had no light pixels before the fix and showed a white tick after it. With DarkModeCS's renderer, the tick was redrawn white too; the harness had only that renderer's light colours, so real dark mode wasn't checked there.

## Custom icons, and no more theme icon folders

Every icon in the old `themes\default\icons` set now has a drawn replacement, so SuperPuTTY no longer reads those folders. Your own icons go through the icon picker instead.

- **Adding one:** icon picker → **Add icon...**, choose a PNG, and name it. It's then selected, so the large preview shows how it came out.
  - **The rules:** the PNG must be square and at least 32x32. SuperPuTTY keeps it as a one-colour shape and colours it like its own icons, so it fits the same colour coding (`CustomIcons.MakeShape`).
  - **One-colour shapes on transparency** are used as they are.
  - **Anything else** uses its **dark parts** as the shape: a black outline with a white fill, line art on a white background, a two-tone icon. Light areas become see-through.
  - **Refused only when there's no clear shape:** blank, or dark all over like a photo. The message gives that one reason and a one-line tip. Names may use letters, digits, spaces, dots, dashes and underscores.
  - **Default colour:** the shape's own colour, with black and near-black shapes defaulting to grey so they show on the dark theme.
- **Where they're kept:** as PNGs in `<SettingsFolder>\icons`, next to Sessions.xml, so a team sharing the settings folder also shares the icons its sessions use.
  - **Format:** each is stored in one flat colour, at up to 128x128. The colour it was uploaded in becomes its default.
- **Using one:** keys are `custom:<name>`, with an optional colour, e.g. `custom:juniper#1E63B4`. The picker lists them under "Custom".
- **Removing one:** select it in the picker → **Remove**. Sessions and buttons using it then show the default icon.
- **The old folders:** they're no longer read, copied by the build, or installed. The 47 old PNGs are gone from the repo and installer, which also removes the uncredited Silk files from them.
  - **Old keys still work:** keys naming those icons (`green_switch`, `book`, `tux`...) still map onto the drawn ones.
  - **Other old custom PNGs:** a PNG someone added to an old theme folder under another name is no longer shown. Add it again with Add icon...
  - **Removed code:** `SuperPuTTY.GetThemeIconFolders`, the post-build theme copy, and the installer's `DefaultTheme` component group.
- **Still embedded:** the app's built-in images in `Properties/Resources` (mostly Silk) are untouched. SuperPuTTY now draws the toolbar ones itself.
- **Button bar icons:** Edit Button's **Icon** list keeps the coloured circles (and "Custom color..."). Instead of listing every image key, old and new, it shows the button's icon, if any, and **Choose icon...**, which opens the same picker as Edit Session, with colours and custom icons.
- **Code:** `Gui/CustomIcons.cs`, the Add icon / Remove buttons in `Gui/dlgIconPicker.cs`, `IconLibrary` (custom keys), and `dlgEditCommandButton.ChooseIcon`.

**Checked against the compiled assembly**, with a scratch settings folder:
- **Uploads:** each was added with the expected shape:
  - a smiley (black outline, white face, transparent corners): the outline became the icon and the face see-through;
  - black on white;
  - a red-and-blue picture;
  - a one-colour triangle.
  A dark-all-over image, a blank one, a 16x16 one, a 64x32 one, a duplicate name and a name with "/" were each refused with their own message.
- **Using it:** the icon was in the image list, drew in red and blue at 16–32 px, and opened selected (and scrolled into view) in the picker. `green_switch` and `book` still resolved.
- **Not checked:** the Add icon and Remove dialogs and the button editor's Choose icon... weren't clicked through.

## Fix: Edit Session replaced a missing PuTTY session profile with Default Settings

**Symptom:** a session whose PuTTY session profile isn't saved in this user's PuTTY or KiTTY, e.g. from a shared Sessions.xml, or before PuTTY is set up, opened in Edit Session showing "Default Settings". Clicking Save then overwrote the profile name in Sessions.xml.

**Cause:** `dlgEditSession` only selected the profile if it was in the list read from PuTTY, and otherwise left the default selected.

**Fix:** a missing profile is added to the list and selected, so Save keeps it.
- **In red:** the profile itself shows in red, in the field and in the open list. The list is drawn by SuperPuTTY, because the dark theme ignores a drop-down list's text colour.
- **Note:** a red note under the list says "Not saved in PuTTY yet" (or KiTTY). Its tooltip explains where SuperPuTTY looked, and that the session opens with Default Settings until a session of that name is saved.
- **Save isn't blocked.**
- **Code:** `dlgEditSession.ShowProfileMissing`, `comboBoxPuttyProfile_DrawItem` and `IsProfileSaved`.

## Portable KiTTY session profiles

With the PuTTY path (Tools → Options) set to KiTTY, SuperPuTTY now finds KiTTY's saved sessions when KiTTY keeps them in files rather than the registry. That's always the case for `kitty_portable.exe`, and for `kitty.exe` with `savemode=dir` in the `kitty.ini` next to it.
- **Where:** the sessions are read from `<configdir>\Sessions`, where `configdir` comes from `kitty.ini` and defaults to KiTTY's folder. File names are URL-encoded like registry keys, and `fileextension` from `kitty.ini` is stripped if set.
- **Used by:**
  - the session profile lists: Edit Session, the quick-connect toolbar, and the session property grid;
  - the missing-profile note;
  - File → Import → from PuTTY/KiTTY (`PuttyDataHelper.ReadKittySessionFile`).
- **Previously:** only the registry was read, so portable KiTTY's sessions never appeared.
- **Refreshing:** the quick-connect toolbar's Session list is now re-read each time it opens. Before, it was filled at startup, so sessions saved in PuTTY or KiTTY afterwards only showed up after restarting SuperPuTTY. Edit Session already re-read the list every time it opened.
- **Code:** `PuttyDataHelper.KittySessionFolder`, `ReadKittyIni`, `GetKittySessionFiles`, `SessionStoreDescription`, `GetSessionNames`.

**Checked against the compiled assembly:**
- With the PuTTY path set (in memory) to `kitty_portable.exe`, the profiles were read from `KiTTY\Sessions`: Default Settings, ssh_key, test and test123, matching KiTTY's own list. `bogus` was reported missing, and the import read all four files.
- With `putty.exe`, the registry was read as before.

## Sharp, larger, colourable icons

Session icons were 16x16 PNGs (mostly the famfamfam "Silk" set, plus custom network icons in colour variants such as `green_switch` and `blue_router`). They were loaded into a 16 px list that ignored display scaling, so on a 125%+ display they looked tiny and pixelated next to other terminal programs. Icons are now drawn at the size they're shown.

- **Icon size:** Tools → Options → GUI → Misc → **Icon size**: 16, 20, 24 (default) or 32. It's scaled for the display and applies after a restart.
  - **Covers:** the session tree, document tabs (bigger icons also make the tab font, and so the tabs, a little taller), the top toolbars and the button bar.
  - **Tab icons** are as tall as the tab text, vertically centred, with a small gap before the text (`Gui/TabIcons.cs`).
    - **Why the extra code:** DockPanelSuite draws a tab's icon in a fixed 16x16 box near the tab's bottom edge. That size is a compile-time constant in both tab styles and isn't scaled for the display, so on a 150% screen the icon was small and sat low.
    - **The workaround:** tabs get a transparent placeholder icon, so DockPanelSuite keeps the icon space but draws nothing there. `TabIcons` then draws the real icon after each tab-strip paint, by listening for the strip's `WM_PAINT`; the strips raise their `Paint` event before drawing the tabs.
    - **Making room:** the tab text gets blank padding in front (U+2800, the blank Braille pattern, which draws as space but isn't trimmed as whitespace), so DockPanelSuite itself widens each tab and moves the text right by the room the larger icon needs.
    - **Who uses it:** set tab icons with `TabIcons.Set`, not the `Icon` property (`SuperPuTTY.ApplyIconForWindow`, the badges in `ctlPuttyPanel`). The tab-list menu shows the real icons and strips the padding.
    - **Classic tabs** have a slanted left edge and start their text a few pixels earlier, so their icons start further in and are a little smaller.
- **Device templates** (`Gui/IconTemplates.cs`) are drawn in code, so they're sharp at any size, and their body takes any colour.
  - **Templates:** switch, layer 3 switch, stack switch, core switch, distribution switch, router, voice router, data centre (Nexus) switch, firewall, VPN concentrator, wireless, access point, wireless controller, load balancer, console server, UPS, server, workstation, terminal, Windows, database, storage, cloud, IP phone, camera, printer, folder and flag.
  - **Colour coding:** "green switch = layer 2, blue switch = layer 3" is one template in two colours.
  - **Core and distribution switches** show what they do: the core switch has the layer 3 arrows going out from a hub, the distribution switch one uplink fanning out to three.
  - **3D boxes** (data centre switch, stack switch, wireless controller, VPN concentrator, access point) follow the network-diagram symbols; the voice router is a call symbol (a handset with arrows each way), redrawn because the old handset read as a blob.
  - **General icons** replace the rest of the old Silk PNGs, drawn the same way: home, globe, map, truck, book, document, archive, email, chat, feed, clock, help, information, light bulb, lightning, television and mobile device. The picker lists them under "General".
  - **The top toolbars' buttons** (connect, clear, send, mask, run script, select tabs, the icon filter's "stop") are drawn the same way, so they match.
- **Logos** (`Gui/BrandLogos.cs`): 26 OS and vendor logos from [Simple Icons](https://simpleicons.org) 16.36.0. The icon data is CC0 (`Resources/third-party/simple-icons-LICENSE.md`), and the logos remain their owners' trademarks.
  - **The list:** Ubuntu, Debian, Red Hat, CentOS, Fedora, Arch, Mint, openSUSE, SUSE, FreeBSD, OpenBSD, Linux, Apple, Android, Cisco, Palo Alto, Fortinet, VMware, Proxmox, Docker, Kubernetes, Raspberry Pi, Alpine, Rocky, Alma and Kali.
  - **Built in:** each is stored as one SVG path plus the brand colour and drawn through `Gui/SvgPath.cs` (an SVG path → GraphicsPath reader), so nothing is downloaded at run time.
  - **Dark themes:** near-black logos are lightened so they show.
  - **Windows:** Simple Icons no longer carries Microsoft's logos, so the drawn four-pane "windows" template stands in.
- **Icon keys** (`Gui/IconLibrary.cs`): a session's `ImageKey` is a name with an optional colour, e.g. `switch`, `switch#1E63B4` or `ubuntu`. Coloured keys are drawn into the image list on first use.
  - **Old keys still work:** they map onto templates and logos, e.g. `green_switch` → `switch#2E7D32`, `red_big_nexus` → `nexus#8E1B1B`, `flag_red` → `flag#C62828`, `tux` → `linux`, `puffy` → `openbsd`, `computer`/`monitor` → `workstation`, `world` → `globe`, `lorry` → `truck`, `report` → `document`. Every icon in the default set now maps to a drawn one, so existing Sessions.xml files keep their icons, now sharp.
  - **Your own icons:** see "Custom icons, and no more theme icon folders" below.
  - **Older SuperPuTTY versions** reading the same Sessions.xml show their default icon for a coloured key.
- **Icon picker** (`Gui/dlgIconPicker.cs`), from Edit Session's icon button:
  - **Layout:** a searchable grid grouped as Devices, General, Logos and Custom, plus a large preview.
  - **Dark theme:** the grid uses Windows' dark "items view" style, so the group headings are readable. DarkModeCS's dark Explorer style left them dark blue.
  - **Colour row:** Default, ten named colours, and More... for any colour; More... is outlined when the chosen colour isn't one of the ten.
  - **Colour preview:** with a device or general icon selected, every device and general icon shows the chosen colour, so you can compare a colour across the whole set (handy for demos). A logo or custom icon shows it only on its own tile. The large preview always shows it.
    - **Before:** first the whole device and general set was recoloured, even when a logo was selected; then the selected icon's whole group, so choosing white for Apple turned every logo white, which looked like a fault; then only the selected icon, which made comparing colours slow.
  - **Explicit colours:** a colour chosen for a logo is used as it is. Only near-black brand colours are lightened on the dark theme.
  - **Result:** the picker returns the key to store, with the colour left off when it's the icon's own.
  - **Replaces:** the old plain grid popup. The toolbar's icon filter still uses the grid.
- **Code:**
  - `SuperPuTTY.LoadImageList` (now `IconLibrary.Fill`) and `GetIconForSession`;
  - `frmSuperPutty.ApplyToolbarIcons`;
  - `SessionTreeview.IsValidImage`;
  - `ButtonBarStrip` (icon size, circle and spinner size);
  - the `IconSize` setting.

**Checked against the compiled assembly:**
- **Image list:** it was built from the installed icon folder at 24 px with 98 icons. `green_switch`, `router#C62828`, `tux`, `ubuntu`, `book`, `flag_red`, `computer`, `folder` and an arbitrary `switch#123456` all resolved; `nonsense` didn't.
- **Session tree:** a mock tree with the real session keys rendered in the light and dark themes.
- **Logos:** all 26 rendered at 16–64 px.
- **Picker:** it opened on `green_switch` with Switch selected, and choosing Blue returned `switch#1E63B4`.
- **Not checked:** this wasn't checked in the running app (tabs, toolbars, button bar, Options), nor at a display scaling other than 100%.

## Fix: "Show folders first" seemed to flip each time Options was saved

**Symptom:** with Tools → Options → GUI → "Show folders first" on, opening Options and clicking OK without touching it rearranged the Sessions tree: folders sometimes came first, sometimes mixed in with sessions, as if the option had been toggled.

**Cause:** upstream's tree sort (`SessionTreeview.Compare`) put a folder first only when it was the first of the two nodes compared. With the nodes the other way round, it compared names instead, so the comparison could say both "the folder goes first" and "the session goes first" for the same pair. The sort's result then depended on the order the nodes happened to be in, and saving Options re-sorts the tree (`Settings_SettingsSaving` → `ResortNodes`). The setting itself never changed.

**Fix:** the comparison is now symmetric (`SessionTreeview.CompareNodes`): with the option on, folders always go before sessions, and nodes of the same kind are sorted by name. The option's tooltip, which wrongly said "Show search box above sessions", now describes it.

**Checked against the compiled assembly:**
- **Old logic:** over a mix of 3 folders and 5 sessions, 24 pairs gave contradictory answers when swapped, and 200 sorts from random starting orders gave 124 different results.
- **New logic:** no pair disagreed, and all 200 sorts gave the same order: folders first, then sessions, each by name.

## Fix: SPSL's SENDKEY ENTER typed "E", and indented lines were skipped

**Symptoms:** two problems in SPSL scripts.
- **SENDKEY with a key name:** `SENDKEY ENTER` typed the letter "E" instead of pressing Enter. The button bar's script hint even suggested `SENDKEY ENTER`.
- **Indented lines:** a script line indented with spaces or a tab was silently skipped.

**Causes:**
- **SENDKEY:** upstream's `Commands.ParseKeys` only recognised key names in braces (`{ENTER}`); otherwise it typed the first character.
- **Indented lines:** `SPSL.TryParseScriptLine` didn't trim leading whitespace, so the command word was read as empty or unknown.

**Fix:**
- **SENDKEY** also accepts bare key names, with optional modifiers: `ENTER`, `^HOME`, `%F4` (`Commands.ParseKeyName`). Braces and single characters work as before. (The `{KEY n}` repeat count was still dropped: see "Fix: SENDKEY's Ctrl, Alt, Shift, symbols and repeat counts".)
- **Leading whitespace** is trimmed before a line is parsed.

**Checked against the compiled assembly:**
- **SENDKEY:** `ENTER` and `{ENTER}` both give Return; `^C`, `%F4` and `^HOME` give Ctrl+C, Alt+F4 and Ctrl+Home; `x` gives X; `{TAB 3}` and `PGDN` give Tab and Page Down.
- **Indentation:** `    SENDLINE indented` parses as SENDLINE.

## Script editing: Run as, saved scripts, command reference, script check, and the mask button

- **Run as** (Script Editor): a drop-down beside Send chooses how the text is sent, like the Action list in Edit Button.
  - **Send lines** types each line as it is, then Enter.
  - **SPSL script** runs SPSL commands.
  - **No marker line:** before, a Script Editor script ran as SPSL only if its first line was `#!/bin/spsl`, and that line was added and saved automatically.
  - **Still recognised:** session script files (Edit Session → SPSL script) are still recognised by that line, as before. An opened `.spsl` file, or an older script that starts with the line, switches Run as to SPSL; the line itself is just a comment.
- **Saved scripts** (`Scripting/ScriptLibrary.cs`, `Gui/ScriptLibraryPanel.cs`): a script library, so scripts are reused without file dialogs.
  - **Where it shows:** on the **left** of the Script Editor, and as a **Saved scripts** tab in Edit Button's SPSL panel, which lists SPSL scripts only.
  - **Inserting:** double-click a script (or **Insert**) to add it. An empty script box is replaced, and the Script Editor's Run as switches to the script's kind; otherwise it goes after the cursor's line. An old `#!/bin/spsl` first line is left out.
  - **Save as...** stores the current script under a name, as the kind being edited, asking before replacing one. **Delete** removes one.
  - **Kind:** each script remembers its kind by its extension: `.spsl` for SPSL scripts and `.txt` for lines to send. The list labels each as "SPSL" or "lines".
  - **Where they're kept:** in `<SettingsFolder>\scripts`, next to Sessions.xml, so a team sharing the settings folder shares its scripts. They're plain text files.
- **SPSL panel** (`Gui/SpslHelpPanel.cs`): the same component in the Script Editor (on the right, in SPSL mode) and in Edit Button (beside the script whenever the action is "Run SPSL script"; the dialog widens and grows taller to fit it). There's no Help button and no header line to add.
  - **Commands tab:** every SPSL command, with its syntax, what it does and an example (`Scripting/SpslReference.cs`), plus comments and `{key}` placeholders. `CLOSESESSION` is marked "not available": SPSL lists it but never implemented it.
  - **Insert example** (or double-click a command): adds the example after the cursor's line.
  - **Check script** (`Scripting/SpslChecker.cs`) lists the problems, one per line; clicking one selects its line and shows the full message.
    - **Errors:** unknown commands, with a suggestion for a near miss (e.g. SENDLNE → SENDLINE) or, for text such as a pasted shell command, how to type it (`ls -la` → "write SENDLINE ls -la"); missing or malformed arguments (SLEEP, WAITFOR and its `/t:`, SENDCHAR, SENDKEY, OPENSESSION); unknown SENDKEY keys and braces; `CLOSESESSION`.
    - **Warnings:** a saved session that doesn't exist, a `{key}` placeholder with no value, a SLEEP over a minute.
  - **Checked automatically:** before the Script Editor sends an SPSL script and before Edit Button saves a script. If there are errors, they're listed in the panel and you're asked whether to go ahead.
- **Script Editor:**
  - **Layout:** larger and resizable: saved scripts, then a monospaced script box that keeps long lines on one line, then the SPSL panel. In Send lines mode the script box takes the panel's space.
  - **Files:** its file buttons are **Open file...** and **Save to file...**, for importing and exporting.
  - **SPSL scripts** now run like button-bar scripts (`ctlPuttyPanel.RunScript`) in each selected session. Before, they went through upstream's line-by-line sender. Now `{key}` placeholders are replaced, and progress and Stop show in the status bar.
- **Edit Button:** switching the action to "Run SPSL script" no longer puts `#!/bin/spsl` in the script box; the action says what it is.
- **Mask button** (Send Commands toolbar): the icon shows the state. A grey eye means typing shows; an amber asterisk means it's masked. The tooltip says which.
- **Pasted AutoHotkey isn't SPSL:** a script like the AutoHotkey one that started this (hotkeys, `Send {Down 9}`, `WinWait`) is reported line by line as not SPSL.
- **Adding a command:** add it to `SpslReference.Entries` as well as `SPSL.keywords`, so the panel and the check know it.

**Checked against the compiled assembly**, with a scratch settings folder:
- **Saved scripts:** an SPSL script, an old-style one starting with `#!/bin/spsl`, and a lines script were saved as `.spsl`, `.spsl` and `.txt` and listed as SPSL, SPSL and lines. Re-saving a name as the other kind replaced the file rather than leaving two.
- **Inserting:** the lines script, inserted into the empty editor, switched Run as to Send lines. The old-style one came in without its header and switched Run as to SPSL. Inserting another appended it.
- **Edit Button:** it started with an empty script box (no header) and listed only the SPSL scripts.
- **Checker:** a correct script gave no problems without a header line. A script of typos and bad arguments gave each expected error or warning, and the pasted AutoHotkey script was flagged on every line.
- **Dialogs:** the Script Editor in both Run as modes, and Edit Button in SPSL mode, were rendered in the dark theme at 150% scaling.
- **Not checked:** sending from the Script Editor to live sessions, and the Save as... and Delete dialogs in the running app.

## Fix: Save stayed disabled in Create new session until the name box was left

**Symptom:** in Create new session, typing the host address first and then the session name left **Save** disabled, with an error mark beside the name, until you clicked out of the name box.

**Cause:** upstream checks the name only when the box is left (`Validating`). Tabbing past the still-empty name box marked it "Empty name". That mark, and the disabled Save it causes, stayed until the box was left again.

**Fix:** the name is checked as it's typed (`textBoxSessionName_TextChanged`), and a host typed after its error clears that error too. Save is enabled once there's a valid name and a host or PuTTY profile, without leaving any box. A taken name now shows its error as it's typed.

**Checked against the compiled assembly**, following the order above:
- **Leaving the empty name:** marked "Empty name", Save disabled.
- **Typing the host:** Save still disabled.
- **Typing the name, without leaving the box:** the mark cleared and Save was enabled.
- **Typing a taken name:** "Session with same name exists" appeared and Save was disabled; correcting it enabled Save again.

## Fix: dragging a folder left the sessions in its subfolders behind

**Symptom:** after dragging a folder that has subfolders (e.g. `Core`, holding `Core/Dist/sw9`) into another folder, the tree looked right, but the subfolders' sessions kept their old IDs. After a reload they were back where they started.

**Cause:** upstream's drop handler (`resetFoldersChildrenPaths`) gave new IDs only to the moved folder's own sessions, not to those further down.

**Fix:** a move now updates every level below the moved folder (`SessionTreeview.MoveNode`, using the existing recursive `UpdateSessionId`). Dragging and the new "Move to Private/Saved sessions" share this code.

**Checked against the compiled assembly:** moving `Core` (holding `Core/Dist/sw9`) into `Home` gave the session the ID `Home/Core/Dist/sw9`.

## Favorites, Recent, private sessions, and a larger search bar

The Sessions tree now has four top-level groups, always in this order: **Favorites**, **Recent**, **Saved sessions** and **Private sessions**.

- **Favorites** (star): sessions you've starred, as shortcuts. The sessions themselves stay in their folders.
  - **Adding one:** right-click a session → **Add to Favorites**, or drag it onto Favorites. **Remove from Favorites** (or Delete on the entry) removes the shortcut, never the session.
  - **The group:** shown only when it has something in it, and sorted by name.
- **Recent** (clock): the last 10 sessions you opened, most recent first.
  - **What counts:** opening a saved session from anywhere: the tree, the Quick Selector or Connect All. A layout reopening its sessions doesn't count, nor do quick connections.
  - **Clearing:** right-click Recent → **Clear Recent**. Delete on an entry removes just that one.
  - **Turning it off:** Tools → Options → GUI → **Show recent**.
- **Open or closed:** both groups start closed, with how many sessions they hold after the name ("Favorites (3)"). Every top-level group has an open/close arrow, like the folders (`ShowRootLines`; before, they had none, so a closed group didn't look openable). Opening or closing one yourself (the arrow, double-click, the keyboard) is remembered between runs, in `SessionShortcuts.json` (`FavoritesExpanded`, `RecentExpanded`); a search opening everything, Expand all and Collapse all aren't (`SessionTreeview.RememberGroup` checks the event's `TreeViewAction`). Clearing a search puts them back as you left them.
- **Entries in both:** double-click or Enter opens the session. Right-click offers **Connect**, **Show in Sessions tree** and removing the entry, plus **Add to Favorites** in Recent. The tooltip says where the session lives. A session that's renamed or moved stays a favourite; a deleted one is dropped from both.
- **Per user:** both lists are in `SessionShortcuts.json` beside your `SuperXPuTTY.settings` (`Data/SessionShortcuts.cs`), not in `Sessions.xml`, so starring a session in a shared settings folder doesn't change it for the team.
- **Private sessions** (padlock): sessions only you see, in `PrivateSessions.xml` beside your `SuperXPuTTY.settings` instead of the shared `Sessions.xml`.
  - **Creating one:** right-click Private sessions → **New session...** or **New folder**.
  - **Moving:** right-click a session or folder → **Move to Private sessions** / **Move to Saved sessions** moves it to the same folder path on the other side, creating folders as needed. Dragging between the two also works.
  - **Everywhere else:** they work like any session: the Quick Selector, search, layouts, Connect All, `OPENSESSION`. Each session's `IsPrivate` (not saved in either file) says which file it's saved to; `SuperPuTTY.SaveSessions` splits them. `PrivateSessions.xml` is only written once you have, or had, a private session.
  - **IDs:** session IDs must be unique across both, since the two can have the same folders.
    - **Creating or renaming:** a session whose ID is already used on the other side is refused, with a message saying where.
    - **Moving:** a move that would clash is refused.
    - **Loading:** a private session whose ID clashes with a shared one on load is renamed (`Lab/sw1` → `Lab/sw1-1`) and logged.
  - **Shared layouts:** a layout that includes a private session opens without it for anyone else.
- **Toolbar** under the search box, like mRemoteNG's, built in code in `SessionTreeview.BuildToolbar`:
  - **New session** and **New folder** add to the selected folder, or to the selected session's folder; with Favorites or Recent selected, they add to Saved sessions.
  - **Expand all** and **Collapse all**. Collapse all closes Favorites and Recent too, leaving Saved sessions and Private sessions open at their top-level folders.
  - **Sort** switches names between A to Z and Z to A. Folders stay first if Options says so, and Recent stays newest first. The choice isn't saved, so it's back to A to Z after a restart.
  - **Icons:** drawn at the icon size, like the main toolbars (`newsession`, `newfolder`, `expandall`, `collapseall`, `sortaz`, `sortza`).
- **Search bar:** follows Tools → Options → **Icon size**, like the toolbars. The text is larger, and the search and clear buttons are drawn icons (`search`, `clear`) at the box's height instead of the old 16 px pictures.
- **New icons:** `star` and `lock` (General, so sessions can use them too), plus the `search` glyph and the toolbar's glyphs.

| File | Change |
|---|---|
| `Data/SessionShortcuts.cs` | New. The Favorites and Recent lists, whether each group was left open, `SessionShortcuts.json`, and the `Changed` event the tree redraws on |
| `Data/SessionData.cs` | `IsPrivate`, not serialized |
| `SuperPuTTY.cs` | `UserSettingsFolder`, `PrivateSessionsFileName`; `LoadSessions` reads both files and `SaveSessions` writes both; `OpenProtoSession` adds to Recent unless `IsRestoringLayout` |
| `frmSuperPutty.cs` | Sets `SuperPuTTY.IsRestoringLayout` while a layout loads |
| `SessionTreeview.cs` | The groups and the Private sessions root, their menus (built in code), moving between roots, drop on Favorites, the search bar size, the toolbar and its temporary sort order |
| `Gui/IconTemplates.cs` | `star`, `lock`, `search`, and the toolbar's glyphs |
| `dlgFindPutty.cs` / `.Designer.cs` | **Show recent** |
| `Properties/Settings.*`, `App.config` | `SessionsShowRecent` (default on) |

**Checked against the compiled assembly**, with a scratch settings folder and scratch files:
- **Lists:** `PushRecent` puts a session first without duplicates and keeps at most 10. `ReplaceId` follows a rename.
- **Loading:** shared `Lab/sw1` and `Lab/r1`, plus private `Lab/sw1` and `Home/pi`, loaded as four sessions, with the private `Lab/sw1` renamed `Lab/sw1-1`.
- **Tree:** with two favourites and two recent sessions, it showed Favorites, Recent, Saved sessions and Private sessions in that order, Recent most recent first.
- **Moving `Lab/r1` to Private:** it moved, stayed a favourite, and the files split as expected (`Sessions.xml`: `Lab/sw1`; `PrivateSessions.xml`: `Home/pi`, `Lab/r1`, `Lab/sw1-1`). Moving it back made it shared again.
- **Moving a folder:** moving folder `Core` with a subfolder also moved `Core/Dist/sw9`.
- **Clashing name:** creating a shared `Home/pi` was refused: "A session with this name is already in the same folder of Private sessions".
- **Search bar:** rendered at icon sizes 16, 24 and 32: the box is 34, 37 and 41 px tall at 150% scaling (was 22 px before scaling).
- **Toolbar:** rendered under the search box with the dark renderer. Sort Z to A put `sw1` before `r1` and `Lab` before `Home`, kept the top-level order, and switched back. New with `Lab/sw1` selected targets `Lab`.
- **Not checked:** the running app (menus, dragging, the dark theme, Options), and opening a session to see it reach Recent.

## Fix: the installer didn't include Newtonsoft.Json.dll

`SuperPuttyInstaller/Product.wxs` lists every file the MSI installs. `Newtonsoft.Json.dll`, which the Environment Variables feature needs (and so, through `{key}` placeholders, every session launch), was never added, so an installed copy would fail to load it. It's now a component of its own. The built MSI's File table lists it next to the exe, its config, log4net and the two DockPanel DLLs.

## Fix: searching sessions with extra arguments recompiled two regexes per session

**Symptom:** found while timing 10,000 database sessions. A search over sessions that have Extra arguments spent most of its time compiling regular expressions: 10,000 calls of `CommandLineOptions.replacePassword` took 48 s, and of `getcommand` 49 s. Search calls `replacePassword` on every session's extra arguments, to hide `-pw`. `getcommand` reads the `-pw` password and, since the WinSCP fix, `-i`.

**Cause:** both built a new `Regex` with `RegexOptions.Compiled` on every call. Compiling to IL is meant to be done once; done per call it costs far more than it saves.

**Fix:** the `-pw` pattern is a static compiled `Regex`, and `getcommand` keeps one compiled `Regex` per option name. The functions' logic is unchanged.

**Checked:** the same input gave the same results (`-pw secret` removed; `-i "C:\keys\a.ppk"` read as `C:\keys\a.ppk`). 10,000 calls now take 58 ms and 33 ms.

## Fix: WinSCP (external) didn't use the session's SSH key

**Symptom:** right-click a session → WinSCP (external) failed with "No supported authentication methods available (server sent: publickey)", although the same session logged in fine in PuTTY with a key.

**Cause:** PuTTY gets the key from the saved session profile it loads (`-load`, whose `PublicKeyFile` names it) or from `-i` in Extra arguments. WinSCP was started with only `sftp://user@host:port/`. WinSCP doesn't read PuTTY's saved sessions, so it had no key (it only tries Pageant).

**Fix:** `ExternalApplications.openWinSCP` passes `-privatekey="<file>"`. The key comes from `-i` in the session's extra arguments, or else from the profile's `PublicKeyFile`, read by `PuttyDataHelper.GetProfileValue` (the registry, or KiTTY's session files). It's skipped, with a log line, if the file doesn't exist. While there:
- **Username:** the session's username is used if it has one, otherwise the profile's Auto-login username (`UserName`), so a user kept only in the PuTTY profile reaches WinSCP too, instead of WinSCP asking (`ExternalApplications.UsernameFor`).
- **Placeholders:** `{key}` placeholders are filled in, as for PuTTY (they were passed to WinSCP literally).
- **Port 0** uses the protocol's usual port.
- **Remote path:** a remote path already ending in `/` no longer gets a second one. The old check, `rp.Substring(rp.Length)`, was always empty.

FileZilla can't be given a key file on its command line, so it still needs the key in Pageant or in FileZilla's own settings.

**Checked:**
- **Key lookup:** for a session using the profile `ssh_key`, and for one with `-i "...ssh_key.ppk"`, it found that profile's `ssh_key.ppk`. For Default Settings, and for a missing file, it found none.
- **Username:** a session with no username using the profile `root` (Auto-login username root, key `ssh_key.ppk`) gave user `root` and that key. With its own username `admin`, `admin` won. With Default Settings and no username there was none, and WinSCP asks.
- **Real login** with WinSCP.com to the same server: without the key, "Authentication failed". As now built, `sftp://root@192.168.3.2:22/` with `-privatekey`, "Session started", in `/root`.

## Fix: sessions with port 0 connected with -P 0

**Symptom:** a session saved without a port (port 0, e.g. imported, or a row a script wrote) started PuTTY with `-P 0`, which fails.

**Cause:** `PuttyStartInfo` always passes `-P` with the session's port, whatever it is.

**Fix:** port 0 now means the protocol's usual port when the command line is built: 22 for SSH, 23 Telnet, 513 Rlogin (`SessionData.DefaultPort`). The session keeps 0, so saving it changes nothing. RDP and VNC already treat 0 as their own default.

**Checked against the compiled assembly:** a session with port 0 gave `-ssh -P 22 -load "Default Settings" 10.5.5.5`.

## Database sessions (PostgreSQL)

Sessions can be shared through a Postgres table. They show under a third root, **Database sessions**, next to Saved and Private sessions. Search, Favorites, Recent, the open-tab dots, layouts, the Quick Selector and `OPENSESSION` treat them like any other session.

**Setting it up:** Tools → **Database sessions...** (`Gui/dlgDatabaseSettings.cs`).
- **Connection:** host, port, database, schema, table, username, password and SSL mode.
- **Password:** saved encrypted with DPAPI for your Windows user, or entered as a `{key}` placeholder whose value lives in Environment Variables (where it can be secret). It's never logged.
- **Test connection** says what it found:
  - "the database doesn't exist yet";
  - "the table has 6 sessions";
  - missing columns;
  - "you can only read it";
  - the server's error, e.g. "password authentication failed".
- **Create / upgrade schema** is enabled only when there's something to do. Nothing is ever created on its own, and never when **Read only** is ticked. It:
  - creates the database, schema and table if they're missing;
  - adds any missing columns;
  - (re)creates the triggers.
  It's safe to run again.

**The table** (`superputty.sessions` by default):

| Column | Holds |
|---|---|
| `id` | A uuid, generated by default |
| `folder` | The tree path, e.g. `Lab/Core`, or `''` at the top |
| `name` | No `/`; unique together with `folder` |
| `protocol`, `host`, `port`, `username`, `putty_profile`, `extra_args`, `icon`, `notes`, `spsl_script`, `rdp_options`, `local_path`, `remote_path` | The session |
| `updated_at`, `updated_by` | Kept current by a trigger; `updated_by` is the user's nickname |
| `deleted_at` | When set, the session is hidden (a soft delete for scripts) |

- **No passwords:** use `{key}` placeholders so each user's credentials stay their own.
- **Written by scripts:** plain `INSERT`s and `UPDATE`s work, and the protocol is matched case-insensitively.
- **Change notice:** a second trigger raises a version in `<table>_version` after every change, which SuperXPuTTY checks every 30 seconds (see "Database sessions: changes noticed every 30 seconds, deleted_at, and Nickname"). It also announces the change on the `superputty_sessions` channel, which nothing listens to yet.

**Reading and offline** (`Data/DatabaseSessions.cs`):
- **Reading:** rows are read in pages of 500 by `(folder, name, id)` on a background thread, with the root showing "loading 1,500...".
- **Cache:** the result is cached in `DatabaseSessions.cache.xml` beside your settings, and shown at once at start-up, before a layout reopens any of it.
- **Offline:** if Postgres can't be reached, the cached sessions stay. The root reads "Database sessions (offline, cached 09:41)", its tooltip gives the error, and it's retried every 2 minutes. Right-click the root → **Refresh** retries at once.
- **Root menu:** also has **Database settings...**.

**Adding and changing them in the tree** works when the table can be written: the connection isn't Read only, the last read succeeded, and the login has INSERT, UPDATE and DELETE on the table.
- **Sessions:**
  - **New session...** (folder menu, Database sessions root menu, or the toolbar with a database folder selected) adds a row in that folder.
  - **Copy as** adds a copy beside the original.
  - **Edit** saves the changes.
  - **Delete** removes the row.
- **Folders:**
  - **New folder** makes a folder in the tree, which exists in the table once a session is put in it; until then it's gone on the next refresh.
  - **Rename** moves every session in it, at every level, to the new path.
  - **Remove folder** deletes its sessions.
- **After each change** the table is read again, and the session just added or changed is selected.
- **Someone else's change:** an edit is saved only if the row hasn't changed since it was read (its `updated_at`, kept as `SessionData.DatabaseVersion`). Otherwise it's refused with "Someone else changed or deleted this session since it was read. Refresh, then make your change again."
- **Failures** are shown and leave the tree as it was: offline, a duplicate name, a refused name.
- **When it can't be written** (read-only, no permission, offline, or not read yet): the sessions open, connect, go in Favorites and copy to Saved sessions, but Edit, Delete, New and Rename are disabled.
- **Dragging within Database sessions:** a session or folder can be dragged into another folder there, including a new, still-empty one, which then exists in the table.
  - A session is saved with its new folder.
  - A folder is renamed to its new path, with everything under it.
  - It's saved at once, or queued while offline, like any other change.
  - It's refused if a session involved has `folder` locked, and a folder can't be dropped inside itself.
- **Between Database sessions and Saved or Private sessions:** see "Copying and moving sessions into and out of Database sessions".
- **Safe from Sessions.xml:** their IDs start with `@db/` (e.g. `@db/Lab/Core/sw1`), so they can't clash with Sessions.xml, and they're never written to Sessions.xml or PrivateSessions.xml.

**For an inventory system**, three more columns:
- **`created_at`:** when the row was added.
- **`managed_by`:** which system owns the row, e.g. `inventory`; NULL for sessions made in SuperXPuTTY.
- **`external_id`:** that system's key, unique per `managed_by` (index `<table>_external_id`). A sync script can upsert on it and delete its own stale rows without touching anyone else's.

The guard trigger stops SuperXPuTTY changing these three, and the tree's tooltip says "Managed by inventory".

**The guide [docs/DatabaseSessions.md](docs/DatabaseSessions.md)** is for whoever manages the table:
- every column and its rules, with icon keys, RDP option keys and protocols;
- the triggers and locks;
- roles and permissions;
- a complete Python (psycopg 3) inventory sync script, and useful SQL.

**Port 0**, a row written without a port, connects on the protocol's usual port (see "Fix: sessions with port 0 connected with -P 0").

**Locks**, for sessions a script manages:
- **`locked`** (boolean): the whole session is locked. It opens and connects, but SuperXPuTTY doesn't edit, delete or rename it, or rename or remove a folder holding it.
- **`locked_fields`** (`text[]`): just those columns are locked, e.g. `'{host,port}'`. Edit Session shows those fields greyed and read-only, with a tooltip and "(greyed fields are locked in the database)" in its title; the rest stay editable. Locking `folder` stops folder renames that would move the session.
- **Enforced by the database too:** a guard trigger (`<table>_guard`) refuses SuperXPuTTY's changes to locked rows and fields, and to the locks themselves. SuperXPuTTY is recognised by its connection's application name, "SuperPuTTY". Your scripts connect under their own name, so they can change everything, locks included. An old copy of the app can't bypass the locks either.
- **Refusals** are shown as e.g. "Host of "partly" is locked in the database".
- **In the tree,** a locked session's tooltip says what's locked.
- **Older tables:** a table made before locks still reads (as unlocked). Its root tooltip says to run Create / upgrade schema, which adds the columns and the trigger.

**Changes while offline** are kept and sent later (`Data/DatabaseOutbox.cs`):
- **Queueing:** while the table can't be reached, adding, editing, deleting and folder renames and removals are queued in `DatabaseSessions.outbox.xml` beside your settings. They show in the tree at once, in italics, with "Waiting to be saved to the database (queued 1:38 AM)" in the tooltip. The root says "3 pending".
- **This needs the login to have been able to write at the last read**, which is kept in the cache. A change that loses the connection half-way is queued too.
- **Changing queued sessions:** editing a queued new session updates it, deleting it just drops it, and two edits of one session make one change, keeping the version first read.
- **Sending:** when the table can be reached again (the retry every 2 minutes, or Refresh), the queue is sent in order before the table is read. A row the queue itself changed earlier in the same sync, e.g. moved by a queued folder rename, is updated without the version check.
- **Refused changes** (someone else changed the session meanwhile, a lock, a name clash) stay in the queue, marked "not saved", and the root says "1 not saved".
- **Pending changes...** on the root's menu (`Gui/dlgPendingChanges.cs`) lists the queue:
  - **Send again** retries the selected changes;
  - **Overwrite theirs** saves a refused edit over the other change;
  - **Discard** drops the selected changes.
- **Each database has its own queue,** so changing the connection settings loses nothing. Online with nothing queued, a change is saved at once, as before.

**10,000 sessions:** Database sessions is filled on demand (since extended to Saved and Private sessions; see "Saved and Private sessions filled on demand").
- **On demand:** the sessions are indexed by folder, only the top level is put in the tree, and each folder is filled the first time it's opened (`FillDatabaseRoot`, `FillDatabaseFolder`, `treeView1_BeforeExpand`).
- **After a refresh,** folders that were open stay open.
- **Whole-folder actions** (Connect all, Rename, Remove, moves and their lock checks) take sessions from the index (`GetAllSessions`), so they include folders not opened yet.
- **Finding a session** (Show in tree, selecting a new session) fills the folders on the way (`FindSessionNode`).
- **Expand all** pauses drawing until it's done (`ExpandFully`): filling folders while expanding redrew the tree once per folder.

Measured against a generated table of 10,000 sessions in 900 folders, 5 levels deep (`superputty.lots_of_sessions`), on the UI thread unless noted:

| | Before | After |
|---|---|---|
| Start-up: cached sessions into the tree | 3.4 s | 0.12 s |
| Start-up to "Database sessions (10,000)", reading the table in the background | | 0.55 s |
| Redraw after a refresh | 3.5 s | 0.01 s |
| Clearing a search | 2.9 s | 0.13 s |
| A search (first one / again) | 2.0 s | 2.1 s / 0.4 s |
| Expand all (11,128 nodes) | 1.4 s | 2.6 s |
| Show a session 5 folders deep | | 0.013 s |
| Open a folder | | 0.009 s |

- **Background:** reading the 10,000 rows takes about 1 s, in the background.
- **Cache:** 4 MB, written in 0.06 s and read in 0.09 s.
- **Memory:** about 90 MB.
- **First search:** most of its time is drawing icons not drawn before.
- **Expand all** creates every node, which is slower than before; it's still the one action that needs them all.

**Dialog layout:** the real cause of the clipped OK and Cancel was DPI scaling.
- **The cause:** the dialog (and Pending changes) is built in code, and WinForms doesn't scale the fixed sizes of such a form, so at 150% the 88 x 23 OK button stayed 23 px tall while its text was drawn for 150%. This only showed with the app's own per-monitor DPI setup (its manifest and `EnableWindowsFormsHighDpiAutoResizing`), not in tests that set DPI awareness another way.
- **The fix:** fixed sizes are given for 100% and converted with `LogicalToDeviceUnits`. OK and Cancel also size themselves to their text, with a minimum width.
- **Checked** with a test program built with the app's manifest and config, on the user's 150% monitors: OK was 88 x 23 and cut off before, and is 132 x 32 and whole now.
- **Before that:** OK and Cancel were docked to the bottom of the window, with the fields above scrolling if the window comes out too short. The dialog also sizes itself to its contents after it's laid out and themed, again when it's shown, and again when it moves to a monitor with another scaling. Before, the form's own AutoSize cut off the OK and Cancel row at some scalings.

| File | Change |
|---|---|
| `Data/DatabaseSessions.cs` | New. Connection settings, password protection, probe, create/upgrade, paged read, writes (insert, update with a version check, delete, rename and delete folder), cache, background sync and status |
| `Gui/dlgDatabaseSettings.cs` | New. The settings dialog, built in code |
| `docs/DatabaseSessions.md` | New. The guide to the table, for scripts and admins |
| `Utils/PuttyStartInfo.cs` | Port 0 means the protocol's usual port |
| `Data/DatabaseOutbox.cs` | New. The queue of changes not yet in the table: merging, saving, and showing it over the table's sessions |
| `Gui/dlgPendingChanges.cs` | New. Pending changes: Send again, Overwrite theirs, Discard |
| `Data/SessionData.cs` | `DatabaseId`, `DatabaseVersion`, `DatabaseLocked` and `DatabaseLockedFields` (written only to the cache); `IsDatabase`, `IsDatabaseFieldLocked`; `DatabasePending` and its note |
| `dlgEditSession.cs` | `LockedFields`: greys out and locks those fields |
| `SuperPuTTY.cs` | `ReplaceDatabaseSessions` swaps them in one go; `SaveSessions` leaves them out; `LoadSessions` (Reload) keeps them |
| `SessionTreeview.cs` | The Database sessions root, its status and menu; adding, editing and removing sessions and folders there (`SaveToDatabase`, `WriteDatabase`), and the guards for when it can't be written (`DatabaseLocked`) |
| `frmSuperPutty.cs` / `.Designer.cs` | Tools → Database sessions...; `DatabaseSessions.Start()` on load |
| `Properties/Settings.*`, `App.config` | `DatabaseEnabled`, `DatabaseHost`, `DatabasePort`, `DatabaseName`, `DatabaseSchema`, `DatabaseTable`, `DatabaseUser`, `DatabasePassword`, `DatabaseSslMode`, `DatabaseReadOnly`, `DatabaseNickname`, `DatabaseCheckSeconds` |
| `SuperPutty.csproj` | Npgsql 8.0.5, `AutoGenerateBindingRedirects` |
| `SuperPuttyInstaller/Product.wxs` | Npgsql and its 15 support libraries |

**Checked against PostgreSQL 17.5**, in a throwaway `superputty_test` database that was dropped afterwards:
- **Logging in:** a wrong password was reported as "password authentication failed". A missing database was reported, and read-only refused to create it.
- **Creating:** Create made the database, table and triggers; running it again changed nothing.
- **The table:** a duplicate folder+name, and a name with `/`, were refused. Five rows written by SQL were read in pages of 2 (progress 2, 4, 5) with the right `@db/` IDs.
- **Triggers:** an update had its `updated_at` and `updated_by` set by the trigger.
- **Upgrading:** two dropped columns were reported and added back, keeping the rows.
- **Writing:**
  - Insert added a row read back as `@db/Lab/Access/new-sw`, and a duplicate insert was refused with a readable message.
  - Update saved a rename and moved the version on. An update from the stale copy was refused ("Someone else changed..."), and one without a version asked for Refresh.
  - RenameFolder moved 4 rows, including a subfolder. DeleteFolder removed only its own 2, leaving a look-alike folder `Lab2x` alone. Delete removed the row.
  - A read-only connection refused to insert, and the read reported writable, but not for read-only.
- **Cache:** it round-tripped, and the password is stored encrypted and decrypts.
- **The app's own code, in a test program:**
  - the tree showed "Database sessions (5)" with its folders, and Sessions.xml wasn't written;
  - adding a session to the Lab folder from the tree wrote it, read it back and selected it ("Database sessions (6)\Lab\made-in-tree"), and its menu then had Edit and Delete enabled;
  - a row a script added appeared after Refresh;
  - with the host unreachable, the six cached sessions stayed, the root read "offline, cached 1:12 AM", and the tooltip gave the error;
  - the dialog's Test connection reported the table.
- **MSI:** it lists all 23 files.
- **Locks** (written by SQL, as a script would):
  - Editing and deleting a locked session were refused by the trigger ("whole" is locked in the database).
  - Changing a locked field was refused ("Host of "partly" is locked"), and changing its unlocked notes saved.
  - Renaming a folder holding a locked session was refused, and nothing moved.
  - A script could still change locked rows.
  - A table without the lock columns still read, was reported as needing the upgrade, and the upgrade added the columns and the trigger.
- **The guide's SQL** (the upsert and delete read straight from docs/DatabaseSessions.md and run under the script's own application name):
  - Three devices were added. Running it again with nothing changed updated nothing.
  - A device moved and re-addressed updated its row, keeping a user's notes and icon and its `created_at`.
  - A device clashing with a hand-made session failed with a unique violation, as the script expects.
  - The stale delete removed only the stale inventory row.
- **Inventory rows in the app:** `managed_by` and the locked fields were read. The app couldn't change a field the inventory locked, nor `managed_by`.
- **Port 0:** a row with port 0 kept 0 and connected with `-P 22`. An earlier attempt, which turned 0 into 22 when reading, made saving an unrelated edit change the port, and the lock trigger refused it, so the default moved to connect time.
- The Python script itself wasn't run: psycopg isn't installed here.
- **Outbox (no database):**
  - A queued insert showed as pending. Editing it updated the queued insert, and deleting it dropped it.
  - Two edits made one change, keeping the first version.
  - A queued folder rename and a delete were applied to what's shown, without changing the table's sessions.
  - The queue was read back after a restart.
- **Offline round trip, with the app connected through a local relay to the server that was then stopped:**
  - three changes made offline were queued; the root read "offline, cached 1:38 AM, 3 pending", and the new session showed in italics;
  - meanwhile dc1 was changed by SQL;
  - with the relay started again, the new session and the edge-r1 edit were saved, while dc1's edit was refused as a conflict ("8, 1 not saved");
  - Overwrite theirs saved it and emptied the queue.
- **Dragging (the drop logic, called directly):**
  - A session could be dropped on a new empty folder, but not on its own folder, and a folder not inside its own child.
  - Moving edge-r1 into the new folder "Moved", then folder Lab/Core into it, left `Moved/edge-r1`, `Moved/Core/core-sw1` and `Moved/Core/core-sw2` in the table, and the tree showed the same.
  - The mouse drag itself wasn't automated.
- **Edit Session** with `host,port,username` locked: those fields were read-only and greyed, and notes stayed editable.
- **Dialog fit, docked buttons:** squeezed 120 px too short, OK and Cancel stayed fully visible and the fields scrolled.
- **10,000 sessions:** see the table above. Lazy filling was checked by finding a session 5 levels deep (found, filling 58 nodes), Connect all's list for an unopened region (2,000 sessions), and the read finishing ("Database sessions (10,000)").
- **Dialog fit:** shown, its OK button fit. Shrunk by 45 px, as a DPI change can do, it was clipped; refit, it fit again.
- **Not checked:** the running app with your settings (the dialogs themselves weren't clicked through), a real move between monitors, SSL modes other than Prefer, and very large tables.

## Database sessions: changes noticed every 30 seconds, deleted_at, and Nickname

**Changes made by others show up on their own.** Before, the table was only read again at start-up, on Refresh, after your own changes and while retrying offline, so a teammate's change or a sync script's run went unseen until then.
- **The change counter:** Create / upgrade schema adds a one-row table, `<table>_version`. The existing statement-level change trigger now raises its `version` after every `INSERT`, `UPDATE`, `DELETE` and `TRUNCATE`, whoever makes it.
  - The trigger function runs as the table's owner (`SECURITY DEFINER`, with a fixed `search_path`), so logins that may change sessions needn't be granted the version table.
  - The version table is readable by everyone.
- **The check** (`DatabaseSessions.CheckForChanges`): every **Check every** seconds (30 by default; 0 turns it off), each SuperXPuTTY reads that one number in the background and reads the whole table again only when it moved.
  - The number is taken just before each full read, so a change made during a read is caught by the next check, and your own changes, read back at once, aren't read a second time.
  - It's skipped while a read is running, while offline (the 2-minute retry takes over) and while a dialog is open, so a session being edited isn't replaced under you.
  - The status bar says "Database sessions changed; reading them again".
- **Tables not yet upgraded:** the check falls back to `count(*)` and `max(updated_at)`, which catches nearly every change.
- **Mixed versions:** an older SuperXPuTTY's Create / upgrade schema would put back a trigger that doesn't raise the version. Test connection notices that (the function's source doesn't mention the version table) and offers the upgrade.
- **Not LISTEN/NOTIFY:** that would need a connection held open by every copy, survive drops and poolers, and still need a poll for what's missed while disconnected. The `NOTIFY` is still sent, for later.

**`deleted_at`, a soft delete for scripts:** a row with `deleted_at` set isn't shown; clearing it shows the row again.
- Deleting still deletes: SuperXPuTTY's Delete and Remove folder remove rows for good.
- **Remove folder** deletes only the rows it showed, leaving hidden ones (`to_jsonb(t) ->> 'deleted_at'`, so it also works before the upgrade).
- **Rename folder** moves hidden rows too.
- A hidden row still holds its name, so adding the same name in that folder is refused with "... (perhaps hidden there by its deleted_at)".

**Nickname** (required in the Database sessions dialog): several people often share one database login, so `updated_by` couldn't tell them apart.
- **On every connection,** SuperXPuTTY runs `set_config('superputty.nickname', ...)`. The touch trigger writes `coalesce(nullif(current_setting('superputty.nickname', true), ''), current_user)` to `updated_by`, so scripts that don't set it still show their login.
- **The tree's tooltip** says "Last changed by Tim" (`SessionData.DatabaseUpdatedBy`, read with the row).
- **OK refuses an empty nickname,** with "Enter a nickname: it shows who changed a session...". Someone who set up Database sessions before this, and hasn't opened the dialog since, is recorded under their Windows user name.

**The dialog** has two new rows: **Nickname** (between Password and SSL) and **Check every [30] seconds (0 = never)**.

| File | Change |
|---|---|
| `Data/DatabaseSessions.cs` | `deleted_at` column; `ChangedBy`; the version table and counter in Create / upgrade; `ChangeCounterExists` in Probe; Nickname set on each connection; `ReadChangeToken`; hidden rows left out of reads and Remove folder; `updated_by` read; the check timer (`StartChecking`, `CheckForChanges`) |
| `Data/SessionData.cs` | `DatabaseUpdatedBy` |
| `SessionTreeview.cs` | "Last changed by" in the tooltip |
| `Gui/dlgDatabaseSettings.cs` | Nickname (required) and Check every |
| `Properties/Settings.*`, `App.config` | `DatabaseNickname`, `DatabaseCheckSeconds` (30) |
| `docs/DatabaseSessions.md` | Noticing changes, Nickname, `deleted_at` and hiding sessions, the version table, scripts that disable triggers, upgrading |

**Checked against PostgreSQL 17.5** at 192.168.3.2, in a throwaway `superputty_test` database (dropped afterwards), with a test program running the app's code (29 checks, all passed):
- **A table from before** (no `deleted_at`, no version table, the old trigger function): Test connection offered the upgrade, the check fell back to count and latest change and noticed an update, and the table still read.
- **The upgrade** added `deleted_at` and the counter, and Test connection then had nothing left to do.
- **The version** moved for an `INSERT`, `UPDATE`, `DELETE`, and a script's `TRUNCATE` and reload, and stayed put when nothing changed. One check (connect plus the one-row query) took 31 ms on average.
- **A login granted only the sessions table** could still save, the version moved, and it could read the version.
- **Nickname:** a session added from SuperXPuTTY had `updated_by` "Tim"; a script's change had its login, "postgres"; "Tim" was read back for the tooltip.
- **`deleted_at`:** a hidden row wasn't shown but stayed in the table; adding its name gave the new message; Remove folder removed the 2 shown sessions and left the hidden one; clearing `deleted_at` showed it again.
- **The check, in a message loop with Check every at 2 s:**
  - a script's insert showed up on its own after 2.1 s, and a soft delete hid it after 2.0 s;
  - 7 s with no changes: no full reads;
  - our own change was read back once and not again by the check;
  - a change made while a dialog was open waited until it closed, then showed 1.9 s later.
- **The dialog** (screenshot, 150%): both new rows fit. OK with an empty nickname showed the message in red and stayed open.
- **Not checked:** the running app clicked through with your settings, a pooled or very slow connection, and many users checking at once.

## Fix: a tab dragged into a split lost its icon

**Symptom:** dragging the current tab to the side to split the tab area left it with a blank space where its icon goes; the other pane's tabs kept theirs.

**Cause:** tab icons are drawn by `TabIcons` on each pane's tab strip once `DocumentTabs.MarkCurrentTabInLists` has hooked it, and that ran only when a tab was added or the active tab changed. Dragging the current tab raises "active tab changed" when the drag starts, before the new pane exists, and nothing after the drop, so the new pane's strip was never hooked.

**Fix:** `frmSuperPutty` also hooks strips when a tab drag finishes (`DockPanel.DocumentDragged`) and when the active pane changes (`ActivePaneChanged`), so panes made by splitting or floating a tab are hooked.

**Checked against the compiled assembly**, with a real mouse drag of the current tab onto a pane's split-right guide:
- **Old wiring (tab added, active tab changed):** the events were ActivePaneChanged, ActiveDocumentChanged, DocumentDragged, and the new pane's strip wasn't hooked.
- **With the fix:** it was hooked, and the split-off tab showed its icon.

## Open sessions marked in the Sessions tree

A session with a tab open shows a dot on its icon in the Sessions tree, and in Favorites and Recent. The icon itself stays, so you can still tell a switch from a router.
- **Green:** a tab for it is open and connected.
- **Red:** all of its tabs have dropped, or are reconnecting (the tab shows the disconnected badge).
- **No dot:** no tab is open.

The dots update as tabs open and close (the dock panel's `ContentAdded`/`ContentRemoved`), and as a session drops and comes back (the new `ctlPuttyPanel.ConnectionChanged`, raised by `SetConnection`). Tabs are matched to sessions by session ID. The dotted icons are drawn into the tree's image list on first use (`IconLibrary.EnsureWithState`, e.g. `router|connected`), with a ring in the background colour so the dot stands off the icon in either theme.

**Checked against the compiled assembly**, with a dock panel, the Sessions tree and two real (not started) session tabs:
- **No tabs:** no dots.
- **sw1 and r1 opened:** both green.
- **r1 marked disconnected:** r1 red, sw1 still green. Back to connected: green again.
- **sw1's tab closed:** its dot went.
- **Render:** at 150% scaling in the light theme.
- **Not checked:** the dark theme, and a real drop with PuTTY running.

## Script Editor: Always on top

An **Always on top** check box under Saved scripts keeps the Script Editor above other windows. It's useful because the editor isn't owned by the main window, so clicking a tab would otherwise hide it. The choice is saved (`ScriptEditorTopMost`, default off) and applied the next time the editor opens.

**Dialogs above it:** a window opened from an always-on-top window can open behind it, leaving it waiting unseen for an answer. The "Send it anyway?" warning did, so the editor seemed stuck.
- **Message boxes:** the editor's message boxes, and the saved-scripts panel's, now name the editor as their owner.
- **`Messenger.MessageBox`** (vendored DarkModeForms) makes a box always on top when its owner is, which fixes this for any always-on-top window.
- **File dialogs:** the editor's Open file and Save to file dialogs are owned by it too.

**Checked against the compiled assembly:** ticking it set `TopMost` and wrote `ScriptEditorTopMost` to the settings file. The send warning, the Save as... naming dialog and the Open file dialog, each opened from an always-on-top editor, were the window on screen at their own title bar (before the fix, the warning was hidden behind the editor). A newly opened editor came up ticked and on top. The editor was rendered in the dark theme at 150% scaling.

## Hints in empty text boxes

Empty boxes now show a grey example of what goes in them, gone as soon as you type (`Gui/HintText.cs`, using Windows' own cue banner, so it's never part of the text):
- **Sessions search box:** "Search... (press Enter)", or "Type to search..." when Options → "Filter while typing" is on, since then there's no Enter to press.
- **Create / Edit session:** Session name "e.g. core-sw1", Host address "e.g. 192.168.0.5", Login username "e.g. admin, or {user}". The last one also shows that a placeholder works there.
- **Quick-connect toolbar:** Host "e.g. 192.168.0.5" and Login "e.g. admin".

The hint stays visible while the box has focus, so the session name box shows its hint even though it has the cursor when the dialog opens.

**Checked:** Create new session rendered in the dark theme, and the search box in the light theme, at 150% scaling, with the hints showing in grey in the empty boxes (including the focused name box).

## Fix: installing a newer build didn't replace the installed program; version 2.1.2

**Symptom:** running a freshly built MSI over an installed SuperXPuTTY seemed to work, but the old program kept running.

**Cause:** the MSI's version comes from `SuperXPuTTY.exe`, which was fixed at 2.1.1.0 in `AssemblyInfo.cs`, so every build had the same version.
- **No upgrade:** `MajorUpgrade` only removes older versions, so a new MSI was installed alongside the old one.
- **Old file kept:** Windows Installer doesn't replace a file with one of the same version.
- **Two installs on the machine:** a 2.1.0.0 and a 2.1.1.0, both in `C:\Program Files (x86)\SuperXPuTTY`. Removing one didn't remove files the other still had.

**Fix:**
- **Version raised to 2.1.2.0** (`AssemblyVersion` and `AssemblyFileVersion`). The new exe is newer than the installed one, so it replaces it. Raise the version again for each build you install over an older one.
- **`MajorUpgrade AllowSameVersionUpgrades="yes"`** in `SuperPuttyInstaller/Product.wxs`, so a rebuild of the same version also replaces the installed copy rather than installing beside it. WiX warns ICE61 about this, which is expected.

**Checked:** the Release MSI was rebuilt. Its ProductVersion is 2.1.2.0, and so is the exe's file version. Its Upgrade table removes any older version up to and including its own. Installing it wasn't tried here.

## Fix: every search added the tree's change handlers to every session again

**Cause:** `SessionTreeview.AddSessionNode` subscribes to each session's `OnPropertyChanged` and `OnPropertyChanging`, and the tree is rebuilt on every search (and every clear). Each rebuild added another subscription, so after a dozen searches, a session's rename ran the tree's handler a dozen times.

**Fix:** it unsubscribes before subscribing, so each session has the handlers once.

## Saved and Private sessions filled on demand

Saved sessions and Private sessions are filled on demand, like Database sessions. With 10,000 sessions in `Sessions.xml`, building every node up front made opening the panel, reloading, searching and clearing a search take 1 to 4 seconds each. A sampling profile put about 80% of that inside the Windows tree control itself, creating and deleting 11,000 items. Pausing drawing and sorting once didn't help, so the fix had to be creating fewer items.

**How it works** (`SessionTreeview`):
- **The index:** every root has an index by folder (`FolderIndex`), rebuilt from the session list when sessions have been added, removed or moved since (`indexDirty`).
- **Filling:** a root shows its top level; each folder has a placeholder child until it's first opened, and is filled from the index (`FillRoot`, `FillFolder`, `treeView1_BeforeExpand`).
- **Big folders:** a folder with over 200 children is filled with sorting off and sorted once.
- **Actions that walk a folder's nodes fill that folder's subtree first (`FillSubtree`):**
  - renaming a folder, which gives every session below its new path;
  - dragging, which copies the nodes;
  - Move to Private/Saved sessions;
  - Connect all and Remove folder (`GetAllSessions`; database folders use the index instead);
  - and the folder a new session goes in.
- **Finding a session** (Show in tree, renames, selecting a new session) fills the folders on the way (`FindSessionNode`).
- **Adding a session** shows it at once if its folder is filled; otherwise it shows when the folder is opened. New folders on the way are made.
- **Reload** (File → Reload sessions) no longer inserts 10,000 nodes one by one as they're re-added. The roots are emptied, the sessions added meanwhile are skipped, and the roots are filled once afterwards, reopening the folders that were open.

Timed with a 10,000-session `Sessions.xml` mirroring the test table (2.5 MB), on the UI thread:

| | Before | After |
|---|---|---|
| Open the Sessions panel | 1.2 s | 0.23 s |
| File → Reload sessions | 3.7 s | 1.4 s (1.0 s of it reading the file) |
| A search (first / again) | 2.1 s / 0.35 s | 0.29 s / 0.33 s |
| Clearing a search | 3.1 s | 0.11 s |
| Expand all (11,135 nodes) | 1.5 s | 2.1 s |
| Saving after an edit | 0.05 s | 0.05 s |

**Checked against the compiled assembly**, with that file:
- **Show in tree:** a session 5 folders deep was found in 11 ms (the tree then had 57 nodes).
- **Connect all** on an unopened region listed its 2,000 sessions.
- **Renaming** an unopened site moved a session two levels below it.
- **Dragging** an unopened Servers folder into another folder moved all 20 of its sessions.
- **Move to Private** of an unopened folder made its 12 sessions private and saved them to PrivateSessions.xml.
- **Adding:** a session added to an opened folder showed at once. One added to an unopened folder was there once it was opened. One in new folders made them.
- **Reload** kept Region 1/Iowa open and gave 10,003 sessions.
- **Database sessions** timings and checks were unchanged.

## Fix: the Log Viewer froze the window during a burst of log lines, and crashed on exit

**Symptom:** with View → Log Viewer open, pointing the settings folder at a 10,000-session Sessions.xml left the window unresponsive for over a minute. Closing the application then failed with "Cannot access a disposed object. Object name: 'RichTextBox'" (`Log4netLogViewer.timerLogPull_Tick`).

**Cause:**
- **The freeze:** loading logged one "Added Session" line per session. The viewer's timer added each new line to its text box separately and scrolled after each one, and for 10,000 lines that took far longer than the timer's interval.
- **The crash:** the timer kept ticking after the viewer was closed, and touched the disposed text box.

**Fix:**
- **One append per tick:** the viewer formats all new lines into one string, appends it once and scrolls once. It keeps at most about 1,000,000 characters, dropping the oldest half when full.
- **The timer** stops for good once the viewer is disposed.
- **Loading** no longer logs each session it adds. `SuperPuTTY.LoadSessions` logs one line ("Loaded 10000 sessions from ... and 0 private sessions"). Adding single sessions is still logged.

**Checked**, with the app's log4net configuration and the viewer open:
- **Loading** the 10,000-session file took 0.99 s; in the app, with per-session logging, it had taken 6 s.
- **A burst of 10,000 log lines** showed in the viewer in 0.35 s, with the longest pause of the UI thread 334 ms.
- **Closing the viewer** caused no error afterwards.

## Sessions.xml reloaded when changed outside SuperXPuTTY

When `Sessions.xml` is changed by something other than this copy of SuperXPuTTY, the sessions are reloaded on their own. That might be a teammate saving to the same shared settings folder, a script, or a text editor.

**How it decides** (`Utils/SessionsFileWatcher.cs`):
- **Watching:** a `FileSystemWatcher` flags changes as they happen. Every 30 s the file's time and size are also compared, since watchers miss changes on some network shares.
- **Deciding:** when something changed, the file's SHA-256 is compared with the one SuperXPuTTY recorded when it last read (`SuperPuTTY.LoadSessions`) or wrote (`SaveSessions`) the file. Only a different hash reloads.
  - So SuperXPuTTY's own saves, and tools that rewrite or touch the file without changing it, don't cause reloads.
  - **Not MD5:** where Windows' FIPS policy is enforced, .NET refuses MD5 (and its own SHA-256), so the Windows provider's SHA-256 is used.
- **Dialogs:** no reload while a dialog is open (e.g. a session being edited); it happens once the dialog is closed.
- **Turning it off:** Tools → Options → GUI → **Reload when changed** (`ReloadSessionsWhenChanged`, default on).
- **The status bar** says "Sessions.xml was changed outside SuperXPuTTY; sessions reloaded". The tree keeps its open folders.
- **Start-up:** the sessions are first read before `Program.Main` has finished setting up WinForms. So the once-a-second timer starts when the message loop first goes idle (`Application.Idle`), not when the file is first watched. A timer created that early made `SetCompatibleTextRenderingDefault` throw, and SuperXPuTTY quit at start-up.

**Checked** in the real app (bin\Debug) on the 10,000-session file: it starts, and an outside edit was reloaded about 1 s later.

**Checked**, in a message loop with a copy of the 10,000-session file:
- **No reload** for SuperXPuTTY's own save, or for touching the file's time.
- **An outside edit** (one session's host changed) was reloaded after about 1 s, with the new host.
- **An outside edit while a dialog was open** wasn't reloaded during the 3.5 s the dialog stayed open, and was reloaded 1.7 s after it closed.
- **With the setting off,** an outside edit wasn't reloaded.
- **Not checked:** a network share (where only the 30 s poll may notice), and two running copies sharing a folder.

## Turning off Sessions.xml

Tools → Options → GUI → **Use Sessions.xml** (`UseSessionsXml`, on by default). Untick it for a team that keeps its sessions only in a Postgres table (Database sessions), and maybe Private sessions. It applies after a restart, which Options says when it changes. With it off:
- **No Saved sessions:** the root isn't shown (it still exists, empty and out of the tree, so code that refers to it keeps working).
- **Sessions.xml isn't read or written,** and isn't watched for changes. It's left exactly as it was, so ticking the box again brings those sessions back, and another copy (or upstream) can keep using it. Folders.xml isn't read either.
- **New sessions** with no folder chosen (or from Favorites and Recent) go to Private sessions (`SessionTreeview.DefaultRoot`).
- **Imports** (File → Import sessions, and CSV rows marked Saved) go to Private sessions, so they aren't lost.
- **Move to / Copy to** don't offer Saved sessions.

| File | Change |
|---|---|
| `SuperPuTTY.cs` | `LoadSessions` and `SaveSessions` skip Sessions.xml, and the watcher stops; `ImportSessions` makes imports private |
| `SessionTreeview.cs` | The Saved sessions root left out of the tree; `DefaultRoot`; Move to / Copy to |
| `Data/FolderSettings.cs` | Folders.xml not read |
| `Data/SessionCsv.cs` | Saved rows imported as private |
| `dlgFindPutty.cs` / `.Designer.cs` | The checkbox, its tooltip, and the restart note |
| `Properties/Settings.*`, `App.config` | `UseSessionsXml` |

**Checked** (a test program running the app's code, with a real Sessions tree; 9 checks with the colour menu below, all passed):
- with it off, a Sessions.xml holding a session wasn't read, PrivateSessions.xml was, and Folders.xml wasn't;
- an import went to Private sessions and was saved there, and so did a CSV row marked Saved;
- after saving, Sessions.xml was byte for byte unchanged, down to its time;
- the tree had no Saved sessions root; new sessions with no folder went to Private sessions; Copy to didn't offer Saved sessions.
- **Not checked:** the Options checkbox clicked and SuperXPuTTY restarted.

## Copying and moving sessions into and out of Database sessions

Sessions can now cross between Saved or Private sessions and Database sessions, e.g. to fill the table from an existing Sessions.xml without writing SQL (`SessionTreeview.Transfer`).
- **Menus:** a session's and a folder's menus have **Move to ▸** and **Copy to ▸**, listing the other roots (Saved, Private and, when it's on, Database sessions). They replace the single "Move to Private / Saved sessions" item. The same folder path is kept in the other root, with subfolders. Database sessions can be chosen only while the table can be written, and moving out of it likewise.
- **Dragging** a session or folder across that boundary **copies** it into the folder it's dropped on. Moving deletes from one side, which for the table means for everyone, so it's only on the menu.
- **Into Database sessions:** one batch of inserts (`DatabaseSessions.SubmitBatch`, which now also says which changes were refused), read back once. For a move, only the sessions that arrived (or were queued while offline) leave Sessions.xml, and their Favorites and Recent entries follow them.
- **Out of Database sessions:** plain copies are added to Sessions.xml or PrivateSessions.xml, without the row's id, version, locks, owner or pending state. A move first asks: "Move 3 sessions out of Database sessions into Saved sessions? They're deleted from the table, for everyone who uses it." Then one batch of deletes runs. Locked rows can't be deleted from here, so they're copied and stay in the table too.
- **Left where they were, and listed:** a name already in that folder on the other side (in the table, perhaps hidden by `deleted_at`), and anything the table refuses.
- **Between Saved and Private sessions,** Move to works as before (the node moves).
- **Not copied:** the folders' own settings (colours, defaults).

| File | Change |
|---|---|
| `SessionTreeview.cs` | Move to / Copy to submenus (`TransferMenu`, `FillTransferMenu`, `TransferToRoot`), `Transfer`, `PlainCopy`, dragging across the boundary |
| `Data/DatabaseSessions.cs` | `SubmitBatch` reports the refused changes |

**Checked** (a test program driving the real Sessions tree against a throwaway database; 12 checks, all passed):
- **Into the table:** Saved folder Lab/Core was copied in; `a` and `Sub/c` were inserted in the same folders, with their username and colour; `b`, already in that folder in the table, was listed; the Saved originals stayed.
- **A move into the table:** `Lab/single` was inserted and removed from Sessions.xml, and its favourite followed it.
- **A copy out:** Database folder DC copied to Private sessions gave plain private copies (no database id or locks), and the table kept all three.
- **A move out:** it asked first; `dc-sw1` and `dc-sw2` were deleted from the table, the locked `dc-locked` wasn't; all three were in Saved sessions, and the locked one was listed.
- **Not checked:** dragging with the mouse (the drop runs the same code), and moves while offline.

## Jump hosts (bastions)

A session can connect through a bastion without its own PuTTY profile: Edit Session → **Jump host** (between Extra arguments and SPSL script), or a folder's **Folder settings... → Jump host** for every session below it (`Utils/JumpHosts.cs`).
- **What to enter:** a saved session's path, e.g. `Bastions/bastion1` (Saved, Private or Database), whose host, port, username and PuTTY profile are used, with its own folder defaults and placeholders resolved; or `user@host:port` (`host` alone means port 22 and PuTTY's own user). Placeholders such as `{user}@jump.example.net` work.
- **How:** PuTTY's command line has no jump-host option, so SSH, Telnet, Rlogin and Raw sessions get `-proxycmd` running **plink.exe** (the one chosen in Options > Paths, else plink.exe or KiTTY's klink.exe beside PuTTY) as `plink -batch -ssh -P <port> [-load <profile>] [-l <user>] <bastion> -nc %host:%port`; PuTTY fills in `%host:%port`. Other protocols ignore it.
- **The bastion must accept a key:** plink runs as a hidden proxy with `-batch`, so it can't ask for a password. Use Pageant, or a key set in the bastion session's PuTTY profile. A bastion that needs a password needs a PuTTY profile with PuTTY's own SSH proxy settings instead.
- **Quoting:** PuTTY reads backslashes in a proxy command as escapes, so they're doubled, and the whole command is one argument with its quotes escaped. plink's path (`C:\Program Files\...`) and profile names with spaces come through intact.
- **When it can't be used,** the session connects directly, and the status bar and log say why: the jump host names no session and isn't `user@host:port`, no plink.exe or klink.exe was found, or it names the session itself. Chains (a bastion with its own jump host) aren't followed.
- **Stored:** a `JumpHost` attribute in Sessions.xml (only when set); `jump_host` columns on the database's sessions and folders tables (lockable with `locked_fields`; a table not yet upgraded is still written, without it); a `Jump host` CSV column; and a field in Edit sessions....
- **Edit Session** shows a folder's jump host as the empty box's hint, e.g. "Bastions/bastion1 (from DC)".
- **Database writes:** the optional session columns (`color`, `jump_host`) are now one list (`DatabaseSessions.OptionalWriteColumns`), written only when the table has them, and remembered in the cache.

| File | Change |
|---|---|
| `Utils/JumpHosts.cs` | New. Resolving the jump host, finding plink, and the `-proxycmd` arguments |
| `Utils/PuttyStartInfo.cs` | Adds them to PuTTY's command line |
| `Utils/SessionVariables.cs` | Placeholders in the jump host are resolved |
| `Data/SessionData.cs` | `JumpHost` |
| `Data/FolderSettings.cs` | `JumpHost` as a folder default |
| `Data/DatabaseSessions.cs` | `jump_host` on both tables; optional write columns generalised |
| `Data/SessionCsv.cs`, `Data/SessionBulkEdit.cs`, `Gui/dlgBulkEdit.cs` | The Jump host column and field |
| `dlgEditSession.cs` / `.Designer.cs` | The Jump host row (the rows below moved down 48 px), its hint, tooltip and lock |
| `Gui/dlgFolderSettings.cs` | The Jump host row |

**Checked** (test programs running the app's code; 20 checks, all passed):
- **Resolving:** a saved session as the jump host gave its host, port 2022, user and profile; port 0 became 22, its folder's default username was used, and Default Settings meant no profile; `ops@jump.example.net:2200` and a bare host parsed; a session naming itself, and nonsense, weren't used.
- **The arguments:** plink was found beside the PuTTY set in Options; an SSH session got `-proxycmd ... -nc %host:%port`, with the target's own `-l admin 10.1.2.3` after; a session without its own got its folder's; RDP got none; an unknown jump host connected directly.
- **The quoting, through the real parser:** the real plink 0.85 (which shares PuTTY's command-line and proxy-command code) was started with these arguments. The proxy command was a stand-in plink in a folder with spaces in its name, which recorded what it received: exactly `-batch -ssh -P 2022 -load "Bastion key" -l jumpuser bastion.example.net -nc 10.1.2.3:22`.
- **Database:** a table with `color` but not `jump_host` was still written (with the colour); the upgrade added `jump_host` to both tables; a session's and a folder's jump hosts were written and read back.
- **Edit Session** (screenshot): the Jump host row fits, and the folder's username and jump host show as hints.
- **Not checked:** a real connection through a bastion (none here accepts a key without a password).

## Folder defaults: username, PuTTY profile and port

Right-click a folder → **Folder settings...** (`Gui/dlgFolderSettings.cs`) sets its colour and the values its sessions use when they have none of their own, so a site's credentials change in one place instead of in 40 sessions.
- **Username:** for sessions with an empty username. `{user}`-style placeholders work.
- **PuTTY profile:** for sessions with no profile or **Default Settings**. A session that should use Default Settings inside a folder with a profile can't say so; give it another profile or leave the folder's empty.
- **Port:** for sessions with port 0; with neither, the protocol's usual port is used, as before.
- **Inheritance:** each field comes from the nearest folder above the session that sets it, so Site can set the profile and port while Site/Access sets a different username.
- **At launch only:** `FolderSettings.WithDefaults` fills them into the copy `SessionVariables.Resolve` makes, so every launch (PuTTY, SCP, WinSCP and the other external tools, RDP) gets them, placeholders in them are resolved and checked like the session's own, and the stored session is never changed.
- **Edit Session** shows the inherited username as the Username box's hint, e.g. "netops (from HQ/Core)".
- **Storage:** with the folder's colour (see "Environment colours"): `Folders.xml`, `PrivateFolders.xml`, or `<table>_folders`, which gains `username`, `putty_profile` and `port` columns. A folders table from before still reads, Test connection offers the upgrade, and saving a default to it asks for it.

| File | Change |
|---|---|
| `Data/FolderSettings.cs` | `Username`, `PuttyProfile`, `Port`; `WithDefaults`, `Describe`, `IsDefaultProfile`; moves keep every setting |
| `Gui/dlgFolderSettings.cs` | New. Folder settings... |
| `Utils/SessionVariables.cs` | `Resolve` and `FindMissing` work on the session with its folders' defaults |
| `Data/DatabaseSessions.cs` | The folders table's new columns: added by the upgrade, read as JSON (so older tables read), written by `SetFolder` |
| `SessionTreeview.cs` | Folder settings... on the folder menu; saving shared with the Colour menu (`SaveFolderSetting`) |
| `dlgEditSession.cs` | The inherited username as the hint |

**Checked** (a test program running the app's code; 18 checks, all passed):
- An empty session two folders down got Site's username, profile and port, and the stored session wasn't changed. A session's own values won. A deeper folder's username beat Site's, while Site's profile and port still applied. Sessions elsewhere got nothing.
- **PuTTY's command line** for the empty session was `... -ssh -P 2200 -load "Router" -l netops 10.0.0.1`.
- `{nosuchkey}` as a folder's username was reported as missing, like a session's own.
- Folders.xml held the defaults (no `Port` for unset ports); a rename kept them all; they read back after a reload.
- **Database:** a folders table from the colours-only version still read and was offered the upgrade; saving a default to it asked for the upgrade; after it, the defaults were written and read back, and a Database session under the folder inherited them.
- **The dialog** (screenshot): colour, username, profile and port, laid out at 150%.
- **Not checked:** Folder settings... clicked through, and a real connection using an inherited profile.

## Snippets that ask for values: {prompt:Interface}

A button bar button (or a command typed in Send Commands) can ask for values when it's used, so one "shut / no shut" button works for any port (`Utils/SnippetPrompts.cs`):

```
conf t\rinterface {prompt:Interface}\rswitchport access vlan {prompt:VLAN=10}\rend\r
```

- **Asking:** clicking it opens a small form with a box per name (Interface, VLAN), titled with the button's name. **Send** fills them in and sends; **Cancel** sends nothing.
- **`{prompt:Name=default}`** fills the box with the default.
- **`{secret:Name}`** is typed masked, and is never remembered or logged.
- **Each name is asked once,** however often it appears, whatever its case.
- **The last value** given for a name is offered next time, for this run of SuperXPuTTY.
- **Order:** `{key}` placeholders (Environment Variables) are filled first. Their names have no colon, so the two never mix. Then the prompts, then the escape codes (`\r`, `\p`, ...). SPSL script buttons get the prompts filled into the script text too.
- **Send Commands:** typing `{prompt:...}` there works the same way, and the history keeps the text as typed, so a recalled command asks again. Nothing is asked when no tab would receive it.
- **The button editor's help line** mentions the syntax.

| File | Change |
|---|---|
| `Utils/SnippetPrompts.cs` | New. Finding the names, the form, filling in, remembering |
| `Gui/ButtonBarStrip.cs` | `RunButton` asks before sending |
| `frmSuperPutty.cs` | `SendToolbarText`: Send Commands asks before sending; its history keeps the text as typed |
| `Gui/dlgEditCommandButton.cs` | The help line |

**Checked** (a test program running the app's code, with the form answered by a timer; 7 checks, all passed):
- **Finding:** Interface, VLAN (default 10) and Key (secret) were found; `{prompt:interface}` later in the text was the same name; `{user}` wasn't taken for a prompt.
- **Filling:** every use of a name got its value whatever its case, and text without prompts went through without asking.
- **The form:** it asked for Interface, VLAN and Key (masked), offering "", "10" and nothing; the result was `interface Gi1/0/5\rswitchport access vlan 10\rkey s3cret\r`, three lines to send. The next time it offered Gi1/0/5 and 10, but not the secret. Cancel returned nothing to send.
- **Screenshot** of the form, dark theme.
- **Not checked:** a real button click sending into a PuTTY tab, and the Send Commands box.

## Edit many sessions at once

Right-click a folder (or Saved, Private or Database sessions) → **Edit sessions...** (`Gui/dlgBulkEdit.cs`, `Data/SessionBulkEdit.cs`), e.g. to give every device at a site a new username.
- **The sessions:** every session in the folder and its subfolders, listed by path and host, all ticked; untick any to leave them out (All / None).
- **The fields:** Username, PuTTY profile, Port, Protocol, Icon, Colour and Extra args. Only ticked fields change; editing a field ticks it. The rest keep each session's own value.
- **Saved and Private sessions** are changed in place, and Sessions.xml (and PrivateSessions.xml) are saved once.
- **Database sessions** go to the table as one batch (`DatabaseSessions.SubmitBatch`): written one by one, then read back once. Offline, or behind queued changes, they're queued like any change.
- **Locked fields** are respected: a session with a changed field locked in the database (or locked entirely) is left alone, and the dialog lists them, e.g. "locked-one: username is locked in the database". A refusal from the table is listed the same way, and the others still change.
- **Disabled** under Database sessions while the table can't be written.

| File | Change |
|---|---|
| `Data/SessionBulkEdit.cs` | New. The change (only the fields set), lock checks, and applying it |
| `Gui/dlgBulkEdit.cs` | New. The dialog |
| `Data/DatabaseSessions.cs` | `SubmitBatch`: many changes, one read back |
| `SessionTreeview.cs` | Edit sessions... on the folder menu |

**Checked** (a test program running the app's code against a throwaway database; 9 checks, all passed):
- **Saved:** two of three sessions ticked got the new username, port and colour, keeping their host and `-pw` arguments; the third was untouched; Sessions.xml was saved.
- **Database:** four sessions, one with `username` in `locked_fields`: three were changed in the table, the locked one was left alone and listed, the table was read back once for the whole batch, and the tree showed the new values.
- **The dialog** (screenshot, dark theme): typing a username ticked its box.
- **Not checked:** the dialog clicked through to the end, and a batch while offline.

## Environment colours: production in red

A session can have a colour (e.g. red for production), so you notice before typing into the wrong device.

**Where it shows:**
- **On its tab:** the selected tab is filled with the colour instead of the theme's (blue in Light), with its text and close button drawn again in white or black, whichever reads better (`TabIcons.FillSelectedTab`). Unselected coloured tabs are tinted with it and have a stripe along the top edge; **hovering** one fills it with a stronger mix of its colour and the theme's tab background, with its text and close button redrawn, because the theme's hover colour (blue) showed through the tint (`TabIcons.Blend`, `HoverMix`). It's painted over the tab strip by `TabIcons`, which already does that for the icons, because DockPanelSuite picks tab colours per theme, not per tab. The close button is placed where the strip puts it (its own `GetCloseButtonRect`) and still works, but it loses its hover highlight on a filled tab. Classic has one close button for the whole strip, and its slanted left edge is covered by the fill.
- **Around the terminal:** a 3 px frame (`ctlPuttyPanel.ApplyEnvironmentColor`: the tab's padding, in the colour, around the docked terminal).
- **In the Sessions tree:** the folder where a colour starts (set on it, or matched by a rule) and a session with its own colour have their icon on a circle of the colour; their names keep the tree's text colour (see "Environment colours in the tree as a circle behind the icon"). Folders don't colour every folder under them, so the tree stays readable.

**Where a session's colour comes from**, first found wins (`Gui/EnvironmentColors.For`):
1. **Its own colour:** right-click the session → **Colour**, or Edit Session's **Colour** button (beside Icon; a swatch, or a slashed box for none, with a tooltip naming the colour it gets from its folder or a rule when it has none of its own). Saved as a `Color` attribute in Sessions.xml (written only when set, so other sessions' lines are unchanged), or the `color` column for Database sessions.
2. **Its folder's, or the nearest folder above with one:** right-click the folder → **Colour**.
3. **A name rule:** Tools → **Environment colours...** (`Gui/dlgEnvironmentColors.cs`): a folder or session name, with `*` and `?` wildcards and any case, and its colour. Rules are tried on the session's folders from the deepest up, then on its own name; the first match wins. New installs start with `Prod`, `Production` red and `Staging`, `UAT` orange (`Settings.EnvironmentColorRules`), so a folder named Prod is red with no setup.
- **The Colour menu** offers red (production), orange (staging), yellow, green (lab), blue and purple, **Other colour...**, and **None**.

**Folder settings** (`Data/FolderSettings.cs`) are kept apart from Sessions.xml, which stays as upstream reads it. Folder defaults are kept there too (see "Folder defaults").
- **Saved sessions:** `Folders.xml` beside Sessions.xml, so a team sharing the settings folder shares the colours.
- **Private sessions:** `PrivateFolders.xml` beside PrivateSessions.xml.
- **Database sessions:** a new table, `<table>_folders` (`folder`, `color`, `updated_at`, `updated_by`), added by Create / upgrade schema, so everyone using the table sees the same colours and a script can set them.
  - It has the same touch trigger (`updated_by` is the nickname) and change trigger (the 30-second check notices folder colour changes).
  - It's granted to whoever has rights on the sessions table.
  - Setting a Database folder's colour needs the database online; it isn't queued like session changes.
- **Following the folder:** renaming, moving (dragging, Move to Private sessions) and removing a folder moves or drops its colour and its subfolders'; Database folder renames and removals do the same in the table.

**Database sessions:** a `color` column on the sessions table (also lockable with `locked_fields`). A table not yet upgraded keeps working: reads and writes leave `color` out until Create / upgrade schema adds it (`DatabaseSessions.HasColorColumn`), and setting a Database session's colour then says to run it.

**CSV:** a `Colour` column, exported and imported (a value that isn't `#RRGGBB` skips the row).

| File | Change |
|---|---|
| `Gui/EnvironmentColors.cs` | New. The rules, the resolution order, the presets and the Colour menu |
| `Gui/dlgEnvironmentColors.cs` | New. Tools → Environment colours... |
| `Data/FolderSettings.cs` | New. Per-folder settings for each root: Folders.xml, PrivateFolders.xml, and the table's for Database sessions |
| `Data/SessionData.cs` | `Color` |
| `Data/DatabaseSessions.cs` | `color` column; `<table>_folders` (create, grants, read with the sessions, cache, `SetFolder`, moved and removed with folders) |
| `Data/SessionCsv.cs` | `Colour` column |
| `Gui/TabIcons.cs` | The tab stripe |
| `ctlPuttyPanel.cs` | `ApplyEnvironmentColor`: the frame |
| `dlgEditSession.cs` / `.Designer.cs` | The Colour button (the session name box narrowed to make room), saved with the session and lockable as `color`. Its menu is shown as its own context menu under the button: shown from a menu item that belongs to no menu, it opened at the top-left of the screen (checked since: it opens at the button's bottom-left) |
| `SessionTreeview.cs` | Colour menus for sessions and folders; node colours (`ColorNode`); folder settings follow renames, moves and removals |
| `frmSuperPutty.cs` / `.Designer.cs` | Tools → Environment colours...; tabs redrawn when a colour changes |
| `SuperPuTTY.cs` | `LoadSessions` reads the folder settings |
| `Properties/Settings.*`, `App.config` | `EnvironmentColorRules` |

**Checked:**
- **Logic and files** (a test program running the app's code; 35 checks with the database ones below, all passed):
  - rules parse and normalise, drop bad entries, and match with wildcards and any case (`Lab*` matches lab-east, not mylab; `Prod` not Products);
  - a session two folders under Prod was red by rule; one named `br1-prd-rtr1` matched `*-prd-*`; a folder colour beat a rule further up, the nearest folder's beat a higher one, and a session's own beat both;
  - Folders.xml was written; renaming Prod moved its subfolder's colour; moving a folder to Private sessions moved its colour to PrivateFolders.xml; removing a folder dropped the colours under it; both files read back after a reload;
  - Sessions.xml had no `Color` attribute for sessions without one; a session's own colour was saved and read back;
  - CSV exported the Colour column, and importing cleared one, set a lower-case one (normalised) and skipped `blue`.
- **The database** (PostgreSQL 17.5, a throwaway database):
  - on a table without `color` or the folders table, writes still worked, and Test connection offered the upgrade, which added both;
  - the folders table was granted to the login allowed to change sessions;
  - a session's colour was written and read back;
  - a folder colour was written (with the nickname as `updated_by`), read back, and moved the change counter; setting it again updated the row; renaming the parent folder moved it; removing the folder removed it; clearing it removed the row.
- **The real main window** (screenshot): four tabs. The production session had a red stripe on its tab and a red frame round the terminal; lab-sw1 (folder set to green) a green stripe; br1-rtr1 (its own blue) a blue stripe; br1-sw1 none. The Prod folder was red in the tree.
- **The filled tab** (screenshots of the real window): in Light and Dark the selected production tab was red with white text and close button in their usual places, and with another tab selected it was tinted with its stripe while the selected one kept the theme's colour; in Classic it was filled red too.
- **Not checked:** the Colour menus and the Environment colours dialog clicked through, and floating or split tab panes.

## Sessions as CSV: export and import

For audits, and for teams that keep their device lists in a spreadsheet (`Data/SessionCsv.cs`).

**Export:**
- **File → Export sessions to CSV...** writes every session (Saved, Private and Database).
- **Right-click a folder or root → Export to CSV...** writes that folder and its subfolders.
- **Columns:** `Source` (Saved, Private or Database), `Folder`, `Name`, `Protocol`, `Host`, `Port`, `Username`, `PuTTY profile`, `Extra args`, `Icon`, `Notes`, `SPSL script`, `RDP options`, `Local path`, `Remote path`, and `Colour` (added with environment colours). The file is UTF-8 with a byte order mark, so Excel shows accents and symbols right.
- **Passwords:** a `-pw` password in Extra args is written as `********`, and the status bar says how many were.
- **Spreadsheet formulas:** a value starting with `=`, `+`, `-` or `@` (common in Extra args, e.g. `-L 8080:...`) is written with a leading `'`, so a spreadsheet shows it as text instead of running it as a formula. Import removes the `'` again.
- **Name** is the last part of the session's ID, which is what places it in the tree (a hand-edited Sessions.xml can have a different display name).

**Import:** File → Import sessions → **From CSV file...**
- **Matching:** each row is matched by Source, Folder and Name. An existing Saved or Private session there is updated with the file's values; otherwise a session is added in that folder. Nothing is renamed.
- **Only the file's columns change.** A sheet with just `Folder,Name,Host` updates hosts and leaves everything else alone; columns a sheet adds (e.g. `Owner`) are ignored and listed.
- **Before anything changes,** a summary asks to go ahead: "1 new session, 1 updated, 7 skipped", with the first skipped lines and why. Sessions.xml is saved once at the end.
- **Passwords survive a round trip:** a masked `-pw ********` keeps the session's real password, and is dropped for a new session rather than setting the password to asterisks.
- **Headers** are matched loosely (case, spaces, `_` and `-` don't matter, and a few other names work, e.g. `putty_profile`, `hostname`, `user`), so a table exported from Postgres imports too. The separator is taken from the header line: a comma, or the `;` or tab some spreadsheets save with.
- **Skipped rows:** no name or a name with `/`, an unknown protocol, a port outside 0 to 65535, an odd Source, the same session twice, a session that's already Private when the row says Saved (or the other way round), and **Database** rows: the table is changed in the tree or with SQL. A file without a Name column is refused.

| File | Change |
|---|---|
| `Data/SessionCsv.cs` | New. Export (`ToCsv`, `Export`), reading (`ParseRows`), the import plan (`Plan`, `Apply`) and the dialogs (`ExportWithDialog`, `ImportWithDialog`) |
| `frmSuperPutty.cs` / `.Designer.cs` | File → Export sessions to CSV..., File → Import sessions → From CSV file... |
| `SessionTreeview.cs` | Export to CSV... on the folder menu (`ExportFolderToCsv`) |

**Checked** with a test program running the app's code (21 checks, all passed):
- **Export:** every column; the `-pw` password left out; `'-L 8080:...` and `'=SUM(...)` kept as text; a note with a comma, quotes and a line break read back as one value; the private session marked Private.
- **Importing its own export** updated all 4 sessions and left every field as it was, including `-pw s3cret!`. Into an empty list it added all 4, with the masked `-pw` left out.
- **A `;` separated sheet** with `putty_profile`-style headers, columns in another order and an extra `Owner` column: added one session and updated another's host only, keeping its username, arguments (with the password) and notes. Sessions.xml was saved.
- **Bad rows:** 7 skipped with the reasons listed above, the good row imported, a blank line ignored; a file without a Name column, and an empty file, refused.
- **10,000 sessions:** exported in 0.1 s (1.1 MB); the re-import planned in 0.2 s as 10,001 updates. It found the hand-edited session whose display name differs from its ID, which is why Name comes from the ID.
- **Not checked:** the menus and dialogs clicked through, and opening the file in Excel itself.

## The update check offers this fork's releases

**Before:** Help → Check for updates (and the check at start-up) asked `SilverGreen93/superputty` for its latest release. Someone running this fork would be offered upstream's build, and installing it would lose the fork's changes.

**Now:** the repository comes from `App.config`, `<add key="SuperPuTTY.UpdateRepository" value="waukeerules/superputty" />`.
- **The code's own default** is still upstream's repository (`frmSuperPutty.UpdateRepository`), so the change could go upstream as it is, with only this fork's `App.config` naming the fork.
- **Upstream's links** (About, Help → Documentation) are unchanged: the documentation still lives there.

**Checked:** built; `SuperXPuTTY.exe.config` has the key. The fork's latest release, read from the GitHub API, is `v2.1.2.0`, which parses the way the check does it, and with this build at 2.1.2.0 no update is offered. The message box itself wasn't shown.

## Fix: clicking a text box sometimes sent the typing to the terminal

**Symptom:** with a terminal tab open, clicking the toolbar's Host, Login or Password box, or the Sessions search box, sometimes showed the cursor there for a moment and then lost it: what was typed next went into PuTTY. Without a terminal tab it never happened.

**Cause:** `ChildWindowFocusHelper` listens to the shell's window-activation messages. When SuperXPuTTY becomes active again after another window was, it gives the active terminal the focus ("SHELLHOOK" in the log), about 100 ms later. That is right for Alt+Tab or the taskbar, but it also happened when you came back by clicking a text box, and took the focus straight out of it. "Another window" includes more than other applications: a floating SuperXPuTTY pane, a dialog, or a moment when no window is active. So it struck while moving between fields too, not only after using another program.

**Fix:** when the activation comes from a mouse click on one of SuperXPuTTY's own controls that takes typing or arrow keys (text boxes, combo boxes, the Sessions tree, lists), the focus stays where the click put it (`ChildWindowFocusHelper.IsClickOnInputControl`). The button is still down when the shell reports the activation, so `GetAsyncKeyState` and `WindowFromPoint` tell which control was clicked. Coming back any other way (Alt+Tab, the taskbar, the title bar, clicking the terminal) still focuses the terminal.

**Checked** with the real main window, an SSH tab to a lab host and real mouse clicks and typing:
- **Before the fix:** after another window was active, clicking the Sessions search box and typing "abc" sent it to PuTTY; the log showed `ReFocusPuTTY ... caller=SHELLHOOK`.
- **After the fix:**
  - clicking Host, Login, Password and the search box kept the typing in every case: from box to box, from the terminal, and coming back from another window;
  - coming back without clicking a box (like Alt+Tab) still put the focus in the terminal, twice out of twice.
- **Not checked:** floating panes and a second monitor.

## Fix: the menu and toolbars needed two clicks when another program was active

**Symptom:** with another window active (Notepad++, or the embedded PuTTY window), clicking SuperPuTTY's menu (e.g. Tools) or a toolbar button only brought SuperPuTTY to the front; it took a second click to open the menu or press the button.

**Cause:** a WinForms `ToolStrip` (and so a `MenuStrip`) answers `WM_MOUSEACTIVATE` with `MA_ACTIVATEANDEAT` when its window isn't active and the cursor is on it, so the click only activates the window. The button bar already worked around this in its own `WndProc`; the other strips didn't.

**Fix:** `Gui/ClickThrough.cs` hooks every menu, toolbar and status strip in the main window (and those added later, such as in tool windows) and each pane's tab strip (also in floating windows, via `DocumentTabs`), turning `MA_ACTIVATEANDEAT` into `MA_ACTIVATE`. frmSuperPutty calls `ClickThrough.Apply(this)`.

**Checked:** with another window active and the cursor over each strip, the menu bar and a toolbar answered "activate and discard the click" before and "activate" after; DockPanelSuite's tab strip already answered "activate". **Not checked:** clicking with the real mouse in SuperXPuTTY.

## Tabs as wide as their name, up to 30 characters

**Before:** every document tab was cut off at 200 pixels, so with the larger tab font (and at 150 % DPI) a name like `192.168.0.50` showed as "192.168.0....". The limit is a private constant inside DockPanelSuite's tab strips, not scaled for DPI or the font, with no setting to change it.

**Now:** tabs are as wide as their text, up to 30 characters of the tab font (`WideTabs.MaxCharacters`), then cut with "...". The tab strips are copies of DockPanelSuite 3.1.1's `VS2013DockPaneStrip` (used by the VS2015 Light, Blue and Dark themes) and `VS2005DockPaneStrip` (Classic), MIT licensed, changed only where marked "SuperPuTTY": the namespace, the width limit, and the theme assembly's internal tooltip strings, button images (loaded from its resources) and drawing path, which a copy can't reach. `WideTabs.Apply` swaps a theme's strip factory for ours; frmSuperPutty calls it after choosing the theme. The copies keep DockPanelSuite's class and member names, which `TabIcons` and `DocumentTabs` look up.

| File | Change |
|---|---|
| `DockPanelSuite/WideTabs.cs` | New. The limit, the factories, the Classic images |
| `DockPanelSuite/VS2013DockPaneStrip.cs`, `VS2005DockPaneStrip.cs` | New. The copied strips |
| `frmSuperPutty.cs` | Calls `WideTabs.Apply` |
| `Resources/third-party/dockpanelsuite-LICENSE.txt` | New. DockPanelSuite's licence |

**Checked:** in a test window with the real DockPanel and both themes, the copied strips were used, and three tabs ("a", "192.168.0.50", a 45-character name) were 57, 123 and 261 pixels wide in Light (24, 90 and 228 in Classic): the first two in full, the long one cut after about 30 characters. **Not checked** in SuperXPuTTY's window: the icons, colours and close buttons drawn over the tabs (TabIcons), the tab list, floating windows, and the Dark and Blue themes.

**When upgrading DockPanelSuite:** copy the two strips again from the new version and redo the marked changes.

## Highlighted Output: a session's output with syntax highlighting

Phase 1 of [docs/MonacoTerminal.md](docs/MonacoTerminal.md). **Off by default:** turn it on in **Tools → Options → Terminal**. Then right-click a PuTTY tab → **Highlighted Output** opens a panel beside it that shows everything the session printed, in a Monaco editor (the editor of VS Code), coloured by rules: on Cisco, `show ip interface brief` lines that are up/up in green and down/down in red, `connected` / `notconnect`, `is up, line protocol is up`, syslog severities, `%` errors, addresses and MACs. **PuTTY runs exactly as before**; the panel only reads the session log PuTTY already writes (`-sessionlog`, see [Session logs](#session-logs-and-spsl-waitfor)).

- **What the panel shows is what you saw.** The log holds what the device sent, including the backspaces and spaces IOS uses to erase `--More--`, line editing and cursor moves, so it can't be read line by line. The page feeds the log to an xterm.js terminal that is never shown, which rebuilds the screen the way PuTTY drew it; lines that scroll off its screen are copied into the editor, and the lines still on its screen are rewritten as they change.
- **Toolbar:** the rule set (Auto picks it from the prompt: `name#` is Cisco, `user@host>` is Junos), theme, Follow (keeps the newest line in view; scrolling up turns it off, scrolling to the end turns it on), Wrap, Find (also Ctrl+F; regex and match case), **Save As...** (the text as you saw it), **Open File...** (show a kept log or any text file instead, e.g. to try out rules), **Rules Folder** and **Reload Rules**. The editor is read-only, with folding (`show running-config` blocks), a minimap and copy.
- **Rules and themes are JSON files.** Defaults in `web\output\rules` (`generic`, `cisco` for IOS/IOS-XE/NX-OS, `junos`) and `web\output\themes` (PuTTY, Dark, Light) beside the exe. Your own `*.rules.json` / `*.theme.json` go in a `highlighting` folder in the SettingsFolder (shared with the team) or beside your `SuperXPuTTY.settings` (yours win); one with the same `id` (rules) or `name` (theme) replaces a default. **Rules Folder** creates the folder with a README describing the format. A rule is a JavaScript regex with a style; `"scope": "line"` colours the whole line, and a match's colour wins over its line's. A rule set can `include` others and list `detect` patterns for Auto. A theme sets the background, foreground, selection, font and the colours of the styles (`good`, `bad`, `warning`, `info`, `address`, `mac`, `prompt`, `keyword`, `comment`, or your own).
- **Restarted sessions:** PuTTY's Restart Session starts the log again; the panel keeps what it has and continues under a "log restarted" line. When the tab closes, the panel stays open with what was shown, titled "(closed)".
- **Needs the WebView2 runtime**, which comes with current Windows 10 and 11. The Options page shows its version; without it the menu item says so instead of opening. WebView2's profile goes in `%LOCALAPPDATA%\SuperXPuTTY\WebView2` (its default, beside the exe, isn't writable under Program Files). The page is served from the exe's folder under a made-up host name (`superputty-output.local`), never from the network; it can't navigate anywhere else, and devtools are off in Release builds.
- **Not saved in layouts:** a layout with the panel open restores without it.

| File | Change |
|---|---|
| `Gui/HighlightedOutputPanel.cs` | New. The docked panel: WebView2, polling the log (every 250 ms, up to 512 KB at a time, the next chunk only after the page says it's done), Save As, Open File, and messages to and from the page |
| `Utils/OutputHighlighting.cs` | New. The web folder, the user's rule folders, loading and merging rule and theme files (`Merge`), the README, the shared WebView2 environment and the runtime check |
| `web/output/index.html`, `output.js`, `output.css` | New. The page: the hidden xterm.js terminal, the Monaco editor, highlighting (decorations for the lines in view only, so long logs stay fast) and the toolbar |
| `web/shared/rules.js` | New. Compiling rule sets, Auto's detection and matching a line; shared with the built-in terminal |
| `web/output/rules/*.rules.json`, `web/output/themes/*.theme.json` | New. The default rule sets and themes |
| `web/lib/monaco` | New. Monaco Editor 0.57.0 (MIT), only the 18 files the page loads (4.9 MB of the package's 25 MB), with its LICENSE and ThirdPartyNotices |
| `web/lib/xterm` | New. xterm.js 6.0.0 (MIT), its stylesheet and LICENSE; also used by the built-in terminal |
| `Utils/SessionLog.cs` | `ReadBytesFrom`: raw bytes, so a UTF-8 character split between two reads isn't garbled |
| `ctlPuttyPanel.cs` | The **Highlighted Output** tab menu item, shown only when the option is on |
| `dlgFindPutty.cs` / `.Designer.cs` | The **Terminal** page (formerly Highlighting): the option, the WebView2 runtime's version, **Open Rules Folder** |
| `Properties/Settings.*`, `App.config` | `HighlightedOutputEnabled` (default off) |
| `SuperPutty.csproj` | The new files, `Microsoft.Web.WebView2` 1.0.4258.31, and `web\**` copied to the output |
| `SuperPuttyInstaller/Product.wxs` | `Microsoft.Web.WebView2.Core.dll`, `.WinForms.dll`, `runtimes\win-x64\|win-x86\|win-arm64\native\WebView2Loader.dll` (the exe is AnyCPU) and everything under `web\` |
| `Resources/third-party/monaco-editor-LICENSE.txt`, `xterm.js-LICENSE.txt` | New. The libraries' licences |

**Checked:**
- Built (Debug), including the installer; the MSI has the WebView2 files and all 30 web files in the right folders.
- The page in a test harness that hosts it in WebView2 the way the panel does (same host mapping, messages and acks), fed a log recorded from a lab Cisco switch (Catalyst, IOS) in 1,000-byte chunks: 118 lines, identical to what was on screen, with every `--More--` page clean; Auto picked Cisco; up/up green and down/down red, `connected` / `notconnect`, addresses and MACs coloured.
- In a browser: the same log fed in 97-byte pieces gives the same 118 lines; Follow, Find, Wrap, the right-click menu, "log restarted", light and dark themes; an 8 MB log (157,000 lines) loads in about 10 s with the window responsive.
- **Not checked:** the panel inside SuperXPuTTY itself (the menu item, docking it beside the tab, floating it, Save As and Open File dialogs, the Options page). These need the manual tests below.

**Known limits:**
- Full-screen programs (`vi`, `top`) show as the screen looked when they scrolled, not perfectly; device CLIs are line-based and come out exactly.
- A `clear` on Linux erases what was still on the hidden terminal's screen (up to 50 lines) before it reached the editor.
- Loading a very large existing log takes a while (about 1 MB a second); output arriving live is shown at once.

Manual tests:
1. **Tools → Options → Terminal**: the runtime's version is shown. Tick the option and click OK.
2. Open an SSH session to a Cisco device, right-click the tab → **Highlighted Output**: the panel opens to the right of the tab. Run `show ip interface brief`, `show interfaces status` and `show interfaces` (page through `--More--` with Space, then `q`): it should follow the output, with up interfaces green and down ones red, and no `--More--` left in the text.
3. Scroll the panel up: Follow unticks and new output doesn't move the view. Scroll to the end: it ticks again.
4. Ctrl+F in the panel, search for `down`. Try **Save As...** and open the file.
5. Switch Theme to Light and back, and change SuperPuTTY's own theme (Options → GUI): a new panel starts in the matching theme.
6. Right-click the tab → **Restart Session**: the panel continues under "log restarted".
7. **Rules Folder**: a `highlighting` folder with a README opens. Copy `web\output\rules\cisco.rules.json` there, change a colour's style (e.g. `connected` to `bad`), click **Reload Rules**: the change shows.
8. **Open File...** a kept log (Options → Advanced → Session logs): the panel shows it instead.
9. Float the panel, dock it back, close the session tab: the panel stays with "(closed)". Save and reload a layout with the panel open: it isn't restored, and nothing else breaks.
10. Turn the option off: the menu item goes away.

## Built-in terminal: highlighted as you type

Phase 2 of [docs/MonacoTerminal.md](docs/MonacoTerminal.md). **Off by default:** Tools → Options → Terminal → **Use the built-in terminal for SSH, Telnet, Rlogin, Raw and Serial sessions**. Tabs opened after that show a terminal drawn by SuperPuTTY instead of a PuTTY window, coloured live by the same rules as Highlighted Output (up interfaces green, down ones red, and so on).

- **The connection is still PuTTY's.** plink.exe (or KiTTY's klink.exe; chosen in Options > Paths, or beside PuTTY) connects with the session's PuTTY profile (`-load`), keys and Pageant, jump host (`-proxycmd`), default parameters and extra arguments: the same arguments PuTTY gets (`PuttyStartInfo.PlinkCommandLine`). It runs in a Windows pseudo console (ConPTY), so password, passphrase and host-key prompts appear in the terminal and are answered there, and the window size reaches the server. Without plink.exe or the WebView2 runtime the session opens in PuTTY as before.
- **It feels like PuTTY:** the PuTTY theme (grey on black, green cursor, PuTTY's colours) by default; the cursor's shape and blinking come from the PuTTY profile (it only blinks while the tab has the keyboard); selecting copies (**Copy on Select** in the menu turns that off); **Shift+Insert** and middle-click paste; Ctrl+Insert copies. Ctrl+C, Ctrl+V and the like go to the device as usual.
- **Matches of the selection are marked**, as in Notepad++: select `Gi0/1` or `unassigned` and every other place it appears gets a background (case-insensitive, whole words when a whole word is selected, one-line selections of 2 to 200 characters). **Mark Matches of Selection** in the menu turns it off. Both options apply to every terminal tab and are remembered. (The Highlighted Output panel does this already: it's Monaco's own selection highlight.)
- **Colours that disappeared after a selection** (fixed after the first test): xterm.js 6.0.0 keeps decorations in a list sorted by their marker's line, and disposing a marker sets its line to -1 before its decorations are removed, so they aren't found, stay in the list under the wrong line, and lookups for other lines fail. The selection and find marks disposed markers that way. Every mark now disposes its decoration first. The selected text itself isn't marked (the mark hid the selection), and find's current match gets a brighter mark.
- **A new tab has the keyboard** (fixed after the first test): SuperPuTTY gives a new tab the focus before its page has loaded, and that request was lost, so the cursor only appeared after a click. The terminal now remembers it and takes the keyboard when the page is ready, if its tab is still the one in front.
- **Submenus can be reached** (fixed after the first test): Highlighting and Theme closed as soon as the mouse was on one of their own items, and on the way to them across the row below. Only the main menu's rows close a submenu now, after a quarter of a second, and the submenu overlaps the menu so there's no gap.
- **The bell** (Ctrl+G from the device, e.g. Backspace at the start of a line or an error) does what the session's PuTTY profile says under Terminal → Bell: a short flash of the terminal for "Visual bell", the system's alert sound for the sound choices, nothing for None; a profile that doesn't set it flashes. PuTTY saves this as `Beep` (0 none, 1 default sound, 2 visual, 3 sound file, 4 PC speaker; `BellType` is only its internal name, which the first version read by mistake and so never found). Its over-use settings are followed too (`BellOverload`, `BellOverloadN`, `BellOverloadT`, `BellOverloadS`; by default 5 bells in 2 seconds silence it until 5 quiet seconds). The menu's **Bell** submenu overrides it for all terminal tabs (From PuTTY Profile, Flash, Sound, None). `PuttyDataHelper.GetProfileValue` now returns numeric settings (such as `Beep`) as text instead of null.
- **The cursor takes the session's environment colour** (red for production), from the session, its folder or a rule (`EnvironmentColors.For`), like its tab; it changes as soon as the colour is changed in the tree. Sessions without a colour keep the theme's cursor (PuTTY's green). The cursor rather than the prompt's text: it's the same on every device, while prompts (Linux, Palo Alto, Aruba, Extreme...) can't be recognised reliably.
- **Find's current match** is bright amber with black text; the other matches stay dark amber. (xterm.js draws the first-registered of two marks on the same cells, so the current match's usual mark is removed while it's current.)
- **cmd, PowerShell and WSL** use the built-in terminal too when the option is on, so every CLI tab looks and works the same (theme, find, menu, highlighting, Copy on Select). The shell runs directly in the pseudo console, as Windows Terminal runs it, starting in `%userprofile%` as before; history, arrows and PSReadLine work as in a console window. They get a session log like PuTTY's (so Search Output, WAITFOR and Watch for Activity work, which they didn't in their own window), and `exit` closes the tab. **Ctrl+C** stops a running command (`ping -t`): typing `0x03` into a pseudo console doesn't raise the console's Ctrl+C signal, so SuperPuTTY raises it (`ConPty.SendCtrlC`, attaching to the shell's console for a moment) after typing it. SuperPuTTY no longer uses the inheritable "ignore Ctrl+C" flag while attached (the arrow-key fix did; every shell started after it would have ignored Ctrl+C) but a handler, and clears the flag if it inherited it. Open in PuTTY is left out of their menu.
- **The cursor follows the PuTTY profile** (Window → Appearance): its shape (`CurType`: block, underline or vertical line) and whether it blinks (`BlinkCur`), read like the bell (`TerminalControl.CursorFromProfile`); a profile that doesn't set them gets PuTTY's steady block. The menu's **Cursor** submenu overrides the shape and the blinking separately, for all terminal tabs. Without the keyboard a block shows as an outline, as in PuTTY.
- **Font:** the menu's **Font** submenu has Larger, Smaller and Default Size (also Ctrl+mouse wheel, as in Windows Terminal), and the font: From Theme, From PuTTY Profile (Window → Appearance: `Font` and `FontHeight` in points, Courier New 10 pt by default) or any installed monospaced font (`TerminalControl.MonospaceFonts`: an "i" as wide as a "W"). Changing the font refits the columns and rows and tells the session.
- **One set of preferences:** the default highlighting rules, theme, font, font size, cursor, blinking, bell, copy on select and marking matches are kept in SuperPuTTY's settings (`TerminalPreferences`, one JSON setting), not in the browser view's own storage as before (choices made earlier start again from the defaults). Every tab's menu and **Tools → Options → Terminal** (formerly Highlighting) edit the same values, and every open terminal follows a change at once. The Terminal page has the two switches (the built-in terminal, Highlighted Output), a **Defaults for terminal tabs** box with all of these (`TerminalOptionsPanel`, built in code, and laid out by `dlgFindPutty.LayOutTerminalPage` rather than given a size, since it's added after the dialog has scaled for DPI: at 150 % a fixed size cut off the last row), the note on rule files, the WebView2 runtime's version and Open Rules Folder.
- **Tabs from the terminal's menu:** Close Tab, Close Other Tabs, Close Tabs to the Right and Close All Tabs, the same as the tab's own menu (`ctlPuttyPanel.RunTabCommand`).
- **PuTTY's Special Commands** (Break, SIGINT and the other signals, IGNORE, repeat key exchange) can't be sent: they're in PuTTY's window menu, and plink has no way to send them while it runs. They're protocol messages (an SSH "break" or "signal" request, Telnet's `IAC BRK`, a serial line held low), not characters, so no key produces them. Tried against the lab switch: a console Ctrl+Break, sent to plink as a key press or as the console signal, doesn't become a break; Windows ends plink and the session drops, with nothing in plink's `-v` event log. **Open in PuTTY (Special Commands)** in the menu opens the same session in a classic PuTTY tab for that (`TerminalControl.OpenInPutty`), e.g. for a Break during Cisco password recovery.
- **Right-click keeps the selection.** The selection is remembered as the right button goes down and put back if the click cleared it, so Copy and Copy and Paste in the menu work on it.
- **Right-click menu:** Copy, Paste, Copy and Paste, Find... (Ctrl+Shift+F; match case, regex, all matches marked), Select All, Clear Scrollback, Clear Screen (the screen moves into the scrollback and the prompt stays at the top), Clear Screen and Scrollback, **Open as Document** (the Highlighted Output panel for the tab), **Highlighting** (Default, Auto, a rule set, None; for this tab only, see below) and **Theme** (for every terminal tab).
- **Dropped sessions:** plink's error stays in the terminal (e.g. `FATAL ERROR: Network error: Connection refused`), the tab gets the disconnected badge, and it reconnects with the Options → Advanced settings (every N seconds, up to N times), or on Enter. Logging out (`exit`) closes the tab, as PuTTY does. The tab menu's Reconnect, Stop Reconnecting, Restart Session, Clear Scrollback, Reset Terminal and Copy All to Clipboard work; Event Log, Change Settings and About PuTTY are hidden.
- **Send Commands, the button bar and SPSL** type straight into plink, with keys as terminal sequences (`TerminalControl.KeySequence`), not window messages.
- **A line feed sent by a button (`\n`) or Send Commands runs the command**, as it does in PuTTY. Before the fix, `ls -la\n` was typed but not run. PuTTY sent the line feed to the device as a character, and devices and shells take that as Enter; typed into a pseudo console it becomes Ctrl+Enter, which plink and cmd drop. `TerminalControl.LineFeedsAsEnter` now sends each line feed (and each CR LF pair) as one Enter (CR). Checked with a test program in cmd and PowerShell: `echo hi-from-button\n` sent through `SendCommand` ran.
- **The prompt after Enter is drawn with its line break.** Before the fix, after Enter the new line often stayed blank for a moment, sometimes for a second or two or until the next Enter (in cmd and over SSH). The pseudo console sends Enter's line break (`\r\n`) at once and the new prompt in its next frame, about 16 ms later (measured on cmd and the lab switch; PowerShell sends both together), and the page drew each as it came, so a frame showed a blank line. WebView2 sometimes left that frame on screen; turning off its GPU drawing made the blank shorter but didn't remove it. Output that ends in a line break (LF, or a lone CR: over SSH the switch's CR and LF sometimes arrived separately, in 15 of 40 Enters) now waits up to 100 ms for more (`TerminalControl.HoldForNextLine`), so the prompt arrives with the line break; typed characters echoed back still go at once. The first version waited 40 ms and only after an LF; the blank still showed once or twice over SSH. On a Linux host it still showed: bash sends Enter's line break between its bracketed-paste switches (`ESC[?2004l`, CR LF, `ESC[?2004h`), so the output ended in an escape sequence. A line break followed only by escape sequences that print nothing (CSI, or OSC such as a window title) now counts too (`TerminalControl.EndsWithLineBreak`, checked against the bytes captured from cmd, the switch and bash, and cases that must not wait: a prompt, an echoed character, coloured text, a cut-off sequence). Checked in the full app with real key presses on the Linux host: each Enter gives one update with the prompt, 14–21 ms after the key. Checked in the full app with real key presses: each Enter in cmd now gives one update with the prompt, about 15 ms after the key. The diagnosis used a timing monitor on the real app (WebView2's DevTools protocol), which showed the page drawing each piece within 25 ms.
- **Slow output is logged:** output that waits more than half a second in SuperPuTTY before reaching the page, or that the page takes that long to draw, is logged at Info with its size and the delay (never the text).
- **Arrow keys and Ctrl+C** (fixed after the first test): plink reads its console with only "processed input" on, so the pseudo console turned the arrows' `ESC [ A` into key events with no character, which plink dropped, and Ctrl+C was a console signal, not `0x03` for the device. Before typing an ESC sequence or Ctrl+C, SuperPuTTY attaches to plink's console for a moment and turns on virtual-terminal input and off processed input (`ConPty.EnsureVirtualTerminalInput`); plink sets its mode once the session starts, so it's checked each time rather than once. Up/Down recall history, Left/Right, Home/End and Delete edit the line, Ctrl+C aborts it. Ctrl+A, Ctrl+E and other control keys already worked.
- **Session log:** written by SuperPuTTY at the same path PuTTY would use (kept or temporary). The pseudo console rewrites the device's bytes (it draws `--More--` being erased as cursor moves, for example), so each line is logged from the screen once the cursor leaves it: the log is clean text, without `--More--` or escape codes. Search Output, Watch for Activity and Highlighted Output read it as before. **WAITFOR** also sees the line the cursor is on, so it finds a prompt like `Password:` before Enter is pressed. SuperPuTTY's own notices ("Session ended...") are in the log too.
- **Passwords:** `-pw` is used as for PuTTY (only when Options allows it) and only for SSH, because plink refuses it for other protocols, where PuTTY ignores it.

| File | Change |
|---|---|
| `Utils/ConPty.cs` | New. A program in a pseudo console: start, output, typing, resize, exit |
| `Utils/TerminalPreferences.cs` | New. The terminal's preferences, kept in the `TerminalPreferences` setting, with a Changed event |
| `Gui/TerminalOptionsPanel.cs` | New. The defaults on Options → Terminal |
| `Gui/TerminalControl.cs` | New. The tab's terminal (`IEmbeddedApplication`, `ICommandReceiver`): the page, plink, the session log, WAITFOR's text, reconnecting, the clipboard |
| `web/terminal/index.html`, `terminal.js`, `terminal.css` | New. The terminal page: xterm.js, fitting to the tab, live highlighting (decorations), the menu, find, PuTTY's keys and mouse, the log lines |
| `web/output/themes/putty.theme.json` | New. PuTTY's default colours |
| `Utils/PuttyStartInfo.cs` | `PlinkCommandLine` |
| `ctlPuttyPanel.cs` | Creates the terminal when the option is on (`TerminalControl.ShouldUse`); WAITFOR, Reconnect, Stop Reconnecting, the PuTTY menu items and the connection badge go to it |
| `dlgFindPutty.cs` / `.Designer.cs` | The option on the Terminal page |
| `Properties/Settings.*`, `App.config` | `BuiltInTerminal` (default off), `TerminalPreferences` (empty: all defaults) |
| `SuperPutty.csproj` | The new files (the page is under `web\**`, already copied and installed) |

**Checked** against the lab Cisco switch (IOS), with a test program hosting `TerminalControl` from the built exe and driving it the way ctlPuttyPanel's WAITFOR and Send Commands do:
- SSH: login, `enable` and its password, WAITFOR for each prompt, `--More--` with Space and `q`, a window resize (125×35 to 158×42 characters), `exit` closing the session (Ended raised); live highlighting as in the screenshot of phase 1. The log had 72 clean lines, no `--More--`, no password.
- A refused connection: plink's error shown, the tab disconnected, a reconnect scheduled with "Reconnecting in 10 s (1 of 6)".
- Serial (COM6, 9600 8N1): the console's prompt and `show clock`. This found the `-pw` problem above.
- In a browser: the menu, find (11 matches for `notconnect`, regex, Enter and Shift+Enter), Select All copying, the Highlighting submenu, the three Clear items (and that they don't add to the log), a page loaded while hidden waiting for a real size before starting.
- Keys and highlighting, with a second test program typing real key events into the page (WebView2's DevTools protocol) against the switch: Up recalled `show clock`, Left inserted a character, Ctrl+A, Ctrl+E, and Ctrl+C aborted the line with the session still up; after three `sh ip int bri` and selecting and clearing `unassigned`, xterm.js's own lookup found a decoration on every one of the 54 lines the rules match (the page before this fix lost 38 of them).
- In SuperXPuTTY, by you: the menu, Copy from it, and copy on select.
- **Not checked:** opening from the tree, docking, floating, layouts, two monitors, high DPI, the dark interface theme, middle-click in WebView2, SuperPuTTY's keyboard shortcuts while the terminal has the keyboard, Telnet and keyboard-interactive one-time passwords through the built-in terminal (Telnet was checked with plink in a pseudo console during the study), and a jump host.

**Known limits:**
- What's typed before plink has started (the first second or so) is dropped.
- Full-screen programs (`vi`, `top`) work, but their screens are logged only as they first appear.
- ~~Clear Screen and Reset Terminal are local: a later resize can bring back what the pseudo console still has on its screen.~~ Fixed: the pseudo console keeps its own copy of the screen and repaints it after every resize (a theme or font change, resizing the tab), so cleared text came back. Clear Screen, Clear Screen and Scrollback and Reset Terminal now clear that copy too, keeping the cursor's line at the top (`ConPty.ClearScreen`, through the console functions, since Windows' pseudo console has no clear call). Checked: on the switch, after clearing and then changing the theme and resizing the window, nothing came back; in cmd, a half-typed command moved to the top could still be edited and run.
- Each terminal tab costs some memory for its browser view (tens of MB).

Manual tests:
1. Options → Terminal: tick **Use the built-in terminal**, OK. Open an SSH session to a Cisco device: the tab shows a black terminal with a green cursor, and the login works as in PuTTY (password prompt, or none with a key).
2. `show ip interface brief`, `show interfaces status`, `show interfaces` with Space and `q`: colours as you go, no `--More--` left on screen.
3. Up/Down for history, Left/Right, Home/End, Delete, Ctrl+A, Ctrl+E, Tab completion and `?` at the device's prompt, as in PuTTY.
   - Select text (copied), Shift+Insert and middle-click paste it; Ctrl+C at the prompt reaches the device. Select text and right-click: Copy is enabled and copies it.
   - Double-click `unassigned` in `show ip interface brief`: every other `unassigned` gets a background. Untick **Mark Matches of Selection** in the menu: the marks go. Untick **Copy on Select**: selecting no longer changes the clipboard. Both stay as set in a new tab.
4. Right-click: try each item, the Highlighting, Theme and Bell submenus, and Find... (Enter, Shift+Enter, Escape; the current match is brighter).
   - Press Backspace at an empty prompt: the terminal flashes (or beeps, if the PuTTY profile's bell is a sound). Hold it down: after a few it goes quiet.
   - Give the session (or its folder) the Red (production) colour in the Sessions tree: the cursor turns red at once, on a Cisco device and on a Linux host alike. Set it back to None: the cursor is green again.
5. Click another tab and back: the cursor stops and starts blinking. Ctrl+Tab and SuperPuTTY's other shortcuts still work.
6. Send Commands and a button-bar button into the tab; an SPSL script with WAITFOR (e.g. `SENDLINE show clock` then `WAITFOR #`).
7. Pull the network cable (or `reload` a lab device): the error shows, the tab badge changes, and it reconnects; Stop Reconnecting in the tab menu stops it; Enter reconnects.
8. `exit`: the tab closes.
9. Telnet and Serial sessions; a session with a jump host; a session with a PuTTY profile that sets port forwarding (the forward should work).
10. Search Output and Watch for Activity on a terminal tab; Open as Document.
11. Float the tab, dock it back, save and restore a layout with it, move it to a second monitor, and try the Dark interface theme.
12. Untick the option: new tabs open in PuTTY again.

## Built-in terminal: paste check, rules per session, scrollback, keys, links, faster drawing, log time stamps

Seven additions to the built-in terminal. All but the per-session rules are on **Tools → Options → Terminal**, whose page now scrolls.

- **Pastes of several lines shown first.** Each pasted line runs as it arrives, so 40 lines of config pasted by mistake into a switch would all be applied. A paste of at least **Check pastes of** lines (2 by default; 0 never; a line break at the very end doesn't count), in any session, opens a **Paste** window (`Gui/dlgPasteCheck`) with the text in a box where it can be read and changed; the heading counts the lines as it's edited. **Paste** (or Ctrl+Enter) types what's in the box, **Cancel** or Esc nothing. The caret starts at the beginning with nothing selected, and Enter adds a line, so a key pressed by mistake doesn't paste. Every paste goes through `TerminalControl.Paste`: Shift+Insert, middle-click, the menu's Paste and Copy and Paste, and the browser's own paste event, which the page catches.
- **Highlighting rules per session or folder.** Edit Session has a **Highlight** drop-down beside the username, Folder settings has **Highlighting**, and the folder's Edit sessions... (bulk edit) can set it. It's `SessionData.HighlightRules`: a rule set's id (`cisco`, `junos`, `generic` or your own), Auto or None. Empty, the session takes the nearest folder's (`FolderSettings.WithDefaults`), else Options' default. It's saved in Sessions.xml only when set, exported to CSV as "Highlighting", and stored in the Database sessions table as `highlight_rules`, in the sessions and the folders table (Create / upgrade schema adds it; lockable). The tab's **Highlighting** menu now chooses for that tab only, until it's closed, with **Default (...)** to go back; before, it changed every tab.
- **Scrollback:** 10,000 lines by default; 0 keeps as many as there are, up to a million (`TerminalPreferences.UnlimitedScrollback`: xterm.js needs a number, and a million lines of 120 columns is over a gigabyte). Not the PuTTY profile's, which is often small.
- **Keys from the PuTTY profile** (Terminal → Keyboard): Backspace as Control-? or Control-H (`BackspaceIsDelete`), Home and End as standard `ESC[1~` / `ESC[4~` or rxvt's (`RXVTHomeEnd`), and F1–F12 in the profile's style (`LinuxFunctionKeys`: ESC[n~, Linux, Xterm R6, VT400, VT100+, SCO, Xterm 216+; without modifiers, 216+ is the same as R6 and VT400 the same as ESC[n~) (`TerminalControl.KeysFromProfile`, sent by the page's `profileKey`). Before, xterm.js's own (`ESC[H`, `ESC OP`) went to the device, which differs from PuTTY's defaults. **Keys** can go back to xterm's. cmd, PowerShell and WSL keep xterm's, which the pseudo console understands. F5 and F12 no longer reach the browser view (F5 reloaded the page).
- **Links:** web addresses in the output are underlined under the mouse and open in the browser with **Ctrl+click**, as in Windows Terminal, so selecting text never opens anything. Only `http` and `https` are opened (`TerminalControl.IsWebLink`); the output comes from the device, so anything else is ignored. xterm.js's web-links add-on 0.12.0.
- **Faster drawing:** xterm.js's WebGL renderer (add-on 0.19.0) draws the terminal on the graphics card, much faster with a lot of output. **Drawing → Compatible** goes back to the DOM renderer, which also takes over when WebGL isn't available or the driver drops it.
- **Time stamps in the session log:** a line like `--- 2026-11-01 02:50:12 ---` before output, at most every 30 seconds (**Log time stamps**, 0 for none). Stamps come only with output, so an idle session adds none; they're in the log file (and so in Search Output and Highlighted Output), not in WAITFOR's text.

| File | Change |
|---|---|
| `web/lib/xterm/addon-web-links.js`, `addon-webgl.js` | New. xterm.js's web-links 0.12.0 and WebGL 0.19.0 add-ons (MIT, the same authors and licence as xterm.js), the UMD builds from npm |
| `web/terminal/index.html`, `terminal.js` | Load the add-ons; per-tab rules and Default; pastes through TerminalControl; scrollback, renderer and the profile's keys; Ctrl+click links |
| `Gui/dlgPasteCheck.cs` | New. The Paste window: the text in an editable box, Paste and Cancel |
| `Gui/TerminalControl.cs` | `Paste` (opens the Paste window), `PasteLineCount`, `ShouldCheckPaste`; `KeysFromProfile`; the session's rules in the page's config; `OpenLink`, `IsWebLink`; `LogStamp`, `TimeStamp` |
| `Utils/TerminalPreferences.cs` | `pasteLines`, `scrollback`, `keys`, `logTimestamps`, `renderer`; `UnlimitedScrollback` |
| `Gui/TerminalOptionsPanel.cs` | Rows for them |
| `dlgFindPutty.cs` | The Terminal page scrolls |
| `Utils/OutputHighlighting.cs` | `FillRuleChoices`, `SelectedRuleChoice`: the highlighting drop-down shared by the dialogs |
| `Data/SessionData.cs` | `HighlightRules` |
| `Data/FolderSettings.cs` | `HighlightRules`, applied by `WithDefaults` and shown by `Describe` |
| `dlgEditSession.cs` / `.Designer.cs` | The Highlight drop-down (the username box is shorter) |
| `Gui/dlgFolderSettings.cs`, `Gui/dlgBulkEdit.cs`, `Data/SessionBulkEdit.cs`, `Data/SessionCsv.cs` | Highlighting there too |
| `Data/DatabaseSessions.cs`, `docs/DatabaseSessions.md` | `highlight_rules` in the sessions and folders tables, optional until the upgrade, lockable |

**Checked:**
- The helpers, against the built exe: line counts (a trailing line break doesn't count), production red versus another red, orange, purple and none, when to ask in each mode, time stamps (first, too soon, after 30 s, off), and links (http and https opened; `file:`, `javascript:` and relative paths not). Both PuTTY profiles here (Default Settings, root) give PuTTY's default keys.
- In the full app (`bin\Debug`, with a test program on WebView2's DevTools protocol), on the cmd session, which is coloured red: WebGL drawing (canvases, no DOM rows) and 10,000 lines of scrollback. A three-line paste asked; **No** pasted nothing; one line pasted without asking; **Yes** pasted and both commands ran. A plain click on `https://example.com/...` did nothing, a Ctrl+click asked SuperPuTTY to open it (the test stopped it there). The Highlighting menu showed Default (Auto) ticked, then the rule sets, and choosing None saved no preference.
- On the Linux host (purple): Home, End, F1, F5, F12 and Backspace sent `ESC[1~`, `ESC[4~`, `ESC[11~`, `ESC[15~`, `ESC[24~` and DEL, and F5 didn't reload the page. Three lines pasted without a question (bash, with bracketed paste, waits for Enter, as with PuTTY). The session log began with a time stamp.
- Edit Session, Folder settings and the Options page drawn at 100 %: the Highlight drop-down beside the username, Highlighting in the folder dialog, the new rows on the Terminal page, scrolled to the bottom.
- The Paste window, after the first test (it was a Yes/No question for red sessions only, and the line count is now a setting): in the full app on the cmd session, three pasted lines showed in the box, typing a fourth line made the heading say 4, replacing the text and pressing Paste ran the new lines and not the old ones, Cancel pasted nothing, and one line went straight in. The Options page drawn at 100 % with the Check pastes of row. The user checked the scrollback, links, the dark theme, high DPI and a Database session with the new column.
- **Not checked:** bulk edit and CSV with the new column, a profile with non-default keys on a device that cares, and the Paste window at high DPI.

Manual tests:
1. Paste three lines into a terminal tab (Shift+Insert, middle-click, the menu): the Paste window shows them. Change a line and press Paste: the changed text runs. Cancel pastes nothing. One line: no window. Options → Terminal → Check pastes of 5: three lines go straight in; 0: never asks.
2. Edit Session → Highlight → Junos on a Cisco session: its new tab uses the Junos rules. Set a folder's Highlighting to Cisco: its sessions with no choice of their own use Cisco. The tab menu's Highlighting → None turns colours off in that tab only; Default brings them back.
3. Scrollback 0, then a long `show tech`: everything stays in the scrollback.
4. A PuTTY profile with Backspace set to Control-H, Home/End to rxvt and Function keys to VT100+: on a Linux host, `cat -v` then the keys shows `^H`, `^[[H`, `^[Ow`, `^[OP`. Options → Keys → xterm's: xterm's codes again.
5. `echo https://example.com`: Ctrl+click opens the browser; a plain click or a drag selects as before.
6. Options → Drawing → Compatible, then back: the terminal keeps working; `cat` a large file in each to compare speed.
7. Leave a session idle, then run a command: the session log gets one time stamp before its output, and no more for 30 seconds.

## Fix: finding text in a terminal tab took the colours off other lines

**Symptom:** in a built-in terminal tab, after using the find bar (for example `unassigned` in `sh ip int bri`, moving between matches with Shift+Enter, then typing more or closing it), some lines lost both their highlighting and their find marks, while others kept theirs. They stayed uncoloured until the rules or theme were changed.

**Cause:** xterm.js 6.0.0 keeps decorations in a list sorted by their marker's line and finds a line's decorations with a binary search. Disposing a decoration only queues its removal; the list is cleaned up later. The earlier fix disposed each decoration before its marker, but disposing the marker straight after sets its line to -1 while the decoration is still in the list. The next removal's binary search then runs over a list with a -1 among the real lines, and when the lines aren't removed top to bottom it misses. Find removed its current match last, wherever it was, so that one stayed in the list for good under line -1, and every later search for lines past it failed: their colours disappeared.

**Fix:** marks and highlighted lines are removed in three steps: all their decorations, then `flushDecorations`, then the markers. `flushDecorations` adds a decoration and removes it again at once, because adding one makes xterm.js apply the queued removals first; it has no other way to ask for that. Changed in `web/terminal/terminal.js` (`unmark`, `disposeEntries`, `flushDecorations`).

**Checked** in the terminal page (served locally, in a browser) with the switch's `sh ip int bri` output and the Cisco rules, typing `unassign` into the find bar, Shift+Enter four times, then typing and deleting a letter:
- **Before the fix:** 8 of the 15 coloured lines had no decoration xterm.js could find, and 7 removed marks were stuck in the list.
- **After the fix:** every line kept its colour, and closing the find bar left no stuck marks.
- **Not checked:** the full app (the page is the same one).

## Environment colours in the tree as a circle behind the icon

The Sessions tree showed an environment colour as the name's text colour, and red or orange names were hard to read, on the dark theme especially. Now the names keep the tree's text colour and the colour is a filled circle behind the icon, larger than it, with a thin dark outline following the icon's shape so it reads on any circle colour (an orange logo on red) (`IconLibrary.EnsureWithEnvironment`, `DrawOnCircle`). The icon keeps its full size: the tree has its own copy of the icon list whose cells are 36 % larger than the icons (`IconLibrary.NewPaddedList`), so every row is taller and the circle shows in the room round the icon. Rows are 2 px (at 100 %) taller than the cells, so circles on neighbouring rows don't touch. The open-tab dot is sized for the icon and sits over its corner. Edit Session's icon button keeps the normal list. Session icons on **tabs** get the same dark outline (`IconLibrary.Outlined`, used by `SuperPuTTY.GetIconForSession`), drawn in by the outline's width (about a pixel) so it isn't cut off: a grey logo was hard to make out on a grey or coloured tab. Status badges (disconnected, activity, script running) aren't outlined. It's another drawn variant of the icon, like the open-tab dot, which still goes on top (key `switch#1E63B4|env#C62828|connected`). The same nodes get it as before: folders where a colour starts, and sessions with their own colour. Favorites and Recent entries now show their session's colour too; before, they weren't coloured.

| File | Change |
|---|---|
| `Gui/IconLibrary.cs` | `EnsureWithEnvironment`, `DrawOnCircle`; `NewPaddedList` (icons at their size in larger cells), which `Ensure`, `Fill` and `DrawStateDot` follow; `Outlined` and `DrawOutlined` (the outline, shared with tabs) |
| `SuperPuTTY.cs` | `GetIconForSession` outlines the tab icon |
| `SessionTreeview.cs` | `ColorNode` sets the icon instead of the text colour; `SessionImageKey` adds the circle; the tree's padded icon list |
| `Gui/EnvironmentColors.cs` | `ForText` removed (it lightened the text colour on the dark theme) |

**Checked:** built, and the real main window on the dark theme: Favorites, Recent and Saved sessions showed red, green, orange and grey circles behind the icons, the open-tab dot kept its place, and every name was in the normal text colour.
Then with the outline and the icons drawn smaller on the circle (84 %, then 78 %): square icons hid most of the circle, so the cells were made larger instead. With the padded list: coloured icons the same size as uncoloured ones, the circle round each, taller rows. A cmd tab on its red (production) fill: the icon's outline showed. With the row spacing: a gap between the circles of neighbouring rows.
**Not checked:** the light theme (the drawing doesn't depend on it), icon sizes other than the one set here, and coloured Database folders.

Manual test: colour a session red and a folder green: both icons sit, at their full size, on larger circles of their colour, and the names stay white (dark theme) or black (light). Rows are taller than before. Open the red session: the green dot shows on top of the circle, and the tab's icon has a dark outline. Change Options → Icon size: the circles are redrawn at the new size.

## Fix: the Sessions pane's edge was hard to grab for resizing

**Symptom:** finding the spot to drag the Sessions pane (or any docked pane) wider or narrower took several tries, on a high-DPI display especially.

**Cause:** DockPanelSuite's VS2015 themes make the splitter between docked panes 6 pixels wide, and don't scale it for the display. At 150 % that's 6 physical pixels to hit, next to the pane's scrollbar. Setting the width after the theme is given to the DockPanel does nothing: the theme's splitter controls copy it when they're made, which is when the theme is set.

**Fix:** `frmSuperPutty.WidenSplitters` sets the theme's `Measures.SplitterSize` to 8 pixels at 100 %, scaled for the display (12 at 150 %), before the theme is set.

That alone left the cursor flickering as you moved over the edge: the splitter was a strip of the background colour, and the line you see and aim at is the pane's own 1-pixel border, just left of it. Resting on it, the smallest movement crossed between the pane (normal pointer) and the splitter (resize arrow). So the splitter now has a line of its own, 2 pixels at 100 %, down its middle, which lights up under the pointer (`Gui/SplitterGrips.cs`: a `VS2013WindowSplitterControl` subclass, made by a factory put in the theme before it's set). Aiming at it puts the pointer in the middle of the zone. The Classic (VS2005) theme's splitters are a different control and are left as they were.

| File | Change |
|---|---|
| `frmSuperPutty.cs` | The theme is built, then widened and given the grips, then set; `WidenSplitters` |
| `Gui/SplitterGrips.cs` | New. The splitter with the line, and the factory that makes it |

**Checked** with a program moving the pointer one pixel at a time across the Sessions pane's right edge in the real window, at 150 %, and recording where the cursor was the resize arrow: 6 pixels before the fix (and still 6 when the width was set after the theme), 12 after. Then, with a recorder of every cursor change while you moved over the edge: the cursor flipped between the pane's border pixel and the splitter's first. With the grip line: its colour read from the screen, dim with the pointer away and lighter over it, in the middle of the 12 pixels (dark theme, 150 %).
**Not checked:** dragging (DockPanelSuite's own, unchanged), the light themes' line colour, the Classic (VS2005) theme, and panes docked elsewhere.

## Selecting several sessions in the tree

**Ctrl+click** adds a session to the selection or takes it out, and **Shift+click** selects every session shown between the last session clicked and this one. A plain click or the arrow keys go back to one. Only sessions take part, not folders or Favorites and Recent entries.

- **Connect** (the menu, or Enter) opens them all; the menu says "Connect 5 sessions".
- **Delete** (the menu, or the Delete key) asks once, listing them ("Delete 5 sessions"). Database sessions go to the table in one batch (`DatabaseSessions.SubmitBatch`); those locked, or in a table that can't be written, are left and listed. Sessions.xml is saved once.
- **Edit** (the menu, or Shift+Enter) opens the dialog a folder's Edit sessions... opens, for the selected sessions ("Edit 5 selected sessions"): tick the fields to set on them all (`SessionTreeview.BulkEditSessions`, `Gui/dlgBulkEdit`). Sessions in a database table that can't be written, or locked in it, are left out, after saying which.
- The other menu items (Rename, Move to...) still act on the session right-clicked.
- The WinForms tree selects one node only, so the others are kept in `SessionTreeview.multiSelection` and painted with a selection background; the last one clicked keeps the tree's own highlight. A right-click on a selected session keeps the selection; anywhere else resets it.
- **No flicker:** Ctrl+click and Shift+click made the other sessions flicker. Every colour set on a node redraws the tree, the selection's colours were set again on every node at each click, and the tree drew straight to the screen. Now only nodes whose colour changes are touched, and the tree control's own double buffering is on (`SessionTreeview.DoubleBufferTree`, `TVS_EX_DOUBLEBUFFER`, set again whenever the handle is made; WinForms on .NET Framework doesn't set it). Checked: the running tree reports the style, and selecting with Shift+click and Ctrl+click still works; whether the flicker is gone was left to the eye.

| File | Change |
|---|---|
| `SessionTreeview.cs` | `treeView1_BeforeSelectMulti`, `SelectedSessionNodes`, `SessionsBetween`, `SetMultiSelection`, `DeleteSessions`, `BulkEditSessions`; Connect, Enter, Edit, Delete and the menu's text use the selection |

**Checked** in the real window with real clicks on rows found by name: a click on 192.168.3.2-2, Shift+click on Media Server and Ctrl+click on Windows 7 Host selected those five; the menu read "Connect 5 sessions" and "Delete 5 sessions"; Delete asked about the five by name, and No left them all (Sessions.xml unchanged); a plain click went back to one. Ctrl+click on a folder reset the selection. Selecting Media Server and Windows 7 Host, the menu offered "Edit 2 sessions...", which opened "Edit 2 selected sessions" listing both; Cancel saved nothing. A real delete of three selected sessions (by the user) removed all three with one save.
**Not checked:** connecting several at once (it opens each as a double-click does), changing fields through Edit on a selection (the dialog is the folder one, unchanged), Database sessions in a selection, and the light theme's colours.

Manual test: select three sessions with Shift+click, right-click one → Connect 3 sessions: three tabs open. Ctrl+click one out, right-click → Edit 2 sessions..., tick Username and set it: both get it. Press Delete: one question for the two; Yes deletes both and Sessions.xml is saved once.

## Fix: changing a session's connection type replaced its icon

**Symptom:** a duplicate of Windows 7 Host (RDP, with a green Windows icon) changed to SSH in the dialog was saved with the plain computer icon. The same happened editing any session: changing the connection type threw away the icon chosen for it.

**Cause:** Edit Session sets an icon for each connection type (a computer for SSH, a map for RDP, a terminal for cmd...) whenever the type changes, whatever icon was there.

**Fix:** it only does so while the icon is still one of those defaults (`dlgEditSession.IsProtocolIcon`); an icon chosen for the session, or a coloured one, stays.

**Checked** in the real window: a duplicate of Windows 7 Host kept `windows#2E7D32` after its type was switched to SSH and saved. The copy was deleted afterwards.

## Fix: a session's tooltip kept its old protocol until a restart

**Symptom:** after editing a session from SSH to RDP, its tooltip in the Sessions tree still said `ssh://...` until SuperXPuTTY was restarted.

**Cause:** the tooltip was written once, when the node was added; edits only updated the node's name and icon.

**Fix:** the tooltip (`SessionTreeview.SessionToolTip`, with the database notes it had) is built again whenever one of the session's properties changes, and Favorites and Recent entries for it are rebuilt too.

**Checked** in the real window: a session edited to another protocol showed the new address (`ps://localhost:3389`) in its tooltip straight after saving. The test session was deleted afterwards.

## The installer's licence names this fork, without a version

The installer's licence page (`License.rtf`) still said "SuperXPuTTY version 2.1.1.0" and listed only upstream's copyrights. Its heading is now just "SuperXPuTTY", so it can't go out of date (the installer shows the version elsewhere), and `License.rtf` and `License.txt` both start with "Copyright (c) 2026 waukeerules", with a link to this repository in the installer.

The installer isn't rebuilt when only `License.rtf` changes, as its project doesn't list the file as an input; the README's release steps say to rebuild it (`/t:SuperPuttyInstaller:Rebuild`).

**Checked:** `License.rtf` loaded into a RichTextBox reads "SuperXPuTTY", "MIT License", then the three copyright lines; the rebuilt MSI's licence text (its `Control` table) has the new line and no "version 2.1.", and its ProductVersion is 2.3.0.0.

## Local overrides, and where a session is kept in Edit Session's title

**Edit Session's title says where the session is kept:** "Edit session: core-sw1 — Database sessions", "— Private sessions (only yours)" or "— Saved sessions (Sessions.xml)"; new sessions and copies say where they'll be saved (`SessionTreeview.StoreDescription`).

**Local overrides:** right-click a session → **Local overrides...** to use your own host, port, username, PuTTY profile, extra arguments or jump host whenever it opens, without editing it. For a Database session whose fields are locked, or a shared Sessions.xml session you don't want to change for everyone.
- **The dialog** (`Gui/dlgSessionOverrides.cs`) has a box to tick per field, the value to use, and the stored value beside it (a `-pw` password in the stored extra arguments is shown as `****`). **Clear all** removes them. A ticked, empty jump host means none, even if the session or its folder has one.
- **Kept** in `SessionOverrides.json` beside your settings (`Data/SessionOverrides.cs`), by session ID; the session itself is never changed. They follow a session that's renamed or moved and are dropped when it's deleted (`SessionShortcuts.Rename` and `Forget` pass these on).
- **Applied** by `SessionVariables.Resolve`, after the folders' defaults, so every launch gets them (PuTTY, the built-in terminal, SCP, WinSCP, RDP) and they beat both the session's and its folder's values; placeholders in them are filled in as usual.
- **Your own notes:** a **Notes** field keeps your own note for a session. It shows instead of the stored note in the tooltip, Search finds it (searching notes), and Edit Session's Notes field says you have one. It doesn't change the stored note, so a team's shared note stays as it is.
- **Several sessions at once:** with several selected, the menu reads "Local overrides for 5 sessions...". Each field's box then has three states: ticked sets the value on all of them, unticked removes that override from all, and filled (where they differ) leaves each as it is. A box starts ticked when all of them share the override, unticked when none has one, filled otherwise, so OK without changes changes nothing (`dlgSessionOverrides.Merge`). The stored values show "(varies)" where the sessions differ.
- **Shown** in the session's tooltip: "Your local overrides: Username, Host".
- **In Edit Session** (a saved session, not a new one or a copy): a **Local overrides** button left of Save names the overridden fields in orange ("Local overrides: Username...") and opens the dialog; overrides changed there are saved at once, whether or not the session is. Each overridden field's tooltip says which value is used instead (extra arguments aren't shown, as they can hold a password). `dlgEditSession.OverridesSessionId`, set by the tree when editing.

| File | Change |
|---|---|
| `Data/SessionOverrides.cs` | New. The overrides: read, set, apply, follow renames, forget |
| `Gui/dlgSessionOverrides.cs` | New. The dialog |
| `Utils/SessionVariables.cs` | `Resolve` applies them |
| `Data/SessionShortcuts.cs` | `Rename` and `Forget` pass renames and deletions on |
| `SessionTreeview.cs` | The menu item, the tooltip line, and where a session is kept in the Edit title |
| `dlgEditSession.cs` | The Local overrides button and the overridden fields' tooltips |

**Checked:**
- In the real window: Edit on Media Server read "Edit session: Media Server — Saved sessions (Sessions.xml)", on Lab Switch "— Private sessions (only yours)". Local overrides on Media Server showed the stored values; ticking Username and typing a name saved `{"Media Server": {"Username": ...}}` and the tooltip read "Your local overrides: Username". The test override was removed afterwards.
- Edit on the Database session Live Test/192.168.3.2, which the user had overridden: the title read "— Database sessions" and the button "Local overrides: Username..." in orange; Cancel left the override as it was.
- `SessionVariables.Resolve` on a session with overrides for host, username and port gave the overridden values, and the stored session kept its own; a rename kept them under the new ID and a delete dropped them.
- The user connected with an override and it was used.
- A note: ticking Notes on Media Server and typing a note saved it, and Clear all removed it (the overrides file matched its copy from before). The tree's tooltip for a session with a stored note and an own note read "Your local overrides: Notes", the address, then the own note; the stored note was unchanged.
- Two sessions selected with Shift+click: "Local overrides for 2 sessions..." with Username ticked and a name set it on both; reopened, Username was ticked with that name and the differing stored values showed "(varies)"; Clear all removed it from both. Another session's override in the same file was left alone (the file matched its copy from before). `Merge` on sessions whose overrides differ, with nothing touched, gave each its own back.
- **Not checked:** the light theme, and changing overrides from Edit Session's button.

Manual test: on a Database session with a locked host, Local overrides... → tick Host, enter another address, OK; connect: it goes to that address, and the session still shows its stored host in Edit. Clear all: it goes back.

## Session counts on folders

Folders in the Sessions tree show how many sessions they hold, with their subfolders: "Core (12)". Saved sessions and Private sessions show their totals; Database sessions keeps its own status ("Database sessions (4)"). While searching, the counts are of the sessions the search shows.

- **Counted** when the folder index is built (`SessionTreeview.FolderIndex.Counts`: each shown session counts for its folder and every folder above it), so folders not opened yet have theirs too. They're shown as a folder is filled, and again shortly after sessions are added, removed or moved (`MarkIndexDirty`, which the places that marked the index stale now call).
- **A folder's name is its node's `Name`**, since its `Text` now has the count. Everything that used a folder's text as its name (paths, renames, sorting, drag and drop, lookups by key, messages) goes through `SessionTreeview.PlainName`. Code that needs a folder's name must use it, never `Text`.

| File | Change |
|---|---|
| `SessionTreeview.cs` | `FolderIndex.Counts`, `CountedName`, `ShowRootCount`, `MarkIndexDirty`, `RefreshFolderCounts`, `PlainName` |

**Checked** in the real window: Saved sessions (7), Test (5), Private sessions (2), Live Test (2) and Region 4 (2) under Database sessions (4), all matching what's in them. Opening Test (5) filled it with its five sessions (its path comes from the folder's name), and Edit on one of them had the right title.
**Not checked:** renaming, moving and dragging folders with the new names, and counts while searching.
Inside a folder the sessions were in reverse order; fixed separately (see "Fix: sessions inside a folder were in reverse order").

## Fix: sessions inside a folder were in reverse order

**Symptom:** opening a folder in the Sessions tree listed its sessions Z to A (RDP Test..., PowerShell Test, cmd Test), while the top level was A to Z.

**Cause:** the tree sorts each node into place as it's added, folders before sessions, and tells a session from a folder by the node's Tag. `AddSessionNode` set the Tag only after adding the node, so while it was being placed each new session looked like a folder and went before the sessions already there: every one went first, reversing them. The top level looked right because the whole tree is sorted again when the sorter is first set. New Favorites entries had the same problem, as an entry's kind also depended on whether it was in the tree yet.

**Fix:** sessions and Favorites/Recent entries get their Tag before they're added, and the sorter takes "folder" to mean a node without a Tag (`SessionTreeview.Compare`), which doesn't depend on where the node is.

**Checked** in the real window: Test opened as cmd Test, PowerShell Test, RDP Test, RDP Test - 127.0.0.1, RDP Test -172.27.133.195; Recent stayed most recent first, in the order SessionShortcuts.json has.

## Environment Variables dialog: one-click editing, and removing rows

The dialog (Tools → Environment Variables) needed two or three clicks to edit a value, rows couldn't be removed, and rows made by accident got stuck: the grid's empty last line made a row as soon as it was touched, and an empty key then couldn't be left ("Key cannot be empty").

- **One click** edits a key or value, or ticks Secret (`EditMode = EditOnEnter`).
- **Remove** (button, or right-click a row) removes the selected rows, even one whose key is empty or not valid: the key isn't checked on the way to Remove or Cancel (`EnvironmentVariablesForm.BeingClicked`), and an edit in progress is dropped first.
- **Add** makes a row with an unused key (`new_key`, `new_key2`, ...) and an empty value, ready to type over; the grid's own empty new-row line is gone.
- Add and Remove sit on the left, OK and Cancel on the right; Esc cancels; the Value column takes the spare width.
- OK no longer shows "Variables saved successfully!", and its messages follow the theme (`Messenger.MessageBox`).

| File | Change |
|---|---|
| `EnvironmentVariablesForm.cs` | `btnAdd_Click`, `btnRemove_Click`, `RemoveSelectedRows`, the right-click menu, `BeingClicked` in the key check |
| `EnvironmentVariablesForm.Designer.cs` | Edit on enter, no new-row line, the Remove button and the button layout |

**Checked** by opening the dialog on its own and clicking: one click on a value started editing it; one click on Secret ticked it and another unticked it; Add made `new_key` with its text selected; emptying that key and clicking Remove removed the row with no message; Cancel closed it. The user's EnvironmentVariables.json was the same before and after.
**Not checked:** the light theme, and removing several rows at once.

## Sharing the Database sessions settings as a file

**Export...** and **Import...** in the Database sessions dialog save and load the connection as a JSON file, so a team can pass one setup around instead of typing it in. **Clear** starts the fields over.

- **In the file:** host, port, database, schema, table, username, SSL mode, read only and the check interval, with a `format` marker (`superxputty-database-settings`) so another JSON file is refused (`Data/DatabaseSettingsFile.cs`).
- **Passwords:** only a `{key}` placeholder is written; each person sets its value in Tools → Environment Variables. A typed password (saved encrypted for its Windows user) is left out, and the export says so. The nickname, being each person's own, is left out too.
- **Export** takes the fields as they are in the dialog, not only what's saved.
- **Clear** empties the host, username and password and puts the database, schema, table (`superputty`, `superputty`, `sessions`), port 5432, SSL Prefer, check every 30 seconds and read only off back as for a new setup (`ClearFields`). The nickname and the "Show sessions" tick stay. Nothing is saved until OK, which then removes the saved password too (an empty box otherwise keeps it); Cancel undoes it.
- **Import** fills the fields and ticks "Show sessions from a PostgreSQL table"; nothing is saved until OK. The nickname stays, the password changes only if the file has a placeholder, and the result line says what's left to do (the password or the key, a nickname, Test connection, OK). The file is checked as the dialog checks its fields: a host, a port from 1 to 65535, and plain names for the database, schema and table; an unknown SSL mode becomes Prefer.

| File | Change |
|---|---|
| `Data/DatabaseSettingsFile.cs` | New. The file: `ToJson`, `FromJson` (with its checks), `IsPlaceholder` |
| `Gui/dlgDatabaseSettings.cs` | Export..., Import... and Clear (`ExportSettings`, `ImportSettings`, `ClearFields`) |
| `docs/DatabaseSessions.md` | Sharing the settings |

**Checked:**
- `DatabaseSettingsFile` (9 checks, all passed): a round trip kept every field; an encrypted and a typed password, and the nickname, weren't written; a placeholder was; a file of another kind, text that isn't JSON, a table name with `; drop`, and a missing host were refused with a message; an unknown SSL mode became Prefer and a plain password in a file was ignored.
- The real dialog, opened on its own: Export wrote the current settings without the (encrypted) password; Import of a co-worker's file filled host, port, database, table, user, `{team_pg}` as the password, SSL and read only, kept the nickname and said what to do next; Cancel left SuperXPuTTY.settings as it was. Clear emptied host, username and password, put the rest back to the defaults above, kept the nickname, and the connection OK would save had an empty password; Cancel again left the settings as they were.
- **Not checked:** the light theme, and an import followed by OK and a real connection.

## Fix: the environment colour's frame around the terminal was missing

**Symptom:** a red (production) session's tab was red, but the terminal had no red frame around it, on the dark theme. It looked as if it had stopped working with the splitter changes; the build before them does the same.

**Cause:** the dark theme (`DarkModeCS`) recolours every control added to the window, from its `ControlAdded` handler. A tab opened from the Sessions tree is added to the window after `ApplyEnvironmentColor` has set its background to the colour, so the theme painted the frame its own grey (#373737). Tabs opened at start-up (`-session`, a layout) were added before the theme hooked in and kept their frame, which is how it was checked before.

**Fix:** `ctlPuttyPanel.OnBackColorChanged` puts the environment colour back when something else sets the background while the session has one; the colour set over it is kept as the tab's own, for when the session's colour is removed.

**Checked** in the real window: cmd Test (red) opened by double-clicking it in the tree had a 4-pixel #C62828 frame round the terminal; before the fix, the same frame was #373737, in this build and in the one before the splitter changes.

## SPSL's PROMPT and PWDPROMPT follow the dark theme

The box `PWDPROMPT` asks for a password with was always light, and `PROMPT` used Visual Basic's `InputBox`, which can't be themed.

- `frmPrivatePrompt` applies `DarkModeCS` like the other dialogs, and takes whether the typing is hidden.
- `PROMPT` uses it too, with the typing shown. Cancel still sends an empty line, as `InputBox` did.

| File | Change |
|---|---|
| `Scripting/frmPrivatePrompt.cs` | The theme, and hidden or shown typing |
| `Scripting/PromptCommand.cs` | Uses `frmPrivatePrompt` instead of `InputBox` |
| `Scripting/PwdpromptCommand.cs`, `ctlPuttyPanel.cs`, `Scripting/SpslReference.cs` | Cancel stops the script |

**Checked:** both prompts opened on their own on the dark theme: dark background, buttons and box, the password masked and PROMPT's text shown.
**Not checked:** the light theme (no theme is applied there, as before), and running PROMPT from a script.
**Cancel on `PWDPROMPT` stops the script.** It used to type the word "Cancelled" and press Enter, so a device took it as the password. The prompt now throws `OperationCanceledException`, which `ctlPuttyPanel.RunScriptLines` reports in the status bar ("Script ... stopped at line 3: you cancelled the password prompt") and logs as information, not an error; nothing more is typed. The SPSL reference says so. Checked by running the command with its box answered automatically: Cancel stopped it with that message, OK gave the typed text.

## plink or KiTTY's klink, and choosing it in Options

The built-in terminal and jump hosts run plink, which they only looked for as plink.exe beside the PuTTY set in Options. With KiTTY there is no plink.exe; KiTTY ships klink.exe, its plink.

- **klink works the same way.** It takes the options SuperPuTTY passes (`-no-antispoof`, `-ssh`, `-P`, `-load`, `-l`, `-batch`, `-proxycmd`, `-nc`) and reads KiTTY's saved sessions, in the registry or, for a portable KiTTY (`savemode=dir` in kitty.ini), the `Sessions` folder beside it. `-load` with such a profile connected to the profile's host.
- **Found on its own.** `JumpHosts.PlinkPath` looks beside PuTTY for klink.exe first when PuTTY is KiTTY (`SuperPuTTY.IsKiTTY`), plink.exe first otherwise.
- **Or chosen.** Tools > Options > Paths has a **plink / klink** row under PuTTY (setting `PlinkExe`, empty by default). When it's set and the file exists it's used; when it's missing the log says so and the search beside PuTTY is used. Saving Options with a path that doesn't exist shows an error. Browse starts in PuTTY's folder. The dialog is one row taller for it.
- The "not found" messages in the terminal tab, status bar and log now name klink and the Options row.

| File | Change |
|---|---|
| `Utils/JumpHosts.cs` | `PlinkPath`: the chosen path, then klink or plink beside PuTTY |
| `Gui/TerminalControl.cs` | Messages |
| `dlgFindPutty.cs` / `.Designer.cs` | The row (label, box with a hint, Browse), its load and save; rows below moved down |
| `Properties/Settings.*`, `App.config` | `PlinkExe` |

**Checked:** built; the Paths page drawn on the dark theme with the new row and hint; `PlinkPath` with KiTTY and with PuTTY, each with nothing chosen (klink beside KiTTY, plink beside PuTTY), a chosen klink (used), and a chosen path that doesn't exist (back to the one beside PuTTY). klink run by hand with SuperPuTTY's command line and a portable KiTTY profile.
**Not checked:** a whole session in the built-in terminal through klink against a real device, a jump host through klink, the light theme, and saving the Options dialog.

**Fix: a portable KiTTY's profile wasn't applied in the built-in terminal.** A session whose KiTTY profile sets an auto-login username still asked "login as:". A portable KiTTY's klink reads `kitty.ini` and the `Sessions` folder from the *current directory*, not from beside itself, and the terminal started it in SuperPuTTY's own folder, so `-load "root"` found no profile and klink used its defaults. `TerminalControl.Connect` now starts plink or klink in its own folder (cmd, PowerShell and WSL still start in the user's folder). Checked by running klink through `ConPty` with a test profile: started elsewhere it found no profile and printed its usage; in its folder it loaded the profile and connected to the profile's host. Not checked against the user's server, or for a jump host through an embedded KiTTY window (the proxy klink then runs in KiTTY's current directory).

## Fix: SENDKEY's Ctrl, Alt, Shift, symbols and repeat counts

Found while comparing SPSL with another tabbed PuTTY tool's script keys (Ctrl+A-Z, Ctrl+[, Ctrl+]). The bytes each key sends were recorded with a small Telnet listener on 127.0.0.1, from a test PuTTY window and from plink in the pseudo console.

**Symptoms:**
- **PuTTY tabs lost Ctrl, Alt and Shift.** `SENDKEY ^C` typed `c`, `^Z` typed `z`, `%x` typed `x` and `+TAB` a plain Tab; `^]` and `^[` sent nothing.
- **Symbols pressed other keys.** `SENDKEY .` pressed Delete, `[` the Windows key, `]` the Menu key, `\` the right Windows key, everywhere (PuTTY, the built-in terminal, RDP).
- **`{KEY n}` pressed the key once.** `{TAB 3}` sent one Tab, though the reference said 3.
- **The built-in terminal sent nothing** for a single character (`SENDKEY y`), Alt with a key, or Ctrl with a symbol. After `^Z`, nothing more reached the device.

**Causes:**
- `CommandData.SendToTerminal` posts a Ctrl or Shift key press before the key, but a posted press doesn't change the keyboard state PuTTY reads, so PuTTY saw the key alone.
- `Commands.ParseKeys` took a character's code as its key code. That's right for letters and digits only: "." is 0x2E, which is VK_DELETE.
- `ParseKeys` read the repeat count and dropped it; `CommandData` had nowhere to keep it.
- `TerminalControl.KeySequence` only knew named keys and Ctrl+letter. `TerminalControl.Type` only switched plink's console to virtual terminal input for ESC and Ctrl+C. Without it, the console took Ctrl+Z (0x1A) as the end of plink's input.

**Fix:**
- **`Utils/TerminalKeys.cs`** (new): `Sequence` gives what a terminal sends for a key, as xterm and PuTTY's defaults do:
  - Ctrl with a letter or `[ \ ] @ ^ _` gives its control character (Ctrl+] is 0x1D, Telnet's escape);
  - a character key gives its character, on the current keyboard layout;
  - Alt puts ESC in front;
  - Shift, Alt or Ctrl with an arrow, Home, End, an editing or a function key gives xterm's modified sequence (Ctrl+Up is `ESC [1;5A`);
  - F13-F16 are Shift+F1-F4.
  - `FromCharacter` finds the key (and Shift) that types a character.
- **SENDKEY:** a single character other than a letter or digit uses its real key (`Commands.CharacterKey`); a character no key types is an error. `{] 2}` (a character in braces) works, and the repeat count is kept in `CommandData.KeyRepeat`.
- **`CommandData.KeyRepeat`** is honoured by PuTTY windows, the built-in terminal and RDP tabs.
- **PuTTY windows:** a key with Ctrl, Alt or Shift is typed as `TerminalKeys.Sequence`'s characters (WM_CHAR). Plain keys are still posted as key presses, so PuTTY's own keyboard settings (Backspace, function keys) apply to them.
- **Built-in terminal:** `KeySequence` uses `TerminalKeys.Sequence`. Every control character but Enter, Tab and Backspace now switches on virtual terminal input first (`TerminalControl.NeedsVirtualTerminalInput`).

| File | Change |
|---|---|
| `Utils/TerminalKeys.cs` (new) | Key to characters, and character to key |
| `Scripting/SendKeyCommand.cs` | Symbols by their real key, repeat counts, characters in braces |
| `Utils/CommandData.cs` | `KeyRepeat`; keys with modifiers typed as characters in PuTTY windows |
| `Gui/TerminalControl.cs` | `KeySequence` via `TerminalKeys`, repeats, virtual terminal input for every control character |
| `Gui/RdpControl.cs` | Repeats |

**Checked** with the Telnet listener; every key gave the bytes below in both a PuTTY window and plink in the pseudo console (the test run had no console of its own, as SuperPuTTY doesn't, so attaching to plink's console worked):
- `^C` 03, `^]` 1D, `^[` 1B, `^\` 1C, `^@` 00, `^_` 1F, `^Z` 1A (and later text still arrived);
- `.` 2E, `!` 21, `x` 78, `%x` 1B 78;
- `UP` `ESC [A`, `+TAB` `ESC [Z`, `^UP` `ESC [1;5A`;
- `{TAB 3}` three 09s, `{] 2}` 5D 5D, `{DOWN 2}` twice `ESC [B`.

**Not checked:** RDP tabs, and a real device. Ctrl+] at a Telnet device only matters to Telnet clients that treat it as their escape; plink and PuTTY pass it on.

## SPSL reference, check and hints brought up to date

The Commands list (Script Editor and Edit Button) now covers everything SPSL can do (`Scripting/SpslReference.cs`):
- **SENDKEY:** modifiers in any combination, single characters, repeats and the control characters.
- **Keys for SENDKEY** (new entry): every key name with its aliases (built from SENDKEY's own table, so it can't drift), modifiers, control characters, repeats, the brace keys `{^}` `{%}` `{+}` `{{}` `{}}`, and the keys with no characters.
- **WAITFOR:** matched exactly, including case, anywhere in a line. It starts from the cursor's line, or from the start of the session for a session's own script. It works in PuTTY tabs and the built-in terminal; it used to say PuTTY only.
- **SLEEP, PROMPT, OPENSESSION:** Stop ends a SLEEP at once; PROMPT's Cancel types an empty line; OPENSESSION's new tab doesn't get the rest of the script.
- **{prompt:Name}** (new entry): `{prompt:Name=default}` and `{secret:Name}`, asked once before the script starts.
- **How scripts run** (new entry): one command per line, in any case, indented or not; where scripts run from and into; why they stop; what Check script finds.
- **{placeholders}:** they're replaced in button bar scripts and the Script Editor; a session's own script file is typed as it is.

Also:
- **The Script Editor asks for `{prompt:...}` and `{secret:...}`** before running an SPSL script, as a button does (`frmSuperPutty.Editor_ScriptReady`); Cancel runs nothing. Before, they were typed literally.
- **Check script** (`Scripting/SpslChecker.cs`):
  - accepts any character a key types (`]`, `?`) and characters in braces (`{] 2}`);
  - reports a modifier with no key (`SENDKEY ^`) and a bare brace (pointing to `{{}`);
  - reports a character no key on the keyboard types;
  - points unknown key names to "Keys for SENDKEY".
- **Edit Button's hint** under the script fits its two lines again (its end was cut off) and points to the reference.

| File | Change |
|---|---|
| `Scripting/SpslReference.cs` | The entries; `CanTypeKey`; the key list |
| `Scripting/SendKeyCommand.cs` | `SendKeyCodes`, for the key list |
| `Scripting/SpslChecker.cs` | The SENDKEY checks |
| `Gui/dlgEditCommandButton.cs` | The shorter hint |
| `frmSuperPutty.cs` | `{prompt:...}` in Script Editor scripts |

**Checked:**
- The Edit Button dialog on the dark theme: the new entries are listed, and the hint fits.
- Check script on `^`, `{`, `ENTRR` (errors) and `^]`, `{] 2}`, `^%DEL`, `{TAB 3}`, `{prompt:...}` (no problems). The checker and SENDKEY's parser agree on the brace keys, symbols, Alt and repeats.

**Not checked:** the light theme, and the Script Editor's prompt box (the code path is a button's).

## Send Key and Special Command in the tab menu

Two submenus in the tab's right-click menu, after Watch for Silence. Send Key is also in the built-in terminal's own menu.

- **Send Key:**
  - **Keys:** Ctrl+C (interrupt), Ctrl+Z (end, leave config), Ctrl+Shift+6 (Cisco escape), Ctrl+] (Telnet escape), Ctrl+D (end of input), Ctrl+\ (quit) and Esc. The list is `TerminalKeys.MenuKeys`; the built-in terminal's page gets it in its `config` message, so both menus show the same keys.
  - **Other Key...** asks for any key written as in SENDKEY (`%F`, `{DOWN 3}`, `+F5`) and remembers the last one.
  - **How keys are sent:** through `ctlPuttyPanel.SendKey`, which parses `SENDKEY <key>` and sends it like a script would. So it works in PuTTY, built-in terminal and RDP tabs, and the tab gets the focus back. Enabled when the tab takes typing (`CanReceiveCommands`).
- **Special Command** (tabs PuTTY could run: SSH, Telnet, Rlogin, Raw, Serial):
  - **Contents:** PuTTY's own Special Command menu, read from PuTTY's window menu when the tab menu opens (`PuttyWindow.SpecialCommands`). That gives a serial Break, Telnet's Break, Are You There, Interrupt Process and so on, and SSH's Break and signals, with PuTTY's submenus and separators.
  - **How it's found:** by its commands' IDs (PuTTY's IDM_SPECIAL range, 0x0400-0x07F0), not its title, so KiTTY and translated builds work.
  - **Sending:** choosing an item sends PuTTY that command (`SendPuttyCommand`, as the PuTTY items above do).
  - **Not connected yet:** PuTTY only fills the menu once connected; until then the submenu says so.
  - **Built-in terminal tabs:** plink can't send these, so the submenu says so and offers **Open in PuTTY** (for a serial session, "replaces this tab": see below).
- **frmPrivatePrompt.SetValue** fills the prompt's box in, for Other Key....

| File | Change |
|---|---|
| `Utils/PuttyWindow.cs` | `SpecialCommands` reads PuTTY's Special Command menu; `SendSpecialCommand` |
| `Utils/TerminalKeys.cs` | `MenuKeys` |
| `ctlPuttyPanel.cs` | The two submenus; `SendKey`, `AskAndSendKey` |
| `Gui/TerminalControl.cs`, `web/terminal/terminal.js` | Send Key in the built-in terminal's menu (`sendKey` message) |
| `Scripting/frmPrivatePrompt.cs` | `SetValue` |

**Checked:**
- A test PuTTY connected by Telnet to a local listener:
  - its Special Command menu was read from outside (Are You There 0x0400 ... End Of File 0x04E0, with both separators);
  - Break and Are You There reached the listener as `FF F3` and `FF F6`.
- Embedding only removes PuTTY's border, so its window menu stays readable.
- The built-in terminal's page in a browser: Send Key lists the seven keys with their hints and Other Key...; choosing Ctrl+] posts `sendKey` with `^]`.
- The keys themselves are those checked for SENDKEY above.

**Not checked:** the WinForms tab menu itself in a running SuperXPuTTY (opening it, the submenus' look on both themes), Other Key..., RDP tabs, and Special Command on SSH or serial.

## Open in PuTTY replaces a serial session's tab; Break and SENDBREAK shown as not available

**Symptom:** in a serial session in the built-in terminal, Open in PuTTY (the terminal's menu, or the tab's Special Command) opened a PuTTY tab that failed with "Unable to open connection to COM6 ... Error 5: Access is denied".

**Cause:** a serial port can only be open once, and the built-in terminal's plink still had it.

**Fix:**
- For a **serial** session in the built-in terminal, Open in PuTTY closes the tab, which ends plink, and opens the PuTTY tab in its place: the same position in the same tab group (`ctlPuttyPanel.OpenInPutty`, `DockPane.SetContentIndex`). The menu items say "replaces this tab". Other protocols open the PuTTY tab beside the old one, as before.
- `ConPty.Dispose` now waits (up to 2 seconds) for the program it terminates to exit. `TerminateProcess` returns before the process is gone, and the port is only free then.
- `TerminalControl.OpenInPutty` returns the new tab.

**Break and SENDBREAK, shown as not available.** A Break is a signal on the line, not a key, so neither a script nor plink can send one:
- **Send Key:** both menus list **Break**, greyed out. The tab menu's hint says to use Special Command; the built-in terminal's says it needs PuTTY.
- **SENDBREAK:** the SPSL Commands list has it as "SENDBREAK (not available)", like CLOSESESSION, pointing to Special Command > Break. Check script reports it as an error; run anyway, the line does nothing, as for any unknown command.

| File | Change |
|---|---|
| `ctlPuttyPanel.cs` | `OpenInPutty`; greyed Break in Send Key; the Special Command item's label |
| `Gui/TerminalControl.cs` | Open in PuTTY through the tab; `serial` in the page's config; `OpenInPutty` returns the tab |
| `Utils/ConPty.cs` | Wait for the terminated program to exit |
| `web/terminal/terminal.js` | Greyed Break; "replaces this tab" for serial sessions |
| `Scripting/SpslReference.cs`, `Scripting/SpslChecker.cs` | SENDBREAK (not available) |

**Checked:**
- Disposing the pseudo console with a running program (`ping -t`), five times: the process had exited each time Dispose returned (1-5 ms).
- Check script on SENDBREAK gives the error.
- The terminal page set up as a serial session shows "Open in PuTTY (Special Commands, replaces this tab)" and a disabled Break; clicking Break posts nothing.

**Not checked:** the replacement with the real COM port in a running SuperXPuTTY: that the PuTTY tab opens the port and lands where the old tab was.

## Fix: an RDP tab closed without a word when the server refused the login

**Symptom:** connecting the built-in RDP client with an account that isn't allowed to log in remotely, the tab simply closed. mstsc shows "The connection was denied because the user account is not authorized for remote login".

**Cause:** the server ends such a connection with disconnect reason 3, "disconnected by the server". `RdpControl.OnDisconnected` took reasons 1-3 as a normal end (like logging off) and closed the tab quietly, without looking at the extended reason.

**Fix:** `RdpControl.ShouldReport` decides. Windows' description of the disconnect is shown in a themed message box, and the tab closes once it's acknowledged, when:
- the reason is an error (anything but 1-3), as before;
- the session never reached the desktop (no login completed), except a disconnect from this side;
- the extended reason is the server's: idle or logon timeout, replaced by another connection, out of memory, denied, insufficient privileges, fresh credentials required (3-10), or a licensing or protocol error (256 and up).

Logging off, disconnecting from the remote's Start menu, an administrator's disconnect and closing the tab stay quiet. The log now records the extended reason too. Closing the tab after the server ended the session no longer logs a warning: the control was already released (`InvalidComObjectException`), so there's nothing to disconnect.

| File | Change |
|---|---|
| `Gui/RdpControl.cs` | `ShouldReport`; the extended reason read once and logged; the quiet close |

**Checked:** `ShouldReport` gives "report" for:
- a refusal before the desktop (reason 3, extended 0 or 9);
- an idle timeout;
- a session replaced by another connection;
- host not found;
- a licensing error.

It gives "quiet" for:
- logging off;
- Start > Disconnect;
- an administrator's disconnect;
- a disconnect from this side.

**Not checked:** the refusal against the real server, and so which text Windows gives for it (with extended reason 0, it may be a general "disconnected by the server" rather than mstsc's wording).

## RDP tabs stay dark while connecting and after the session ends

The Remote Desktop ActiveX control paints itself white until the connection is up and after it ends, and has no setting for that colour; on the dark theme the tab glared white behind "Connecting" and behind the disconnect message.

- `RdpControl` covers itself with a panel in the theme's colour (dark grey, or the light theme's control colour), saying **"Connecting to <session>..."** while it connects and **"Disconnected from <session>"** once it ends, before any message box.
- **Shown again:** when the connection is up (the control's `OnConnected` event, newly declared in `IMsTscAxEvents`), before Windows' own login screen, so a login prompt in the session stays visible; and on login, to be sure.
- **Why a cover:** it's a sibling in front of the control rather than the control hidden, so the control stays active while it connects.

| File | Change |
|---|---|
| `Gui/RdpControl.cs` | `ShowCover`, `HideCover`, `OnConnected` |

**Checked** by hosting the control in a test window and connecting to 127.0.0.1:1 (nothing listening): dark grey with "Connecting to Cover test..." while connecting; dark grey with "Disconnected from Cover test" behind the error box.

**Not checked:** a real connection, which should reveal Windows' login screen or the desktop when connected; the light theme.

## Open questions

- **Naming.** "Environment Variables" could be confused with Windows environment variables, which SuperPuTTY already expands as `%VAR%`. "Session Variables" or "User Placeholders" might be clearer.
- **Storage format.** JSON via Newtonsoft adds a dependency. The values could live in `SuperXPuTTY.settings` like other per-user settings.
- **Secrets.** Values are stored in plain text. If people put passwords in them, a DPAPI-protected store may be warranted.
- **Dialog details.** Clicking **Add** twice creates two `NewKey` rows (the grid's own new row already covers adding). **OK** closes even if saving fails, and shows a "saved" popup each time.
- **Nesting and defaults.** Not supported: no `{key:default}`, and values aren't expanded again.

## Building and testing

Requirements: Visual Studio 2022 (or Build Tools) with .NET Framework 4.8 and the WiX v3 toolset for the installer.

```bash
MSBuild.exe SuperPutty.sln /restore /p:Configuration=Debug
```

```bash
MSBuild.exe SuperPutty.sln /restore /p:Configuration=Release
```

Output: `bin\Debug\SuperXPuTTY.exe` (the installer goes to `SuperPuttyInstaller\bin\x86\Debug\SuperXPuTTYSetup.msi`). Use `/p:Configuration=Release` for `bin\Release\`.

Build the `.sln`, so the installer is built too.

Manual test:

1. Run `bin\Debug\SuperXPuTTY.exe` and open **Tools → Environment Variables...**. Add `user = jdoe` and `domain = example.com`, then click OK.
2. Create a session whose Login username is `{user}@{domain}`. The preview under it should read *Connects as jdoe@example.com to ...*. Change it to `{user}@{zone}`: the field turns red and the preview says `{zone}` is undefined. Click the preview, add `zone`, and check that it updates.
3. Double-click the session. PuTTY should open with `-l jdoe@example.com`; the SuperPuTTY log records the PuTTY arguments.
4. Remove `zone` again and launch: SuperPuTTY should stop and offer to open the Environment Variables dialog instead of connecting.
5. Try the quick-connect toolbar, **Connect All** on a folder, Quick Selector and Restart.
6. Close SuperPuTTY and open `Sessions.xml` (its location is shown in **Tools → Options**). The session should still say `{user}@{domain}`, not `jdoe@example.com`.

Secrets, icon-linked bars, logs and WAITFOR:

1. In Tools → Environment Variables, tick **Secret** on a variable: its value shows as dots. Close and reopen the dialog, open `EnvironmentVariables.json` and check that it has a `ProtectedValue` and no plain value. Check that a session using it still logs in.
2. Right-click the button bar → **Show for Tab Icons...**, tick `windows` for a "Windows" bar, and switch between a Windows tab and an SSH tab: the bar should follow. Pick another bar by hand on the SSH tab, switch to the Windows tab and back: the hand-picked bar should return.
3. Turn on **Session logs** (Options → Advanced), open an SSH session, and check that a log appears in `Documents\SuperXPuTTY\logs`. Turn it off: the next session's log goes to `%TEMP%\SuperPuTTY\logs` and disappears a few seconds after its tab closes.
4. Make a button with an SPSL script such as `WAITFOR $` then `SENDLINE uname -a`, on a logged-in Linux tab sitting at its prompt: it should run. Change it to `WAITFOR /t:3 nothing-like-this` then `SENDLINE echo hi`: after 3 s the status bar should say the script stopped, and nothing should be typed.
5. Login script as a button: define `user` and a secret `vault_pw`, and make a button with `WAITFOR login as:` / `SENDLINE {user}` / `WAITFOR password:` / `SENDLINE {vault_pw}`. Open a session with no login username, so PuTTY asks `login as:`, and click the button at that prompt: it should log in. Clicking it on an already logged-in tab should do nothing, and stop after 30 s.
6. The same script as the session's own SPSL script file (Edit Session → SPSL script) should log in by itself when the session opens.
7. Click the login button on an already logged-in tab: the status bar should show `▶ Login_Test (...): line 1/4 · WAITFOR login as: · N s`, and the button and the tab (with Show tab icons on) should show a spinner. Click the status-bar item → Stop: all three should go back to normal at once, with "stopped by you" in the status bar.

The script runner was checked against the compiled assembly: a script blocked on `WAITFOR` showed `line 2/3 · WAITFOR password:` and stopped 12 ms after Stop, without typing the next line; a `SLEEP 20000` stopped after 15 ms; and nothing was left running.

Classic theme:

1. Tools → Options → GUI → **Classic**, OK, restart: tabs should be angled, the selected one white, with a single X and the tab list at the right end of the strip. The X closes the active tab.
2. Check that tab badges (Watch for Activity) and the script spinner still show on tabs, the button bar and Sessions pane look right, and dialogs are light.
3. Switch back to Dark and restart: everything should be as before.

Tab list and Dark colours:

1. Open several tabs of the same session, select one in the middle, and click the drop-down arrow at the right end of the tab strip: that tab should be ticked and bold. Select another and reopen the list: the mark should move. Try it in Classic and Dark, and in a tab group split off to the side.
2. In the Dark theme, the selected tab should be a muted slate blue, and hovering other tabs should show grey, not bright blue.
3. In the Dark theme, right-click a tab: the tick next to "Accept commands" should be a clear light tick. So should the one in the tab list.

Session profiles and portable KiTTY:

1. Edit a session whose PuTTY session profile doesn't exist (e.g. `PuttySession="bogus"` in Sessions.xml). The profile list should show "bogus" in red, with a red "Not saved in PuTTY yet" under it, and the tooltip should say where SuperPuTTY looked. Save without changes: Sessions.xml should still say `bogus`.
2. Set Tools → Options → PuTTY path to `kitty_portable.exe`, then open Edit Session: the profile list should show the KiTTY sessions and the note should say "KiTTY".
3. Save a new session in PuTTY or KiTTY while SuperPuTTY is running, then open the quick-connect Session list: it should be there without restarting.

Icons:

1. Restart SuperPuTTY: the session tree, tabs, top toolbars and button bar should show the new, larger icons. Sessions using `green_switch`, `blue_router`, `ubuntu` and the like should show the drawn versions.
2. Tools → Options → GUI → Misc → Icon size: try 16, 20 and 32 (each needs a restart). Everything should be sharp at each size, and the tabs a little taller at 24 and 32.
   - **Tab icons:** each should be as tall as the tab text, centred, with a small gap before the text, in Dark, Light and Classic. Watch for Activity and the script spinner should still show on tabs, and the tab-list drop-down should show each tab's icon and name with no leading blank.
3. Edit a session → click its icon: search for "switch", pick Blue, OK, Save. The tree and tab should show a blue switch, and Sessions.xml should say `ImageKey="switch#1E63B4"`.
   - **Logo colours:** select Apple and pick white with More...: only Apple's tile (and the preview) should turn white, every other icon should keep its colours, and More... should be outlined. Select another icon: the white moves to it.
4. In the dark theme, an Apple or AlmaLinux logo should show light grey, not black.
5. Custom icons: in the picker, **Add icon...** a square PNG, 32x32 or bigger, and name it.
   - **A black-outline icon or black-on-white image:** it should appear under Custom, selected, with its dark parts as the shape. It should take colours and be saved in `<SettingsFolder>\icons`.
   - **A photo:** it should be refused with one reason.
   - **Remove:** select the custom icon → **Remove**; it should go after you confirm.
6. Button bar: right-click a button → Edit → **Icon** → **Choose icon...**, pick a coloured switch, OK. The button should show it, and the list should now show that icon above "Choose icon...". The colour circles should still work.

Show folders first:

1. Tools → Options → GUI: tick "Show folders first", OK. The Sessions tree should list folders before sessions at every level.
2. Open Options and click OK several times without changing anything: the tree order shouldn't change. Untick it: folders and sessions should mix, by name.

Script editing:

1. Open the Script Editor (Send Commands toolbar → script button). Run as should read **SPSL script**, with the commands and check on the right. Switch to **Send lines**: the right panel goes and the script box widens.
2. In SPSL mode, select a command → **Insert example**: it's added with no `#!/bin/spsl` line. Type `SENDLNE hi` and `SENDKEY ENTR`, then **Check script**: two errors, with "did you mean SENDLINE?". Click one: its line is selected.
3. With a tab selected, send `SENDLINE echo {user}` / `SENDKEY ENTER` as SPSL: the tab should get `echo <your user>` and one Enter (not an "E"), with the script shown in the status bar while it runs. Send `show version` in Send lines mode: it's typed as is.
4. **Save as...** on the left saves the script as the current kind (it's listed as SPSL or lines). Clear the editor, double-click it: it comes back, and Run as switches to match.
5. Edit a button-bar button → Action: Run SPSL script: the panel appears beside the script by itself, with a Saved scripts tab listing your SPSL scripts, and the script box doesn't get a `#!/bin/spsl` line. Click OK with an error in the script: you're asked whether to save anyway.
6. Click the mask button on the Send Commands toolbar: the icon switches between a grey eye and an amber asterisk, and typing is masked accordingly.
7. Tick **Always on top** in the Script Editor, click a session tab: the editor stays in front. Close and reopen it, and restart SuperPuTTY: it's still ticked. With it ticked, send `SENDLNE hi` as SPSL: the "Send it anyway?" warning shows in front of the editor. So do Save as... and Open file....

Favorites, Recent and private sessions:

1. Right-click a session → **Add to Favorites**: a Favorites group appears at the top with it. Drag another session onto Favorites: it's added too. Delete on a favourite removes only the shortcut.
2. Open two sessions: they appear under Recent, the latest first. Load a layout: its sessions aren't added. Right-click Recent → **Clear Recent**. Untick Options → GUI → **Show recent**: the group goes.
3. Rename a favourite (Edit session): it stays in Favorites under its new name.
3a. Start SuperXPuTTY: Favorites and Recent are closed, with their counts and a ▸ arrow; Saved, Private and Database sessions show an open arrow. Double-click Favorites open, restart: it's still open. Close it, restart: closed. Search for something: both open with the matches; clear the search: they go back as you left them.
4. Right-click **Private sessions** → New session...: it's saved to `PrivateSessions.xml` in `Documents\SuperXPuTTY` (or beside the exe if portable), not `Sessions.xml`. Right-click it → **Move to Saved sessions**, then back. Drag a folder from Saved sessions to Private sessions.
5. Try to create a shared session with the same folder and name as a private one: it's refused, with the reason.
6. Change Options → Icon size to 32 and back to 16: the search box, its buttons and the toolbar follow (after a restart, as for the other icons). Check the dark theme.
7. Toolbar: select a session, click **New session**: it's created in that session's folder. **New folder**, **Expand all**, **Collapse all**. **Sort** twice: Z to A, then A to Z; restart: A to Z.
8. Create new session: type the host first, then the name. **Save** is enabled without clicking elsewhere. Type an existing name: the error shows at once.
9. Hints: the empty search box says "Search... (press Enter)", or "Type to search..." with "Filter while typing" on. Create new session shows grey examples in Session name, Host address and Login username; each goes when you type. The toolbar's Host and Login boxes show theirs too.
10. Open a session: its icon in the Sessions tree gets a green dot (in Favorites and Recent too). Let it drop (e.g. 192.168.3.2 left at the login prompt): the dot turns red, and green again once it reconnects. Close the tab: the dot goes.
11. A session whose PuTTY profile (or `-i`) uses a key: right-click → WinSCP (external) logs in with the key. Clear the session's username, with the user only in the PuTTY profile's Auto-login username: WinSCP still logs in as that user. A session with `{user}` as its username opens WinSCP as your user, not "{user}".
12. With two copies of SuperXPuTTY on one settings folder (or a text editor), change a session in one: the other reloads within a few seconds ("Sessions.xml was changed outside SuperXPuTTY") and shows the change. Save in the first again: it doesn't reload itself. Untick Options → GUI → Reload when changed: changes are no longer picked up until File → Reload sessions.
13. Open View → Log Viewer, then point the settings folder at a large Sessions.xml: the window stays responsive. Close SuperXPuTTY: no error.
14. With an SSH tab open, switch to another program, then click straight into the toolbar's Host box and type: the text goes into the box, not the terminal. Do the same with the Sessions search box. Switch away and come back with Alt+Tab: the terminal has the focus.
15. File → Export sessions to CSV..., open it in Excel: one row per session, Extra args like `-L ...` shown as text, `-pw` passwords as `********`. Change a host and add a row with Folder, Name and Host, save as CSV, then File → Import sessions → From CSV file...: the summary says 1 new and the rest updated; Yes, and the host is changed and the new session is in its folder. A session with `-pw` still connects with its password.
16. Make a folder named Prod with a session in it and open it: its tab is red (tinted with a red stripe when another tab is selected) and the terminal has a red frame, and Prod's icon in the tree sits on a red circle, its name in the normal text colour. Right-click another folder → Colour → Green: its sessions' tabs turn green. Right-click a session in it → Colour → Blue: that one turns blue. Rename and drag the green folder: it stays green. Tools → Environment colours..., add `Lab*` purple: folders named Lab... turn purple. Do the same under Database sessions (after Create / upgrade schema): a second copy of SuperXPuTTY shows the folder's colour within 30 seconds.
17. Right-click a folder → Edit sessions...: untick one session, tick Username and type `{user}`, tick Port and set 2222, Change: every ticked session has the new username and port, the unticked one doesn't. Under Database sessions, lock `username` on one row (`locked_fields = '{username}'`) and repeat: it's listed as left alone.
18. Add a button with `interface {prompt:Interface}\rshow this\r`, open a session on a switch and click it: it asks for Interface, then types both lines. Click it again: the last interface is offered. Cancel: nothing is typed. Type `show interface {prompt:Port}` in Send Commands and press Enter: it asks, sends, and the history keeps `{prompt:Port}`.
19. Right-click a folder → Folder settings...: Username `netops`, Port 2222. A session in a subfolder with no username and port 0 connects as netops on 2222; Edit Session shows "netops (from ...)" in the empty Username box. A session with its own username keeps it.
20. With Pageant holding a key the bastion accepts, make a session `Bastions/bastion1` for it, then set a device's Jump host to `Bastions/bastion1` (or a folder's, in Folder settings...): the device connects through it. Set the jump host to `nosuch`: it connects directly and the status bar says why.
21. Right-click a Saved folder → Copy to → Database sessions: its sessions appear under Database sessions in the same folders (check the table), and the Saved ones stay. Drag a Database session onto a Saved folder: it's copied there. Right-click a Database folder → Move to → Saved sessions: it asks, then the rows are deleted from the table and the sessions are in Saved sessions.
22. Tools → Options → GUI: untick Use Sessions.xml, OK, restart: no Saved sessions, Sessions.xml unchanged; New session with nothing selected goes to Private sessions. Tick it again and restart: they're back.

Database sessions:

1. Tools → Database sessions...: tick "Show sessions...", host 192.168.3.2, username and password, keep database `superputty`. **Test connection**: "The database doesn't exist yet". **Create / upgrade schema**, then Test again: "The table has 0 sessions".
2. Add rows with SQL, e.g. `INSERT INTO superputty.sessions (folder, name, host, username) VALUES ('Lab', 'sw1', '10.0.0.1', '{user}');`. Right-click Database sessions → Refresh: they appear in their folders. Double-click one: it connects.
3. Right-click Database sessions → **New session...**, fill it in, OK: it's added to the table and selected. Edit it (rename it), Copy as, then Delete the copy. Make a folder with New folder, add a session to it, Rename the folder, then Remove folder. Check the table with SQL after each. `Sessions.xml` doesn't change.
3a. Edit a session in SQL (`UPDATE ... SET host = ...`), then edit the same session in the tree without Refresh: saving says someone else changed it. Refresh and edit again: it saves.
4. Stop Postgres (or set a wrong host): after Refresh the root says "offline, cached ..." and the sessions still open. Start it again and Refresh: back to the count.
5. Tick Read only: Create / upgrade schema stays disabled, and New, Edit, Delete and Rename are disabled in Database sessions.
5a. Locks: run Create / upgrade schema once (it adds the lock columns), then `UPDATE superputty.sessions SET locked_fields = '{host,port}' WHERE name = 'sw1';` and Refresh. Edit sw1: host and port are greyed, notes can be changed and saved. `UPDATE ... SET locked = true`: Edit and Delete are disabled, and renaming its folder is refused.
5c. Dragging: right-click Database sessions → New folder "Moved". Drag a session onto it: it moves there, in the table too. Drag a folder onto it: the folder and everything in it move.
5b. Offline: stop Postgres (or block the port), Refresh, then add a session and edit another: they show in italics, the root says "2 pending". Start Postgres, Refresh: they're in the table, and the italics go. Repeat, but change the edited session by SQL before reconnecting: it stays "not saved"; Database sessions → Pending changes → Overwrite theirs saves it.
6. Move the Database sessions dialog to a monitor with another scaling: OK and Cancel stay fully visible.
7. Clear Nickname and press OK: it asks for one and stays open. Enter one, run **Create / upgrade schema** (it adds `deleted_at` and the change counter), OK. Edit a session: `SELECT name, updated_by FROM superputty.sessions` shows your nickname, and the session's tooltip says "Last changed by ...".
8. With SuperXPuTTY open, add a row with SQL: within about 30 seconds it appears on its own, and the status bar says "Database sessions changed; reading them again". `UPDATE ... SET deleted_at = now()` on it: it disappears within 30 seconds; set it back to NULL: it returns. With Edit Session open, the change waits until the dialog is closed.

Dropped sessions:

1. Open an SSH session to a device without logging in (e.g. 192.168.3.2 at `login as:`) and wait for the server's login timeout (about 2 minutes). The tab should stay open with a red-cross badge. The status bar should say "... disconnected: Remote side unexpectedly closed network connection. Reconnecting in 10 s", and about 10 s later `login as:` should come back in the same tab, with the badge gone and "reconnected" in the status bar.
2. Untick **Tools → Options → Advanced → Reconnect automatically** and repeat: PuTTY's error box stays up and the tab gets the badge. Right-click the tab → **Reconnect**: the box closes and the session comes back.
3. Pull the network (or point a session at a host that's down) with auto-reconnect on: each failed attempt should show "Reconnect n of 6 ... failed" in the status bar, then "Gave up reconnecting ...". Right-click → **Stop Reconnecting** during the wait should cancel the next attempt.
4. Type `exit` in a logged-in session: the tab should close as before.
5. RDP: with an RDP tab connected, briefly disconnect the network: the remote desktop should show its own "reconnecting" overlay and come back.

Alerts and search:

1. Right-click an SSH tab → **Watch for Activity**. Run `sleep 10; echo done` there and switch to another tab: about 10 s later the tab should get an orange-dot badge and the status bar should say "New output in ...". Switch back: the badge goes away.
2. **Watch for Silence** → 10 seconds on a tab running `ping` against a host, then stop the ping (Ctrl+C) and switch away: about 10 s later the tab gets the pause badge. Minimise SuperPuTTY first to see the taskbar flash.
3. Right-click a tab → **Search Output...**, search for something it printed earlier (e.g. after `ls --color`), try match case and a regex, double-click a line to copy it, and try **Open log**.
4. Build `SuperPuttyInstaller`, install the MSI on a test machine (or check `C:\Program Files (x86)\SuperXPuTTY` after installing), and confirm `Newtonsoft.Json.dll` is there and sessions open.

RDP options and sending:

1. In **Tools → Options → RDP**, set Desktop size to "Fixed size" at 1280×720, then connect: the remote should be 1280×720, scaled to fit if "Scale the remote desktop to fit" is on. Set it back to "Fit the tab".
2. Untick Clipboard, reconnect, and check that copy/paste between the computers stops. Tick Drives and check that your drives appear in the remote's File Explorer.
3. With an RDP tab active and logged in, open Notepad there, type `echo hello` into the Send Commands box and press Enter: it should appear in Notepad. Click a button-bar button: its text should appear too.
4. Shift+click a button with an SSH tab and an RDP tab both selected: it should go to both.
5. Edit an RDP session → **RDP options...**, set Desktop size to Fixed 1280×720 and untick Clipboard, then Save: the button should read **RDP options ✓**, and the session should connect at 1280×720 without clipboard sharing while other RDP sessions keep the global options. Set everything back to Default: the tick goes away.

Button bar:

1. Right-click the bar → **New Button...**. Label `uptime`, Send string `uptime\r`. Click it with a session tab active: it should type `uptime` and press Enter.
   - Click into the terminal, then click the button **once**: it should send right away, and you should be able to keep typing in the terminal without clicking it.
   - Edit the button and pick a color from **Icon**, then **Custom color...**, then a session icon. Each should show on the button.
2. Open two sessions and Shift+click the button: both should run it. Untick one in the Send Commands "Select documents" picker and Shift+click again: only the ticked one should.
3. Make a button with `echo {user}\r`, then one with `echo {nokey}\r`. The first should echo your value; the second should stop and offer to open Environment Variables.
4. Make an SPSL button (`SENDLINE whoami`), and a send string with `\p` between two commands; the pause shouldn't freeze the window.
5. Create a second bar, switch between them, restart SuperPuTTY: the last bar shown should come back. Check that `ButtonBars.xml` is next to `Sessions.xml`.
6. Toggle **View → Toolbars → Button Bar**, and go full screen and back. Check the bar and the editor dialog in both light and dark themes.

Icons: start `bin\Debug\SuperXPuTTY.exe` from another folder (for example, from a command prompt in `C:\`) and check that session icons still show. They're drawn by SuperPuTTY, so no icon folder is needed.

Session search:

1. Put a word in a session's Notes, then type it in the search box above the session tree: that session should show.
2. In **Tools → Options → GUI**, click **Search in...**, untick Notes and click OK. The same search should no longer find it.
3. Try a folder name, a port such as `22`, and `ssh`. In Regex mode, try `^10\.` against hosts.

`SessionTreeview.SearchFilter` was checked directly against the compiled assembly (20 checks): each field, every mode, invalid regex, empty text, a session with no host, a password in the extra arguments, and how the setting text is written and read.

The escape-code parser and the `ButtonBars.xml` format were checked directly against the compiled assembly: Enter, pauses, Tab/Escape/Backspace/line feed, clipboard (read only when `\v` is used), literal and unknown backslashes, typed line breaks, empty input, and an XML round-trip including an SPSL script.

The substitution logic (`SessionVariables.Substitute` and `FindMissing`) was checked directly against the compiled assembly: the CyberArk string, case-insensitive keys, one pass with no re-expansion, undefined keys left in place and reported, and braces in a password not flagged. `SuperPuttyUnitTests` uses an NUnit 2.5 GUI runner, and most of its tests need a live SCP server, so these checks aren't in that project yet.

---
*I used Grok/ChatGPT to learn C# and to generate some of this code, so it may not follow best practices. Feedback on the approach is very welcome.*
