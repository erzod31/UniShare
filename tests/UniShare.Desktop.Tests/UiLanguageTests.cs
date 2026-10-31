using UniShare.Desktop;

namespace UniShare.Desktop.Tests;

public sealed class UiLanguageTests
{
    [Theory]
    [InlineData("Guardar", "Save")]
    [InlineData("El índice de destino de Obsidian no pertenece a UniShare. Conserva el archivo o elige otra bóveda.",
        "The destination Obsidian index does not belong to UniShare. Keep the file or choose another vault.")]
    [InlineData("La nota de destino de Obsidian no tiene marcadores válidos de UniShare. Conserva el archivo o elige otra bóveda.",
        "The destination Obsidian note does not have valid UniShare markers. Keep the file or choose another vault.")]
    [InlineData("Carpeta: Lecturas", "Folder: Lecturas")]
    [InlineData("Eliminar (3)", "Delete (3)")]
    [InlineData("Se aplicó la versión recibida.", "The received version was applied.")]
    [InlineData("3 conflicto(s) pendiente(s).", "3 pending conflicts.")]
    [InlineData("1 descarga.", "1 download.")]
    [InlineData("4 descargas.", "4 downloads.")]
    [InlineData("3 elementos seleccionados. Eliminar y restaurar se aplicarán al grupo.",
        "3 items selected. Delete and restore will apply to the group.")]
    public void TranslateEnglishCoversStaticAndDynamicText(string spanish, string expected) =>
        Assert.Equal(expected, UiLanguage.Translate(spanish, UiLanguageChoice.English));

    [Fact]
    public void TranslateSpanishPreservesOriginalText() =>
        Assert.Equal("Guardar", UiLanguage.Translate("Guardar", UiLanguageChoice.Spanish));

    [Fact]
    public void TranslateUnknownTextDoesNotAlterUserContent() =>
        Assert.Equal("Mi título personal", UiLanguage.Translate("Mi título personal", UiLanguageChoice.English));
}
