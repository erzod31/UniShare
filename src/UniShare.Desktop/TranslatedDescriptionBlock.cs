using System.Windows;
using System.Windows.Controls;
using UniShare.Application;

namespace UniShare.Desktop;

/// <summary>A translated reading view with explicit access to the unchanged original.</summary>
public sealed class TranslatedDescriptionBlock : UserControl, IDisposable
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(
        nameof(ContentText), typeof(string), typeof(TranslatedDescriptionBlock),
        new FrameworkPropertyMetadata(string.Empty, OnContentChanged));
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TranslatedDescriptionBlock), new PropertyMetadata(false));
    private readonly LinkifiedTextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) };
    private readonly Button _toggle = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
    private readonly Button _retry = new() { Content = UiLanguage.T("Reintentar traducción"), HorizontalAlignment = HorizontalAlignment.Left };
    private CancellationTokenSource? _request;
    private DescriptionTranslationResult? _result;
    private bool _showOriginal;

    public TranslatedDescriptionBlock()
    {
        var panel = new StackPanel();
        panel.Children.Add(_text);
        panel.Children.Add(_status);
        panel.Children.Add(_toggle);
        panel.Children.Add(_retry);
        Content = panel;
        _status.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
        _toggle.Click += (_, _) => { _showOriginal = !_showOriginal; Render(); };
        _retry.Click += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            DescriptionTranslation.Changed += SettingsChanged;
            await RefreshAsync();
        };
        Unloaded += (_, _) => Dispose();
    }

    public string? ContentText
    {
        get => (string?)GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private static async void OnContentChanged(DependencyObject control, DependencyPropertyChangedEventArgs _) =>
        await ((TranslatedDescriptionBlock)control).RefreshAsync();

    private async void SettingsChanged(object? sender, EventArgs _) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var request = _request;
        _result = null;
        _showOriginal = false;
        Render();
        var original = ContentText;
        if (!IsLoaded || !DescriptionTranslation.Enabled || string.IsNullOrWhiteSpace(original) ||
            original.Length > 100_000 || DescriptionTranslation.Translator is not { } translator)
        {
            return;
        }
        _status.Text = UiLanguage.T("Traduciendo en el dispositivo… La primera vez se descargan los idiomas.");
        _status.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
        try
        {
            var result = await translator.TranslateAsync(original, DescriptionTranslation.Target, request.Token);
            if (!request.IsCancellationRequested && ReferenceEquals(_request, request))
            {
                _result = result;
                Render();
            }
        }
        catch (OperationCanceledException)
        {
            // A recycled row, edit or setting change must never display an older response.
        }
    }

    private void Render()
    {
        _text.TextWrapping = Compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _text.TextTrimming = Compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        _text.ContentText = _result?.IsTranslated == true && !_showOriginal ? _result.Text : ContentText;
        _toggle.Visibility = !Compact && _result?.IsTranslated == true ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = UiLanguage.T(_showOriginal ? "Ver traducción" : "Ver original");
        _status.Text = _result?.Status switch
        {
            "translated" => UiLanguage.T(_showOriginal ? "Descripción original" : "Traducción automática local"),
            "unsupported" => UiLanguage.T("Idioma no identificado o no compatible. Se muestra el original."),
            "unavailable" => UiLanguage.T("Traducción no disponible. Se muestra el original. Comprueba la conexión para descargar los idiomas."),
            _ => string.Empty,
        };
        _status.Visibility = !Compact && _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = !Compact && _result?.Status == "unavailable" ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = _status.Text.Length > 0 ? _status.Text : null;
    }

    public void Dispose()
    {
        DescriptionTranslation.Changed -= SettingsChanged;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
    }
}
