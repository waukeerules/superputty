# Highlighted terminal: Monaco / xterm.js feasibility study

Status: **phases 1 and 2 built, both off by default** (Highlighted Output, and the built-in terminal). The idea is an optional view, off by default, that shows a session's
terminal in a web view so device output can be syntax highlighted (Cisco `up/up` green, `down` red) and themed. This
document covers what was checked, a recommended architecture, the risks and a phased plan.

Spike code lives in [`docs/spikes/`](spikes/) and is not part of `SuperPutty.sln`.

## Summary

| Question | Answer |
|---|---|
| Can PuTTY itself be "hijacked"? | Not for live I/O. putty.exe draws its own window and has no API for its byte stream. Its `-sessionlog` gives the **output only**, which is enough for a highlighted read-only view (phase 1). |
| Byte stream for a replacement terminal | **plink.exe run inside a Windows pseudo console (ConPTY)**. plink takes `-load <profile>`, so profiles, keys, Pageant, proxy settings and our `-proxycmd` jump hosts all keep working. It handles SSH, Telnet, rlogin, raw and serial. Because plink sees a real console, password, passphrase and host-key prompts appear in the terminal, and resizing works. **Tested against a lab Cisco switch over SSH, Telnet and serial (COM6)**: host key, login, enable, `--More--` and line editing all work. |
| pscp | File transfer only, no use here. |
| Monaco as the terminal? | **No.** Monaco is a text editor and has no terminal emulator: it can't handle `--More--`, cursor moves, line editing or full-screen apps. It is the right tool for a **document view** of the output. |
| Terminal emulator | **xterm.js**, the emulator inside VS Code's terminal. Highlighting is done with its decorations and link providers, so the bytes never change. |
| Recommendation | **Hybrid.** Phase 1: a Monaco "Highlighted output" panel fed from the session log PuTTY already writes; PuTTY doesn't change at all. Phase 2 (optional per session): xterm.js + plink/ConPTY as an alternative terminal, with "Open as document" handing the scrollback to the phase 1 Monaco view. |
| Hosting | WebView2 (`Microsoft.Web.WebView2` NuGet, supports .NET Framework 4.6.2+). The Evergreen runtime is on this machine (156.0.4314.58) and on current Windows 10/11. JS assets ship offline in the installer and are served with `SetVirtualHostNameToFolderMapping`. |

## 1. Getting the byte stream

### What was checked

`plink --help` from the installed PuTTY 0.85 (`C:\Program Files\PuTTY\plink.exe`):

- `-load sessname`, `-ssh -telnet -rlogin -raw -serial`, `-sercfg`, `-proxycmd`, `-batch`, `-t/-T`, `-L/-R/-D`,
  `-X/-x`, `-A/-a`, `-i key`, `-pwfile file`, `-hostkey`, `-share`.
- `-sanitise-stdout` / `-no-sanitise-stdout`: plink can strip control characters from stdout. It must not here, because
  the terminal needs the escape sequences.
- **No `-sessionlog`.** plink only has `-sshlog` and `-sshrawlog`, which log the protocol, not the terminal. In the plink
  path SuperPuTTY writes the session log itself (see section 4).

### The options

| Option | SSH | Telnet | Serial | Keeps PuTTY profiles, keys, jump hosts | Prompts (password, host key) | Resize | Verdict |
|---|---|---|---|---|---|---|---|
| putty.exe `-sessionlog` (current) | yes | yes | yes | yes | in PuTTY's window | yes | **Output only.** Good for phase 1, can't type. |
| plink with redirected stdin/stdout | yes | yes | yes | yes (`-load`, `-proxycmd`) | Unreliable: plink treats a pipe as non-interactive, so prompts only work with keys/Pageant, `-pwfile` or `-batch` plus `-hostkey` | **No.** A pipe has no window size, so the device always sees the default size | Possible, but a poor terminal |
| **plink inside ConPTY** | yes (tested) | yes (tested) | yes (tested, COM6) | yes | **Yes**, tested: host key, keyboard-interactive password, enable | **Yes**, `ResizePseudoConsole` makes plink send a window change (IOS ignores it, Linux uses it) | **Recommended** |
| SSH.NET (pure .NET) | yes | no | no (use `System.IO.Ports`) | **No**: ignores PuTTY profiles, `.ppk` (v3 partly), Pageant, proxy settings and our jump hosts; needs a NuGet download | ours to build | yes | Rejected: it rebuilds everything PuTTY already does |

