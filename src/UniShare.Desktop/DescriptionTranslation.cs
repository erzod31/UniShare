using System.IO;
using UniShare.Application;

namespace UniShare.Desktop;

internal static class DescriptionTranslation
{
    private static string? _preferencePath;
    internal static IDescriptionTranslator? Translator { get; private set; }
    internal static bool Enabled { get; private set; } = true;
    internal static event EventHandler? Changed;
    internal static string Target => UiLanguage.Choice == UiLanguageChoice.English ? "en" : "es";

    internal static void Initialize(IDescriptionTranslator translator, string profileRoot)
    {
        Translator = translator;
        _preferencePath = Path.Combine(profileRoot, "config", "automatic-translation.txt");
        Enabled = !File.Exists(_preferencePath) || File.ReadAllText(_preferencePath).Trim() != "off";
    }

    internal static void Toggle()
    {
        if (_preferencePath is null)
        {
            return;
        }
        Enabled = !Enabled;
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
        var partial = _preferencePath + ".partial";
        File.WriteAllText(partial, Enabled ? "on" : "off");
        File.Move(partial, _preferencePath, overwrite: true);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
