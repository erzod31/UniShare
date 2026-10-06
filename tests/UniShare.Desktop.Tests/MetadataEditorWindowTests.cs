using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Desktop.Tests;

public sealed class MetadataEditorWindowTests
{
    [Fact]
    public void ExistingSummaryStaysReadOnlyUntilModifyButtonIsPressed()
    {
        RunInSta(() =>
        {
            var application = System.Windows.Application.Current as App;
            if (application is null)
            {
                application = new App();
                application.InitializeComponent();
            }
            var item = LibraryItem.Create(
                ItemKind.Link,
                "Ejemplo",
                "https://example.com",
                DateTimeOffset.UtcNow,
                description: "Consulta https://example.com/guide.");
            var window = new MetadataEditorWindow(item, new ItemOrganization([], []));
            var preview = Assert.IsType<Border>(window.FindName("DescriptionPreview"));
            var previewText = Assert.IsType<LinkifiedTextBlock>(window.FindName("DescriptionPreviewText"));
            var modify = Assert.IsType<Button>(window.FindName("ModifyDescriptionButton"));
            var editor = Assert.IsType<TextBox>(window.FindName("DescriptionBox"));

            Assert.Equal(Visibility.Visible, preview.Visibility);
            Assert.Equal(Visibility.Collapsed, editor.Visibility);
            Assert.Single(previewText.Inlines.OfType<Hyperlink>());

            modify.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Collapsed, preview.Visibility);
            Assert.Equal(Visibility.Collapsed, modify.Visibility);
            Assert.Equal(Visibility.Visible, editor.Visibility);
            Assert.Equal("Consulta https://example.com/guide.", editor.Text);
            window.Close();
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "UniShare.WpfTest",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(60)),
            "La comprobación WPF no terminó en 60 segundos.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
