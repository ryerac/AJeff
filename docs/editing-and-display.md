# Editing controls and display settings

## Copy/paste and keyboard editing

Tab into the device view, then use arrow keys to select a button or dial. The
focused control has a highlighted outline. Enter or Space moves to its action
editor; Tab and Shift+Tab reach the remaining settings, including artwork.
The settings area scrolls when needed.

With a device control focused:

- **Ctrl+C** copies its actions, parameters, and custom artwork.
- **Ctrl+V** pastes onto a compatible control, asking before replacing an existing
  configuration. Buttons paste onto buttons and dials onto dials. A configuration
  with artwork requires a control with a display.
- **Delete** offers to reset the selected control.
- **Shift+F10** opens the copy/paste context menu, also available by right-click.

Copies are snapshots: editing either control afterwards does not change the
other. You can paste across scenes and connected devices. The copied
configuration stays inside AJeff until it closes; it does not go onto the Windows
clipboard. Text fields retain normal Windows copy/paste behaviour.

To apply a preset with the keyboard, first select the destination control, then
Tab to the preset and press Enter or Space.

## Display settings

Open **Plugins & Settings…**, choose a device under **Display**, and configure:

- **Brightness:** 1–100%, default 80%.
- **Sleep display after deck inactivity:** optional, off by default. Enter a
  timeout of 1–1440 minutes (default 15).
- **Sleep behaviour:** **Dim to lowest** (1% brightness) or **Screen off**.

The brightness slider previews changes immediately on the device. Closing Settings
or selecting another device restores the saved brightness if you have not saved.
Choose **Save display settings** to keep the brightness and apply the other choices for that device.
Saving starts a fresh inactivity interval and wakes an idle display. Settings
are retained across application restarts and device reconnections.

Inactivity is measured from button presses, dial presses, and dial turns on the
deck, independently of mouse and keyboard activity on the PC. Idle checks occur
approximately every two seconds. Dynamic icon updates do not reset the timeout
or wake the display; the latest artwork is restored when it wakes.

The first button press, dial press, or dial turn wakes a dimmed or sleeping
display without executing its action. Because the current input protocol does
not report releases, AJeff also suppresses reports from that same waking control
until there has been a 500 ms quiet interval. A very quick second press on that
control may therefore be ignored. Other controls remain usable.

Windows suspend turns displays off. Resume restores their configured brightness
and starts a fresh inactivity interval.

The 1% dim level, physical screen-off wake behaviour, and repeat-report timing
still need verification on an AKP03E; automated tests cover the software logic.
