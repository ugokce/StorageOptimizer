namespace StorageOptimizer.Core;

public enum CacheGroup
{
    Windows,
    Browsers,
    Developer,
    Projects,
}

public enum CacheSafety
{
    /// <summary>Recreated automatically when needed; selected by default.</summary>
    Safe,

    /// <summary>Can be recreated but may hold something worth keeping; never selected by default.</summary>
    Caution,
}

public enum CacheAction
{
    /// <summary>Delete everything inside each location (or only items matching <see cref="CacheTarget.FilePatterns"/>), keeping the folders.</summary>
    DeleteContents,

    /// <summary>Delete each location itself (project build folders).</summary>
    DeleteItem,

    /// <summary>Empty the Windows Recycle Bin.</summary>
    EmptyRecycleBin,
}

public static class CacheGroups
{
    public static string Title(this CacheGroup group) => group switch
    {
        CacheGroup.Windows => "Windows",
        CacheGroup.Browsers => "Browsers",
        CacheGroup.Developer => "Developer Tools",
        _ => "Project Build Folders",
    };
}

/// <summary>Something the cache cleaner can measure and clean.</summary>
public sealed record CacheTarget
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public CacheGroup Group { get; init; }
    public CacheSafety Safety { get; init; }
    public IReadOnlyList<string> Locations { get; init; } = [];
    public CacheAction Action { get; init; } = CacheAction.DeleteContents;

    /// <summary>When set, only items directly inside the locations whose names match one of these wildcards are cleaned.</summary>
    public IReadOnlyList<string> FilePatterns { get; init; } = [];

    /// <summary>Whether cleaning needs the app to run as administrator.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Process names (without .exe) that should be closed before cleaning.</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = [];

    /// <summary>Locations owned by other targets that this one must leave alone.</summary>
    public IReadOnlyList<string> ExcludedPaths { get; init; } = [];

    /// <summary>Whether the target is selected by default.</summary>
    public bool IsRecommended => Safety == CacheSafety.Safe && Group != CacheGroup.Projects;

    public static string ArtifactId(string path) => "artifact:" + PathText.Normalize(path);

    /// <summary>A target for a rebuildable project folder found by a scan.</summary>
    public static CacheTarget ForArtifact(ArtifactDirectory artifact) => new()
    {
        Id = ArtifactId(artifact.Path),
        Title = $"{artifact.ProjectName} · {artifact.Kind.DisplayName()}",
        Detail = artifact.Kind.RestoreHint(),
        Group = CacheGroup.Projects,
        Safety = CacheSafety.Safe,
        Locations = [artifact.Path],
        Action = CacheAction.DeleteItem,
    };
}
