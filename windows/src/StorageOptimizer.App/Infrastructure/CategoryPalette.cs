using System.Windows.Media;
using Microsoft.Win32;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.Infrastructure;

/// <summary>
/// One color per file category, the same set the Mac app uses. The order of the eight hues was
/// checked for color-blind separation in both themes; "Other" is neutral gray. Brushes are updated
/// in place when Windows switches between light and dark mode, so bound UI follows automatically.
/// </summary>
public static class CategoryPalette
{
    private static readonly Dictionary<FileCategory, (uint Light, uint Dark)> Colors = new()
    {
        [FileCategory.Document] = (0x2A78D6, 0x3987E5),
        [FileCategory.Application] = (0xEB6834, 0xD95926),
        [FileCategory.Image] = (0x1BAF7A, 0x199E70),
        [FileCategory.Audio] = (0xEDA100, 0xC98500),
        [FileCategory.Video] = (0xE87BA4, 0xD55181),
        [FileCategory.Archive] = (0x008300, 0x008300),
        [FileCategory.Developer] = (0x4A3AA7, 0x9085E9),
        [FileCategory.DiskImage] = (0xE34948, 0xE66767),
        [FileCategory.Other] = (0x898781, 0x898781),
    };

    private static readonly (uint Light, uint Dark) UnscannedColor = (0xC3C2B7, 0x4A4A46);

    private static readonly Dictionary<FileCategory, SolidColorBrush> Brushes =
        Colors.ToDictionary(pair => pair.Key, _ => new SolidColorBrush());

    /// <summary>Space outside the scanned folder (system files, other users, ...).</summary>
    public static SolidColorBrush Unscanned { get; } = new();

    public static SolidColorBrush Brush(FileCategory category) => Brushes[category];

    public static void Initialize()
    {
        UseTheme(IsDarkTheme());
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            {
                UseTheme(IsDarkTheme());
            }
        };
    }

    public static bool IsDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    /// <summary>Switches every brush to the light or dark colors. Screenshot mode uses it to show both themes.</summary>
    public static void UseTheme(bool dark)
    {
        foreach (var (category, brush) in Brushes)
        {
            var (light, darkColor) = Colors[category];
            brush.Color = ToColor(dark ? darkColor : light);
        }
        Unscanned.Color = ToColor(dark ? UnscannedColor.Dark : UnscannedColor.Light);
    }

    private static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
