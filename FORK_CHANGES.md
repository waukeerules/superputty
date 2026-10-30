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

Output: `bin\Debug\SuperXPuTTY.exe` (the installer goes to `SuperPuttyInstaller\bin\x86\Debug\SuperXPuTTYSetup.msi`). Use `/p:Configuration=Release` for `bin\Release\`.

Build the `.sln`, not `SuperPutty\SuperPutty.csproj`. The post-build step that copies the themes folder uses `$(SolutionDir)`, which is empty when the project is built on its own.

Manual test:

1. Run `bin\Debug\SuperXPuTTY.exe` and open **Tools → Environment Variables...**. Add `user = jdoe` and `domain = example.com`, then click OK.
2. Create a session whose Login username is `{user}@{domain}`. The preview under it should read *Connects as jdoe@example.com to ...*. Change it to `{user}@{zone}`: the field turns red and the preview says `{zone}` is undefined. Click the preview, add `zone`, and check that it updates.
3. Double-click the session. PuTTY should open with `-l jdoe@example.com`; the SuperPuTTY log records the PuTTY arguments.
4. Remove `zone` again and launch: SuperPuTTY should stop and offer to open the Environment Variables dialog instead of connecting.
5. Try the quick-connect toolbar, **Connect All** on a folder, Quick Selector and Restart.
6. Close SuperPuTTY and open `Sessions.xml` (its location is shown in **Tools → Options**). The session should still say `{user}@{domain}`, not `jdoe@example.com`.

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
