namespace StorageOptimizer.Core;

public enum DeletionRefusal
{
    /// <summary>A drive root, a Windows or user standard folder, or a registry or credential file.</summary>
    ProtectedLocation,

    /// <summary>Outside your user folder and other local data drives (Windows, Program Files, ...).</summary>
    OutsideAllowedLocations,

    /// <summary>The path goes through a junction or symbolic link.</summary>
    ThroughLink,
}

public sealed class DeletionRefusedException(string path, DeletionRefusal reason) : Exception(MessageFor(path, reason))
{
    public string Path { get; } = path;
    public DeletionRefusal Reason { get; } = reason;

    private static string MessageFor(string path, DeletionRefusal reason) => reason switch
    {
        DeletionRefusal.ProtectedLocation => $"{path} is a protected location and won't be deleted.",
        DeletionRefusal.ThroughLink => $"{path} is reached through a junction or symbolic link, so it won't be deleted.",
        _ => $"{path} is outside your user folder and data drives, so it won't be deleted. Uninstall apps from Settings > Apps instead.",
    };
}

/// <summary>Locations the deletion guard needs to know about.</summary>
public sealed record DeletionRules
{
    /// <summary>The user profile folder, e.g. <c>C:\Users\Name</c>.</summary>
    public required string UserProfile { get; init; }

    /// <summary>Roots of local drives other than the system drive (e.g. <c>D:\</c>); items below them may be deleted.</summary>
    public IReadOnlyList<string> DataDriveRoots { get; init; } = [];

    /// <summary>Cache folders whose contents may be deleted even in protected areas; the folders themselves never.</summary>
    public IReadOnlyList<string> CacheRoots { get; init; } = [];
}

/// <summary>
/// Decides whether an item may be deleted or moved to the Recycle Bin. Allowed: inside the user
/// profile and on other local data drives. Refused: drive roots, Windows, Program Files,
/// ProgramData, other users' folders, the profile's standard folders, registry hives and
/// credential stores, and anything reached through a junction or symbolic link.
/// </summary>
public sealed class DeletionGuard
{
    private static readonly string[] ProtectedProfileFolders =
    [
        "", "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "Favorites", "Links", "Contacts",
        "Searches", "Saved Games", "3D Objects", "AppData", @"AppData\Local", @"AppData\Roaming", @"AppData\LocalLow",
        @"AppData\Local\Temp", @"AppData\Local\Microsoft", @"AppData\Local\Microsoft\Windows", @"AppData\Local\Packages",
        @"AppData\Local\Programs", @"AppData\Roaming\Microsoft", @"AppData\Roaming\Microsoft\Windows",
    ];

    private static readonly string[] ProtectedProfileSubtrees =
    [
        @"AppData\Roaming\Microsoft\Protect", @"AppData\Roaming\Microsoft\Crypto", @"AppData\Roaming\Microsoft\Credentials",
        @"AppData\Roaming\Microsoft\SystemCertificates", @"AppData\Local\Microsoft\Credentials",
    ];

    private static readonly string[] ProtectedDriveFolders = ["System Volume Information", "$Recycle.Bin"];

    private readonly string _profile;
    private readonly HashSet<string> _protectedPaths;
    private readonly List<string> _protectedSubtrees;
    private readonly List<string> _dataDrives;
    private readonly List<string> _cacheRoots;

    public DeletionGuard(DeletionRules rules)
    {
        _profile = PathText.Normalize(rules.UserProfile);
        _protectedPaths = new HashSet<string>(
            ProtectedProfileFolders.Select(folder => PathText.Normalize(Path.Join(rules.UserProfile, folder))),
            StringComparer.OrdinalIgnoreCase);
        _protectedSubtrees = ProtectedProfileSubtrees.Select(folder => PathText.Normalize(Path.Join(rules.UserProfile, folder))).ToList();
        _dataDrives = rules.DataDriveRoots.Select(PathText.Normalize).ToList();
        _cacheRoots = rules.CacheRoots.Select(PathText.Normalize).ToList();
    }

    public bool CanDelete(string path) => Check(path) is null;

    /// <summary>Throws <see cref="DeletionRefusedException"/> if the item must not be deleted.</summary>
    public void Validate(string path)
    {
        if (Check(path) is { } refusal)
        {
            throw new DeletionRefusedException(path, refusal);
        }
    }

    /// <summary>Why the item must not be deleted, or <c>null</c> if it may be.</summary>
    public DeletionRefusal? Check(string path)
    {
        var full = PathText.Normalize(path);
        if (full.Length == 0 || PathText.IsRoot(full))
        {
            return DeletionRefusal.ProtectedLocation;
        }
        if (GoesThroughLink(path))
        {
            return DeletionRefusal.ThroughLink;
        }

        foreach (var cacheRoot in _cacheRoots)
        {
            if (full.Equals(cacheRoot, StringComparison.OrdinalIgnoreCase))
            {
                return DeletionRefusal.ProtectedLocation;
            }
            if (PathText.IsInside(full, cacheRoot))
            {
                return null;
            }
        }

        var name = PathText.FileName(full);
        var parent = PathText.Parent(full) ?? string.Empty;

        if (PathText.IsInside(full, _profile))
        {
            if (_protectedPaths.Contains(full) || _protectedSubtrees.Any(subtree => PathText.IsInside(full, subtree)))
            {
                return DeletionRefusal.ProtectedLocation;
            }
            // Registry hives (NTUSER.DAT, UsrClass.dat) and OneDrive roots ("OneDrive - Company").
            if (name.StartsWith("ntuser", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("usrclass", StringComparison.OrdinalIgnoreCase)
                || (parent.Equals(_profile, StringComparison.OrdinalIgnoreCase) && name.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)))
            {
                return DeletionRefusal.ProtectedLocation;
            }
            return null;
        }

        foreach (var drive in _dataDrives)
        {
            if (!PathText.IsInside(full, drive))
            {
                continue;
            }
            if (ProtectedDriveFolders.Any(folder => PathText.IsInside(full, PathText.Normalize(Path.Join(drive, folder)))))
            {
                return DeletionRefusal.ProtectedLocation;
            }
            return null;
        }

        return DeletionRefusal.OutsideAllowedLocations;
    }

    /// <summary>Whether any parent folder of the item is a junction or symbolic link.</summary>
    private static bool GoesThroughLink(string path)
    {
        // Windows-style test paths can't be inspected on other systems; their rules are still checked.
        if (!OperatingSystem.IsWindows() && !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        for (var directory = Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null)
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
        return false;
    }
}
