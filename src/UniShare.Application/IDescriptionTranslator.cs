namespace UniShare.Application;

/// <summary>A derived reading view. Translation never edits the library item.</summary>
public sealed record DescriptionTranslationResult(string Status, string Text, string Source, string Target)
{
    public bool IsTranslated => Status == "translated";
}

public interface IDescriptionTranslator
{
    Task<DescriptionTranslationResult> TranslateAsync(string text, string targetLanguage,
        CancellationToken cancellationToken = default);
}
