using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using Window = System.Windows.Window;
using Color = System.Windows.Media.Color;
using Canvas = System.Windows.Controls.Canvas;
using TextBlock = System.Windows.Controls.TextBlock;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using SizeChangedEventArgs = System.Windows.SizeChangedEventArgs;
using WindowState = System.Windows.WindowState;
using AmberSigenergy.Desktop.Models;
using AmberSigenergy.Desktop.Services;

namespace AmberSigenergy.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly SigenergyDailyEnergyTracker _dailyTracker = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly TrayHoverWindow _trayHoverWindow = new();
    private readonly DispatcherTimer _trayHoverTimer = new();
    private DateTime _lastTrayHover = DateTime.MinValue;
    private AppSettings? _settings;
    private AmberApiClient? _amberApi;
    private AmberDashboardService? _amberDashboard;
    private SigenergyModbusClient? _sigenergy;
    private AmberDashboardSnapshot? _amberSnapshot;
    private DateTimeOffset _lastAmberRefresh = DateTimeOffset.MinValue;
    private bool _polling;
    private bool _allowExit;
    private IReadOnlyList<ExportPriceBand> _chartPoints = Array.Empty<ExportPriceBand>();
    private DateTime _chartDay;
    private double _chartLeft, _chartTop, _chartWidth, _chartHeight;
    private decimal _chartMin, _chartMax;
    private Line? _chartNowLine;
    private Line? _chartHoverLine;
    private Border? _chartHoverCard;
    private TextBlock? _chartHoverText;

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
            Icon = File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "AmberSigenergy.ico"))
                ? new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "AmberSigenergy.ico"))
                : System.Drawing.SystemIcons.Application,
            Text = "Amber + Sigenergy",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowDashboard();
        _trayIcon.MouseMove += (_, _) => ShowTrayHover();
        _trayHoverWindow.PointerEntered += (_, _) => _trayHoverWindow.IsPointerInside = true;
        _trayHoverWindow.PointerLeft += (_, _) => _trayHoverWindow.IsPointerInside = false;
        _trayHoverTimer.Interval = TimeSpan.FromMilliseconds(150);
        _trayHoverTimer.Tick += (_, _) =>
        {
            if (_trayHoverWindow.IsVisible && !_trayHoverWindow.IsPointerInside &&
                DateTime.UtcNow - _lastTrayHover > TimeSpan.FromMilliseconds(700))
                _trayHoverWindow.Hide();
        };
        _trayHoverTimer.Start();

        try
        {
            _settings = AppSettings.Load();
            _sigenergy = new SigenergyModbusClient(_settings.SigenergyHost, _settings.SigenergyPort, _settings.SigenergyUnitId, _settings.SigenergyInverterUnitId);
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
            AmberConnectButton.Content = "Amber !";
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
        NetEnergyValueText.Text = data.Today.NetEnergyValue >= 0 ? $"+{data.Today.NetEnergyValue:C2}" : $"-{Math.Abs(data.Today.NetEnergyValue):C2}";

        var importCoverage = data.Today.ImportedKwh <= 0 ? "n/a" : $"{data.PricedImportIntervals}/{data.UsageIntervals}";
        var exportCoverage = data.Today.ExportedKwh <= 0 ? "n/a" : $"{data.PricedExportIntervals}/{data.UsageIntervals}";
        AmberStatusText.Text = $"Amber · {data.Network} · {data.IntervalMinutes}-min meter · priced intervals import {importCoverage}, export {exportCoverage} · {data.BillableIntervals} billable / {data.EstimatedIntervals} estimated";
        AmberConnectButton.Content = "Amber ✓";
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
                        AmberConnectButton.Content = "Amber ✓";
                    else
                        AmberConnectButton.Content = "Amber · connect";
                }
            }

            if (_amberDashboard is not null && DateTimeOffset.Now - _lastAmberRefresh >= TimeSpan.FromMinutes(1))
            {
                try { await RefreshAmberAsync(); }
                catch (Exception ex) { AmberConnectButton.Content = "Amber !"; AmberStatusText.Text = "Amber: " + ex.Message; }
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
            SolarPowerText.Text = FormatPower(data.PvPowerKw.Value);
        if (data.BatterySocPercent.HasValue)
        {
            var soc = Math.Clamp(data.BatterySocPercent.Value, 0, 100);
            BatterySocText.Text = $"{soc:0.0}%";
            BatteryEnergyText.Text = $"{soc / 100d * capacity:0.0} / {capacity:0.#} kWh";
            BatterySocBar.Width = Math.Max(0, Math.Min(150, soc * 1.5));
        }
        if (data.BatteryPowerKw.HasValue)
        {
            var power = data.BatteryPowerKw.Value;
            var charging = power > 0.03;
            var discharging = power < -0.03;
            var label = charging ? "CHARGING" : discharging ? "DISCHARGING" : "IDLE";
            var accent = charging ? Color.FromRgb(43, 130, 91) : discharging ? Color.FromRgb(45, 105, 177) : Color.FromRgb(65, 73, 84);
            BatteryStatePanel.Background = new SolidColorBrush(Color.FromArgb(55, accent.R, accent.G, accent.B));
            BatteryStatePanel.BorderBrush = new SolidColorBrush(accent);
            BatteryStateText.Text = label;
            BatteryStateText.Foreground = new SolidColorBrush(charging ? Color.FromRgb(125, 224, 167) : discharging ? Color.FromRgb(133, 184, 255) : Color.FromRgb(190, 197, 207));
            BatteryPowerText.Text = FormatPower(Math.Abs(power));
        }
        var grid = data.GridPowerKw;
        GridPowerText.Text = FormatPower(Math.Abs(grid));
        var flow = Math.Abs(grid) < 0.01 ? "Grid balanced" : grid > 0 ? "Grid import" : "Grid export";
        HomeLoadText.Text = FormatPower(_dailyTracker.GetHomeLoadKw(data));
        UpdateTraySummary(data);
        TelemetryStatusText.Text = $"Sigenergy read OK · {flow} · {data.ReadAt.ToLocalTime():HH:mm:ss} · read-only";
        if (_amberSnapshot is not null)
            AmberConnectButton.Content = "Amber ✓";
    }

    private void UpdateTraySummary(SigenergyTelemetry data)
    {
        static string CompactPower(double? kw)
        {
            if (!kw.HasValue) return "—";
            var watts = Math.Abs(kw.Value) * 1000d;
            return watts >= 1000d ? $"{watts / 1000d:0.0}kW" : $"{watts:0}W";
        }

        var pv = CompactPower(data.PvPowerKw);
        var load = CompactPower(_dailyTracker.GetHomeLoadKw(data));
        var gridDirection = data.GridPowerKw > 0.01 ? "in" : data.GridPowerKw < -0.01 ? "out" : "bal";
        var grid = CompactPower(data.GridPowerKw);
        var soc = data.BatterySocPercent.HasValue ? $"{data.BatterySocPercent.Value:0}%" : "—";
        var batteryPower = data.BatteryPowerKw ?? 0;
        var batteryState = batteryPower > 0.03 ? "CHG" : batteryPower < -0.03 ? "DIS" : "IDLE";
        _trayIcon.Text = $"Amber + Sigenergy · BAT {batteryState}";
        _trayHoverWindow.UpdateSummary(data, _dailyTracker.GetHomeLoadKw(data), _amberSnapshot);
    }

    private void ShowTrayHover()
    {
        _lastTrayHover = DateTime.UtcNow;
        if (_trayHoverWindow.IsVisible) return;
        var cursor = Forms.Cursor.Position;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        _trayHoverWindow.Left = Math.Max(0, cursor.X / dpi.DpiScaleX - 365);
        _trayHoverWindow.Top = Math.Max(0, cursor.Y / dpi.DpiScaleY - 320);
        _trayHoverWindow.Show();
    }

    private static string FormatPower(double powerKw)
    {
        var watts = Math.Abs(powerKw) * 1000d;
        return watts >= 1000d
            ? $"{watts / 1000d:0.00} kW"
            : $"{watts:0} W";
    }

    private void UpdateDailyDashboard()
    {
        var totals = _dailyTracker.Totals;
        SigDailySolarText.Text = $"{totals.SolarGeneratedKwh:0.00} kWh";
        SigDailyLoadText.Text = $"{totals.HomeLoadKwh:0.00} kWh";
        SigDailyChargeText.Text = $"{totals.BatteryChargedKwh:0.00} kWh";
        SigDailyDischargeText.Text = $"{totals.BatteryDischargedKwh:0.00} kWh";
        SigDailyStatusText.Text = $"Battery charge/discharge: {( _dailyTracker.HasNativeDailyCounters ? "native Modbus counters" : "integrated estimate")}. Solar: {(_dailyTracker.HasNativePvCounter ? "native/experimental plant counter" : "integrated estimate")}. Home load: integrated from live power balance. Grid: integrated from live CT power.";
    }

    private void FeedInChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderExportPriceChart();

    private void RenderExportPriceChart()
    {
        if (FeedInChartCanvas is null || FeedInChartCanvas.ActualWidth < 100 || FeedInChartCanvas.ActualHeight < 70) return;
        FeedInChartCanvas.Children.Clear();
        _chartPoints = _amberSnapshot?.ExportPriceBands ?? Array.Empty<ExportPriceBand>();
        if (_chartPoints.Count == 0)
        {
            AddChartText("No Amber feed-in price intervals returned for today.", 8, 12, "#9BA4B0", 11);
            return;
        }

        _chartDay = _chartPoints[0].BlockStart.ToLocalTime().Date;
        _chartMin = _chartPoints.Min(p => p.LowCents);
        _chartMax = _chartPoints.Max(p => p.HighCents);
        if (_chartMax - _chartMin < 1m) { _chartMin -= 1m; _chartMax += 1m; }
        _chartLeft = 44d; _chartTop = 12d;
        var right = 14d; var bottom = 26d;
        _chartWidth = FeedInChartCanvas.ActualWidth - _chartLeft - right;
        _chartHeight = FeedInChartCanvas.ActualHeight - _chartTop - bottom;
        if (_chartWidth <= 0 || _chartHeight <= 0) return;

        double Y(decimal value) => _chartTop + (double)(_chartMax - value) / (double)(_chartMax - _chartMin) * _chartHeight;
        double X(DateTimeOffset block) => _chartLeft + Math.Clamp((block.ToLocalTime().DateTime - _chartDay).TotalMinutes / 1440d, 0, 1) * _chartWidth;

        for (var i = 0; i <= 2; i++)
        {
            var value = _chartMax - (_chartMax - _chartMin) * i / 2m;
            var y = Y(value);
            FeedInChartCanvas.Children.Add(new Line { X1 = _chartLeft, X2 = _chartLeft + _chartWidth, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(58, 65, 75)), StrokeThickness = 1 });
            AddChartText($"{value:0.#}", 2, y - 8, "#9BA4B0", 10);
        }

        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(217, 154, 50)), StrokeThickness = 2 };
        foreach (var p in _chartPoints)
        {
            var x = X(p.BlockStart);
            FeedInChartCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = Y(p.HighCents), Y2 = Y(p.LowCents), Stroke = new SolidColorBrush(Color.FromRgb(112, 184, 162)), StrokeThickness = 3, Opacity = 0.85 });
            line.Points.Add(new System.Windows.Point(x, Y(p.MidCents)));
        }
        FeedInChartCanvas.Children.Add(line);

        for (var hour = 0; hour <= 24; hour += 4)
        {
            var x = _chartLeft + hour / 24d * _chartWidth;
            AddChartText(hour == 24 ? "24:00" : $"{hour:00}:00", x - 16, _chartTop + _chartHeight + 5, "#AAB4C0", 10);
        }

        var now = DateTime.Now;
        if (now.Date == _chartDay)
        {
            var nowX = _chartLeft + now.TimeOfDay.TotalMinutes / 1440d * _chartWidth;
            _chartNowLine = new Line
            {
                X1 = nowX, X2 = nowX, Y1 = _chartTop, Y2 = _chartTop + _chartHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(244, 244, 244)),
                StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false
            };
            FeedInChartCanvas.Children.Add(_chartNowLine);
            AddChartText("NOW", Math.Min(nowX + 4, _chartLeft + _chartWidth - 34), _chartTop + 2, "#FFFFFF", 9);
        }
        _chartHoverLine = new Line
        {
            Y1 = _chartTop, Y2 = _chartTop + _chartHeight,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            StrokeThickness = 1, Opacity = 0.65, Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        FeedInChartCanvas.Children.Add(_chartHoverLine);
        _chartHoverText = new TextBlock { Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };
        _chartHoverCard = new Border { Background = new SolidColorBrush(Color.FromRgb(35, 42, 51)), BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 132)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10), Child = _chartHoverText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        FeedInChartCanvas.Children.Add(_chartHoverCard);
    }

    private void FeedInChartCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_chartPoints.Count == 0 || _chartWidth <= 0 || _chartHoverLine is null) return;
        var position = e.GetPosition(FeedInChartCanvas);
        if (position.X < _chartLeft || position.X > _chartLeft + _chartWidth) return;
        var minutes = Math.Clamp((position.X - _chartLeft) / _chartWidth * 1440d, 0, 1439.9);
        var target = _chartDay.AddMinutes(minutes);
        var nearest = _chartPoints.OrderBy(p => Math.Abs((p.BlockStart.ToLocalTime().DateTime - target).TotalMinutes)).First();
        var x = _chartLeft + (nearest.BlockStart.ToLocalTime().DateTime - _chartDay).TotalMinutes / 1440d * _chartWidth;
        _chartHoverLine.X1 = x;
        _chartHoverLine.X2 = x;
        _chartHoverLine.Visibility = Visibility.Visible;
        if (_chartHoverCard is null || _chartHoverText is null) return;
        _chartHoverText.Text = $"{nearest.BlockStart.ToLocalTime():ddd h:mm tt} · {nearest.Source}\\nLow  {nearest.LowCents:0.0#} c/kWh\\nForecast  {nearest.MidCents:0.0#} c/kWh\\nHigh  {nearest.HighCents:0.0#} c/kWh";
        _chartHoverCard.Visibility = Visibility.Visible;
        _chartHoverCard.Measure(new Size(270, 160));
        var cardWidth = _chartHoverCard.DesiredSize.Width;
        var cardHeight = _chartHoverCard.DesiredSize.Height;
        var cardLeft = position.X + 14 + cardWidth > FeedInChartCanvas.ActualWidth ? position.X - cardWidth - 14 : position.X + 14;
        var cardTop = position.Y + 14 + cardHeight > FeedInChartCanvas.ActualHeight ? position.Y - cardHeight - 14 : position.Y + 14;
        Canvas.SetLeft(_chartHoverCard, Math.Max(4, cardLeft));
        Canvas.SetTop(_chartHoverCard, Math.Max(4, cardTop));
    }

    private void FeedInChartCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_chartHoverLine is not null) _chartHoverLine.Visibility = Visibility.Collapsed;
        if (_chartHoverCard is not null) _chartHoverCard.Visibility = Visibility.Collapsed;
    }

    private void AddChartText(string text, double left, double top, string colour, double size)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(
                (byte)(System.Convert.ToInt32(colour.TrimStart('#'), 16) >> 16),
                (byte)(System.Convert.ToInt32(colour.TrimStart('#'), 16) >> 8),
                (byte)System.Convert.ToInt32(colour.TrimStart('#'), 16))),
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
        this.WindowState = System.Windows.WindowState.Maximized;
        Activate();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        _refreshTimer.Stop();
        _trayHoverTimer.Stop();
        _trayHoverWindow.Close();
        _dailyTracker.Save();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
