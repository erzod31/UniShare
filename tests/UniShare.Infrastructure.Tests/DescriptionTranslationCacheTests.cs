using System.Text.Json;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class DescriptionTranslationCacheTests
{
    [Fact]
    public async Task RestartKeepsDerivedTextButEditingOrChangingLanguageInvalidatesIt()
    {
        using var directory = new TemporaryDirectory();
        const string original = "Hello 🌍\nhttps://example.com/?a=1&b=2";
        var cache = new DescriptionTranslationCache(directory.Path);
        var translated = new DescriptionTranslationResult("translated", "Hola 🌍\nhttps://example.com/?a=1&b=2", "en", "es");
        await cache.WriteAsync(original, translated, TestContext.Current.CancellationToken);

        var restarted = new DescriptionTranslationCache(directory.Path);
        Assert.Equal(translated, await restarted.ReadAsync(original, "es", TestContext.Current.CancellationToken));
        Assert.Null(await restarted.ReadAsync(original + " edited", "es", TestContext.Current.CancellationToken));
        Assert.Null(await restarted.ReadAsync(original, "en", TestContext.Current.CancellationToken));
        Assert.Equal("Hello 🌍\nhttps://example.com/?a=1&b=2", original);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("error")]
    public async Task TemporaryFailuresAreNotCachedAndDoNotPoisonRetries(string status)
    {
        using var directory = new TemporaryDirectory();
        var cache = new DescriptionTranslationCache(directory.Path);
        await cache.WriteAsync("Hello", new DescriptionTranslationResult(status, "Hello", "en", "es"), TestContext.Current.CancellationToken);
        Assert.Null(await cache.ReadAsync("Hello", "es", TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DamagedOrWrongLanguageCacheIsIgnored()
    {
        using var directory = new TemporaryDirectory();
        var cache = new DescriptionTranslationCache(directory.Path);
        var path = Path.Combine(directory.Path, DescriptionTranslationCache.Key("Hello", "es") + ".json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        Assert.Null(await cache.ReadAsync("Hello", "es", TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new DescriptionTranslationResult("translated", "Bonjour", "en", "en")), TestContext.Current.CancellationToken);
        Assert.Null(await cache.ReadAsync("Hello", "es", TestContext.Current.CancellationToken));
        await cache.WriteAsync("Hello", new DescriptionTranslationResult("translated", "Hola", "en", "es"), TestContext.Current.CancellationToken);
        Assert.Equal("Hola", (await cache.ReadAsync("Hello", "es", TestContext.Current.CancellationToken))!.Text);
    }

    [Fact]
    public void TargetsAndPathLikeInputsCannotEscapeTheCache()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DescriptionTranslationCache.Key("Hello", "../../other"));
        Assert.Matches("^[0-9a-f]{64}$", DescriptionTranslationCache.Key("../../private 🌍", "es"));
        Assert.NotEqual(DescriptionTranslationCache.Key("Hello", "es"), DescriptionTranslationCache.Key("Hello", "en"));
    }
}
