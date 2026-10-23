using StorageOptimizer.Core;

namespace StorageOptimizer.App.Infrastructure;

/// <summary>Segoe Fluent Icons glyphs (they share code points with Segoe MDL2 Assets on Windows 10).</summary>
public static class Icons
{
    public const string HardDrive = "";
    public const string Search = "";
    public const string Folder = "";
    public const string List = "";
    public const string Clean = "";
    public const string Refresh = "";
    public const string Stop = "";
    public const string OpenFolder = "";
    public const string Back = "";
    public const string ChevronRight = "";
    public const string Warning = "";
    public const string Shield = "";
    public const string Files = "";

    public static string For(FileCategory category) => category switch
    {
        FileCategory.Document => "",
        FileCategory.Application => "",
        FileCategory.Image => "",
        FileCategory.Audio => "",
        FileCategory.Video => "",
        FileCategory.Archive => "",
        FileCategory.Developer => "",
        FileCategory.DiskImage => HardDrive,
        _ => "",
    };
}
