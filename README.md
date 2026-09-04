# AJeff

AJeff is a community-built Windows desktop app for configuring AJAZZ macro pads
without relying on the vendor application.

The name is a small chain of wordplay: AJAZZ → jazz → Jazzy Jeff → AJeff.  Pointlessly stupid names is the best bit of any new project.

It discovers supported devices over USB HID, presents their controls in a visual
editor, and lets you turn buttons and dials into useful desktop actions.

> [!IMPORTANT]
> AJeff is an early-stage community project. Expect rough edges and changes to
> settings, plugin APIs, and device support while the project matures.

> [!IMPORTANT]
> Zero warranty is implied, this was heavily human instructed, but essentially implemented by AI. 
> It simply renders data over HID and whilst it works with my device, I take no responsibility for you breaking your own devices.

## What it can do

- Detect supported AJAZZ devices over USB HID
- Configure buttons, dial presses, and dial turns
- Control speaker volume and speaker or microphone mute state
- Send media keys and keyboard shortcuts
- Organise layouts into named scenes and switch between them from the device
- Choose bundled icons, recolour SVG icons, or upload your own artwork
- Show state-aware icons for actions such as mute
- Extend the action palette through plugins

AJeff stores bindings and settings locally. Its built-in actions do not require
an online account or cloud service.

## Supported hardware

The currently included device profile is:

- AJAZZ AKP03E (`VID 0x0300`, `PID 0x3002`)

Other models may use different USB protocols even when they look similar. They
need their own tested device profile before they can be considered supported.

## Included plugins

AJeff currently bundles a small set of trusted, in-process plugins:

- **Mouse Mover** — Simplistic mouse jiggle. Periodically nudges the pointer
- **Timer** — provides timer actions and visual state
- **Fun** — provides a simple built-in game helpers - Dice roller (D6 only at time of writing) and a coin flip.
- **[Pi-hole Monitor](JeffDock.Plugins/JeffDock.Plugins.PiHole/README.md)** — monitors Pi-hole v6 availability and blocked-query counts

Plugins run with the same permissions as AJeff. Currently all plugins are part of this repo (Rather than sub-repos), although nothing is stopping someone writing their own

## Project status

AJeff is being prepared for its first "release" (In the loosest sense possible). Packaging, installation,
upgrade instructions, and a formal release process are still to come.

The application currently targets Windows and .NET 10. Hardware behaviour should
be treated as model-specific; reports from real devices are especially valuable.

## Contributing

Bug reports, device protocol findings, focused fixes, and new device profiles are
welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup and
project structure.

When reporting a hardware issue, include the exact model name and USB VID/PID if
possible. Avoid posting device serial numbers or other personal information.

## Acknowledgements

The bundled Elgato icon set comes from
[elgatosf/icons](https://github.com/elgatosf/icons) and is distributed under its
MIT licence. Its pinned version and licence text are included with the assets.

AJeff is an independent community project and is not affiliated with or
endorsed by AJAZZ or Elgato.
