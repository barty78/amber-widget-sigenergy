using System.Windows;
using System.Windows.Threading;
using AmberSigenergy.Desktop.Models;
using AmberSigenergy.Desktop.Services;

namespace AmberSigenergy.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new();
    private AppSettings? _settings;
    private AmberApiClient? _amberApi;
    private AmberDashboardService? _amberDashboard;
    private SigenergyModbusClient? _sigenergy;
    private AmberDashboardSnapshot? _amberSnapshot;
    private DateTimeOffset _lastAmberRefresh = DateTimeOffset.MinValue;
    private bool _polling;

    public MainWindow()
    {
        InitializeComponent();

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
                    UpdateSigenergyDashboard(telemetry);
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
        TelemetryStatusText.Text = $"Sigenergy read OK · {flow} · {data.ReadAt.ToLocalTime():HH:mm:ss} · read-only";
        if (_amberSnapshot is not null)
            ConnectionStatusText.Text = "Amber + Sigenergy connected";
    }
}
