# SuperXPuTTY Terminal Manager

SuperXPuTTY is a Windows application which manages PuTTY/KiTTY terminals. It supports multiple protocol types such as RDP, VNC, serial, SSH, Telnet by using third party apps.

![SuperPuTTY v2.0.0.0 screenshot](/screenshot.png?raw=true)

This repository is a fork of jimradford's SuperPuTTY which aims to fix some old issues and improve the overall quality of life.

## Latest release

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/SilverGreen93/superputty)](https://github.com/SilverGreen93/superputty/releases/latest) [![Downloads (Latest Release)](https://img.shields.io/github/downloads/SilverGreen93/superputty/latest/total?label=latest%20release%20downloads)](https://github.com/SilverGreen93/superputty/releases/latest) ![GitHub all releases](https://img.shields.io/github/downloads/SilverGreen93/superputty/total?label=total%20downloads)

All releases are available for download at https://github.com/SilverGreen93/superputty/releases

Documentation is available at https://github.com/jimradford/superputty/wiki/Documentation

**Note!** PuTTY is mandatory for the application to work. The recommended version of PuTTY is [PuTTY Plus](https://github.com/SilverGreen93/putty/), which adds some more extra-features to the experience.

## Features

- Dark theme (2 variants), using [Dark Mode Forms](https://github.com/BlueMystical/Dark-Mode-Forms).
- Supports protocols: Telnet, Rlogin, VNC, SSH, Serial, RDP, MinTTY, WSL, CMD, PS
- Start/stop X11 server right from the applicaton.
- Support for Windows Subsystem for Linux hosts.
- Quickly connect to hosts using the connect bar.
- Quickly send commands to all terminals.
- Save and restore different layouts.
- Import existing sessions from various software such as MobaXterm.
- Snappy tabbed interface based on VS2015 interface.
- Simple scripting language for basic tasks.
- Lots of bugs fixed and quality of life features added compared to the original SuperPutty.

## Building and issue tracking

[![Build status](https://ci.appveyor.com/api/projects/status/github/SilverGreen93/superputty?svg=true)](https://ci.appveyor.com/project/SilverGreen93/superputty)

The project was built with Visual Studio 2022.

To request new features or report any found issues, use the Issues tab.

### Making a release of this fork

These steps build the installer and a portable zip, then publish them as a GitHub release of [waukeerules/superputty](https://github.com/waukeerules/superputty/releases).

**1. Set the version.** Edit `SuperPutty\SuperPutty.csproj` and raise `Version` and `InformationalVersion`, e.g. `3.0.0.0` → `3.0.1.0`. The installer takes its version from the exe, and only upgrades an existing install when the version changes. Commit and push the change.

**2. Build Release.** This needs the .NET 10 SDK, and CMake with a C compiler for `putty-embed.dll` (run `git submodule update --init` once). Close any SuperXPuTTY running from the Release folder first. In PowerShell, from the repository folder:

```powershell
dotnet build SuperPutty.sln -c Release -v:minimal
```

This builds:
- the program: `SuperPutty\bin\x64\Release\net10.0-windows\win-x64\SuperXPuTTY.exe`
- the installer: `SuperPuttyInstaller\bin\x64\Release\SuperXPuTTYSetup.msi`

Both need the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) on the machine they run on; the installer checks for it.

If only `License.rtf` changed, the installer isn't rebuilt: it doesn't count the file as an input. Add `-t:SuperPuttyInstaller:Rebuild` to the command to rebuild it.

**3. Make the portable zip.** Don't publish the exe on its own: it needs the DLLs, `SuperXPuTTY.dll.config` and the `.json` files beside it.

```powershell
Compress-Archive -Path SuperPutty\bin\x64\Release\net10.0-windows\win-x64\* -DestinationPath SuperXPuTTY-2.1.3.0-portable.zip -Force
```

**4. Publish the release.** This needs the [GitHub CLI](https://cli.github.com/) (`gh`), signed in with `gh auth login`. Use the version from step 1:

```bash
gh release create v2.1.3.0 SuperPuttyInstaller/bin/x64/Release/SuperXPuTTYSetup.msi SuperXPuTTY-2.1.3.0-portable.zip --repo waukeerules/superputty --target master --title "SuperXPuTTY 2.1.3.0" --generate-notes --draft
```

- **The command** tags master with the version, creates the release and uploads both files.
- **`--generate-notes`** lists the commits since the previous tag; use `--notes-file notes.md` to write your own instead.
- **`--draft`** keeps the release hidden. Check it on GitHub, then press **Publish release** there, or run `gh release edit v2.1.3.0 --repo waukeerules/superputty --draft=false`.
- **The zip** can stay in the folder: `.gitignore` excludes `SuperXPuTTY-*.zip`, so it's never committed.

## License

MIT

For additional License information please read the License.txt included with the download
