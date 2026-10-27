using System.Diagnostics;

namespace PowerMonitor.Services;

/// <summary>Raw per-process activity measured over one sampling interval.</summary>
public sealed class ProcSample
{
    public int Pid;
    public string Name = "";
    public double CpuPercent;      // percent of total CPU capacity (0-100)
    public double GpuActivity;     // relative GPU busy-time delta (arbitrary units; used for shares)
}

/// <summary>
/// Samples per-process CPU and GPU usage between ticks. CPU comes from process processor-time
/// deltas; GPU comes from the "GPU Engine" performance-counter instances, keyed per PID.
/// Neither requires administrator rights. The first sample seeds baselines and returns empty.
/// </summary>
public sealed class ProcessSampler
{
    private readonly int _coreCount = Environment.ProcessorCount;
    private readonly Dictionary<int, long> _lastCpuTicks = new();
    private readonly Dictionary<int, long> _lastGpuRaw = new();
    private DateTime _lastTime;
    private bool _seeded;

    private PerformanceCounterCategory? _gpuCategory;
    private bool _gpuCounterTried;

    public IReadOnlyList<ProcSample> Sample()
    {
        var now = DateTime.UtcNow;
        double elapsed = (now - _lastTime).TotalSeconds;
        _lastTime = now;

        var cpuNow = new Dictionary<int, long>();
        var names = new Dictionary<int, string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                cpuNow[p.Id] = p.TotalProcessorTime.Ticks; // 100ns units
                names[p.Id] = p.ProcessName;
            }
            catch { /* protected / exited process — skip */ }
            finally { p.Dispose(); }
        }

        var gpuNow = ReadGpuRawPerPid();

        // First pass only seeds baselines; deltas need two samples.
        if (!_seeded || elapsed <= 0)
        {
            _lastCpuTicks.Clear();
            foreach (var kv in cpuNow) _lastCpuTicks[kv.Key] = kv.Value;
            _lastGpuRaw.Clear();
            foreach (var kv in gpuNow) _lastGpuRaw[kv.Key] = kv.Value;
            _seeded = true;
            return Array.Empty<ProcSample>();
        }

        double cpuDenom = elapsed * _coreCount * TimeSpan.TicksPerSecond; // total available CPU ticks
        var results = new List<ProcSample>(cpuNow.Count);

        foreach (var kv in cpuNow)
        {
            int pid = kv.Key;
            double cpuPercent = 0;
            if (_lastCpuTicks.TryGetValue(pid, out long prev))
            {
                long delta = kv.Value - prev;
                if (delta > 0 && cpuDenom > 0)
                    cpuPercent = delta / cpuDenom * 100.0;
            }

            double gpuActivity = 0;
            if (gpuNow.TryGetValue(pid, out long gNow) && _lastGpuRaw.TryGetValue(pid, out long gPrev))
            {
                long gDelta = gNow - gPrev;
                if (gDelta > 0) gpuActivity = gDelta;
            }

            if (cpuPercent > 0.05 || gpuActivity > 0)
            {
                results.Add(new ProcSample
                {
                    Pid = pid,
                    Name = names.TryGetValue(pid, out var n) ? n : $"pid {pid}",
                    CpuPercent = cpuPercent,
                    GpuActivity = gpuActivity,
                });
            }
        }

        _lastCpuTicks.Clear();
        foreach (var kv in cpuNow) _lastCpuTicks[kv.Key] = kv.Value;
        _lastGpuRaw.Clear();
        foreach (var kv in gpuNow) _lastGpuRaw[kv.Key] = kv.Value;

        return results;
    }

    /// <summary>
    /// Sums the "GPU Engine \ Utilization Percentage" raw counter across all engine instances
    /// for each PID. Returns cumulative busy-time per PID; callers use deltas between samples.
    /// </summary>
    private Dictionary<int, long> ReadGpuRawPerPid()
    {
        var result = new Dictionary<int, long>();
        if (!_gpuCounterTried)
        {
            _gpuCounterTried = true;
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                    _gpuCategory = new PerformanceCounterCategory("GPU Engine");
            }
            catch { _gpuCategory = null; }
        }
        if (_gpuCategory is null) return result;

        try
        {
            var data = _gpuCategory.ReadCategory();
            if (!data.Contains("Utilization Percentage")) return result;

            InstanceDataCollection util = data["Utilization Percentage"];
            foreach (System.Collections.DictionaryEntry entry in util)
            {
                string instance = (string)entry.Key;
                int pid = ParsePid(instance);
                if (pid < 0) continue;
                long raw = ((InstanceData)entry.Value!).RawValue;
                result[pid] = result.TryGetValue(pid, out long cur) ? cur + raw : raw;
            }
        }
        catch { /* counters momentarily unavailable — treat as no GPU data this tick */ }

        return result;
    }

    // Instance names look like: "pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D"
    private static int ParsePid(string instance)
    {
        const string prefix = "pid_";
        if (!instance.StartsWith(prefix, StringComparison.Ordinal)) return -1;
        int start = prefix.Length;
        int end = instance.IndexOf('_', start);
        if (end < 0) return -1;
        return int.TryParse(instance.AsSpan(start, end - start), out int pid) ? pid : -1;
    }
}
