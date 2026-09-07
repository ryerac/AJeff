# System plugin

The built-in System plugin provides these actions:

- **Lock Computer** locks the current Windows session.
- **Sleep Computer** puts Windows to sleep immediately.
- **Run App** opens an application, file, folder, or URL, with optional arguments and a working directory.
- **Run Command** runs a command through `cmd.exe`, with an optional working directory and visible command window.
- **CPU Usage** displays live CPU activity as a percentage.
- **RAM Usage** displays the percentage of physical memory in use.

Lock and Sleep act immediately when pressed. The four command actions ignore repeat presses
on the same control for one second.

## Usage displays

Drag **CPU Usage** or **RAM Usage** from System onto a display button. Each shows
a large percentage, a label, and a small usage bar. Pressing these buttons does
nothing. Values are sampled every second; artwork updates only when the rounded
percentage changes. CPU needs two readings, so it initially shows `--`. Failed
readings also show `--` and are retried automatically.

CPU measures active time between Windows
[GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
readings, including all processes. This is activity time, so it can differ from
Task Manager's frequency-adjusted CPU display. On machines with more than 64
logical processors, this API covers the calling thread's primary processor group.
RAM is `(total physical memory - available physical memory) / total`, using
[GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex).
These are local Windows readings and require no account or additional service.

Displays update on connected devices even when another device is selected in the
editor. Existing brightness and sleep settings apply; image updates do not wake
an idle display.

## Security

Run App and Run Command execute with the same Windows permissions as AJeff. Only
configure targets and commands you trust. Action parameters are stored in AJeff's
local bindings file, so do not place passwords, API keys, or other secrets in
commands or arguments.
