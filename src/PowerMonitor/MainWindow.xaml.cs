using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PowerMonitor.Models;
using PowerMonitor.Services;
using PowerMonitor.UI;

namespace PowerMonitor;

public partial class MainWindow : Window
{
    private readonly MonitorEngine _engine;
    private readonly MainViewModel _vm = new();
    private readonly Queue<double> _history = new();
    private const int HistoryLength = 120;
    private bool _reallyClosing;

    public MainWindow(MonitorEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        DataContext = _vm;

        var icon = IconFactory.CreateBolt(64);
        Icon = icon;
        Tray.IconSource = icon;

        _engine.SnapshotUpdated += OnSnapshot;
        Loaded += (_, _) => DrawChart();
        SizeChanged += (_, _) => DrawChart();
    }

    private void OnSnapshot(Snapshot snap)
    {
        // Event fires on the engine's background thread; marshal to the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            _vm.Update(snap);

            _history.Enqueue(_vm.HeadlineWatts);
            while (_history.Count > HistoryLength) _history.Dequeue();
            DrawChart();

            UpdateTrayTooltip(snap);
        });
    }

    private void UpdateTrayTooltip(Snapshot snap)
    {
        string watts = _vm.BatteryHeadline;
        string pct = snap.Battery.Present ? $" · {snap.Battery.ChargePercent:0}%" : "";
        Tray.ToolTipText = $"Power Monitor — {watts}{pct}";
    }

    private void DrawChart()
    {
        var canvas = ChartCanvas;
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 1 || h <= 1 || _history.Count < 2) return;

        double max = Math.Max(_history.Max(), 5.0); // floor so an idle line isn't full-scale
        double stepX = w / (HistoryLength - 1);

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x1C)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x1C)) };

        var pts = new PointCollection();
        int n = _history.Count;
        int offset = HistoryLength - n; // right-align newest sample
        var arr = _history.ToArray();
        for (int i = 0; i < n; i++)
        {
            double x = (offset + i) * stepX;
            double y = h - (arr[i] / max) * (h - 4) - 2;
            pts.Add(new Point(x, y));
        }
        line.Points = pts;

        var areaPts = new PointCollection(pts) { };
        areaPts.Insert(0, new Point(pts[0].X, h));
        areaPts.Add(new Point(pts[^1].X, h));
        area.Points = areaPts;

        canvas.Children.Add(area);
        canvas.Children.Add(line);

        // current-value label
        var label = new System.Windows.Controls.TextBlock
        {
            Text = $"{max:0} W",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
            FontSize = 10,
        };
        Canvas.SetLeft(label, 2);
        Canvas.SetTop(label, 0);
        canvas.Children.Add(label);
    }

    private void ElevateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            _reallyClosing = true;
            Application.Current.Shutdown();
        }
        catch
        {
            // User declined the UAC prompt, or relaunch failed — keep running non-elevated.
        }
    }

    // ---- Tray / window lifecycle ----
    private void Tray_DoubleClick(object sender, RoutedEventArgs e) => ShowFromTray();
    private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _reallyClosing = true;
        Application.Current.Shutdown();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window minimizes to tray instead of exiting.
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _engine.SnapshotUpdated -= OnSnapshot;
        base.OnClosing(e);
    }
}
