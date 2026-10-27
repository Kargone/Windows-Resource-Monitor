using System.Management;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// Reads real-time battery power flow from WMI. The root\WMI <c>BatteryStatus</c> class exposes
/// instantaneous ChargeRate / DischargeRate in milliwatts, which is the true watts-in / watts-out.
/// Does not require administrator rights.
/// </summary>
public sealed class BatteryService
{
    private const uint RunTimeUnknown = 0x04444440; // 71582788 == "unknown" sentinel from Win32_Battery

    private readonly ManagementScope _wmiScope = new(@"root\WMI");
    private readonly ManagementScope _cimScope = new(@"root\CIMV2");

    public BatteryInfo Read()
    {
        try
        {
            if (!TryGetFirst(_wmiScope, "SELECT * FROM BatteryStatus", out var status))
                return BatteryInfo.NoBattery();

            uint chargeRate = GetUInt(status, "ChargeRate");       // mW
            uint dischargeRate = GetUInt(status, "DischargeRate");  // mW
            uint voltageMv = GetUInt(status, "Voltage");            // mV
            uint remainingMwh = GetUInt(status, "RemainingCapacity"); // mWh
            bool charging = GetBool(status, "Charging");
            bool onAc = GetBool(status, "PowerOnline");

            double voltage = voltageMv / 1000.0;
            double powerWatts = (chargeRate - (double)dischargeRate) / 1000.0; // +in / -out
            double currentAmps = voltage > 0 ? Math.Abs(powerWatts) / voltage : 0;

            double fullWh = 0;
            if (TryGetFirst(_wmiScope, "SELECT * FROM BatteryFullChargedCapacity", out var full))
                fullWh = GetUInt(full, "FullChargedCapacity") / 1000.0;

            double chargePercent = 0;
            int? minutesRemaining = null;
            if (TryGetFirst(_cimScope, "SELECT * FROM Win32_Battery", out var win32))
            {
                chargePercent = GetUInt(win32, "EstimatedChargeRemaining");
                uint runtime = GetUInt(win32, "EstimatedRunTime");
                if (!charging && runtime > 0 && runtime < RunTimeUnknown)
                    minutesRemaining = (int)runtime;
            }
            if (chargePercent <= 0 && fullWh > 0)
                chargePercent = Math.Round(remainingMwh / 1000.0 / fullWh * 100.0);

            return new BatteryInfo
            {
                Present = true,
                OnAc = onAc,
                Charging = charging,
                ChargePercent = chargePercent,
                PowerWatts = powerWatts,
                Voltage = voltage,
                CurrentAmps = currentAmps,
                MinutesRemaining = minutesRemaining,
                RemainingCapacityWh = remainingMwh / 1000.0,
                FullCapacityWh = fullWh,
            };
        }
        catch
        {
            // No battery (desktop), WMI disabled, or a transient query failure.
            return BatteryInfo.NoBattery();
        }
    }

    private static bool TryGetFirst(ManagementScope scope, string query, out ManagementBaseObject obj)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
        foreach (ManagementBaseObject o in searcher.Get())
        {
            obj = o;
            return true;
        }
        obj = null!;
        return false;
    }

    private static uint GetUInt(ManagementBaseObject o, string prop)
    {
        try
        {
            object? v = o[prop];
            return v is null ? 0u : Convert.ToUInt32(v);
        }
        catch { return 0u; }
    }

    private static bool GetBool(ManagementBaseObject o, string prop)
    {
        try
        {
            object? v = o[prop];
            return v is not null && Convert.ToBoolean(v);
        }
        catch { return false; }
    }
}
