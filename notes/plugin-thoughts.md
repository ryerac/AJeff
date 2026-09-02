# Plugin Thoughts

App-specific actions, state sources, and icon packs should not all become part of
JeffDock Core. A plugin boundary would let integrations such as VS Code, OBS, and
Spotify extend JeffDock while keeping the device and application layers focused.

## Proposed Architecture

```text
JeffDock
  Core: HID and device protocols
  App: UI, bindings, scenes, and presentation
  Plugin contracts: stable extension interfaces
  Plugins: application-specific integrations
```

A plugin should be able to register actions, state sources, and icon packs:

```csharp
public interface IJeffDockPlugin
{
    string Id { get; }
    string DisplayName { get; }

    void Register(IJeffDockPluginRegistry registry);
}

public interface IJeffDockPluginRegistry
{
    void AddAction(IDeckAction action);
    void AddStateSource(IDeckStateSource stateSource);
    void AddIconPack(IIconPack iconPack);
}
```

Plugins should register capabilities with JeffDock rather than directly accessing
WPF controls, binding JSON, HID sessions, or device profiles. JeffDock remains
responsible for presentation, persistence, scenes, and physical-device updates.

## Namespaced IDs

Plugin-provided identifiers should be stable and namespaced:

```text
vscode.editor.format-document
vscode.editor.toggle-terminal
vscode.debugger.state
vscode.workspace.state
```

This allows existing binding and dynamic-icon models to reference plugin features
without introducing app-specific fields:

```json
{
  "ActionId": "vscode.editor.toggle-terminal",
  "Icon": {
    "Mode": "Dynamic",
    "StateSourceId": "vscode.debugger.state"
  }
}
```

## Plugin Packaging

Installed plugin binaries should live under local application data rather than the
roaming bindings directory:

```text
%LocalAppData%\JeffDock\Plugins\
  vscode\
    plugin.json
    JeffDock.Plugin.VSCode.dll
    Icons\
      pack.json
      Editor\
      Debugger\
```

JeffDock may also have an application-directory plugin location for bundled
plugins.

An example manifest:

```json
{
  "id": "jeffdock.vscode",
  "name": "Visual Studio Code",
  "version": "1.0.0",
  "apiVersion": 1,
  "entryAssembly": "JeffDock.Plugin.VSCode.dll",
  "entryType": "JeffDock.Plugin.VSCode.VSCodePlugin"
}
```

## Startup Discovery

At startup JeffDock should:

1. Scan the defined bundled and user plugin directories only.
2. Read and validate manifests before loading assemblies.
3. Check plugin IDs, versions, API compatibility, and duplicate registrations.
4. Load enabled plugins.
5. Allow each plugin to register actions, state sources, and icon packs.
6. Log and skip an invalid plugin without preventing JeffDock from starting.

Do not search the whole machine for plugins. Initially, loading changes can require
an application restart; safe live unloading is complicated by event subscriptions,
timers, and plugin-owned resources.

## Loading Options

Possible approaches:

- Compile-time modules are simplest but still make every integration part of the
  main application distribution.
- In-process .NET plugins offer a straightforward initial extension model.
- Out-of-process plugins provide stronger isolation but require an IPC protocol and
  process lifecycle management.

The recommended starting point is trusted in-process .NET plugins loaded through a
dedicated `AssemblyLoadContext`, with per-plugin dependency isolation where
practical. Third-party or untrusted integrations could eventually use an
out-of-process plugin host.

## VS Code Example

A VS Code plugin could provide:

```text
Actions:
  vscode.editor.format-document
  vscode.editor.toggle-terminal
  vscode.workspace.next-editor

State sources:
  vscode.debugger.state -> running / paused / stopped
  vscode.workspace.state -> dirty / clean

Icons:
  vscode/editor/format
  vscode/debug/running
  vscode/debug/paused
```

Simple actions may use the VS Code command-line interface. Reliable live editor,
workspace, and debugger state will probably require a small VS Code extension that
communicates with the JeffDock VS Code plugin over a local named pipe or socket:

```text
VS Code extension
        <-> local IPC
JeffDock VS Code plugin
        -> IDeckStateSource events
        -> dynamic GUI and dock icons
```

The extension reports application state, the plugin translates it into JeffDock's
generic state-source model, and the existing dynamic-icon coordinator handles the
result.

## Open Decisions

- Define the smallest stable plugin contracts assembly.
- Decide how users enable, disable, install, and remove plugins.
- Establish plugin trust and signing expectations.
- Decide whether icon packs are folders, assembly resources, or both.
- Define logging and diagnostics exposed to plugin authors.
- Define API-version compatibility and migration policy.
- Determine when an integration must run out of process.
