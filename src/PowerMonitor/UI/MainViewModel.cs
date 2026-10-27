using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PowerMonitor.Models;

namespace PowerMonitor.UI;

public sealed class ProcessRow : INotifyPropertyChanged
{
    public int Pid { get; init; }

    private string _name = "";
    private string _cpuText = "";
    private string _gpuText = "";
    private string _wattsText = "";
    private double _barFraction;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string CpuText { get => _cpuText; set => Set(ref _cpuText, value); }
    public string GpuText { get => _gpuText; set => Set(ref _gpuText, value); }
    public string WattsText { get => _wattsText; set => Set(ref _wattsText, value); }
    public double BarFraction { get => _barFraction; set => Set(ref _barFraction, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly Brush Amber = Frozen(0xFF, 0x9F, 0x1C);
    private static readonly Brush Green = Frozen(0x2E, 0xCC, 0x71);
    private static readonly Brush Red = Frozen(0xE7, 0x4C, 0x3C);
    private static readonly Brush Gray = Frozen(0x9A, 0xA0, 0xA6);

    public ObservableCollection<ProcessRow> Processes { get; } = new();

    private string _batteryHeadline = "—";
    private string _batteryDirection = "Starting…";
    private Brush _headlineBrush = Gray;
    private double _batteryPercent;
    private string _batteryPercentText = "";
    private string _timeRemainingText = "";
    private string _voltageCurrentText = "";
    private string _capacityText = "";

    private string _cpuWattsText = "—";
    private string _gpuWattsText = "—";
    private string _otherWattsText = "—";
    private string _totalWattsText = "—";
    private double _cpuBar, _gpuBar, _otherBar;
    private double _otherRowOpacity = 1.0;
    private string _otherLabel = "Screen, RAM & rest";
    private string _otherNote = "";
    private string _totalLabel = "Total power draw";

    private string _statusText = "";
    private Visibility _elevateVisibility = Visibility.Collapsed;

    public string BatteryHeadline { get => _batteryHeadline; set => Set(ref _batteryHeadline, value); }
    public string BatteryDirection { get => _batteryDirection; set => Set(ref _batteryDirection, value); }
    public Brush HeadlineBrush { get => _headlineBrush; set => Set(ref _headlineBrush, value); }
    public double BatteryPercent { get => _batteryPercent; set => Set(ref _batteryPercent, value); }
    public string BatteryPercentText { get => _batteryPercentText; set => Set(ref _batteryPercentText, value); }
    public string TimeRemainingText { get => _timeRemainingText; set => Set(ref _timeRemainingText, value); }
    public string VoltageCurrentText { get => _voltageCurrentText; set => Set(ref _voltageCurrentText, value); }
    public string CapacityText { get => _capacityText; set => Set(ref _capacityText, value); }

    public string CpuWattsText { get => _cpuWattsText; set => Set(ref _cpuWattsText, value); }
    public string GpuWattsText { get => _gpuWattsText; set => Set(ref _gpuWattsText, value); }
    public string OtherWattsText { get => _otherWattsText; set => Set(ref _otherWattsText, value); }
    public string TotalWattsText { get => _totalWattsText; set => Set(ref _totalWattsText, value); }
    public double CpuBar { get => _cpuBar; set => Set(ref _cpuBar, value); }
    public double GpuBar { get => _gpuBar; set => Set(ref _gpuBar, value); }
    public double OtherBar { get => _otherBar; set => Set(ref _otherBar, value); }
    public double OtherRowOpacity { get => _otherRowOpacity; set => Set(ref _otherRowOpacity, value); }
    public string OtherLabel { get => _otherLabel; set => Set(ref _otherLabel, value); }
    public string OtherNote { get => _otherNote; set => Set(ref _otherNote, value); }
    public string TotalLabel { get => _totalLabel; set => Set(ref _totalLabel, value); }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public Visibility ElevateVisibility { get => _elevateVisibility; set => Set(ref _elevateVisibility, value); }

    /// <summary>The headline watts value used for the trend chart (total draw, else CPU+GPU).</summary>
    public double HeadlineWatts { get; private set; }

    public void Update(Snapshot s)
    {
        UpdateBattery(s.Battery);
        UpdateComponents(s.Components);
        UpdateProcesses(s.TopProcesses);

        StatusText = s.Status;
        ElevateVisibility = s.Elevated ? Visibility.Collapsed : Visibility.Visible;

        HeadlineWatts = s.Components.TotalWatts
                        ?? ((s.Components.CpuWatts ?? 0) + (s.Components.GpuWatts ?? 0));
    }

    private void UpdateBattery(BatteryInfo b)
    {
        if (!b.Present)
        {
            BatteryHeadline = "On AC";
            BatteryDirection = "No battery detected";
            HeadlineBrush = Gray;
            BatteryPercent = 0;
            BatteryPercentText = "";
            TimeRemainingText = "";
            VoltageCurrentText = "";
            CapacityText = "";
            return;
        }

        BatteryPercent = b.ChargePercent;
        BatteryPercentText = $"{b.ChargePercent:0}%";
        VoltageCurrentText = b.Voltage > 0 ? $"{b.Voltage:0.00} V · {b.CurrentAmps:0.00} A" : "";
        CapacityText = b.FullCapacityWh > 0 ? $"{b.RemainingCapacityWh:0.0} / {b.FullCapacityWh:0.0} Wh" : "";

        if (b.Charging)
        {
            BatteryHeadline = $"+{b.PowerWatts:0.0} W";
            BatteryDirection = b.ChargePercent >= 99 ? "Charging (full)" : "Charging — power in";
            HeadlineBrush = Green;
            TimeRemainingText = "";
        }
        else if (b.PowerWatts < -0.05)
        {
            BatteryHeadline = $"−{Math.Abs(b.PowerWatts):0.0} W"; // U+2212 minus
            BatteryDirection = "Discharging — power out";
            HeadlineBrush = b.ChargePercent <= 15 ? Red : Amber;
            TimeRemainingText = b.MinutesRemaining is int m ? $"{m / 60}h {m % 60:00}m left" : "";
        }
        else
        {
            BatteryHeadline = "0.0 W";
            BatteryDirection = b.OnAc ? "On AC — not charging" : "Idle";
            HeadlineBrush = Gray;
            TimeRemainingText = "";
        }
    }

    private void UpdateComponents(ComponentPower c)
    {
        CpuWattsText = Watts(c.CpuWatts);
        GpuWattsText = Watts(c.GpuWatts);

        bool hasTotal = c.TotalWatts is not null;

        if (hasTotal)
        {
            // On battery: the discharge rate is the true total system draw.
            double total = Math.Max(c.TotalWatts!.Value, 0.1);
            TotalLabel = "Total power draw";
            TotalWattsText = Watts(c.TotalWatts);
            OtherWattsText = Watts(c.OtherWatts);
            OtherNote = "";
            OtherRowOpacity = 1.0;
            CpuBar = Clamp01((c.CpuWatts ?? 0) / total);
            GpuBar = Clamp01((c.GpuWatts ?? 0) / total);
            OtherBar = Clamp01((c.OtherWatts ?? 0) / total);
        }
        else
        {
            // On AC: the system's true total draw isn't sensor-measurable (the charger feeds the
            // system and the battery at once), so show the CPU+GPU measured sum, and keep the
            // "rest of system" row present but greyed out with a note instead of hiding it.
            bool anyMeasured = c.CpuWatts is not null || c.GpuWatts is not null;
            double sum = (c.CpuWatts ?? 0) + (c.GpuWatts ?? 0);
            double reference = Math.Max(sum, 0.1);
            TotalLabel = "CPU + GPU (plugged in)";
            TotalWattsText = anyMeasured ? Watts(sum) : "—";
            OtherWattsText = "—";
            OtherNote = "not measurable while plugged in";
            OtherRowOpacity = 0.5;
            CpuBar = Clamp01((c.CpuWatts ?? 0) / reference);
            GpuBar = Clamp01((c.GpuWatts ?? 0) / reference);
            OtherBar = 0;
        }
    }

    private void UpdateProcesses(IReadOnlyList<ProcessPower> procs)
    {
        bool wattsMode = procs.Any(p => p.EstimatedWatts > 0.01);
        double max = wattsMode
            ? Math.Max(procs.Select(p => p.EstimatedWatts).DefaultIfEmpty(0).Max(), 0.01)
            : Math.Max(procs.Select(p => p.CpuPercent).DefaultIfEmpty(0).Max(), 0.01);

        // Reconcile in place by PID so the grid doesn't flicker or lose scroll position.
        var seen = new HashSet<int>();
        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            seen.Add(p.Pid);
            ProcessRow row = i < Processes.Count ? Processes[i] : AddRow();
            if (row.Pid != p.Pid)
            {
                // Replace row identity when the ordering changed.
                Processes[i] = new ProcessRow { Pid = p.Pid };
                row = Processes[i];
            }
            row.Name = p.Name;
            row.CpuText = $"{p.CpuPercent:0.0}%";
            row.GpuText = p.GpuPercent > 0.05 ? $"{p.GpuPercent:0.0}%" : "—";
            row.WattsText = p.EstimatedWatts > 0.01 ? $"{p.EstimatedWatts:0.0} W" : "—";
            row.BarFraction = Clamp01((wattsMode ? p.EstimatedWatts : p.CpuPercent) / max);
        }
        while (Processes.Count > procs.Count)
            Processes.RemoveAt(Processes.Count - 1);
    }

    private ProcessRow AddRow()
    {
        var row = new ProcessRow { Pid = -1 };
        Processes.Add(row);
        return row;
    }

    private static string Watts(double? w) => w is null ? "—" : $"{w.Value:0.0} W";
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
