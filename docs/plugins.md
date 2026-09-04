# Plugins

AJeff has two plugin categories.

## Official plugins

Mouse Mover, Timer, Fun, and Pi-hole Monitor are compiled into `AJeff.exe`.
Their implementation files and manifests are not distributed alongside the
application. Their settings remain editable through **Plugins & Settings**.

The Pi-hole icon is also embedded in the application. See the
[Pi-hole Monitor guide](../JeffDock.Plugins/JeffDock.Plugins.PiHole/README.md)
for configuration and credential guidance.

## External plugins

Optional third-party plugins can be placed beneath:

```text
%LOCALAPPDATA%\JeffDock\Plugins
```

An external plugin retains its own DLL, `plugin.json`, and any assets. External
code plugins run in-process with the same permissions as AJeff. An external
plugin cannot replace a loaded official plugin by reusing its ID.
