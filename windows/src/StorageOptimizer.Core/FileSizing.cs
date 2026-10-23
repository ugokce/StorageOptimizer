using System.IO.Enumeration;

namespace StorageOptimizer.Core;

/// <summary>How file sizes are measured.</summary>
public enum SizeMode
{
    /// <summary>Space used on disk: rounded up to whole clusters, 0 for cloud-only files.</summary>
    Allocated,

    /// <summary>The logical length of the file contents.</summary>
    Logical,
}

public static class FileSizing
{
    // Attributes Windows sets on OneDrive and other cloud placeholder files whose data isn't local.
    private const int RecallOnOpen = 0x0004_0000;
    private const int RecallOnDataAccess = 0x0040_0000;

    public const long DefaultClusterSize = 4096;

    public static bool IsCloudPlaceholder(FileAttributes attributes) =>
        ((int)attributes & (RecallOnOpen | RecallOnDataAccess)) != 0
        || (attributes & FileAttributes.Offline) != 0;

    /// <summary>The space a file takes, given its length and attributes.</summary>
    public static long SizeOf(long length, FileAttributes attributes, SizeMode mode, long clusterSize)
    {
        if (length <= 0)
        {
            return 0;
        }
        if (mode == SizeMode.Logical)
        {
            return length;
        }
        if (IsCloudPlaceholder(attributes))
        {
            return 0;
        }
        if (clusterSize <= 1)
        {
            return length;
        }
        return (length + clusterSize - 1) / clusterSize * clusterSize;
    }

    /// <summary>
    /// Whether a file entry should be counted. On Windows, reparse-point files are usually
    /// OneDrive files and take real space; elsewhere a reparse point is a symbolic link.
    /// </summary>
    internal static bool ShouldCountFile(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) == 0 || OperatingSystem.IsWindows();

    /// <summary>
    /// Whether a folder may be entered. Junctions, symbolic links and mount points are skipped so
    /// nothing is counted twice; other reparse points (OneDrive folders) are entered.
    /// </summary>
    internal static bool ShouldEnterDirectory(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return true;
        }
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        try
        {
            return new DirectoryInfo(path).LinkTarget is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Measures folders without building a tree (cache cleaner, very deep folders).</summary>
public static class DirectorySizer
{
    /// <param name="excludedPaths">Normalized paths (see <see cref="PathText.Normalize"/>) to skip entirely.</param>
    public static long Measure(string path, SizeMode mode = SizeMode.Allocated, long clusterSize = FileSizing.DefaultClusterSize,
        IReadOnlySet<string>? excludedPaths = null)
    {
        try
        {
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return FileSizing.ShouldCountFile(file.Attributes) ? FileSizing.SizeOf(file.Length, file.Attributes, mode, clusterSize) : 0;
            }
            if (!Directory.Exists(path))
            {
                return 0;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            };
            var sizes = new FileSystemEnumerable<long>(path,
                (ref FileSystemEntry entry) => FileSizing.SizeOf(entry.Length, entry.Attributes, mode, clusterSize), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory && FileSizing.ShouldCountFile(entry.Attributes),
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                {
                    var fullPath = entry.ToFullPath();
                    if (excludedPaths is { Count: > 0 } && excludedPaths.Contains(PathText.Normalize(fullPath)))
                    {
                        return false;
                    }
                    return FileSizing.ShouldEnterDirectory(fullPath, entry.Attributes);
                },
            };

            long total = 0;
            foreach (var size in sizes)
            {
                total += size;
            }
            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
