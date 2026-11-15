using Window = System.Windows.Window;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using AmberSigenergy.Desktop.Services;

namespace AmberSigenergy.Desktop;

public partial class AmberConnectWindow : Window
{
    public AmberConnectWindow()
    {
        InitializeComponent();
        TokenInput.Password = AmberTokenStore.Load() ?? string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TokenInput.Password))
        {
            MessageBox.Show(this, "Enter your Amber API token first.", "Token required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            AmberTokenStore.Save(TokenInput.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save token", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
