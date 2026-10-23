using System.Diagnostics;
using System.IO.Enumeration;

namespace StorageOptimizer.Core;

/// <summary>Walks a folder tree and builds a <see cref="ScanResult"/>.</summary>
public sealed class DiskScanner
{
    /// <summary>Folders deeper than this are measured without building nodes, to bound recursion.</summary>
    private const int MaxTreeDepth = 200;

    private readonly ScanOptions _options;
    private readonly HashSet<string> _skipPaths;

    public DiskScanner(ScanOptions? options = null)
    {
        _options = options ?? new ScanOptions();
        _skipPaths = new HashSet<string>(_options.SkipPaths.Select(PathText.Normalize), StringComparer.OrdinalIgnoreCase);
    }

    public ScanOptions Options => _options;

    public async Task<ScanResult> ScanAsync(string root, ScanProgress? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(root);
        var info = new DirectoryInfo(fullPath);
        if (!info.Exists)
        {
            throw new DirectoryNotFoundException($"{fullPath} is not a folder.");
        }

        var stopwatch = Stopwatch.StartNew();
        var rootNode = new FileNode(fullPath, string.IsNullOrEmpty(info.Name) ? fullPath : info.Name, isDirectory: true,
            FileCategory.Other, info.LastWriteTimeUtc, info.LastAccessTimeUtc);

        // Never run the (blocking) file system work on the caller's thread, which may be the UI thread.
        var accumulator = await Task.Run(
            () => ScanDirectoryAsync(rootNode, new ScanContext(InsideArtifact: false), depth: 0, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        rootNode.SortChildren();
        accumulator.FlushProgress(progress, fullPath);
        return new ScanResult(rootNode, accumulator, stopwatch.Elapsed);
    }

    private async Task<ScanAccumulator> ScanDirectoryAsync(FileNode node, ScanContext context, int depth,
        ScanProgress? progress, CancellationToken cancellationToken)
    {
        var accumulator = new ScanAccumulator(_options);
        if (depth >= _options.ParallelDepth)
        {
            ScanDirectorySync(node, context, depth, accumulator, progress, cancellationToken);
            accumulator.FlushProgress(progress, node.Path);
            return accumulator;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var subdirectories = new List<PendingDirectory>();
        foreach (var entry in ListChildren(node.Path, accumulator))
        {
            if (entry.IsDirectory)
            {
                subdirectories.Add(MakePendingDirectory(entry, context));
            }
            else
            {
                AddFile(entry, node, accumulator, progress);
            }
        }

        var results = await Task.WhenAll(subdirectories.Select(pending => Task.Run(
            () => ScanDirectoryAsync(pending.Node, pending.Context, depth + 1, progress, cancellationToken),
            cancellationToken))).ConfigureAwait(false);

        for (var index = 0; index < subdirectories.Count; index++)
        {
            accumulator.Merge(results[index]);
            Complete(subdirectories[index], node, accumulator);
        }
        accumulator.FlushProgress(progress, node.Path);
        return accumulator;
    }

    private void ScanDirectorySync(FileNode node, ScanContext context, int depth, ScanAccumulator accumulator,
        ScanProgress? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth > MaxTreeDepth)
        {
            // Extremely deep hierarchies are measured iteratively instead of being expanded.
            node.Size = DirectorySizer.Measure(node.Path, _options.SizeMode, _options.ClusterSize);
            return;
        }

        foreach (var entry in ListChildren(node.Path, accumulator))
        {
            if (entry.IsDirectory)
            {
                var pending = MakePendingDirectory(entry, context);
                ScanDirectorySync(pending.Node, pending.Context, depth + 1, accumulator, progress, cancellationToken);
                Complete(pending, node, accumulator);
            }
            else
            {
                AddFile(entry, node, accumulator, progress);
            }
        }
    }

    private List<RawEntry> ListChildren(string directory, ScanAccumulator accumulator)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        List<RawEntry> raw;
        try
        {
            raw = new FileSystemEnumerable<RawEntry>(directory, (ref FileSystemEntry entry) => new RawEntry(
                entry.ToFullPath(),
                entry.FileName.ToString(),
                entry.IsDirectory,
                entry.Attributes,
                entry.Length,
                entry.LastWriteTimeUtc.UtcDateTime,
                entry.LastAccessTimeUtc.UtcDateTime), options).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            accumulator.InaccessibleCount++;
            return [];
        }

        var entries = new List<RawEntry>(raw.Count);
        foreach (var entry in raw)
        {
            if (entry.IsDirectory)
            {
                if (_skipPaths.Count > 0 && _skipPaths.Contains(PathText.Normalize(entry.Path)))
                {
                    continue;
                }
                if (!FileSizing.ShouldEnterDirectory(entry.Path, entry.Attributes))
                {
                    continue;
                }
            }
            else if (!FileSizing.ShouldCountFile(entry.Attributes))
            {
                continue;
            }
            entries.Add(entry);
        }
        return entries;
    }

    private void AddFile(in RawEntry entry, FileNode parent, ScanAccumulator accumulator, ScanProgress? progress)
    {
        var size = FileSizing.SizeOf(entry.Length, entry.Attributes, _options.SizeMode, _options.ClusterSize);
        parent.Size += size;
        parent.FileCount++;
        accumulator.NoteFile(size, parent.Path, progress);

        var extension = PathText.Extension(entry.Name);
        var category = FileCategories.ForExtension(extension);

        if (size >= _options.DetailThreshold)
        {
            parent.AddChild(new FileNode(entry.Path, entry.Name, isDirectory: false, category,
                entry.LastWriteTimeUtc, entry.LastAccessTimeUtc, size, fileCount: 1));
        }
        else
        {
            parent.SmallFilesSize += size;
            parent.SmallFilesCount++;
        }

        accumulator.RecordItem(entry.Path, size, category, extension, entry.LastWriteTimeUtc, entry.LastAccessTimeUtc);
    }

    private static PendingDirectory MakePendingDirectory(in RawEntry entry, ScanContext parentContext)
    {
        var kind = parentContext.InsideArtifact ? null : ArtifactKinds.Detect(entry.Path, entry.Name);
        var node = new FileNode(entry.Path, entry.Name, isDirectory: true, FileCategory.Other,
            entry.LastWriteTimeUtc, entry.LastAccessTimeUtc);
        return new PendingDirectory(node, new ScanContext(parentContext.InsideArtifact || kind is not null), kind);
    }

    /// <summary>Attaches a fully scanned folder to its parent and records artifact information.</summary>
    private static void Complete(PendingDirectory pending, FileNode parent, ScanAccumulator accumulator)
    {
        var child = pending.Node;
        child.SortChildren();
        parent.AddChild(child);
        parent.Size += child.Size;
        parent.FileCount += child.FileCount;
        if (pending.ArtifactKind is { } kind)
        {
            accumulator.Artifacts.Add(new ArtifactDirectory(child.Path, kind, child.Size));
        }
    }

    private readonly record struct RawEntry(string Path, string Name, bool IsDirectory, FileAttributes Attributes,
        long Length, DateTime LastWriteTimeUtc, DateTime LastAccessTimeUtc);

    private readonly record struct ScanContext(bool InsideArtifact);

    private sealed record PendingDirectory(FileNode Node, ScanContext Context, ArtifactKind? ArtifactKind);
}

/// <summary>Statistics gathered by one scan task; merged when tasks finish.</summary>
internal sealed class ScanAccumulator(ScanOptions options)
{
    private const int ProgressBatchSize = 512;

