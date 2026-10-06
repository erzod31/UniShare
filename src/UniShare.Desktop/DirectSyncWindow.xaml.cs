using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using UniShare.Infrastructure;

namespace UniShare.Desktop;

public partial class DirectSyncWindow : Window
{
    private readonly DirectSyncServer _server;
    private string? _pairingLink;

    public DirectSyncWindow(
        DirectSyncServer server,
        bool isRunning,
        string? startError,
        string? tailscaleSetupError)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
        InitializeComponent();
        LocalPortBox.Text = server.Configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PairingKeyBox.Text = server.Configuration.PairingKey;
        CommandBox.Text = server.Configuration.TailscaleServeCommand;
        EndpointBox.Text = server.Configuration.PublicEndpoint ?? string.Empty;
        ServerStatus.Text = isRunning
            ? $"Servidor local activo en {server.Configuration.LocalAddress}."
            : $"El servidor local no pudo iniciarse: {startError ?? "error desconocido"}";
        ServerStatus.Foreground = isRunning
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.Firebrick;

        if (!string.IsNullOrWhiteSpace(server.Configuration.PublicEndpoint))
        {
            RenderPairingQr();
        }
        else if (!string.IsNullOrWhiteSpace(tailscaleSetupError))
        {
            EndpointStatus.Text = tailscaleSetupError;
            EndpointStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(CommandBox.Text);

    private void CopyPort_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LocalPortBox.Text);
        ConnectionStatus.Text = UiLanguage.T("Puerto local copiado.");
        ConnectionStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
    }

    private void CopyPairingKey_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_server.Configuration.PairingKey);
        ConnectionStatus.Text = UiLanguage.T("Clave privada copiada. Pégala en Edge y evita compartirla.");
        ConnectionStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
    }

    private void SaveEndpoint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _server.UpdatePublicEndpoint(EndpointBox.Text);
            EndpointBox.Text = _server.Configuration.PublicEndpoint;
            EndpointStatus.Text = UiLanguage.T("Dirección guardada. Ya puedes escanear el QR.");
            EndpointStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
            RenderPairingQr();
        }
        catch (ArgumentException error)
        {
            EndpointStatus.Text = error.Message;
            EndpointStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private void RenderPairingQr()
    {
        var endpoint = _server.Configuration.PublicEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        _pairingLink = $"unishare://pair?endpoint={Uri.EscapeDataString(endpoint)}" +
            $"&key={Uri.EscapeDataString(_server.Configuration.PairingKey)}";
        using var data = QRCodeGenerator.GenerateQrCode(_pairingLink, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(8, drawQuietZones: true));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        PairingQr.Source = image;
        QrPanel.Visibility = Visibility.Visible;
        QrPlaceholder.Visibility = Visibility.Collapsed;
        CopyPairingLinkButton.Visibility = Visibility.Visible;
    }

    private void CopyPairingLink_Click(object sender, RoutedEventArgs e)
    {
        if (_pairingLink is not null)
        {
            Clipboard.SetText(_pairingLink);
        }
    }
}
