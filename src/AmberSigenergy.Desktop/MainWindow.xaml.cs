using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmberSigenergy.Desktop.Services;

namespace AmberSigenergy.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

            var api = new AmberApiClient(token);
            using var sites = await api.GetSitesAsync();
            var formatted = JsonSerializer.Serialize(sites.RootElement, new JsonSerializerOptions { WriteIndented = true });
            ShowSelectableText("Amber connected — site details",
                "Amber API connection successful. The site details are below. Select the channel type values (for E1 and B1), or use Copy all.");
            ShowSiteDetails(formatted);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Amber connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowSelectableText(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowSiteDetails(string formatted)
    {
        var textBox = new TextBox
        {
            Text = formatted,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(10)
        };

        var copyButton = new Button
        {
            Content = "Copy all",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(formatted);
            copyButton.Content = "Copied";
        };

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(copyButton, Dock.Bottom);
        panel.Children.Add(copyButton);
        panel.Children.Add(textBox);

        var window = new Window
        {
            Title = "Amber site details — selectable",
            Owner = this,
            Width = 720,
            Height = 560,
            MinWidth = 480,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        window.ShowDialog();
    }
}
