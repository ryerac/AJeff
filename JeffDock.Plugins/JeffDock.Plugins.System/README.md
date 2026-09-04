# System plugin

The built-in System plugin provides four actions:

- **Lock Computer** locks the current Windows session.
- **Sleep Computer** puts Windows to sleep immediately.
- **Run App** opens an application, file, folder, or URL, with optional arguments and a working directory.
- **Run Command** runs a command through `cmd.exe`, with an optional working directory and visible command window.

Lock and Sleep act immediately when pressed. All four actions ignore repeat presses
on the same control for one second.

## Security

Run App and Run Command execute with the same Windows permissions as AJeff. Only
configure targets and commands you trust. Action parameters are stored in AJeff's
local bindings file, so do not place passwords, API keys, or other secrets in
commands or arguments.
