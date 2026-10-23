namespace StorageOptimizer.Core;

/// <summary>Access to the Windows Recycle Bin (implemented by the app).</summary>
public interface IRecycleBin
{
    long QuerySize();
    void Empty();
}

public sealed record CleanFailure(string Path, string Reason);

/// <summary>The outcome of a cleaning run.</summary>
public sealed class CleanReport
{
    private const int MaxListedFailures = 500;

    private readonly List<CleanFailure> _failures = [];

    public long FreedBytes { get; internal set; }
    public List<string> CleanedTargetIds { get; } = [];

    /// <summary>Items deleted as a whole (project build folders).</summary>
    public List<string> RemovedItems { get; } = [];

    /// <summary>The first failures (up to 500); see <see cref="FailureCount"/> for the total.</summary>
    public IReadOnlyList<CleanFailure> Failures => _failures;
    public int FailureCount { get; private set; }

    internal void AddFailure(string path, string reason)
    {
        FailureCount++;
        if (_failures.Count < MaxListedFailures)
        {
            _failures.Add(new CleanFailure(path, reason));
        }
    }
}

/// <summary>Measures and permanently deletes cache data. Every deletion is checked by the deletion guard.</summary>
public sealed class CacheCleaner(
    DeletionGuard deletionGuard,
    SizeMode sizeMode = SizeMode.Allocated,
    long clusterSize = FileSizing.DefaultClusterSize,
    IRecycleBin? recycleBin = null)
{
    /// <summary>Bytes currently used by the target, excluding locations owned by other targets.</summary>
    public long Measure(CacheTarget target)
    {
        if (target.Action == CacheAction.EmptyRecycleBin)
        {
            try
            {
                return recycleBin?.QuerySize() ?? 0;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return 0;
            }
        }

        var excluded = ExcludedSet(target);
        long total = 0;
        foreach (var location in target.Locations)
        {
            if (target.FilePatterns.Count == 0)
            {
                total += DirectorySizer.Measure(location, sizeMode, clusterSize, excluded);
                continue;
            }
            foreach (var child in MatchingChildren(location, target.FilePatterns))
            {
                total += DirectorySizer.Measure(child, sizeMode, clusterSize, excluded);
            }
        }
        return total;
    }

    public CleanReport Clean(IEnumerable<CacheTarget> targets, CancellationToken cancellationToken = default)
    {
        var report = new CleanReport();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = Measure(target);
            switch (target.Action)
            {
                case CacheAction.DeleteContents:
                    var excluded = ExcludedSet(target);
                    foreach (var location in target.Locations)
                    {
                        RemoveContents(location, excluded, target.FilePatterns, report);
                    }
                    break;
                case CacheAction.DeleteItem:
                    foreach (var location in target.Locations)
                    {
                        if (Remove(location, report))
                        {
                            report.RemovedItems.Add(location);
                        }
                    }
                    break;
                case CacheAction.EmptyRecycleBin:
                    try
                    {
                        recycleBin?.Empty();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        report.AddFailure("Recycle Bin", exception.Message);
                    }
                    break;
            }
            var after = Measure(target);
            report.FreedBytes += Math.Max(0, before - after);
            report.CleanedTargetIds.Add(target.Id);
        }
        return report;
    }

    private void RemoveContents(string directory, HashSet<string> excluded, IReadOnlyList<string> patterns, CleanReport report)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report.AddFailure(directory, exception.Message);
            return;
        }

        foreach (var child in children)
        {
            var childPath = PathText.Normalize(child);
            if (excluded.Contains(childPath))
            {
                continue;
            }
            if (excluded.Any(path => PathText.IsInside(path, childPath)))
            {
                // Something below this child belongs to another target: only clean around it.
                RemoveContents(child, excluded, [], report);
                continue;
            }
            if (patterns.Count > 0 && !patterns.Any(pattern => Wildcard.Matches(PathText.FileName(child), pattern)))
            {
                continue;
            }
            Remove(child, report);
        }
    }

    private bool Remove(string path, CleanReport report)
    {
        try
        {
            deletionGuard.Validate(path);
            DeleteTree(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DeletionRefusedException)
        {
            report.AddFailure(path, exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Deletes a file or folder tree. Read-only files are deleted too, links are removed without
    /// touching their targets, and deletion continues past locked files before reporting the first error.
    /// </summary>
    internal static void DeleteTree(string path)
    {
        var attributes = File.GetAttributes(path);
        var isLink = (attributes & FileAttributes.ReparsePoint) != 0 && IsLink(path, attributes);

        if ((attributes & FileAttributes.Directory) == 0 || isLink)
        {
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(path, recursive: false);
            }
            else
            {
                File.Delete(path);
            }
            return;
        }

        Exception? firstError = null;
        foreach (var child in Directory.EnumerateFileSystemEntries(path).ToList())
        {
            try
            {
                DeleteTree(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                firstError ??= exception;
            }
        }
        if (firstError is not null)
        {
            throw firstError;
        }
        Directory.Delete(path, recursive: false);
    }

    private static bool IsLink(string path, FileAttributes attributes)
    {
        FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is not null;
    }

    private static HashSet<string> ExcludedSet(CacheTarget target) =>
        new(target.ExcludedPaths.Select(PathText.Normalize), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> MatchingChildren(string directory, IReadOnlyList<string> patterns)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory)
                .Where(child => patterns.Any(pattern => Wildcard.Matches(PathText.FileName(child), pattern)))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