ConPTY needs Windows 10 1809 or later, which .NET Framework 4.8 users have.

### Spike: plink in ConPTY ([`spikes/ConPtyPlink/ConPtyPlink.cs`](spikes/ConPtyPlink/ConPtyPlink.cs))

About 100 lines of P/Invoke (`CreatePseudoConsole`, `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`, `ResizePseudoConsole`),
built with the .NET Framework 4.8 `csc.exe`, no packages. Run against `cmd.exe` and against plink (`-ssh` and `-telnet`
to a closed local port, since there's no SSH server here):

```
\e[?9001h\e[?1004hFATAL ERROR: Network error: Connection refused
\e[?25l\e[2J\e[m\e[H ... (blank lines) ... \e[H\e]0;C:\Program Files\PuTTY\plink.exe\x07\e[?25h\e[?9001l\e[?1004l
```

What it shows:

- plink's output comes back as a VT stream that xterm.js can display as it is.
- plink's fatal errors come through as text (`FATAL ERROR: ...`), and the process exits. That's simpler to detect than
  PuTTY's Fatal Error box (`PuttyWindow.FindFatalErrorBox`).
- **ConPTY quirks to plan for.** It turns on win32-input-mode (`?9001h`) and focus reporting (`?1004h`); xterm.js ignores
  the first and should answer the second. After a resize it **repaints the screen** (`\e[2J` and the whole screen
  again). It may also write runs of spaces as cursor moves (`\e[5C`). These matter for the session log and for
  `WAITFOR` (section 4).

### Spike against a real Cisco switch ([`spikes/ConPtyPlink/ConPtyScript.cs`](spikes/ConPtyPlink/ConPtyScript.cs))

A scripted version of the spike (wait for text, send keys, send a secret from a file without printing it, resize) was
run against a lab Cisco IOS switch over SSH, Telnet and the serial console on COM6. The host-key prompt was answered
"n" (connect once without storing), so PuTTY's host-key cache was not changed. Captured output for testing rules is in
[`spikes/samples/cisco-ios-switch.txt`](spikes/samples/cisco-ios-switch.txt).

| Check | Result |
|---|---|
| SSH: host-key prompt for an unknown host | **Works.** plink's full "host key is not cached" text and `(y/n, Return cancels connection, i for more info)` appear in the terminal; answered by typing |
| SSH: password (IOS uses keyboard-interactive) | **Works.** plink frames it with `-- Keyboard-interactive authentication prompts from server --`, like PuTTY does in its window. A wrong password gives `Access denied` and asks again |
| plink's anti-spoofing pause after login | `Access granted. Press Return to begin session.` Pass `-no-antispoof` so the user isn't asked to press Return (PuTTY doesn't ask) |
| `enable` and its password | Works, not echoed |
| `--More--` paging, Space, `q` | Works |
| IOS line editing (backspace), `% Invalid input` with the `^` marker | Works |
| Telnet (`-telnet`): IOS `Username:` / `Password:` | Works |
| Serial (`-serial COM6 -sercfg 9600,8,n,1,N`) | Works. The console was already logged in at the `#` prompt (worth knowing about the lab switch itself) |
| Window size | This IOS reports `Length: 24 lines, Width: 80 columns` whether the session starts at 132×40 or is resized mid-session. **IOS ignores the SSH window size**, as it does under PuTTY. Resizing still matters for Linux hosts, and IOS users keep using `terminal length` / `terminal width` |

Two findings that change the design:

1. **Child processes must not inherit our std handles.** The first runs lost all output after login: the spike's own
   stdout was redirected, plink inherited it, and after authentication plink wrote the session there rather than to
   the pseudo console (its prompts went to the console, so they still showed). Setting `STARTF_USESTDHANDLES` with
   empty handles fixed it. SuperXPuTTY is a GUI app with no std handles, but the real code must set this flag anyway.
2. **ConPTY rewrites the stream.** IOS erases `--More--` with backspaces and spaces. ConPTY sends the same screen
   change as `--More--\e[1C` followed straight by the next page's first line, with no line break, and wraps at the
   bottom of the screen with `\e[24;1H` rather than `\r\n`. Reading the raw text line by line (as `CleanForDisplay`
   does today) would join `--More--` and `FastEthernet0/1 is down...` into one line. So **highlighting, the session
   log and `WAITFOR` must read the emulated screen (xterm.js's buffer), not the raw bytes.** PuTTY's own
   `-sessionlog` has the same kind of problem, with raw backspaces instead of cursor moves, which is why phase 1 runs
   the log through `@xterm/headless`.

## 2. Emulating the terminal

| | Monaco transcript + input line | xterm.js | Hybrid (recommended) |
|---|---|---|---|
| VT100/xterm emulation, `--More--`, `?` help, tab completion, line editing, colours, alternate screen (`vi`, `top`) | No. A separate input line can't do tab completion or `?`, or show the line being edited on the device | Yes, it is a full emulator | Yes (xterm.js) |
| Highlighting device output | Monarch tokenizer, very flexible | Decorations on buffer ranges and a link provider; regex rules | Both: decorations live, Monarch in the document view |
| Document features: search, folding, minimap, select a column, copy as text | Excellent | Basic (search addon) | In the Monaco view |
| Size | Monaco ~10 MB unpacked (trimmable to a few MB without the languages we don't need) | ~0.5 MB with addons | Both |

**Recommendation: hybrid.** xterm.js is the live terminal (phase 2) and Monaco is the "document" view of a session's
output (phase 1), which also covers rConfig-style config reading: `show running-config` folding by `interface` block,
search and a minimap. Monaco as the terminal itself would break the CLI's interactive features network engineers rely
on every minute (`?`, Tab, `--More--`).

## 3. Hosting in WinForms (.NET Framework 4.8)

- **Control:** `Microsoft.Web.WebView2.WinForms.WebView2`, from the `Microsoft.Web.WebView2` NuGet package (needs
  approval to download). It brings `Microsoft.Web.WebView2.Core.dll`, `.WinForms.dll` and a native
  `WebView2Loader.dll` per architecture. All of them go in `SuperPuttyInstaller/Product.wxs`, like the Npgsql libraries.
- **Runtime:** use the Evergreen WebView2 runtime if it's present (`CoreWebView2Environment.GetAvailableBrowserVersionString`).
  If it isn't, the option is greyed out with a link to Microsoft's installer. No bundled runtime (it's ~150 MB).
- **User data folder:** WebView2's default is next to the exe, which **isn't writable under Program Files**. Create one
  shared `CoreWebView2Environment` with `%LOCALAPPDATA%\SuperXPuTTY\WebView2` (or `UserSettingsFolder` when portable).
  Sharing it keeps one browser process for all tabs. Each tab still gets a renderer of roughly 30–60 MB, so this stays
  per-session opt-in.
- **Offline assets:** ship `web\terminal\` (xterm.js, addons, our page) and `web\monaco\` (`min\vs` trimmed) next to
  the exe, and map them with `SetVirtualHostNameToFolderMapping("superputty.local", folder, Deny-cross-origin)`. No
  network access and no CDN. Turn off devtools, context menus and the status bar outside Debug builds. Monaco's
  hundreds of files mean the installer needs a harvested `ComponentGroup` (WiX `heat`) rather than hand-listed files.
- **Bridge:** C# → page with `PostWebMessageAsString` (output chunks, base64 or JSON). Page → C# with
  `window.chrome.webview.postMessage` (typed keys, resize, selection). Output is batched, every ~16 ms or 64 KB, so
  something like `show tech` doesn't flood the bridge.
- **Fitting it into `ctlPuttyPanel`:** the same seam the RDP control uses. `ctlPuttyPanel.CreatePanel` sets
  `AppPanel.EmbeddedApplicationFactory = () => new WebTerminalControl(resolvedSession)` when the session (or folder, or
  global default) has "Highlighted terminal" turned on. `WebTerminalControl : UserControl, IEmbeddedApplication,
  ICommandReceiver` owns the WebView2 and the ConPTY/plink process. `ApplicationPanel.StartEmbeddedApplication` already
  adds it, fills the tab and calls `Start()` after the tab is laid out (see `ctlApplicationPanel.cs:671`).
- **Things to test in docking:** floating a tab and moving it between DockPanel windows reparents the WebView2's HWND;
  this needs a test like the RDP control had. Keyboard: WebView2 swallows keys, so SuperPuTTY's hotkeys (Ctrl+Tab,
  the Quick Selector, ...) have to be forwarded from `AcceleratorKeyPressed` or handled in the page and posted back.

## 4. What breaks or has to be rebuilt

Only for the **phase 2 terminal**. Phase 1 leaves PuTTY running exactly as it does today, so nothing on this list
changes for it.

| Feature | Today | In the plink/ConPTY terminal |
|---|---|---|
| `ctlPuttyPanel.SendCommand` / `ICommandReceiver` (Send Commands, button bar, SPSL via `RunScript`) | Window messages to PuTTY (`CommandData.SendToTerminal`) | **Gets better:** `WebTerminalControl.SendCommand` writes the text and keys as bytes to the ConPTY input pipe; no focus or window-message timing. Needs a key → VT sequence map (`{ENTER}` → `\r`, arrows → `\e[A`, ...). The button bar and `frmSuperPutty` already call `panel.SendCommand`. `Scripting/Spsl.cs:105` still calls `SendToTerminal` with a handle, which is the old path AGENTS.md says not to use; it would need routing through `SendCommand` |
| SPSL `WAITFOR`, activity watch, Search Output | Read PuTTY's `-sessionlog` | SuperPuTTY writes the log itself, keeping the same `SessionLogPath` contract. **Risk:** ConPTY repaints the screen on resize and may write spaces as cursor moves, so raw log text can differ from what's on screen. Mitigation: write the log from xterm.js's buffer (lines as they scroll off and complete), or match `WAITFOR` against the page's buffer text. Pick one in phase 2's first step |
| `CheckConnection` / reconnect | Finds PuTTY's Fatal Error box and sends Restart Session | Rebuilt, more simply: plink prints `FATAL ERROR:` and exits. The control shows a "Disconnected, reconnecting in N s" banner and restarts plink using the existing `AutoReconnect*` settings. `IEmbeddedApplication.Ended` closes the tab today, so the control must only raise it when the user ends the session, or the interface gains a disconnected state |
| Session logging (kept logs) | PuTTY | Ours (above). Same folder and file names, so `SessionLog` is reused |
| Copy/paste | PuTTY's select-to-copy, right-click paste | Rebuilt in the page: select-to-copy and right-click paste as an option (PuTTY-style), Ctrl+Shift+C/V, a paste guard for multi-line pastes (useful on routers). Clipboard through the bridge |
| PuTTY tab menu (Change Settings, Event Log, Copy All, Clear Scrollback, Reset Terminal, Restart Session) | PuTTY system-menu commands | Copy All, Clear, Reset and Restart map to xterm.js calls / restarting plink; Event Log and Change Settings are hidden (`AdjustMenu`), as they are for RDP |
| PuTTY colours and fonts | From the PuTTY profile | **Not used** by xterm.js. Our own themes (below), optionally imported from a profile's `Colour0..21` and `Font` registry values under `HKCU\Software\SimonTatham\PuTTY\Sessions\<profile>` |
| Port forwarding, X11, agent forwarding | PuTTY | **Still works:** plink applies them from the profile (`-load`) and accepts `-L/-R/-D/-X/-A` |
| Jump hosts (`JumpHosts.ArgsFor`) | `-proxycmd` on putty.exe | Same arguments on plink, unchanged |
| Session placeholders, `-pw` | `PuttyStartInfo` | Same: build plink's command line through `SessionVariables.Resolve` and `PuttyStartInfo`'s argument code. A password goes through `-pwfile` (temporary file, deleted at once) rather than `-pw`, so it's not visible in the process list. Never log it |
| Environment colour frame, tab icons | `ctlPuttyPanel` | Unchanged; they're outside the `ApplicationPanel` |
| Serial | PuTTY `-serial` | plink `-serial` with `-sercfg`, then test with a real console cable |
| Mintty, Cygterm, cmd, PowerShell, WSL, RDP, VNC | Their own programs | Out of scope; the option applies to PuTTY protocols only |

## 5. Syntax highlighting

### Rules, not hard-coded languages

One rule set feeds both views, so the user configures it once:

```json
{
  "name": "Cisco interface status",
  "platforms": ["ios", "nxos"],
  "rules": [
    { "match": "^\\S+\\s+\\S+\\s+\\S+\\s+\\S+\\s+up\\s+up\\s*$",                   "scope": "line", "style": "good" },
    { "match": "\\bis up, line protocol is up\\b|\\bconnected\\b",                  "style": "good" },
    { "match": "\\b(administratively down|down|err-disabled|notconnect)\\b",        "style": "bad" },
    { "match": "\\b(\\d{1,3}\\.){3}\\d{1,3}(/\\d{1,2})?\\b",                        "style": "address", "link": "ip" },
    { "match": "\\b(CRC|input errors|output errors|collisions|drops)\\b\\s*\\d*",   "style": "warning" },
    { "match": "^[\\w.\\-()]+[#>]\\s?",                                              "style": "prompt" },
    { "match": "^%.*$",                                                              "style": "bad" }
  ]
}
```

- **Live terminal (phase 2):** after each `write`, the page scans the lines that changed (`onWriteParsed`), runs the
  rules and adds `registerMarker` + `registerDecoration` with `foregroundColor` / `backgroundColor`, or underlines
  `link` matches with `registerLinkProvider` (click an IP → copy, ping or open a new session to it). Decorations sit on
  top of the cells, so the bytes, the log and `WAITFOR` never change. Lines that are redrawn (a `--More--` erased) get
  rescanned. Only lines on screen and recently written are scanned, so long scrollback stays fast.
- **Document view (phase 1, built):** the rules become **Monaco decorations** (`createDecorationsCollection`) for the
  lines in view plus a margin, worked out again on scroll and on new output. This replaced the Monarch tokenizer
  planned first: Monarch matches from the current token, so `^` can't anchor a rule to the start of a line, and it
  can't colour a whole line and a match inside it differently. Decorations use the same JavaScript regexes the phase 2
  terminal will use. Folding is Monaco's indentation folding, which suits `show running-config`.
- **Which rule set:** phase 1 has a toolbar choice (Auto / each set / None), remembered per user. Auto takes the first
  set whose `detect` pattern matches a line (`name#` → Cisco, `user@host>` → Junos). A per-session or folder field
  could come later.

The rules above were written against the captured switch output in
[`spikes/samples/cisco-ios-switch.txt`](spikes/samples/cisco-ios-switch.txt), which can be the first test fixture.
Note that `\bconnected\b` doesn't match `notconnect`, but a naive `connect` would. Rules need tests like that.

Shipped starter sets: Cisco IOS/IOS-XE (`show ip interface brief`, `show interfaces`, `show interfaces status`,
`show ip route` codes, `%` messages, `show logging` severities), NX-OS, and Junos (`show interfaces terse`, `up`/`down`
columns, `error:`).

### Where users configure it

- **Tools → Options → Highlighting** (new page): turn the feature on, pick the default rule sets, edit themes.
- **Rules and themes are files** in `<SettingsFolder>\highlighting\` (`*.rules.json`, `*.theme.json`). The
  SettingsFolder can be shared, so a team gets the same rules. A per-user folder next to `SuperXPuTTY.settings` overrides
  them. The options page has a list with a test box: paste output, see it coloured.
- **Themes:** background, foreground, cursor, selection, the 16 ANSI colours, font family and size, plus the style
  names the rules use (`good`, `bad`, `warning`, `address`, `prompt`). Light and dark defaults follow SuperPuTTY's
  theme; "Import from PuTTY profile" copies a profile's colours.
- Later, rules could live in the Database sessions table (`<table>_highlighting`), like folders do. Not in the first
  phases.

## 6. Effort and phasing

Rough estimates for one developer who knows this codebase.

### Phase 1: "Highlighted output" panel (no change to how PuTTY runs), ~4–6 days

**Built** as Highlighted Output; see FORK_CHANGES.md for the files, what was checked and the manual tests. What
changed from the plan below:
- the hidden terminal is `@xterm/xterm` 6.0.0 created without `open()`, not `@xterm/headless`, which is built for Node;
- highlighting is decorations, not Monarch (section 5);
- Monaco 0.57.0's AMD build (`min/vs`, deprecated upstream but still shipped) is loaded with its own `loader.js`, trimmed
  to the 18 files the page requests (4.9 MB); a later Monaco without the AMD build would need a small ESM bundle instead;
- the Options page only has the switch, the runtime's version and the folder button; rules are tried out with the
  panel's Open File... instead of a test box;
- an 8 MB log (157,000 lines) loads in about 10 s, so the 50 MB case takes about a minute.

Also learned while building it: plink accepts `-sessionlog` but writes nothing (PuTTY logs from its terminal code, which
plink lacks). With plain pipes, `-pwfile`, `-hostkey` and `-t`, plink does give exactly the bytes PuTTY would log, which
is how the test log was recorded.

- WebView2 package, shared environment, user data folder, installer entries, runtime check. *(Needs approval to
  download `Microsoft.Web.WebView2` and the `monaco-editor` and `@xterm/headless` npm assets.)*
- A `ToolWindow` (or a tab menu item "Highlighted output") showing Monaco, read-only, following the session's log:
  poll `SessionLog.LengthOf` / `ReadFrom` like `WAITFOR` does, and append. The raw log is fed through `@xterm/headless`
  in the page so backspaces, `--More--` erasing and line redraws come out as the lines a person saw. `CleanForDisplay`
  only strips sequences and would leave `sh ip int brr\b` style noise.
- Monarch languages for IOS/NX-OS/Junos from the rule files, two themes, Options page.
- "Follow" toggle, search, "Save as...", and a column/block copy.
- Off by default; nothing changes unless a user opens the panel.
- Manual tests: a Cisco lab device (or a recorded log), light and dark theme, a floating panel, a 50 MB log.

### Phase 2: alternative terminal (opt-in per session), ~3–5 weeks

**Built** as the built-in terminal; see FORK_CHANGES.md for the files, checks and manual tests. What changed from the
plan below:
- it's one global option (Options → Highlighting), not per session or folder yet;
- **logging comes from the screen.** ConPTY's passthrough mode (`PSEUDOCONSOLE_PASSTHROUGH_MODE`, 0x8), which would
  forward plink's bytes unchanged, isn't in this Windows build's ConPTY: the flag is ignored and the stream is still
  rewritten. So the page logs each line once the cursor leaves it, and WAITFOR also reads the cursor's line. That
  makes WAITFOR work on positions in that text rather than bytes in the file (`TerminalControl.MarkOutput`);
- plink refuses `-pw` for anything but SSH, where PuTTY ignores it, so the command line drops it for other protocols;
- **keys:** plink reads its console with only processed input on, so arrows (which the pseudo console turns into key
  events without a character) were dropped and Ctrl+C was a signal. Neither a separate ESC, an ESC sent as a
  win32-input-mode key event, nor plink's stdin on a pipe helped (plink reads login prompts from the console, not
  stdin). What works: attach to plink's console briefly and set `ENABLE_VIRTUAL_TERMINAL_INPUT` without
  `ENABLE_PROCESSED_INPUT` (`ConPty.EnsureVirtualTerminalInput`), checked before each ESC or Ctrl+C because plink sets
  its mode when the session starts;
- **xterm.js 6.0.0 decoration bug:** disposing a marker before its decorations corrupts the decoration list (the
  marker's line is already -1 when the decoration is looked up to be removed), and other lines lose their colours.
  Always dispose the decoration first;
- no xterm.js addons were needed: fitting reads the same cell size the fit addon does, find and the IP/MAC colours
  use the public decoration API, and the clipboard goes through TerminalControl (the page has no clipboard access);
- xterm.js's `scrollOnEraseInDisplay` keeps a cleared screen in the scrollback, as PuTTY does by default;
- not done yet: clickable links on addresses, a paste guard for multi-line pastes, and importing a PuTTY profile's
  colours (the PuTTY theme has PuTTY's defaults).

1. `ConPty` wrapper class (from the spike, with `STARTF_USESTDHANDLES`) and `PlinkStartInfo` (from `PuttyStartInfo` /
   `JumpHosts`, `-pwfile`, `-no-antispoof`). The pseudo console starts at the tab's size.
2. `WebTerminalControl : IEmbeddedApplication, ICommandReceiver` with xterm.js, fit/search/web-links/unicode11 addons,
   resizing, focus and hotkey forwarding, copy/paste.
3. Session log written by SuperPuTTY; settle how `WAITFOR` reads (raw or buffer text) and test SPSL scripts.
4. Reconnect banner and the `AutoReconnect*` settings; tab menu mapping.
5. Live highlighting with decorations and link providers, from the same rule files.
6. "Open as document" → the phase 1 Monaco view, with the full scrollback.
7. Session/folder option "Terminal: PuTTY / Highlighted", default PuTTY, plus FORK_CHANGES.md and manual tests:
   SSH with password, key, Pageant, keyboard-interactive (RADIUS/TACACS OTP), host-key first connect and change,
   jump host, Telnet, serial, `--More--`, `terminal length 0`, `vi`/`top` on Linux, resize, 30 open tabs (memory),
   floating windows, two monitors, high DPI.

### Phase 3 (optional)

Shared rules in the database, more platforms (Arista EOS, FortiOS, Palo Alto, MikroTik), highlighting for Mintty/cmd
tabs, and a diff of two outputs in Monaco (`show run` before and after).

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| ConPTY rewrites the stream (repaints on resize, cursor moves for spaces, `--More--` overwritten without a line break; all seen on the switch) | `WAITFOR` and logs differ from PuTTY's | Log/match from xterm.js's buffer; test SPSL scripts early in phase 2 |
| plink behaviour differs subtly from putty.exe (e.g. keyboard-interactive, Telnet negotiation, serial flow control) | Some devices behave differently | Per-session opt-in; PuTTY stays the default; test the auth methods listed above |
| WebView2 runtime missing or blocked by policy on locked-down machines | The feature can't run | Detect it and grey the option out; PuTTY still works |
| Memory with many tabs | ~30–60 MB per web tab | Shared environment; opt-in; consider suspending hidden tabs (`TrySuspendAsync`) |
| Keyboard focus and hotkeys inside WebView2, and reparenting when floating | Hotkeys stop working; a blank tab after floating | Forward accelerators; test docking like the RDP control was tested |
| Monaco's size in the installer | +several MB, hundreds of files | Trim languages and workers; harvest with WiX `heat` |
| Secrets in the page (typed passwords pass through JS) | Leaks if logged | Never log bridge traffic; devtools off in Release; `-pwfile` deleted immediately |
| Licences | Monaco and xterm.js are MIT | Add their licences to `Resources/third-party/` |

## Open decisions for the user

1. Which phase to build first. The recommendation is phase 1: it's useful on its own, low risk, and builds the
   WebView2 and highlighting pieces phase 2 reuses.
2. Approval to download `Microsoft.Web.WebView2` (NuGet), `monaco-editor` and `@xterm/xterm` / `@xterm/headless` (npm).
3. Whether rule files should live in the shared SettingsFolder (team-wide) by default, or per user.
