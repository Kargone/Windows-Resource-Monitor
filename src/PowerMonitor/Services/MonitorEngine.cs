using System.Security.Principal;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// Drives the sampling loop: reads battery + component sensors, samples processes, attributes
/// measured CPU/GPU watts to processes, and raises <see cref="SnapshotUpdated"/> each tick.
/// The event fires on a background thread; handlers must marshal to the UI thread themselves.
/// </summary>
public sealed class MonitorEngine : IDisposable
{
    private const int TopProcessCount = 14;
    private const double Alpha = 0.4; // EMA smoothing factor for displayed power values

    private readonly SensorService _sensors = new();
    private readonly BatteryService _battery = new();
    private readonly ProcessSampler _procs = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    private double? _emaBattery, _emaCpu, _emaGpu;

    public int IntervalMs { get; set; } = 1000;
    public bool Elevated { get; } = CheckElevated();

    public event Action<Snapshot>? SnapshotUpdated;

    public void Start()
    {
        if (_loop is not null) return;
        _sensors.Open();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snap = Tick();
                SnapshotUpdated?.Invoke(snap);
            }
            catch
            {
                // A single bad tick must not kill the loop.
            }

            try { await Task.Delay(IntervalMs, token); }
            catch (TaskCanceledException) { break; }
        }
    }

    private Snapshot Tick()
    {
        _sensors.Refresh();

        BatteryInfo battery = _battery.Read();
        double? cpuW = Smooth(ref _emaCpu, _sensors.ReadCpuPackageWatts());
        double? gpuW = Smooth(ref _emaGpu, _sensors.ReadGpuWatts());

        bool onBatteryDraw = battery.Present && !battery.Charging && battery.PowerWatts < 0;
        double? totalW = null;
        if (onBatteryDraw)
            totalW = Smooth(ref _emaBattery, Math.Abs(battery.PowerWatts));
        else
            _emaBattery = null; // reset so a later discharge starts fresh

        var components = new ComponentPower
        {
            CpuWatts = cpuW,
            GpuWatts = gpuW,
            TotalWatts = totalW,
        };

        var raw = _procs.Sample();
        var top = AttributeProcesses(raw, cpuW, gpuW);

        bool cpuAvail = cpuW is not null;
        bool gpuAvail = gpuW is not null;

        return new Snapshot
        {
            Battery = battery,
            Components = components,
            TopProcesses = top,
            Elevated = Elevated,
            CpuPowerAvailable = cpuAvail,
            GpuPowerAvailable = gpuAvail,
            Status = BuildStatus(cpuAvail, gpuAvail),
        };
    }

    private IReadOnlyList<ProcessPower> AttributeProcesses(IReadOnlyList<ProcSample> raw, double? cpuW, double? gpuW)
    {
        if (raw.Count == 0) return Array.Empty<ProcessPower>();

        double sumCpu = raw.Sum(r => r.CpuPercent);
        double sumGpu = raw.Sum(r => r.GpuActivity);

        var list = new List<ProcessPower>(raw.Count);
        foreach (var r in raw)
        {
            double cpuShare = sumCpu > 0 ? r.CpuPercent / sumCpu : 0;
            double gpuShare = sumGpu > 0 ? r.GpuActivity / sumGpu : 0;

            double watts = 0;
            if (cpuW is not null) watts += cpuW.Value * cpuShare;
            if (gpuW is not null) watts += gpuW.Value * gpuShare;

            list.Add(new ProcessPower
            {
                Pid = r.Pid,
                Name = r.Name,
                CpuPercent = r.CpuPercent,
                GpuPercent = gpuShare * 100.0,
                EstimatedWatts = watts,
            });
        }

        // When CPU watts are unavailable (not elevated) the watt estimate is mostly empty, so
        // rank by CPU usage instead to still surface the heaviest processes.
        bool rankByWatts = cpuW is not null;
        list.Sort((a, b) => rankByWatts
            ? b.EstimatedWatts.CompareTo(a.EstimatedWatts)
            : b.CpuPercent.CompareTo(a.CpuPercent));

        return list.Count > TopProcessCount ? list.GetRange(0, TopProcessCount) : list;
    }

    private static string BuildStatus(bool cpuAvail, bool gpuAvail)
    {
        if (!cpuAvail)
            return "CPU power unavailable — restart as administrator to read CPU watts.";
        if (!gpuAvail)
            return "CPU power active. GPU power sensor not detected.";
        return "All sensors active.";
    }

    private static double? Smooth(ref double? ema, double? value)
    {
        if (value is null) return null;
        ema = ema is null ? value : Alpha * value.Value + (1 - Alpha) * ema.Value;
        return ema;
    }

    private static bool CheckElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(2000); } catch { }
        _sensors.Dispose();
        _cts?.Dispose();
    }
}
