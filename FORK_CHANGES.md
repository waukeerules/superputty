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

A row of one-click buttons above the status bar, modelled on SecureCRT's Button Bar. Show or hide it with **View → Toolbars → Button Bar**. The Send Commands toolbar is unchanged and still works alongside it.

**Using it:**
- **Click** a button to send it to the active tab. **Shift+click** sends it to every tab selected in the Send Commands toolbar's "Select documents" picker, filtered by its icon group the same way.
- **Right-click** the bar to add, edit, delete or move buttons, and to create, rename or delete bars. The dropdown on the left switches bars.
- Each button either sends a string or runs an SPSL script.
  - **Send string:** escape codes `\r` Enter, `\n` line feed, `\t` Tab, `\b` Backspace, `\e` Escape, `\p` 1-second pause, `\v` paste clipboard, `\\` backslash. A line break typed in the editor also means Enter.
  - **SPSL script:** the existing scripting language (`SENDLINE`, `SENDKEY`, `SLEEP`, ...).
- `{key}` placeholders work in both and are filled in from **Tools → Environment Variables**. A button that uses an undefined key stops and offers to open that dialog, the same way a session does.
- **Icons:** a button can show a colored circle, like SecureCRT's (eight presets or any custom color), or one of SuperPuTTY's session icons. The circle is stored as `Color="#3FB950"` and the icon as `ImageKey`.
- **Focus:** one click is enough even while you're typing in a terminal, and the keyboard goes back to the active tab afterwards. A ToolStrip normally uses the first click on an inactive window only to activate it (`WM_MOUSEACTIVATE` → `MA_ACTIVATEANDEAT`); the bar answers `MA_ACTIVATE` instead, then calls `FocusActiveDocument`.

**Storage:** `ButtonBars.xml` sits in the settings folder next to `Sessions.xml`, so a team that shares that folder shares the buttons; placeholders keep per-user values out of the file. Which bar is showing, and whether the bar is visible, are per-user settings (`ActiveButtonBar`, `ShowButtonBar`). The file is reloaded whenever the right-click menu opens, so edits apply to the latest version, and it's saved through a temporary file so a failed write can't leave a half-written file. If two people save at the same moment, the last one wins.

**Files:**

| File | Purpose |
|---|---|
| `Data/ButtonBarData.cs` | New. `CommandButton`, `ButtonBar` and `ButtonBarList`, the XML model |
| `Utils/ButtonBarStore.cs` | New. Loads and saves `ButtonBars.xml` |
| `Utils/SendStringParser.cs` | New. Turns a send string with escape codes into `CommandData` steps for the existing `SendToTerminal` |
| `Gui/ButtonBarStrip.cs` | New. The toolbar: dropdown, buttons, right-click menu, and sending on background threads |
| `Gui/dlgEditCommandButton.cs` / `.Designer.cs` | New. The button editor dialog, with dark mode |
| `frmSuperPutty.cs` / `.Designer.cs` | Hosts the bar, adds the menu item, and hides it in full screen. The Send Commands target filter is moved, unchanged, into `GetSelectedPuttyPanels()` so both toolbars use it |
| `Utils/SessionVariables.cs` | `EnsureDefined` also checks arbitrary text, for buttons |
| `Properties/Settings.*`, `App.config` | `ShowButtonBar` (default on) and `ActiveButtonBar` |

**Not done yet:** keyboard shortcuts for buttons, a default bar per session, and SecureCRT's other button actions (launch an application, menu functions).

## Session search across all fields

Upstream's search box above the session tree only looked at the session name and host, and Regex mode only at the name. It now looks at every field by default: session name, folder, host, login username, port, connection type, PuTTY session profile, extra arguments, notes and SPSL script file.

- **Choosing fields:** **Tools → Options → GUI → Session tree → Search in...** opens a checklist. It stays open while you tick fields and has an "All fields" entry; at least one field always stays ticked. The choice is a per-user setting, `SessionsSearchFields`, stored as flag names such as `Name, Host` or `All` (the default).
- **Search modes:** case-sensitive, case-insensitive and Regex all apply to every chosen field.
- **Passwords:** never searched. A `-pw` password in the extra arguments is removed before matching.
- **Placeholders:** the stored text is searched, so `{user}` finds sessions that use that placeholder.
- **Bug fixed:** a session with no host no longer crashes the search (`s.Host.ToLower()` threw on null).

