using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace UniShare.Desktop;

internal enum DesktopThemeChoice
{
    System,
    Light,
    Dark,
}

internal static class DesktopTheme
{
    private static DesktopThemeChoice _choice;
    private static int _textScaleIndex;

    internal static DesktopThemeChoice Choice => _choice;

    internal static void LoadAndApply(System.Windows.Application application)
    {
        var raw = File.Exists(ConfigurationPath()) ? File.ReadAllText(ConfigurationPath()).Trim() : null;
        _choice = Enum.TryParse<DesktopThemeChoice>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : DesktopThemeChoice.System;
        var scaleRaw = File.Exists(TextScaleConfigurationPath())
            ? File.ReadAllText(TextScaleConfigurationPath()).Trim()
            : null;
        _textScaleIndex = int.TryParse(scaleRaw, out var scale) ? Math.Clamp(scale, 0, 2) : 0;
        Apply(application);
        ApplyTextScale(application);
    }

    internal static void CycleAndApply(System.Windows.Application application)
    {
        _choice = _choice switch
        {
            DesktopThemeChoice.System => DesktopThemeChoice.Light,
            DesktopThemeChoice.Light => DesktopThemeChoice.Dark,
            _ => DesktopThemeChoice.System,
        };
        Save();
        Apply(application);
    }

    internal static string Label => UiLanguage.T(_choice switch
    {
        DesktopThemeChoice.Light => "Claro",
        DesktopThemeChoice.Dark => "Oscuro",
        _ => "Sistema",
    });

    internal static string TextScaleLabel => UiLanguage.T(_textScaleIndex switch
    {
        1 => "Grande",
        2 => "Muy grande",
        _ => "Normal",
    });

    internal static void CycleTextScaleAndApply(System.Windows.Application application)
    {
        _textScaleIndex = (_textScaleIndex + 1) % 3;
        var path = TextScaleConfigurationPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        File.WriteAllText(partial, _textScaleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.Move(partial, path, overwrite: true);
        ApplyTextScale(application);
    }

    private static void Apply(System.Windows.Application application)
    {
        if (SystemParameters.HighContrast)
        {
            Set(application, "WindowBackgroundBrush", SystemColors.WindowBrush);
            Set(application, "PanelBackgroundBrush", SystemColors.WindowBrush);
            Set(application, "SubtleBackgroundBrush", SystemColors.ControlBrush);
            Set(application, "BorderBrush", SystemColors.ActiveBorderBrush);
            Set(application, "PrimaryBrush", SystemColors.HighlightBrush);
            Set(application, "FocusBrush", SystemColors.HotTrackBrush);
            Set(application, "PrimaryTextBrush", SystemColors.WindowTextBrush);
            Set(application, "SecondaryTextBrush", SystemColors.GrayTextBrush);
            Set(application, "IconBackgroundBrush", SystemColors.ControlBrush);
            Set(application, "IconTextBrush", SystemColors.HighlightBrush);
            Set(application, "StatusBrush", SystemColors.WindowTextBrush);
            Set(application, "SidebarBackgroundBrush", SystemColors.ControlBrush);
            Set(application, "AccentSubtleBrush", SystemColors.InactiveSelectionHighlightBrush);
            Set(application, "HoverBackgroundBrush", SystemColors.ControlLightBrush);
            Set(application, "SuccessBrush", SystemColors.WindowTextBrush);
            Set(application, "WarningBrush", SystemColors.WindowTextBrush);
            Set(application, "DangerBrush", SystemColors.WindowTextBrush);
            return;
        }

        var dark = _choice == DesktopThemeChoice.Dark ||
            (_choice == DesktopThemeChoice.System && SystemUsesDarkTheme());
        var colors = dark
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#0C111A",
                ["PanelBackgroundBrush"] = "#131A25",
                ["SubtleBackgroundBrush"] = "#192231",
                ["BorderBrush"] = "#2B3748",
                ["PrimaryBrush"] = "#7C5CFC",
                ["FocusBrush"] = "#9C8CFF",
                ["PrimaryTextBrush"] = "#F3F4F6",
                ["SecondaryTextBrush"] = "#AEB8C9",
                ["IconBackgroundBrush"] = "#27234A",
                ["IconTextBrush"] = "#B8ACFF",
                ["StatusBrush"] = "#A8B4C8",
                ["SidebarBackgroundBrush"] = "#121A26",
                ["AccentSubtleBrush"] = "#27234A",
                ["HoverBackgroundBrush"] = "#252F3E",
                ["SuccessBrush"] = "#57D3B0",
                ["WarningBrush"] = "#F4B74F",
                ["DangerBrush"] = "#FF8D86",
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#F6F7FB",
                ["PanelBackgroundBrush"] = "#FFFFFF",
                ["SubtleBackgroundBrush"] = "#EEF2F8",
                ["BorderBrush"] = "#D8DEEA",
                ["PrimaryBrush"] = "#6554D9",
                ["FocusBrush"] = "#6554D9",
                ["PrimaryTextBrush"] = "#172033",
                ["SecondaryTextBrush"] = "#5A6478",
                ["IconBackgroundBrush"] = "#ECEAFF",
                ["IconTextBrush"] = "#5A48C8",
                ["StatusBrush"] = "#59657A",
                ["SidebarBackgroundBrush"] = "#F0F3F9",
                ["AccentSubtleBrush"] = "#ECEAFF",
                ["HoverBackgroundBrush"] = "#F0F2F7",
                ["SuccessBrush"] = "#167A62",
                ["WarningBrush"] = "#99600A",
                ["DangerBrush"] = "#B42318",
            };
        foreach (var (key, value) in colors)
        {
            Set(application, key, new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)));
        }

        // Los templates nativos de WPF consultan estas claves del sistema para popups y menús.
        // Se redefinen dentro de la aplicación para evitar texto claro sobre fondos blancos en tema oscuro.
        application.Resources[SystemColors.ControlBrushKey] = application.Resources["PanelBackgroundBrush"];
        application.Resources[SystemColors.ControlTextBrushKey] = application.Resources["PrimaryTextBrush"];
        application.Resources[SystemColors.MenuBrushKey] = application.Resources["PanelBackgroundBrush"];
        application.Resources[SystemColors.MenuTextBrushKey] = application.Resources["PrimaryTextBrush"];
        application.Resources[SystemColors.HighlightBrushKey] = application.Resources["PrimaryBrush"];
        application.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
        application.Resources[SystemColors.GrayTextBrushKey] = application.Resources["SecondaryTextBrush"];
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void Save()
    {
        var path = ConfigurationPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        File.WriteAllText(partial, _choice.ToString());
        File.Move(partial, path, overwrite: true);
    }

    private static string ConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniShare", "config", "theme.txt");

    private static string TextScaleConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniShare", "config", "text-scale.txt");

    private static void ApplyTextScale(System.Windows.Application application) =>
        application.Resources["AppFontSize"] = _textScaleIndex switch
        {
            1 => 16d,
            2 => 18d,
            _ => 14d,
        };

    private static void Set(System.Windows.Application application, string key, Brush brush) =>
        application.Resources[key] = brush;
}
