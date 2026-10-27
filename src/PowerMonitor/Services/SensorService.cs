using LibreHardwareMonitor.Hardware;

namespace PowerMonitor.Services;

/// <summary>
/// Wraps LibreHardwareMonitor to read CPU package power (Intel/AMD RAPL) and GPU board power.
/// Reading CPU RAPL requires the kernel driver, which needs administrator rights; without them
/// the CPU power sensor is simply absent and <see cref="ReadCpuPackageWatts"/> returns null.
/// GPU power (NVIDIA via NVML) is available without elevation.
/// </summary>
public sealed class SensorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private bool _opened;

    public SensorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
        };
    }

    public void Open()
    {
        if (_opened) return;
        _computer.Open();
        _opened = true;
    }

    /// <summary>Refresh all hardware sensors. Call once per poll before reading values.</summary>
    public void Refresh()
    {
        if (!_opened) return;
        _computer.Accept(_visitor);
    }

    /// <summary>CPU package power in watts, or null if no sensor is available (e.g. not elevated).</summary>
    public double? ReadCpuPackageWatts()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;
            double? pkg = FindPowerSensor(hw, "package");
            if (pkg is not null) return pkg;
            // Fallback: largest Power sensor on the CPU (some parts name it differently).
            double? max = null;
            foreach (var s in hw.Sensors)
                if (s.SensorType == SensorType.Power && s.Value is float v)
                    max = max is null ? v : Math.Max(max.Value, v);
            if (max is not null) return max;
        }
        return null;
    }

    /// <summary>Total GPU board power in watts across all GPUs, or null if no GPU reports power.</summary>
    public double? ReadGpuWatts()
    {
        double sum = 0;
        bool any = false;
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                continue;

            double? power = FindPowerSensor(hw, "package")
                            ?? FindPowerSensor(hw, "total")
                            ?? FirstPowerSensor(hw);
            if (power is not null)
            {
                sum += power.Value;
                any = true;
            }
        }
        return any ? sum : null;
    }

    private static double? FindPowerSensor(IHardware hw, string nameContains)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Power
                && s.Value is float v
                && s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    private static double? FirstPowerSensor(IHardware hw)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Power && s.Value is float v)
                return v;
        return null;
    }

    public void Dispose()
    {
        if (_opened)
        {
            try { _computer.Close(); } catch { /* ignore shutdown races */ }
            _opened = false;
        }
    }

    /// <summary>Visitor that forces each hardware node (and its sub-hardware) to update.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
