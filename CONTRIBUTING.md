# Contributing

This repository is a Windows-first C# application for working with AJAZZ deck devices without vendor software.

The project currently includes:

- a reusable core HID and protocol layer
- a WPF desktop app for device discovery, raw input monitoring, and basic control binding
- an initial AKP03E profile implementation

This guide is intended to help contributors get productive quickly.

## Scope

Current goals:

- detect supported AJAZZ devices on Windows
- parse raw HID input through model-specific profiles
- render a model-driven faceplate in the WPF app
- allow per-device control bindings
- execute a small set of local actions such as volume adjustment and mute toggle

Non-goals for now:

- vendor software compatibility
- cross-platform support
- broad macro automation beyond the current binding system

## Repository Layout

- `JeffDock.Core/`: device, protocol, layout, and monitoring logic
- `JeffDock.Cli/`: console-based device diagnostic harness
- `JeffDock.App/`: WPF application on top of the core layer
- `JeffDock.Core.Tests/`: focused core tests
- `nuget.config`: repo-local NuGet source config

Important core files:

- `JeffDock.Core/Deck/IDeckProtocolProfile.cs`: per-device protocol contract
- `JeffDock.Core/Deck/DeckProfileCatalog.cs`: registry of supported deck profiles
- `JeffDock.Core/Deck/HidDeckDiscovery.cs`: shared HID discovery and connection open logic
- `JeffDock.Core/Deck/HidDeckDeviceService.cs`: single-device runtime listener
- `JeffDock.Core/Deck/DeckMonitorService.cs`: multi-device monitor used by the WPF app
- `JeffDock.Core/Deck/DeckLayoutDefinition.cs`: model-driven visual layout metadata
- `JeffDock.Core/Akp03e/Akp03eProfile.cs`: current AKP03E implementation

Important app files:

- `JeffDock.App/MainWindow.xaml`: main desktop UI
- `JeffDock.App/MainWindow.xaml.cs`: device selection, faceplate rendering, binding editor, runtime event handling
- `JeffDock.App/Bindings/DeckBindingStore.cs`: persisted bindings
- `JeffDock.App/Bindings/DeckActionExecutor.cs`: action execution for live device input

## Environment Requirements

- Windows
- .NET SDK 10.0+
- a supported AJAZZ deck for live validation

## Build And Run

Use the repo-local NuGet config. This is required because some environments have a global private feed configured that fails restore.

Build the app:

```powershell
dotnet build .\JeffDock.App\JeffDock.App.csproj --configfile .\nuget.config
```

Build the core only:

```powershell
dotnet build .\JeffDock.Core\JeffDock.Core.csproj --configfile .\nuget.config
```

Run the WPF app:

```powershell
dotnet run --project .\JeffDock.App\JeffDock.App.csproj --configfile .\nuget.config
```

Run the console diagnostic app:

```powershell
dotnet run --project .\JeffDock.Cli\JeffDock.Cli.csproj --configfile .\nuget.config
```

Run core tests:

```powershell
dotnet restore .\JeffDock.Core.Tests\JeffDock.Core.Tests.csproj --configfile .\nuget.config
dotnet test .\JeffDock.Core.Tests\JeffDock.Core.Tests.csproj --no-restore
```

## Architecture Rules

Keep these boundaries intact:

- `JeffDock.Core` should own HID, protocol parsing, device identity, layout metadata, and monitoring.
- `JeffDock.App` should own UI, persisted user bindings, and invoking user-facing actions.
- device-specific knowledge belongs in profile classes, not in generic HID services.
- visual layout belongs in profile metadata, not as hardcoded WPF geometry for one model.

If you add support for another AJAZZ deck, prefer this shape:

1. add a new profile implementing `IDeckProtocolProfile`
2. define its `VendorId`, `ProductId`, `PreferredInputPacketLength`, `Layout`, and parser
3. register it in `DeckProfileCatalog`
4. validate it in the WPF app without changing generic HID plumbing

## Device Identity

The code currently prefers a stable device id based on:

- vendor id
- product id
- HID serial number when available

If a device does not expose a serial number, the fallback identity includes the HID device path.

Be careful when changing this: binding persistence depends on the device id remaining stable enough across runs.

## Current Binding Model

The WPF app currently supports:

- selecting a control from the rendered faceplate
- assigning actions for knob turn, knob press, or button press
- saving bindings per device in `%AppData%\JeffDock\bindings.json`

The built-in action set includes:

- `None`
- `VolumeAdjust`
- `ToggleMute`
- `ToggleMicrophoneMute`
- media keys
- keyboard shortcuts
- scene navigation

Additional actions and state sources can be supplied by plugins.

If you extend this, keep action modeling explicit and typed. Avoid turning bindings into stringly-typed ad hoc payloads too early.

## UI Notes

The faceplate is rendered dynamically from `DeckLayoutDefinition`.

That means:

- do not reintroduce AKP03E-only control geometry directly into XAML
- if a new device needs a different layout, add it in the profile metadata
- selection and highlighting should work from control type/index, not control names

## Testing Guidance

The current tests are intentionally focused and small.

Good places to add tests:

- protocol parsing for known action codes
- shifted and unshifted HID packet layouts
- layout metadata integrity for device profiles
- monitor service behavior when devices connect/disconnect

If you are making a parser or identity change, tests should be updated in the same change.

## Contribution Style

- keep changes narrow and intentional
- prefer fixing root causes over patching symptoms
- avoid mixing large UI redesigns with protocol or HID refactors
- avoid duplicating HID/session lifecycle code in multiple services
- preserve Windows-first assumptions unless the repository direction changes

## Pull Request Expectations

- explain the user-visible effect
- explain which layer changed: profile, core HID, monitor, binding model, or UI
- note any required manual validation with real hardware
- include build and test status when relevant
