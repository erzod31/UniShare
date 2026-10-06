using System.IO;
using System.Windows;
using Microsoft.Win32;
using UniShare.Application;

namespace UniShare.Desktop;

public partial class ItemEditorWindow : Window
{
    public ItemEditorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TitleBox.Focus();
    }

    public CreateItemRequest? Request { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "Seleccionar archivo para UniShare",
        };
        if (dialog.ShowDialog(this) == true)
        {
            FileBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show(this, UiLanguage.T("Escribe un título."), "UniShare", MessageBoxButton.OK, MessageBoxImage.Information);
            TitleBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(UrlBox.Text) && string.IsNullOrWhiteSpace(FileBox.Text))
        {
            MessageBox.Show(this, UiLanguage.T("Indica un enlace, un archivo o ambos."), "UniShare", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Request = new CreateItemRequest(
            TitleBox.Text,
            UrlBox.Text,
            FileBox.Text,
            SourceBox.Text,
            AuthorBox.Text,
            DescriptionBox.Text,
            FavoriteBox.IsChecked == true,
            SplitNames(TagsBox.Text),
            SplitNames(CollectionsBox.Text));
        DialogResult = true;
    }

    private static string[] SplitNames(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