**Files:** `SessionTreeview.cs` (the `SearchFields` flags, field labels and `SearchFilter`), `dlgFindPutty.cs` / `.Designer.cs` (the "Search in..." button and checklist), and `Properties/Settings.*` and `App.config` (`SessionsSearchFields`).

## Other fix: session icons missing depending on how SuperPuTTY is started

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

Session icons were 16x16 PNGs (mostly the famfamfam "Silk" set, plus custom network icons in colour variants such as `green_switch` and `blue_router`). They were loaded into a 16 px list that ignored display scaling, so on a 125%+ display they looked tiny and pixelated next to SecureCRT or MobaXterm. Icons are now drawn at the size they're shown.

- **Icon size:** Tools → Options → GUI → Misc → **Icon size**: 16, 20, 24 (default) or 32. It's scaled for the display and applies after a restart.
  - **Covers:** the session tree, document tabs (bigger icons also make the tab font, and so the tabs, a little taller), the top toolbars and the button bar.
  - **Tab icons** are as tall as the tab text, vertically centred, with a small gap before the text (`Gui/TabIcons.cs`).
    - **Why the extra code:** DockPanelSuite draws a tab's icon in a fixed 16x16 box near the tab's bottom edge. That size is a compile-time constant in both tab styles and isn't scaled for the display, so on a 150% screen the icon was small and sat low.
    - **The workaround:** tabs get a transparent placeholder icon, so DockPanelSuite keeps the icon space but draws nothing there. `TabIcons` then draws the real icon after each tab-strip paint, by listening for the strip's `WM_PAINT`; the strips raise their `Paint` event before drawing the tabs.
    - **Making room:** the tab text gets blank padding in front (U+2800, the blank Braille pattern, which draws as space but isn't trimmed as whitespace), so DockPanelSuite itself widens each tab and moves the text right by the room the larger icon needs.
    - **Who uses it:** set tab icons with `TabIcons.Set`, not the `Icon` property (`SuperPuTTY.ApplyIconForWindow`, the badges in `ctlPuttyPanel`). The tab-list menu shows the real icons and strips the padding.
    - **Classic tabs** have a slanted left edge and start their text a few pixels earlier, so their icons start further in and are a little smaller.
- **Device templates** (`Gui/IconTemplates.cs`) are drawn in code, so they're sharp at any size, and their body takes any colour.
  - **Templates:** switch, layer 3 switch, router, voice router, data centre (Nexus) switch, firewall, wireless, load balancer, server, workstation, terminal, Windows, database, storage, cloud, IP phone, camera, printer, folder and flag.
  - **Colour coding:** "green switch = layer 2, blue switch = layer 3" is one template in two colours.
  - **General icons** replace the rest of the old Silk PNGs, drawn the same way: home, globe, map, truck, book, document, archive, email, chat, feed, clock, help, information, light bulb, lightning, television and mobile device. The picker lists them under "General".
  - **The top toolbars' buttons** (connect, clear, send, mask, run script, select tabs, the icon filter's "stop") are drawn the same way, so they match.