    private long _pendingFiles;
    private long _pendingBytes;

    public TopFilesCollector Largest { get; } = new(options.TopFileCount);
    public Dictionary<FileCategory, UsageStats> CategoryStats { get; } = [];
    public Dictionary<FileCategory, TopFilesCollector> CategoryTop { get; } = [];
    public Dictionary<string, (FileCategory Category, UsageStats Stats)> ExtensionStats { get; } = new(StringComparer.Ordinal);
    public List<ArtifactDirectory> Artifacts { get; } = [];
    public int InaccessibleCount { get; set; }

    public void RecordItem(string path, long size, FileCategory category, string extension,
        DateTime? lastWriteTimeUtc, DateTime? lastAccessTimeUtc)
    {
        Stats(category).Add(size);
        ExtensionStat(extension, category).Stats.Add(size);

        var keepOverall = Largest.Accepts(size);
        var keepInCategory = CategoryTop.TryGetValue(category, out var top) ? top.Accepts(size) : options.TopFilesPerCategory > 0;
        if (!keepOverall && !keepInCategory)
        {
            return;
        }

        var entry = new FileEntry(path, size, category, lastWriteTimeUtc, lastAccessTimeUtc);
        if (keepOverall)
        {
            Largest.Add(entry);
        }
        if (keepInCategory)
        {
            CategoryCollector(category).Add(entry);
        }
    }

    public void Merge(ScanAccumulator other)
    {
        Largest.Merge(other.Largest);
        foreach (var (category, stats) in other.CategoryStats)
        {
            Stats(category).Merge(stats);
        }
        foreach (var (category, top) in other.CategoryTop)
        {
            CategoryCollector(category).Merge(top);
        }
        foreach (var (extension, value) in other.ExtensionStats)
        {
            ExtensionStat(extension, value.Category).Stats.Merge(value.Stats);
        }
        Artifacts.AddRange(other.Artifacts);
        InaccessibleCount += other.InaccessibleCount;
    }

    public void NoteFile(long bytes, string directory, ScanProgress? progress)
    {
        _pendingFiles++;
        _pendingBytes += bytes;
        if (_pendingFiles >= ProgressBatchSize)
        {
            FlushProgress(progress, directory);
        }
    }

    public void FlushProgress(ScanProgress? progress, string directory)
    {
        if (_pendingFiles == 0 && _pendingBytes == 0)
        {
            return;
        }
        progress?.Record(_pendingFiles, _pendingBytes, directory);
        _pendingFiles = 0;
        _pendingBytes = 0;
    }

    private UsageStats Stats(FileCategory category)
    {
        if (!CategoryStats.TryGetValue(category, out var stats))
        {
            stats = new UsageStats();
            CategoryStats[category] = stats;
        }
        return stats;
    }

    private TopFilesCollector CategoryCollector(FileCategory category)
    {
        if (!CategoryTop.TryGetValue(category, out var top))
        {
            top = new TopFilesCollector(options.TopFilesPerCategory);
            CategoryTop[category] = top;
        }
        return top;
    }

    private (FileCategory Category, UsageStats Stats) ExtensionStat(string extension, FileCategory category)
    {
        if (!ExtensionStats.TryGetValue(extension, out var value))
        {
            value = (category, new UsageStats());
            ExtensionStats[extension] = value;
        }
        return value;
    }
}
