namespace StorageOptimizer.Core;

public sealed record ScanOptions
{
    public SizeMode SizeMode { get; init; } = SizeMode.Allocated;

    /// <summary>Cluster size of the scanned drive, used by <see cref="SizeMode.Allocated"/>.</summary>
    public long ClusterSize { get; init; } = FileSizing.DefaultClusterSize;

    /// <summary>Files at or above this size become nodes; smaller ones are only summed per folder.</summary>
    public long DetailThreshold { get; init; } = 1_000_000;

    /// <summary>How many of the largest files to keep overall.</summary>
    public int TopFileCount { get; init; } = 2_000;

    /// <summary>How many of the largest files to keep per category.</summary>
    public int TopFilesPerCategory { get; init; } = 300;

    /// <summary>Absolute paths that are not scanned.</summary>
    public IReadOnlyCollection<string> SkipPaths { get; init; } = [];

    /// <summary>Folder depth up to which subfolders are scanned in parallel.</summary>
    public int ParallelDepth { get; init; } = 2;

    /// <summary>Folders to skip when scanning a whole drive.</summary>
    public static IReadOnlyCollection<string> DriveRootSkipPaths(string driveRoot) =>
        [Path.Join(driveRoot, "System Volume Information")];
}

/// <summary>Thread-safe progress counters that the UI polls while a scan runs.</summary>
public sealed class ScanProgress
{
    private long _files;
    private long _bytes;
    private string _currentPath = string.Empty;

    public readonly record struct Snapshot(long FilesScanned, long BytesScanned, string CurrentPath);

    public Snapshot Read() => new(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), Volatile.Read(ref _currentPath));

    internal void Record(long files, long bytes, string? currentPath)
    {
        Interlocked.Add(ref _files, files);
        Interlocked.Add(ref _bytes, bytes);
        if (currentPath is not null)
        {
            Volatile.Write(ref _currentPath, currentPath);
        }
    }
}
