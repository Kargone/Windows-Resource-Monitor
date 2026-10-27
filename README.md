# Power Monitor

A native Windows desktop app that shows, in real time:

- **Battery power in / out** — watts flowing into the battery (charging) or out of it (discharging), plus charge %, time remaining, voltage and current.
- **Where the power goes** — measured **CPU** package watts and **GPU** board watts, with the rest (screen, RAM, board, losses) shown as **Other**.
- **Top programs by estimated power** — running processes ranked by how much power they're pulling.

It lives in the system tray and updates once a second.

## How accurate is each number?

| Reading | Source | Accuracy |
|---|---|---|
| Battery watts in/out, %, V/A, time left | WMI `BatteryStatus` / `Win32_Battery` | Real sensor data |
| CPU package watts | Intel/AMD RAPL via LibreHardwareMonitor | Real sensor data *(needs admin)* |
| GPU watts | NVML / vendor API via LibreHardwareMonitor | Real sensor data |
| "Other" watts | Total battery draw − (CPU + GPU) | Derived remainder |
| Per-program watts | Measured CPU/GPU watts split across processes by their CPU/GPU usage | **Estimate**, not exact |

Windows exposes no exact per-process wattage, so the per-program column is a principled
estimate: the watts we *do* measure are apportioned by each process's share of CPU time and
GPU-engine activity. The per-app numbers won't sum exactly to the battery total — the gap is
shown honestly as the **Other** bucket rather than hidden.

## Administrator rights

Reading **CPU** power uses the processor's RAPL counters, which require a kernel driver and
therefore **administrator rights**. Everything else (battery, GPU, per-process usage) works
without elevation.

- Launched normally, the app runs fine but shows CPU watts as unavailable and offers a
  **"Restart as administrator"** button in the status bar.
- Run it as administrator (or click that button) to unlock CPU watts.

## Build & run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd src\PowerMonitor
dotnet run -c Release
```

To produce a standalone build:

```powershell
dotnet publish src\PowerMonitor -c Release
```

## Project layout

```
src/PowerMonitor/
  Models/Snapshot.cs        data model for one reading
  Services/
    BatteryService.cs       WMI battery power (watts in/out)
    SensorService.cs        LibreHardwareMonitor wrapper (CPU + GPU watts)
    ProcessSampler.cs       per-process CPU + GPU usage sampling
    MonitorEngine.cs        1 Hz loop, smoothing, power attribution
  UI/
    MainViewModel.cs        formats a snapshot for display
    IconFactory.cs          runtime-drawn tray/app icon
  MainWindow.xaml(.cs)      window layout, trend chart, tray icon
  App.xaml(.cs)             startup / lifetime
```

## Dependencies

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) — CPU/GPU sensors
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — system-tray icon
- `System.Management` — WMI access
