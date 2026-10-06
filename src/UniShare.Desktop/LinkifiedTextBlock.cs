using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace UniShare.Desktop;

public sealed class LinkifiedTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(
        nameof(ContentText),
        typeof(string),
        typeof(LinkifiedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, OnContentTextChanged));

    public string? ContentText
    {
        get => (string?)GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    private static void OnContentTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var control = (LinkifiedTextBlock)dependencyObject;
        control.RebuildInlines(eventArgs.NewValue as string);
    }

    private void RebuildInlines(string? text)
    {
        Inlines.Clear();
        foreach (var segment in SummaryLinkParser.Parse(text))
        {
            if (segment.Uri is null)
            {
                Inlines.Add(new Run(segment.Text));
                continue;
            }

            var hyperlink = new Hyperlink(new Run(segment.Text))
            {
                NavigateUri = segment.Uri,
                ToolTip = $"Abrir {segment.Uri.AbsoluteUri}",
            };
            hyperlink.RequestNavigate += OpenLink;
            Inlines.Add(hyperlink);
        }
    }

    private static void OpenLink(object sender, RequestNavigateEventArgs eventArgs)
    {
        if (eventArgs.Uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(eventArgs.Uri.AbsoluteUri) { UseShellExecute = true });
        eventArgs.Handled = true;
    }
}

public sealed record SummaryTextSegment(string Text, Uri? Uri);

public static partial class SummaryLinkParser
{
    public static IReadOnlyList<SummaryTextSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = new List<SummaryTextSegment>();
        var nextTextIndex = 0;
        foreach (Match match in WebLinkRegex().Matches(text))
        {
            var linkText = TrimTrailingPunctuation(match.Value);
            if (linkText.Length == 0 ||
                !Uri.TryCreate(linkText, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            if (match.Index > nextTextIndex)
            {
                segments.Add(new SummaryTextSegment(text[nextTextIndex..match.Index], null));
            }

            segments.Add(new SummaryTextSegment(linkText, uri));
            nextTextIndex = match.Index + linkText.Length;
        }

        if (nextTextIndex < text.Length)
        {
            segments.Add(new SummaryTextSegment(text[nextTextIndex..], null));
        }

        return segments;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        var length = value.Length;
        while (length > 0)
        {
            var character = value[length - 1];
            if (character is '.' or ',' or ';' or ':' or '!' or '?')
            {
                length--;
                continue;
            }

            if (character is ')' or ']' or '}')
            {
                var opening = character switch { ')' => '(', ']' => '[', _ => '{' };
                var candidate = value.AsSpan(0, length);
                if (candidate.Count(character) > candidate.Count(opening))
                {
                    length--;
                    continue;
                }
            }

            break;
        }

        return value[..length];
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebLinkRegex();
}
