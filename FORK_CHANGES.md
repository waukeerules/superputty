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

The substitution logic (`SessionVariables.Substitute` and `FindMissing`) was checked directly against the compiled assembly: the CyberArk string, case-insensitive keys, one pass with no re-expansion, undefined keys left in place and reported, and braces in a password not flagged. `SuperPuttyUnitTests` uses an NUnit 2.5 GUI runner, and most of its tests need a live SCP server, so these checks aren't in that project yet.

---
*I used Grok/ChatGPT to learn C# and to generate some of this code, so it may not follow best practices. Feedback on the approach is very welcome.*
