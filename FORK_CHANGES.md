# Fork changes: Session Placeholders ("Environment Variables")

Fork: <https://github.com/waukeerules/superputty>
Upstream: <https://github.com/SilverGreen93/superputty>
Base: upstream `master` @ `639ef12` (v2.1.1.0). The fork is up to date with upstream, with one feature commit on top (`ea014af`).

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

## What changed vs. upstream

### New files

| File | Purpose |
|---|---|
| `SuperPutty/Models/EnvironmentVariable.cs` | `EnvironmentVariable` (`Key`, `Value`) and `EnvironmentVariables` (a `BindingList` container) |
| `SuperPutty/Utils/EnvironmentVariablesManager.cs` | Loads and saves `EnvironmentVariables.json` in the SuperPuTTY settings folder (falls back to `%APPDATA%`). Creates the file if it's missing and retries the save 3× on `IOException` |
| `SuperPutty/EnvironmentVariablesForm.cs` / `.Designer.cs` / `.resx` | A dialog with a Key/Value grid and Add/OK/Cancel buttons. It rejects empty or duplicate keys (case-insensitive) and drops blank rows on save |

### Modified files

| File | Change |
|---|---|
| `frmSuperPutty.Designer.cs` / `frmSuperPutty.cs` | Adds the **Tools → Environment Variables...** menu item. The quick-connect toolbar (`TryConnectFromToolbar`) now substitutes placeholders in host, login and password. The method moved to the bottom of the file; upstream's VNC port fix was merged into it. Adds `SubstituteVariables` and `OpenSessionWithSubstitutions` |
| `SessionTreeview.cs` | Double-click, **Connect** from the context menu and **Connect All** in a folder now go through `OpenSessionWithSubstitutions`. It clones the session, replaces placeholders in the clone's `Host`, `Username`, `Password` and `ExtraArgs`, then calls `SuperPuTTY.OpenProtoSession` |
| `Utils/PuttyStartInfo.cs` | Wraps the constructor, `GetExecutable` and `StartStandalone` in try/catch with extra logging. Throws if no executable is found |
| `dlgEditSession.Designer.cs` | Adds a tooltip on the Login username box: "Enter username or placeholder (e.g., {user})" |
| `SuperPutty.csproj` | Adds the new files, a `Newtonsoft.Json` 13.0.3 package reference and a `System.Xml.Linq` reference |
| `Properties/Settings.cs` | Formatting only (brace style, whitespace) |

### Storage

`EnvironmentVariables.json` sits next to `SuperXPuTTY.settings`:

```json
{
  "Variables": [
    { "Key": "user",   "Value": "jdoe" },
    { "Key": "domain", "Value": "corp.example.com" }
  ]
}
```

## Known issues / open questions

These are things I'm aware of and would like feedback on before proposing this upstream.

- ~~**Resolved values can be written back to `Sessions.xml`.**~~ **Fixed.** `OpenSessionWithSubstitutions` used to modify the live `SessionData` object from the tree, so a later save wrote that user's values over the placeholders. It now substitutes on `session.Clone()`, which keeps the `SessionId` but not the change handlers, so the stored session keeps its `{placeholders}`.
- **Not every launch path substitutes.** These all call `OpenProtoSession` directly and skip substitution: Quick Selector (`frmSuperPutty.cs`), Restart/Duplicate session (`ctlPuttyPanel.cs`), layouts and command-line startup (`SuperPuTTY.cs`). Putting substitution in one place (`SuperPuTTY.OpenProtoSession` or `PuttyStartInfo`) would cover all of them.
- **`%VAR%` expansion regression.** Upstream's `TryParseEnvVars(this.Args)` call at the end of the `PuttyStartInfo` constructor was dropped, so Windows-style `%VAR%` references in arguments are no longer expanded. It should be restored.
- **Passwords in logs.** The new logging writes `session.Password` (the `?? "[hidden]"` only applies when it's null) and the variable values at INFO level. These should be masked.
- **Duplicated code.** `SubstituteVariables` and `OpenSessionWithSubstitutions` exist in both `frmSuperPutty.cs` and `SessionTreeview.cs`. `frmSuperPutty.SessionTreeview_SessionDoubleClick` is never subscribed, and `EventHelper` is unused.
- **Process environment side effect.** Each launch also calls `Environment.SetEnvironmentVariable` for every key. It's unclear whether this is useful (child PuTTY processes inherit it) or just noise.
- **Naming.** "Environment Variables" could be confused with Windows environment variables, which SuperPuTTY already expands as `%VAR%`. "Session Placeholders" or "User Variables" might be clearer.
- **Storage format.** JSON via Newtonsoft adds a dependency. The values could live in `SuperXPuTTY.settings` like other per-user settings.
- **Secrets.** Values are stored in plain text. If people put passwords in them, a DPAPI-protected store may be warranted.
- **Nesting and defaults.** Not supported: no `{key:default}`, no recursion, and an undefined key is left as literal `{key}`.

## Uncommitted work (not in the fork yet)

`C:\Python\superputty_fork` has uncommitted edits to `EnvironmentVariablesForm` that would be a follow-up:

- Segoe UI 12pt font and a larger row height in the grid
- Dark mode when the SuperPuTTY theme is dark (`DarkModeCS`)
- Designer-regenerated layout (docked label, grid and button bar)

## Building and testing

Requirements: Visual Studio 2022 (or Build Tools) with .NET Framework 4.8 and the WiX v3 toolset for the installer.

```bash
MSBuild.exe SuperPutty.sln /restore /p:Configuration=Debug
```

Output: `bin\Debug\SuperXPuTTY.exe` (the installer goes to `SuperPuttyInstaller\bin\x86\Debug\SuperXPuTTYSetup.msi`).

Manual test:

1. Run `bin\Debug\SuperXPuTTY.exe` and open **Tools → Environment Variables...**. Add `user = jdoe` and `domain = example.com`, then click OK.
2. Create a session whose Login username is `{user}@{domain}` and double-click it. PuTTY should open with `-l jdoe@example.com`; the SuperPuTTY log records the PuTTY arguments.
3. Do the same through the quick-connect toolbar and through **Connect All** on a folder.

`SuperPuttyUnitTests` uses an NUnit 2.5 GUI runner, and most of its tests need a live SCP server. There are no automated tests for this feature yet.

---
*I used Grok/ChatGPT to learn C# and to generate some of this code, so it may not follow best practices. Feedback on the approach is very welcome.*
