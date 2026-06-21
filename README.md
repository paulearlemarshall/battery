# BatteryTray

BatteryTray is a lightweight Windows tray application for monitoring battery status and nearby system diagnostics. It shows a live, color-coded battery percentage in the notification area, exposes diagnostics from the tray menu, and includes a battery history chart.

The app is written in C# with WinForms and builds with the .NET Framework compiler that ships with Windows/.NET Framework installations. No project file or external package restore is required.

## Features

- Live tray icon showing battery percentage.
- Color-coded icon states for normal, low, critical, charging, and unavailable battery data.
- Right-click tray menu with battery, system, process, display, network, and power-plan diagnostics.
- Double-click tray icon to open the battery history chart.
- Battery chart windows for the last 1 hour, 6 hours, or 24 hours.
- Estimated time to full or empty based on recent battery trend.
- Low battery, critical battery, and full battery notifications.
- Configurable tray icon size and update interval.
- Optional sound alerts.
- Optional "Start with Windows" registry toggle.
- One-click diagnostics copy to clipboard.
- Windows battery report generation through `powercfg`.
- Optional debug logging with log rotation.

## Screens and Interaction

- Left/double-click the tray icon to open the chart.
- Right-click the tray icon to open diagnostics and settings.
- Click an informational menu item to copy its value to the clipboard.
- Use `Battery Chart` in the menu to choose the chart time window.
- Use `Diagnostics` in the menu to open the config folder or log file.

## Requirements

- Windows.
- .NET Framework 4.x compiler at:

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

Most Windows machines with .NET Framework installed already have this compiler. The app uses WinForms, WMI, Performance Counters, and Windows power APIs.

## Build

Run:

```cmd
build.bat
```

This compiles all `*.cs` files into `BatteryTray.exe`.

Equivalent direct compiler command:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /target:winexe ^
  /out:BatteryTray.exe ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Windows.Forms.DataVisualization.dll ^
  /r:System.Management.dll ^
  /r:System.dll ^
  /r:System.Core.dll ^
  *.cs
```

## Run

```cmd
BatteryTray.exe
```

The app enforces a single running instance. If it is already running, a second launch shows a message and exits.

## Smoke Test

Run:

```cmd
scripts\smoke_test.bat
```

The smoke test builds the app, verifies that `BatteryTray.exe` exists, starts it briefly, then terminates it.

## Configuration

Runtime configuration is stored outside the repository:

```text
%AppData%\BatteryTray\config.txt
```

Supported keys:

| Key | Values | Purpose |
| --- | --- | --- |
| `icon_size` | `16`, `32`, `48`, `64` | Tray icon render size. |
| `update_interval_seconds` | `5` to `300` | Tray update interval. |
| `low_battery_threshold` | `5` to `50` | Low battery notification threshold. |
| `critical_battery_threshold` | `1` to `20` | Critical notification threshold. |
| `full_battery_alert` | `0` or `1` | Enables the full-charge unplug reminder. |
| `sound_alerts` | `0` or `1` | Enables system sound alerts. |
| `history_sample` | `utc|percent|charging` | Persisted battery history sample. |

Example:

```text
# BatteryTray config (key=value)
icon_size=32
update_interval_seconds=10
low_battery_threshold=20
critical_battery_threshold=5
full_battery_alert=1
sound_alerts=1
history_sample=2026-03-19T08:15:00.0000000Z|78|0
history_sample=2026-03-19T08:16:00.0000000Z|77|0
```

Older config files containing only the icon size are still accepted.

## Debug Logging

Debug logging is disabled by default. Enable it before launch:

```cmd
set BATTERYTRAY_DEBUG=1
BatteryTray.exe
```

Logs are written to:

```text
%AppData%\BatteryTray\logs\batterytray.log
```

The log rotates automatically at 1 MB and keeps one `.log.bak` backup.

## Auto-start

The tray menu item `Start with Windows` writes or removes this current-user registry value:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BatteryTray
```

## Metric Availability

Some diagnostics depend on hardware, firmware, Windows permissions, and whether the device has a battery.

Usually available:

- Battery percentage and AC status.
- RAM, disk, uptime, model, Windows version, display resolution.

Sometimes unavailable:

- Battery health fields.
- Battery cycle count.
- Charge or discharge rate.
- CPU temperature.
- Monitor brightness, especially on desktops or external monitors.

Network-dependent:

- Public IP address, fetched from `api.ipify.org` and cached.

Unavailable metrics are shown as `n/a`.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `Program.cs` | Entry point, DPI setup, single-instance guard. |
| `BatteryTrayApp.cs` | Main tray application, menu, alerts, auto-start, diagnostics orchestration. |
| `ChartForm.cs` | Battery history chart window. |
| `BatteryHistoryService.cs` | Battery samples, trend text, estimates, prediction samples. |
| `ConfigStore.cs` | Config and persisted history load/save. |
| `SystemInfoService.cs` | WMI-backed system and battery diagnostics with TTL caching. |
| `NetworkInfoService.cs` | Local IP, Wi-Fi SSID, and public IP lookup. |
| `ProcessInfoService.cs` | Top CPU and memory process detection. |
| `TrayIconFactory.cs` | Dynamic tray icon rendering. |
| `PowerInterop.cs` | Windows power status and DPI interop. |
| `AppLogger.cs` | Optional debug logger with rotation. |
| `Interfaces.cs` | Service interfaces used by the app. |
| `build.bat` | Build script. |
| `scripts\smoke_test.bat` | Basic build/startup smoke test. |
| `scripts\perf_checklist.md` | Manual performance release checklist. |

See [ARCHITECTURE.md](ARCHITECTURE.md) for a more detailed code overview.

## Git Notes

Generated files such as `BatteryTray.exe`, logs, and local agent/tooling folders are ignored by `.gitignore`. Build the executable locally or attach it to a GitHub release rather than committing it to the repository.

## License

No open-source license has been selected yet. Until a license is added, all rights are reserved by default. Add a `LICENSE` file before publishing publicly if you want others to use, modify, or redistribute the code.