- **Logos** (`Gui/BrandLogos.cs`): 26 OS and vendor logos from [Simple Icons](https://simpleicons.org) 16.36.0. The icon data is CC0 (`Resources/third-party/simple-icons-LICENSE.md`), and the logos remain their owners' trademarks.
  - **The list:** Ubuntu, Debian, Red Hat, CentOS, Fedora, Arch, Mint, openSUSE, SUSE, FreeBSD, OpenBSD, Linux, Apple, Android, Cisco, Palo Alto, Fortinet, VMware, Proxmox, Docker, Kubernetes, Raspberry Pi, Alpine, Rocky, Alma and Kali.
  - **Built in:** each is stored as one SVG path plus the brand colour and drawn through `Gui/SvgPath.cs` (an SVG path → GraphicsPath reader), so nothing is downloaded at run time.
  - **Dark themes:** near-black logos are lightened so they show.
  - **Windows:** Simple Icons no longer carries Microsoft's logos, so the drawn four-pane "windows" template stands in.
- **Icon keys** (`Gui/IconLibrary.cs`): a session's `ImageKey` is a name with an optional colour, e.g. `switch`, `switch#1E63B4` or `ubuntu`. Coloured keys are drawn into the image list on first use.
  - **Old keys still work:** they map onto templates and logos, e.g. `green_switch` → `switch#2E7D32`, `red_big_nexus` → `nexus#8E1B1B`, `flag_red` → `flag#C62828`, `tux` → `linux`, `puffy` → `openbsd`, `computer`/`monitor` → `workstation`, `world` → `globe`, `lorry` → `truck`, `report` → `document`. Every icon in the default set now maps to a drawn one, so existing Sessions.xml files keep their icons, now sharp.
  - **Other PNGs:** any other PNG in a theme folder (e.g. your own, in the settings folder's `themes\default\icons`) is still loaded, scaled smoothly, and listed under "Other icons".
  - **Older SuperPuTTY versions** reading the same Sessions.xml show their default icon for a coloured key.
- **Icon picker** (`Gui/dlgIconPicker.cs`), from Edit Session's icon button:
  - **Layout:** a searchable grid grouped as Devices, General, Logos and Other icons, plus a large preview.
  - **Dark theme:** the grid uses Windows' dark "items view" style, so the group headings are readable. DarkModeCS's dark Explorer style left them dark blue.
  - **Colour row:** Default, ten named colours, and More... for any colour. Choosing a colour shows every device template in it; logos keep their brand colours in the grid.
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

## Fix: the installer didn't include Newtonsoft.Json.dll

`SuperPuttyInstaller/Product.wxs` lists every file the MSI installs. `Newtonsoft.Json.dll`, which the Environment Variables feature needs (and so, through `{key}` placeholders, every session launch), was never added, so an installed copy would fail to load it. It's now a component of its own. The built MSI's File table lists it next to the exe, its config, log4net and the two DockPanel DLLs.

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

Build the `.sln`, not `SuperPutty\SuperPutty.csproj`. The post-build step that copies the themes folder uses `$(SolutionDir)`, which is empty when the project is built on its own.

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
4. In the dark theme, an Apple or AlmaLinux logo should show light grey, not black.

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

Icons: start `bin\Debug\SuperXPuTTY.exe` from another folder (for example, from a command prompt in `C:\`) and check that session icons still show. Copy a `.png` into `<SettingsFolder>\themes\default\icons`, restart, and check that it appears in the Edit Session icon picker. `GetThemeIconFolders` was checked against the compiled assembly from the Temp folder: it found the exe's `themes` folder and `C:\Program Files (x86)\SuperXPuTTY\themes\default\icons`, and loaded 49 icons.

Session search:

1. Put a word in a session's Notes, then type it in the search box above the session tree: that session should show.
2. In **Tools → Options → GUI**, click **Search in...**, untick Notes and click OK. The same search should no longer find it.
3. Try a folder name, a port such as `22`, and `ssh`. In Regex mode, try `^10\.` against hosts.

`SessionTreeview.SearchFilter` was checked directly against the compiled assembly (20 checks): each field, every mode, invalid regex, empty text, a session with no host, a password in the extra arguments, and how the setting text is written and read.

The escape-code parser and the `ButtonBars.xml` format were checked directly against the compiled assembly: Enter, pauses, Tab/Escape/Backspace/line feed, clipboard (read only when `\v` is used), literal and unknown backslashes, typed line breaks, empty input, and an XML round-trip including an SPSL script.

The substitution logic (`SessionVariables.Substitute` and `FindMissing`) was checked directly against the compiled assembly: the CyberArk string, case-insensitive keys, one pass with no re-expansion, undefined keys left in place and reported, and braces in a password not flagged. `SuperPuttyUnitTests` uses an NUnit 2.5 GUI runner, and most of its tests need a live SCP server, so these checks aren't in that project yet.

---
*I used Grok/ChatGPT to learn C# and to generate some of this code, so it may not follow best practices. Feedback on the approach is very welcome.*
