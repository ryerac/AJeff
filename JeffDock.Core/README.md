# JeffDock.Core

Windows-first, C#-native AJAZZ deck controller starter.

Current scope:

- Detect supported AJAZZ decks over USB HID
- Parse knob input through protocol profiles
- Map large top knob (encoder index 1 on AKP03E) to system volume
- Map top knob press to mute toggle

## Requirements

- Windows
- .NET SDK 10.0+
- A supported AJAZZ deck connected over USB

## Run

```powershell
dotnet run --project .\JeffDock.Core.csproj --configfile ..\nuget.config
```

You should see:

- `Device connected: <profile name>` once attached
- Event lines while turning/pressing knobs

## Architecture

- `Deck/IDeckProtocolProfile.cs`: contract per deck model (VID/PID, optional init packet, parser)
- `Deck/HidDeckDeviceService.cs`: generic HID connector/reader that tries all registered profiles
- `Akp03e/Akp03eProfile.cs`: AKP03E implementation of the profile contract

## Adding Another AJAZZ Model

1. Create a new profile class implementing `IDeckProtocolProfile`.
2. Set its `VendorId` and `ProductId`.
3. Implement `Parse(...)` for that model's action codes.
4. Add the profile to the `profiles` array in `Program.cs`.

No HID loop rewrite needed when adding another model.

## Notes

- Current included profile: AKP03E (`0x0300/0x3002`).
- This is intentionally minimal and stable as a foundation for a GUI app.
- Next step can be a WinUI 3 or WPF front-end reusing the same services.
