# AJeff

![AJeff logo](docs/images/logo.png)

[![CI](https://github.com/ryerac/AJeff/actions/workflows/ci.yml/badge.svg)](https://github.com/ryerac/AJeff/actions/workflows/ci.yml)

AJeff is a small Windows portable app for configuring AJAZZ macro pads without
relying on the vendor application. A project born out of an evening of simply not wanting to use the official software. 
For full transparency, this app was heavily human-led, but coded using Codex and similar agents.

It discovers supported devices over USB HID, presents their controls in a visual
editor, and lets you turn buttons and dials into useful desktop actions.

The aim is **deliberately simple**: provide the functionality needed for everyday
use in one auditable, portable application, without requiring an account, cloud
service, marketplace, or wider software ecosystem.

That narrow scope will not suit everyone. For Linux, broader device coverage,
or a more extensible Stream Deck-compatible environment, consider
[OpenDeck](https://github.com/nekename/OpenDeck) or the Linux-focused
[OpenDeck AJAZZ fork](https://github.com/mistweaverco/opendeck-ajazz). For more
advanced automation, live telemetry, multiple hosts, and a wider plugin
ecosystem, see [JIB — Jack In the Box](https://androme13.github.io/JIB/) which may suit your needs much better than this.

The name is a small chain of wordplay: AJAZZ → jazz → Jazzy Jeff → AJeff.  Pointlessly stupid names is the best bit of any new project.


![AJeff main window](docs/images/ajeff-main-window.png)

> [!IMPORTANT]
> AJeff is an early-stage community project. Expect rough edges and changes to
> settings, plugin APIs, and device support while the project matures.

> [!IMPORTANT]
> Zero warranty is implied, this was heavily human instructed, but essentially implemented by AI. 
> It simply renders data over HID and whilst it works with my device, I take no responsibility for you breaking your own devices.

## What it can do

- Configure buttons, dial presses, and dial turns.
- Assign audio, media, keyboard, system, and scene actions.
- Create multiple scenes and switch between them from the device.
- Use bundled, custom, recoloured, and state-aware icons.
- Copy, paste, reset, and keyboard-edit controls.
- Manage brightness and automatic display sleep.
- Monitor live CPU, RAM, and Pi-hole statistics.
- Use timers, mouse movement, images, dice, and coin-flip plugins.
- Simulate supported layouts without physical hardware.
- Store everything locally without an account or cloud service.

Bindings and icons are stored under `%APPDATA%\JeffDock`; application, display,
and plugin settings are stored under `%LOCALAPPDATA%\JeffDock`.

## Supported hardware

Windows only.

The currently included device profile is:

- AJAZZ AKP03E (`VID 0x0300`, `PID 0x3002`)

Other models may use different USB protocols even when they look similar. They
need their own tested device profile before they can be considered supported.
If you have an AJAZZ, please feel free to create a discussion so we can add the profile! Heck, it may even work with other chinese-marketplace pads which operate in the same way!

For UI development without another physical device, open **Settings**, enable
**Enable simulator tools**, and save the application settings. **Add simulated...**
then appears below the device list and opens the model menu. The bundled JSON
catalogue contains AKP153 and AKP03E layouts; **Remove simulated** removes the
selected simulator for the current run. Simulators never open a USB device or
send HID commands. Right-click a simulated control to fire its press or encoder
action.

## Included plugins

AJeff currently bundles a small set of trusted, in-process plugins:

- **[Image](JeffDock.Plugins/JeffDock.Plugins.Image/README.md)** — displays still artwork without a press action; GIF imports are static.
- **Mouse Mover** — toggles configurable periodic pointer movement and shows its current state.
- **Timer** — runs a configurable countdown with live button artwork.
- **Fun** — rolls a six-sided die or flips a coin and displays the result.
- **[Pi-hole Monitor](JeffDock.Plugins/JeffDock.Plugins.PiHole/README.md)** — shows Pi-hole v6 availability and blocked-query counts.
- **[System](JeffDock.Plugins/JeffDock.Plugins.System/README.md)** — displays CPU and RAM usage, locks or sleeps Windows, and launches apps, files, folders, URLs, or commands.

Plugins run with the same permissions as AJeff. The included plugins live in this
repository; external plugins can also be loaded from `%LOCALAPPDATA%\JeffDock\Plugins`.

## Project status

AJeff supports the core workflow: detecting a compatible device, assigning
actions, managing scenes, updating button artwork, and loading plugins. Its
interface now uses WPF UI with a themed Fluent shell, while further visual and
accessibility refinement remains in progress. Expect rough edges and changes
while the interaction model is refined.

The application currently targets Windows and .NET 10. Hardware behaviour should
be treated as model-specific; reports from real devices are especially valuable.
No plans to target Linux currently, as there are already options for that platform.

## Roadmap

The current priorities are:

- Improve the visual design and general usability of the editor
- Make device setup, action assignment, and plugin configuration clearer
- Harden portable releases, upgrades, diagnostics, and error handling
- Expand the built-in action and plugin selection
- Add support for more devices as their USB protocols can be tested
- Stabilise configuration formats and the plugin API as the project matures

This roadmap describes the intended direction rather than committed release
dates or guarantees.

## Get AJeff

> [!IMPORTANT]
> Fully close the AJAZZ software before starting AJeff. The vendor application
> otherwise retains ownership of the device connection and prevents AJeff from
> connecting to the macro pad.

Download `AJeff.exe` from the
[latest GitHub Release](https://github.com/ryerac/AJeff/releases/latest). AJeff is
portable: place the executable wherever you want and run it without an
installer or a separate .NET installation. Release downloads also include a
SHA-256 checksum file for verifying the executable.

Alternatively, install the .NET 10 SDK, clone this repository, and build the
same self-contained Windows x64 executable locally:

```powershell
dotnet publish .\JeffDock.App\JeffDock.App.csproj -p:PublishProfile=win-x64-single-file
```

The locally built `AJeff.exe` is written beneath
`JeffDock.App\bin\Release\net10.0-windows\win-x64\publish`. It includes the .NET
runtime and all official plugins. Optional third-party plugins can be placed in
`%LOCALAPPDATA%\JeffDock\Plugins`.

## Documentation

Additional guides are collected in the [documentation index](docs/README.md),
including plugin packaging and user-data locations.


## FAQ

### Is the interface finished?
Not yet. AJeff now uses WPF UI and a replaceable theme system, but the visual
refresh is being delivered in stages while the interaction model settles.

### Docs/Guides?
Not yet, its fairly self explanitory but a proper guide is on the cards if anyone actually uses this.


## Contributing

Bug reports, device protocol findings, focused fixes, and new device profiles are
welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup and
project structure.

When reporting a hardware issue, include the exact model name and USB VID/PID if
possible. Avoid posting device serial numbers or other personal information.

Anyone with a real device that operates in the same way, feel free to aid in adding real device configuration options!

## Acknowledgements

The bundled Elgato icon set comes from
[elgatosf/icons](https://github.com/elgatosf/icons) and is distributed under its
MIT licence. Its pinned version and licence text are included with the assets.
Also inspired by me finding https://github.com/tomekceszke/ajazz-akp03 by  [@tomekceszke](https://www.github.com/tomekceszke) which did some of the hard work of mapping out some key codes.

AJeff is an independent community project and is not affiliated with or
endorsed by AJAZZ or Elgato.
