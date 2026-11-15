using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop;

public sealed class TrayHoverWindow : Window
{
    private readonly Border _panel;
    private readonly TextBlock _state;
    private readonly TextBlock _batteryPower;
    private readonly TextBlock _soc;
    private readonly TextBlock _solar;
    private readonly TextBlock _grid;
    private readonly TextBlock _home;
    private readonly TextBlock _currentPrice;
    private readonly TextBlock _nextPrice;
    private readonly TextBlock _priceHeading;

    public bool IsPointerInside { get; set; }
    public event EventHandler? PointerEntered;
    public event EventHandler? PointerLeft;

    public TrayHoverWindow()
    {
        Width = 360;
        Height = 310;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        _state = new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        _batteryPower = new TextBlock { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0) };
        _soc = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(226, 233, 240)) };
        _solar = NewMetricValue();
        _home = NewMetricValue();
        _grid = NewMetricValue();

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 15) };
        metrics.Children.Add(Metric("SOLAR", _solar));
        metrics.Children.Add(Metric("HOME", _home));
        metrics.Children.Add(Metric("GRID", _grid));

        _priceHeading = new TextBlock { Text = "LIVE ENERGY PRICE", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(207, 218, 229)) };
        _currentPrice = new TextBlock { FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 0) };
        _nextPrice = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(226, 233, 240)), Margin = new Thickness(0, 3, 0, 0) };

        var content = new StackPanel { Margin = new Thickness(19) };
        content.Children.Add(_state);
        content.Children.Add(_batteryPower);
        content.Children.Add(_soc);
        content.Children.Add(metrics);
        content.Children.Add(_priceHeading);
        content.Children.Add(_currentPrice);
        content.Children.Add(_nextPrice);

        _panel = new Border
        {
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Child = content,
            Background = new SolidColorBrush(Color.FromRgb(38, 45, 55)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 112, 126))
        };
        Content = _panel;
        MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
        MouseLeave += (_, _) => PointerLeft?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSummary(SigenergyTelemetry data, double homeLoadKw, AmberDashboardSnapshot? amber)
    {
        var batteryKw = data.BatteryPowerKw ?? 0;
        var charging = batteryKw > 0.03;
        var discharging = batteryKw < -0.03;
        var importing = data.GridPowerKw > 0.01;
        var exporting = data.GridPowerKw < -0.01;
        var state = charging ? "BATTERY CHARGING" : discharging ? "BATTERY DISCHARGING" : "BATTERY IDLE";
        var background = charging ? Color.FromRgb(31, 105, 70) : discharging ? Color.FromRgb(32, 76, 135) : Color.FromRgb(38, 45, 55);
        var border = charging ? Color.FromRgb(74, 190, 127) : discharging ? Color.FromRgb(92, 157, 238) : Color.FromRgb(100, 112, 126);
        _panel.Background = new SolidColorBrush(background);
        _panel.BorderBrush = new SolidColorBrush(border);
        _state.Text = state;
        _batteryPower.Text = $"{FormatPower(Math.Abs(batteryKw))}  ·  {data.BatterySocPercent:0}%";
        _soc.Text = "BATTERY POWER  /  STATE OF CHARGE";
        _solar.Text = FormatPower(data.PvPowerKw);
        _home.Text = FormatPower(homeLoadKw);
        var direction = importing ? "IMPORT" : exporting ? "EXPORT" : "BALANCED";
        _grid.Text = $"{direction}  {FormatPower(Math.Abs(data.GridPowerKw))}";

        if (amber is null)
        {
            _priceHeading.Text = "AMBER PRICE";
            _currentPrice.Text = "Waiting for Amber";
            _nextPrice.Text = "Connect Amber to show live and next-slot prices";
            return;
        }

        var nextTime = amber.NextPriceBlockStart?.ToLocalTime().ToString("h:mm tt") ?? "next slot";
        if (importing)
        {
            _priceHeading.Text = "IMPORT PRICE · CURRENT";
            _currentPrice.Text = FormatPrice(amber.CurrentImportPriceCents);
            _nextPrice.Text = $"Next ({nextTime}): {FormatPrice(amber.NextImportPriceCents)}";
        }
        else if (exporting)
        {
            _priceHeading.Text = "EXPORT PRICE · CURRENT";
            _currentPrice.Text = FormatPrice(amber.CurrentExportPriceCents);
            _nextPrice.Text = $"Next ({nextTime}): {FormatPrice(amber.NextExportPriceCents)}";
        }
        else
        {
            _priceHeading.Text = "ENERGY PRICES · GRID BALANCED";
            _currentPrice.Text = $"Import {FormatPrice(amber.CurrentImportPriceCents)}";
            _nextPrice.Text = $"Export {FormatPrice(amber.CurrentExportPriceCents)}  ·  Next slot {nextTime}: import {FormatPrice(amber.NextImportPriceCents)} / export {FormatPrice(amber.NextExportPriceCents)}";
        }
    }

    private static string FormatPrice(decimal? cents) => cents.HasValue ? $"{cents.Value:0.0#} c/kWh" : "—";
    private static TextBlock NewMetricValue() => new()
    {
        Text = "—", FontSize = 15, FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0)
    };

    private static StackPanel Metric(string heading, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(207, 218, 229)) });
        panel.Children.Add(value);
        return panel;
    }

    private static string FormatPower(double? kw)
    {
        if (!kw.HasValue) return "—";
        var watts = Math.Abs(kw.Value) * 1000;
        return watts >= 1000 ? $"{watts / 1000:0.00} kW" : $"{watts:0} W";
    }
}
