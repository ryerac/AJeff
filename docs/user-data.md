# User data

AJeff is installation-free, but it deliberately stores user configuration
outside the application directory so replacing `AJeff.exe` does not erase it.

## Roaming application data

`%APPDATA%\JeffDock` contains device bindings, scenes, action parameters, and
uploaded or generated button icons. AJeff creates this directory automatically
when it starts.

The Pi-hole URL and application password are action parameters and are currently
stored in `bindings.json` as plain text. Use a dedicated Pi-hole application
password rather than the web-interface password.

## Local application data

`%LOCALAPPDATA%\JeffDock\PluginSettings` contains plugin-level settings and is
created when settings are first saved.

`%LOCALAPPDATA%\JeffDock\application-settings.json` contains application-level
preferences such as whether automatic launches start minimised.

`%LOCALAPPDATA%\JeffDock\Plugins` is the optional external-plugin location. It
is inspected when AJeff starts but is not created automatically.

When **Start AJeff when I sign in to Windows** is enabled, AJeff also creates an
`AJeff` value beneath the current user's
`Software\Microsoft\Windows\CurrentVersion\Run` registry key. Disabling the
setting removes that value. If **Start minimised** is enabled, the registered
command includes the `--minimized` argument. Moving `AJeff.exe` requires saving
the startup setting again so the registered path is updated.

Back up the relevant `JeffDock` directories before manually editing or removing
configuration files.
