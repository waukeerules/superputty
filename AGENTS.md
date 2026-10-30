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

- Build the `.sln`, not `SuperPutty\SuperPutty.csproj`. The post-build theme copy relies on `$(SolutionDir)`.
- The output is `bin\Debug\SuperXPuTTY.exe`. If SuperPuTTY is running from there, the build fails to copy the exe; close it first.
- The log goes to `%TEMP%\SuperPuTTY.log`. `ApplicationPanel` logs every `MoveWindow` with the panel size, which is useful for window and sizing bugs.
- `SuperPuttyUnitTests` uses an old NUnit 2.5 GUI runner, and most of its tests need a live SCP server. For new logic, keep it in small static methods that can be checked directly, and list the manual test steps in FORK_CHANGES.md.
- Say what you actually verified (built, ran, checked in the UI) and what you didn't.

## Things that bite in this codebase

- **Mixed line endings.** Some files are CRLF and some LF; some are mixed. Preserve each file's existing endings. A script that reads in text mode and writes back can turn a 3-line change into a whole-file diff, so check `git diff --stat` before committing.
- **WinForms designer files** (`*.Designer.cs`): add controls the way the designer would: declare the field, construct it in `InitializeComponent`, set its properties, add it to its parent's `Controls`, and wire its events.
- **Dark mode.** New dialogs apply `DarkModeCS` the same way the existing dialogs do (`if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme) new DarkModeCS(this) { ... }`). It recolors controls during the form's `Load` event, so read themed colors in `OnLoad`, not in the constructor.
- **Stored sessions.** Never modify a `SessionData` taken from the session tree just to launch it. Its setters raise change events, and the change is saved to `Sessions.xml`, which may be shared by a team. Work on `session.Clone()`, as `SessionVariables.Resolve` does.
- **Session placeholders** (`{key}`) are resolved only in `PuttyStartInfo` and `SuperPuTTY.OpenScpSession`, and checked in `SuperPuTTY.OpenProtoSession`. New launch paths go through those, not their own substitution.
- **Starting PuTTY.** `SuperPuTTY.OpenProtoSession` starts the hosted application after `panel.Show()`, so it starts at the tab's real size. Don't move the start back into `OnVisibleChanged` or a constructor.
- **Secrets.** Never log passwords, `-pw` arguments or placeholder values.
