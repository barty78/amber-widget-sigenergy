using System.Text.Json;
using System.Windows;
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
            MessageBox.Show(this,
                "Amber API connection successful. Your site response is shown below so we can identify the correct electricity site.\n\n" + formatted,
                "Amber connected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Amber connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
