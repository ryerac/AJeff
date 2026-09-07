# Features in progress

Agreed feature backlog for AJeff. Checked implementation items are complete;
unchecked acceptance checks still need verification.
Keep additions focused on everyday use in a simple, portable application with
local settings and no required account or cloud service.

## Copy/paste and keyboard-accessible device editing

Status: Implemented; end-to-end keyboard and screen-reader verification pending.

- [x] Copy and paste a control's configuration, including its bindings, action
  parameters, and static or state-aware artwork.
- [x] Support Ctrl+C and Ctrl+V when a device control has keyboard focus, without
  intercepting normal copy/paste in text fields.
- [x] Allow keyboard navigation between device controls, including empty controls,
  with a clearly visible focus indicator.
- [x] Allow selecting a control and reaching its action and appearance settings
  without a mouse.
- [x] Give controls meaningful accessible names for screen readers.
- [x] Prevent incompatible pastes between control types and make replacement of
  an existing configuration clear to the user.

Copy/paste uses an in-memory AJeff clipboard, including across scenes and devices;
action parameters are not put on the Windows clipboard. Enter opens the selected
control's action editor; presets can also be applied with Enter. The editor
scrolls to keep settings reachable in smaller windows.

Acceptance checks:

- [ ] Configure a control using only the keyboard, then copy it to another
  compatible control and verify its actions and artwork.
- [x] Editing the pasted control does not change the original (automated).
- [ ] Verify focus order, visible focus, accessible names, and ordinary text-field
  copy/paste.

## Display management

Status: Implemented under Settings; physical AKP03E verification pending.

- [x] Add a brightness level control, preferably a slider with a visible numeric
  value.
- [x] Preview brightness as the slider moves; save to keep it, or restore the
  saved brightness when closing Settings or selecting another device.
- [x] Add an optional sleep setting with a configurable timeout in minutes.
- [x] Offer two sleep behaviours: **Dim to lowest** and **Screen off**.
- [x] Restore the configured awake brightness when the device wakes.
- [x] Consume the first press that wakes a dimmed or sleeping device: it must not
  execute its assigned action or change scene. Subsequent presses work normally.
- [x] Persist display preferences locally and preserve them across restarts,
  device reconnection, Windows suspend/resume, and button artwork updates.
- [x] Validate the minutes value and disable timeout/behaviour controls when
  optional sleep is disabled.

Implementation decisions:

- Inactivity means no button presses, dial presses, or dial turns on that deck.
- Display settings are saved per device. Defaults: 80% brightness, sleep disabled,
  15-minute timeout, dim behaviour. Timeouts accept 1–1440 minutes.
- Saving applies brightness and starts a fresh inactivity interval; disabling
  sleep restores the awake display. Reconnection and Windows resume also start
  a fresh interval. Idle checks run with the device scan, approximately every two seconds.
- Dial turns wake the device and are consumed too.
- Dim uses 1% brightness. The lowest visible level still needs hardware validation.
- The current protocol has no release event. Repeat reports from the waking
  control are suppressed until a 500 ms quiet interval has passed. A rapid second
  press on that same control can therefore be suppressed too; verify held-button
  and dial-repeat timing on hardware.

Display output is serialized with sleep/wake handling. Artwork updates are cached
while idle and restored on wake, without changing brightness or waking the panel.

Acceptance checks:

- [ ] Verify brightness changes and both idle sleep behaviours on an AKP03E.
- [ ] Verify the configured timeout and that disabling sleep prevents idle sleep.
- [ ] Wake with a button or dial press and confirm no bound action runs, including
  repeat events belonging to that initial press.
- [ ] Verify subsequent input works and the configured brightness is restored.
- [ ] Verify dynamic icon updates, reconnection, restart, and Windows
  suspend/resume preserve the intended display behaviour.

Automated validation:

- [x] Core suite: 38 passing tests, including idle timing, both sleep modes,
  brightness restoration, cached artwork, waking input/repeats, and failed wake.
- [x] Application suite: 11 passing tests, including independent copied bindings
  and artwork, incompatible paste targets, settings persistence, and WPF display
  settings and automation peers, plus usage calculations and live Windows readings.
  Application tests also run in CI.

See [editing and display settings](docs/editing-and-display.md) for usage.

## Image plugin

Status: Implemented using the existing static artwork pipeline.

- [x] Bundle an Image plugin and preset for display buttons.
- [x] Choose artwork using Library or Upload Icon; pressing the button does nothing.
- [x] Reuse local icon storage, scenes, copy/paste, and display management.

GIF files can be uploaded as still images. Animated GIF playback is not currently
supported; it would require retaining and scheduling animation frames rather
than converting uploads into one JPEG.

## CPU and RAM usage

Status: Implemented in the System plugin.

- [x] CPU Usage and RAM Usage presets for display buttons.
- [x] Live percentage, metric label, and usage bar; pressing does nothing.
- [x] Sample once per second and redraw when the displayed percentage changes.
- [x] Show `--` while waiting for CPU's first interval or when a reading fails.
- [x] Keep updates working on all connected devices, with existing idle sleep behaviour.
- [ ] Verify readability and live updates on the physical deck.

## Device simulation

Status: JSON-backed multi-device UI simulator implemented.

- [x] Load simulated models from `Assets/Simulation/simulated-devices.json`.
- [x] Persist an **Enable simulator tools** application setting for normal launches.
- [x] Add and remove simulated devices from the device list while AJeff is running.
- [x] Include software-only AJAZZ AKP153 and AKP03E layouts.
- [x] Launch with `--simulate-akp153` or `--simulate=<catalogue-id>`.
- [x] Provide a VS Code launch profile named **AJeff (Simulated AKP153)**.
- [x] Support scenes, bindings, presets, icons, copy/paste, and simulated presses.
- [x] Never register a USB VID/PID or send HID/display commands for the simulator.
- [ ] Confirm AKP153 firmware variant, per-key image geometry, transforms, reports,
  and commands on physical hardware before adding real support.
