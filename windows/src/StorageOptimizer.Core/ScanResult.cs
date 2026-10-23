namespace StorageOptimizer.Core;

/// <summary>Everything learned from scanning one folder or drive.</summary>
public sealed class ScanResult
{
    private readonly List<FileEntry> _largestFiles;
    private readonly Dictionary<FileCategory, UsageStats> _categoryStats;
    private readonly Dictionary<FileCategory, List<FileEntry>> _categoryFiles;
    private readonly Dictionary<string, (FileCategory Category, UsageStats Stats)> _extensionStats;
    private readonly List<ArtifactDirectory> _artifacts;

    internal ScanResult(FileNode root, ScanAccumulator accumulator, TimeSpan duration)
    {
        Root = root;
        _largestFiles = accumulator.Largest.SortedDescending();
        _categoryStats = accumulator.CategoryStats;
        _categoryFiles = accumulator.CategoryTop.ToDictionary(pair => pair.Key, pair => pair.Value.SortedDescending());
        _extensionStats = accumulator.ExtensionStats;
        _artifacts = accumulator.Artifacts.OrderByDescending(artifact => artifact.Size).ToList();
        InaccessibleCount = accumulator.InaccessibleCount;
        Duration = duration;
        ScannedAt = DateTime.Now;
    }

    public FileNode Root { get; }

    /// <summary>The largest files, largest first.</summary>
    public IReadOnlyList<FileEntry> LargestFiles => _largestFiles;

    public IReadOnlyDictionary<FileCategory, UsageStats> CategoryStats => _categoryStats;

    /// <summary>Rebuildable project folders (node_modules, Unity Library, ...), largest first.</summary>
    public IReadOnlyList<ArtifactDirectory> ArtifactDirectories => _artifacts;

    /// <summary>Folders that could not be read (usually missing permissions).</summary>
    public int InaccessibleCount { get; }

    public TimeSpan Duration { get; }
    public DateTime ScannedAt { get; }

    public long TotalSize => Root.Size;
    public int FileCount => Root.FileCount;

    /// <summary>The largest files of a category, largest first.</summary>
    public IReadOnlyList<FileEntry> FilesOf(FileCategory category) =>
        _categoryFiles.TryGetValue(category, out var files) ? files : [];

    /// <summary>Categories that contain data, largest first.</summary>
    public IReadOnlyList<CategorySummary> CategorySummaries() =>
        _categoryStats
            .Where(pair => pair.Value.Size > 0)
            .Select(pair => new CategorySummary(pair.Key, pair.Value.Size, pair.Value.Count))
            .OrderByDescending(summary => summary.Size)
            .ToList();

    /// <summary>File extensions, largest first, optionally limited to one category.</summary>
    public IReadOnlyList<ExtensionSummary> ExtensionSummaries(FileCategory? category = null) =>
        _extensionStats
            .Where(pair => (category is null || pair.Value.Category == category) && pair.Value.Stats.Size > 0)
            .Select(pair => new ExtensionSummary(pair.Key, pair.Value.Category, pair.Value.Stats.Size, pair.Value.Stats.Count))
            .OrderByDescending(summary => summary.Size)
            .ToList();

    /// <summary>Finds the node for a path, if it is part of the tree.</summary>
    public FileNode? FindNode(string path)
    {
        var rootPath = PathText.Normalize(Root.Path);
        var target = PathText.Normalize(path);
        if (target.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Root;
        }
        if (!PathText.IsInside(target, rootPath))
        {
            return null;
        }

        var relative = target[(rootPath.Length + (rootPath.EndsWith('\\') ? 0 : 1))..];
        var current = Root;
        foreach (var component in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Children.FirstOrDefault(child => child.Name.Equals(component, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Updates the tree and statistics after a file or folder was deleted or moved to the Recycle Bin.
    /// Statistics are exact for files; for folders, small files inside them are only removed from
    /// the totals, not from the per-type statistics.
    /// </summary>
    public void ApplyRemoval(string path)
    {
        var removedPath = PathText.Normalize(path);
        bool IsAffected(string other) => PathText.IsInside(PathText.Normalize(other), removedPath);

        var node = FindNode(path);
        if (node is not null && !ReferenceEquals(node, Root))
        {
            SubtractStatistics(node);
            node.RemoveFromParent();
        }
        else if ((_largestFiles.FirstOrDefault(entry => IsAffected(entry.Path))
                  ?? _categoryFiles.Values.SelectMany(files => files).FirstOrDefault(entry => IsAffected(entry.Path))) is { } smallFile)
        {
            // A file below the detail threshold only exists in its folder's small-file totals.
            if (PathText.Parent(smallFile.Path) is { } parentPath)
            {
                FindNode(parentPath)?.RemoveSmallFile(smallFile.Size);
            }
            Subtract(smallFile.Category, PathText.Extension(smallFile.Name), smallFile.Size);
        }

        _largestFiles.RemoveAll(entry => IsAffected(entry.Path));
        foreach (var files in _categoryFiles.Values)
        {
            files.RemoveAll(entry => IsAffected(entry.Path));
        }
        _artifacts.RemoveAll(artifact => IsAffected(artifact.Path));
    }

    private void SubtractStatistics(FileNode node)
    {
        var stack = new Stack<FileNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!current.IsDirectory)
            {
                Subtract(current.Category, PathText.Extension(current.Name), current.Size);
                continue;
            }
            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    private void Subtract(FileCategory category, string extension, long size)
    {
        if (_categoryStats.TryGetValue(category, out var stats))
        {
            stats.Remove(size);
        }
        if (_extensionStats.TryGetValue(extension, out var value))
        {
            value.Stats.Remove(size);
        }
    }
}
