namespace PowerMonitor.Models;

/// <summary>Battery state at a point in time. All power values are in watts.</summary>
public sealed class BatteryInfo
{
    public bool Present { get; init; }
    public bool OnAc { get; init; }
    public bool Charging { get; init; }

    public double ChargePercent { get; init; }

    /// <summary>Signed power flow: positive = charging (watts in), negative = discharging (watts out).</summary>
    public double PowerWatts { get; init; }

    public double Voltage { get; init; }
    public double CurrentAmps { get; init; }

    /// <summary>Estimated minutes of runtime left on battery, or null if unknown / charging.</summary>
    public int? MinutesRemaining { get; init; }

    public double RemainingCapacityWh { get; init; }
    public double FullCapacityWh { get; init; }

    public static BatteryInfo NoBattery() => new() { Present = false, OnAc = true };
}

/// <summary>Measured per-component power draw in watts. Nulls mean "no sensor / unavailable".</summary>
public sealed class ComponentPower
{
    public double? CpuWatts { get; init; }
    public double? GpuWatts { get; init; }

    /// <summary>Total system draw from the battery (watts out), when on battery.</summary>
    public double? TotalWatts { get; init; }

    /// <summary>Total minus the parts we can measure. Represents screen, RAM, board, losses, etc.</summary>
    public double? OtherWatts
    {
        get
        {
            if (TotalWatts is null) return null;
            double measured = (CpuWatts ?? 0) + (GpuWatts ?? 0);
            double other = TotalWatts.Value - measured;
            return other < 0 ? 0 : other;
        }
    }
}

/// <summary>A single process's estimated power contribution.</summary>
public sealed class ProcessPower
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Percent of total CPU capacity used by this process (0-100).</summary>
    public double CpuPercent { get; init; }

    /// <summary>This process's share of total GPU activity (0-100).</summary>
    public double GpuPercent { get; init; }

    /// <summary>Estimated watts attributed to this process (CPU + GPU share).</summary>
    public double EstimatedWatts { get; init; }
}

/// <summary>One full reading of the system, produced by the monitor loop each tick.</summary>
public sealed class Snapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public BatteryInfo Battery { get; init; } = BatteryInfo.NoBattery();
    public ComponentPower Components { get; init; } = new();
    public IReadOnlyList<ProcessPower> TopProcesses { get; init; } = Array.Empty<ProcessPower>();

    public bool Elevated { get; init; }
    public bool CpuPowerAvailable { get; init; }
    public bool GpuPowerAvailable { get; init; }

    /// <summary>Human-readable note about sensor availability, shown in the UI status line.</summary>
    public string Status { get; init; } = "";
}
