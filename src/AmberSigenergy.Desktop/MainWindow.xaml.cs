using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using AmberSigenergy.Desktop.Models;
using AmberSigenergy.Desktop.Services;

namespace AmberSigenergy.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly SigenergyDailyEnergyTracker _dailyTracker = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private AppSettings? _settings;
    private AmberApiClient? _amberApi;
    private AmberDashboardService? _amberDashboard;
    private SigenergyModbusClient? _sigenergy;
    private AmberDashboardSnapshot? _amberSnapshot;
    private DateTimeOffset _lastAmberRefresh = DateTimeOffset.MinValue;
    private bool _polling;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open dashboard", null, (_, _) => ShowDashboard());
        trayMenu.Items.Add("Refresh now", null, async (_, _) => await RefreshAllAsync());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Amber + Sigenergy",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowDashboard();

        try
        {
            _settings = AppSettings.Load();
            _sigenergy = new SigenergyModbusClient(_settings.SigenergyHost, _settings.SigenergyPort, _settings.SigenergyUnitId);
            _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            TelemetryStatusText.Text = $"Read-only Modbus · {_settings.SigenergyHost}:{_settings.SigenergyPort}";
        }
        catch (Exception ex)
        {
            TelemetryStatusText.Text = "Configuration: " + ex.Message;
        }

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var token = AmberTokenStore.Load();
        if (!string.IsNullOrWhiteSpace(token))
            await ConnectAmberDataAsync(token);
        await PollTelemetryAsync();
        UpdateDailyDashboard();
    }

    private async void ConnectAmber_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AmberConnectWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var token = AmberTokenStore.Load();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("No Amber API token has been saved.");
            await ConnectAmberDataAsync(token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Amber connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ConnectAmberDataAsync(string token)
    {
        try
        {
            _amberApi = new AmberApiClient(token);
            _amberDashboard = new AmberDashboardService(_amberApi);
            await RefreshAmberAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Amber connection error";
            AmberStatusText.Text = "Amber: " + ex.Message;
        }
    }

    private async Task RefreshAmberAsync()
    {
        if (_amberDashboard is null || _settings is null) return;
        var snapshot = await _amberDashboard.LoadTodayAsync(_settings.AmberSiteId);
        _amberSnapshot = snapshot;
        _lastAmberRefresh = DateTimeOffset.Now;
        UpdateAmberDashboard(snapshot);
    }

    private void UpdateAmberDashboard(AmberDashboardSnapshot data)
    {
        ImportPriceText.Text = data.CurrentImportPriceCents is null ? "—" : $"{data.CurrentImportPriceCents.Value:0.0#}";
        ExportPriceText.Text = data.CurrentExportPriceCents is null ? "—" : $"{data.CurrentExportPriceCents.Value:0.0#}";
        EarnedTodayText.Text = data.Today.ExportEarnings.ToString("C2");
        ImportCostTodayText.Text = data.Today.ImportCost.ToString("C2");

        ImportedKwhText.Text = $"{data.Today.ImportedKwh:0.00} kWh";
        ExportedKwhText.Text = $"{data.Today.ExportedKwh:0.00} kWh";
        ExportEarningsText.Text = data.Today.ExportEarnings.ToString("C2");
        ImportCostText.Text = data.Today.ImportCost.ToString("C2");
        NetEnergyValueText.Text = data.Today.NetEnergyValue >= 0 ? $"+{data.Today.NetEnergyValue:C2}" : $"-{Math.Abs(data.Today.NetEnergyValue):C2}";

        var importCoverage = data.Today.ImportedKwh <= 0 ? "n/a" : $"{data.PricedImportIntervals}/{data.UsageIntervals}";
        var exportCoverage = data.Today.ExportedKwh <= 0 ? "n/a" : $"{data.PricedExportIntervals}/{data.UsageIntervals}";
        AmberStatusText.Text = $"Amber · {data.Network} · {data.IntervalMinutes}-min meter · priced intervals import {importCoverage}, export {exportCoverage} · {data.BillableIntervals} billable / {data.EstimatedIntervals} estimated";
        ConnectionStatusText.Text = "Amber connected";
        RenderExportPriceChart();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await PollTelemetryAsync();

    private async Task PollTelemetryAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            if (_sigenergy is not null)
            {
                try
                {
                    var telemetry = await _sigenergy.ReadPlantTelemetryAsync();
                    _dailyTracker.AddSample(telemetry);
                    UpdateSigenergyDashboard(telemetry);
                    UpdateDailyDashboard();
                }
                catch (Exception ex)
                {
                    TelemetryStatusText.Text = "Sigenergy Modbus: " + ex.Message;
                    if (_amberSnapshot is not null)
                        ConnectionStatusText.Text = "Amber connected · inverter unavailable";
                    else
                        ConnectionStatusText.Text = "Waiting for connections";
                }
            }

            if (_amberDashboard is not null && DateTimeOffset.Now - _lastAmberRefresh >= TimeSpan.FromMinutes(1))
            {
                try { await RefreshAmberAsync(); }
                catch (Exception ex) { ConnectionStatusText.Text = "Amber refresh failed"; AmberStatusText.Text = "Amber: " + ex.Message; }
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task RefreshAllAsync()
    {
        await PollTelemetryAsync();
        if (_amberDashboard is not null)
        {
            try { await RefreshAmberAsync(); }
            catch (Exception ex) { AmberStatusText.Text = "Amber: " + ex.Message; }
        }
    }

    private void UpdateSigenergyDashboard(SigenergyTelemetry data)
    {
        var capacity = data.RatedBatteryCapacityKwh is > 0
            ? data.RatedBatteryCapacityKwh.Value
            : _settings?.BatteryCapacityKwh ?? 40d;
        if (data.PvPowerKw.HasValue)
            SolarPowerText.Text = $"{data.PvPowerKw.Value:0.00} kW";
        if (data.BatterySocPercent.HasValue)
        {
            var soc = Math.Clamp(data.BatterySocPercent.Value, 0, 100);
            BatterySocText.Text = $"{soc:0.0}%";
            BatteryEnergyText.Text = $"{soc / 100d * capacity:0.0} / {capacity:0.#} kWh";
            BatterySocBar.Width = Math.Max(0, Math.Min(340, soc * 3.4));
        }
        if (data.BatteryPowerKw.HasValue)
        {
            var power = data.BatteryPowerKw.Value;
            var label = Math.Abs(power) < 0.01 ? "Idle" : power > 0 ? "Charging" : "Discharging";
            BatteryPowerText.Text = $"{Math.Abs(power):0.00} kW";
            BatteryPowerSummaryText.Text = $"{Math.Abs(power):0.00} kW {label.ToLowerInvariant()}";
        }
        var grid = data.GridPowerKw;
        GridPowerText.Text = $"{Math.Abs(grid):0.00} kW";
        var flow = Math.Abs(grid) < 0.01 ? "Grid balanced" : grid > 0 ? "Grid import" : "Grid export";
        HomeLoadText.Text = $"LIVE HOME LOAD  {_dailyTracker.GetHomeLoadKw(data):0.00} kW";
        TelemetryStatusText.Text = $"Sigenergy read OK · {flow} · {data.ReadAt.ToLocalTime():HH:mm:ss} · read-only";
        if (_amberSnapshot is not null)
            ConnectionStatusText.Text = "Amber + Sigenergy connected";
    }

    private void UpdateDailyDashboard()
    {
        var totals = _dailyTracker.Totals;
        SigDailyImportText.Text = $"{totals.ImportedKwh:0.00} kWh";
        SigDailyExportText.Text = $"{totals.ExportedKwh:0.00} kWh";
        SigDailySolarText.Text = $"{totals.SolarGeneratedKwh:0.00} kWh";
        SigDailyLoadText.Text = $"{totals.HomeLoadKwh:0.00} kWh";
        SigDailyStatusText.Text = $"{totals.Samples:N0} live samples today · accumulated while this app is running";
    }

    private void FeedInChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderExportPriceChart();

    private void RenderExportPriceChart()
    {
        if (FeedInChartCanvas is null || FeedInChartCanvas.ActualWidth < 100 || FeedInChartCanvas.ActualHeight < 70) return;
        FeedInChartCanvas.Children.Clear();
        var points = _amberSnapshot?.ExportPriceBands;
        if (points is null || points.Count == 0)
        {
            AddChartText("No Amber feed-in price intervals returned for today.", 8, 12, "#9BA4B0", 11);
            return;
        }

        var minValue = points.Min(p => p.LowCents);
        var maxValue = points.Max(p => p.HighCents);
        if (maxValue - minValue < 1m) { minValue -= 1m; maxValue += 1m; }
        var left = 42d; var right = 12d; var top = 8d; var bottom = 25d;
        var width = FeedInChartCanvas.ActualWidth - left - right;
        var height = FeedInChartCanvas.ActualHeight - top - bottom;
        if (width <= 0 || height <= 0) return;

        double Y(decimal value) => top + (double)(maxValue - value) / (double)(maxValue - minValue) * height;
        double X(int index) => left + (points.Count == 1 ? width / 2 : index * width / (points.Count - 1));

        for (var i = 0; i <= 2; i++)
        {
            var value = maxValue - (maxValue - minValue) * i / 2m;
            var y = Y(value);
            FeedInChartCanvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(58, 65, 75)), StrokeThickness = 1 });
            AddChartText($"{value:0.#}", 2, y - 8, "#9BA4B0", 9);
        }

        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(217, 154, 50)), StrokeThickness = 2 };
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var x = X(i);
            FeedInChartCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = Y(p.HighCents), Y2 = Y(p.LowCents), Stroke = new SolidColorBrush(Color.FromRgb(112, 184, 162)), StrokeThickness = 3, Opacity = 0.85 });
            line.Points.Add(new System.Windows.Point(x, Y(p.MidCents)));
            if (i % Math.Max(1, points.Count / 6) == 0 || i == points.Count - 1)
                AddChartText(p.BlockStart.ToLocalTime().ToString("HH:mm"), x - 16, top + height + 5, "#9BA4B0", 9);
        }
        FeedInChartCanvas.Children.Add(line);
        for (var i = 0; i < points.Count; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(217, 154, 50)) };
            Canvas.SetLeft(dot, X(i) - 2); Canvas.SetTop(dot, Y(points[i].MidCents) - 2);
            FeedInChartCanvas.Children.Add(dot);
        }
    }

    private void AddChartText(string text, double left, double top, string colour, double size)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)),
            FontSize = size
        };
        Canvas.SetLeft(label, left); Canvas.SetTop(label, top);
        FeedInChartCanvas.Children.Add(label);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        Hide();
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        _refreshTimer.Stop();
        _dailyTracker.Save();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
