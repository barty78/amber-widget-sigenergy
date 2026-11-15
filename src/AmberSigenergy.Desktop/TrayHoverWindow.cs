using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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

    public bool IsPointerInside { get; set; }
    public event EventHandler? PointerEntered;
    public event EventHandler? PointerLeft;

    public TrayHoverWindow()
    {
        Width = 320;
        Height = 190;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        _state = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        _batteryPower = new TextBlock { FontSize = 29, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0) };
        _soc = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(226, 233, 240)) };
        _solar = NewMetricValue();
        _home = NewMetricValue();
        _grid = NewMetricValue();

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 15, 0, 0) };
        metrics.Children.Add(Metric("SOLAR", _solar));
        metrics.Children.Add(Metric("HOME", _home));
        metrics.Children.Add(Metric("GRID", _grid));

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(_state);
        content.Children.Add(_batteryPower);
        content.Children.Add(_soc);
        content.Children.Add(metrics);

        _panel = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Child = content,
            Background = new SolidColorBrush(Color.FromRgb(48, 57, 68)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 112, 126))
        };
        Content = _panel;
        MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
        MouseLeave += (_, _) => PointerLeft?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSummary(SigenergyTelemetry data, double homeLoadKw)
    {
        var batteryKw = data.BatteryPowerKw ?? 0;
        var charging = batteryKw > 0.03;
        var discharging = batteryKw < -0.03;
        var state = charging ? "CHARGING" : discharging ? "DISCHARGING" : "BATTERY IDLE";
        var background = charging ? Color.FromRgb(31, 105, 70) : discharging ? Color.FromRgb(32, 76, 135) : Color.FromRgb(48, 57, 68);
        var border = charging ? Color.FromRgb(74, 190, 127) : discharging ? Color.FromRgb(92, 157, 238) : Color.FromRgb(100, 112, 126);
        _panel.Background = new SolidColorBrush(background);
        _panel.BorderBrush = new SolidColorBrush(border);
        _state.Text = state;
        _batteryPower.Text = $"{FormatPower(Math.Abs(batteryKw))}  ·  {data.BatterySocPercent:0}%";
        _soc.Text = "BATTERY POWER  /  STATE OF CHARGE";
        _solar.Text = FormatPower(data.PvPowerKw);
        _home.Text = FormatPower(homeLoadKw);
        var direction = data.GridPowerKw > 0.01 ? "IMPORT" : data.GridPowerKw < -0.01 ? "EXPORT" : "BALANCED";
        _grid.Text = $"{direction}  {FormatPower(Math.Abs(data.GridPowerKw))}";
    }

    private static TextBlock NewMetricValue() => new()
    {
        Text = "—",
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 3, 0, 0)
    };

    private static StackPanel Metric(string heading, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(207, 218, 229)) });
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
