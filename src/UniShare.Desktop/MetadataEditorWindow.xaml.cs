using System.Windows;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Desktop;

public partial class MetadataEditorWindow : Window
{
    public MetadataEditorWindow(LibraryItem item, ItemOrganization organization)
    {
        InitializeComponent();
        TitleBox.Text = item.Title;
        SourceBox.Text = item.Source;
        AuthorBox.Text = item.Author;
        DescriptionBox.Text = item.Description;
        DescriptionPreviewText.ContentText = string.IsNullOrWhiteSpace(item.Description)
            ? "Sin resumen disponible."
            : item.Description;
        FavoriteBox.IsChecked = item.Favorite;
        TagsBox.Text = string.Join(", ", organization.Tags.Select(tag => tag.Name));
        CollectionsBox.Text = string.Join(", ", organization.Collections.Select(collection => collection.Name));
    }

    public string ItemTitle => TitleBox.Text;
    public string? ItemSource => SourceBox.Text;
    public string? ItemAuthor => AuthorBox.Text;
    public string? ItemDescription => DescriptionBox.Text;
    public bool ItemFavorite => FavoriteBox.IsChecked == true;
    public IReadOnlyCollection<string> ItemTags => SplitNames(TagsBox.Text);
    public IReadOnlyCollection<string> ItemCollections => SplitNames(CollectionsBox.Text);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show(this, UiLanguage.T("Escribe un título."), "UniShare", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void ModifyDescription_Click(object sender, RoutedEventArgs e)
    {
        DescriptionPreview.Visibility = Visibility.Collapsed;
        ModifyDescriptionButton.Visibility = Visibility.Collapsed;
        DescriptionBox.Visibility = Visibility.Visible;
        DescriptionBox.Focus();
        DescriptionBox.CaretIndex = DescriptionBox.Text.Length;
    }

    private static string[] SplitNames(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

