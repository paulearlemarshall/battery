# BatteryTray Architecture

This document describes the current C# WinForms implementation. It is intended for maintainers and for readers evaluating the code on GitHub.

## Runtime Model

BatteryTray is a single-process WinForms tray app. `Program.Main` sets process DPI awareness, creates a named mutex so only one instance can run, and starts `BatteryTrayApp`.

`BatteryTrayApp` owns the `NotifyIcon`, update timer, context menu, alert state, chart window, and service instances. The tray updates on a configurable timer. More expensive diagnostics are refreshed through cached service calls so opening the tray menu stays responsive.

## Main Components

| Component | Responsibility |
| --- | --- |
| `BatteryTrayApp` | Coordinates the tray icon, menu, alerts, chart launch, diagnostics copying, auto-start, and cleanup. |
| `BatteryHistoryService` | Stores rolling battery samples, computes 1/5/10 minute deltas, estimates time to full/empty, and produces prediction samples. |
| `ChartForm` | Displays recent battery samples and prediction traces using WinForms charting. |
| `ConfigStore` | Reads and writes `%AppData%\BatteryTray\config.txt`, including persisted history samples. |
| `SystemInfoService` | Reads WMI and system data such as RAM, disk, model, BIOS, battery health, charge rate, and power plan. |
| `NetworkInfoService` | Resolves local IP, Wi-Fi SSID, and public IP with caching. |
| `ProcessInfoService` | Detects the highest CPU and memory consuming processes. |
| `TrayIconFactory` | Draws the icon bitmap and converts it to a tray icon. |
| `PowerInterop` | Wraps native Windows APIs used for power status and DPI awareness. |
| `AppLogger` | Writes optional debug logs when `BATTERYTRAY_DEBUG=1` is set. |
| `Interfaces` | Defines service interfaces for dependency injection and testability. |

## Data Flow

1. `BatteryTrayApp.UpdateTray` calls `PowerInterop.GetSystemPowerStatus`.
2. If the battery percentage is known, the current sample is passed to `BatteryHistoryService.MaybeAddSample`.
3. When a new sample is added, `ConfigStore.SaveBatterySamples` persists the rolling history.
4. The tray icon is redrawn by `TrayIconFactory`.
5. Alert rules run against the latest battery percentage and charge state.
6. Menu-opening updates cheap fields immediately and queues slower diagnostics through `QueueMenuInfoRefresh`.
7. Chart windows query `BatteryTrayApp` for sample windows and prediction samples.

## Configuration and Persistence

Configuration lives at:

```text
%AppData%\BatteryTray\config.txt
```

The file is line-oriented `key=value` text. Most settings are scalar values, while battery history uses repeated `history_sample=utc|percent|charging` lines.

The config store validates numeric ranges and preserves unknown keys where possible. It also supports the earlier single-value config format for icon size.

## Caching Strategy

Several diagnostics rely on WMI, network calls, or process inspection. These are intentionally cached:

- Public IP is cached by `NetworkInfoService`.
- Wi-Fi SSID is cached briefly.
- WMI-backed system values use per-key TTLs in `SystemInfoService`.
- Menu refreshes are queued and throttled by `BatteryTrayApp`.

This keeps the tray menu usable even when a metric is slow or unavailable.

## Error Handling

The app treats diagnostics as best effort. If a metric fails, the UI generally shows `n/a` and logs the exception only when debug logging is enabled. Tray update failures are caught so a transient WMI or power API issue does not crash the app.

## Build Design

The repository intentionally avoids external dependencies. `build.bat` invokes the .NET Framework compiler directly against all C# files:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ... *.cs
```

This keeps the app easy to build on Windows machines without restoring NuGet packages.

## Non-current Files

`main.cpp` is an earlier C++ prototype and is not part of the current build. Keep it only if the historical reference is useful; otherwise it can be removed before the first public release.
