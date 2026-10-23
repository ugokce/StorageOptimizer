namespace StorageOptimizer.Core;

/// <summary>
/// A folder, or a file at or above the scan's detail threshold, in the scanned tree. Smaller
/// files are only summed into their folder's <see cref="SmallFilesSize"/> and
/// <see cref="SmallFilesCount"/>, which keeps memory bounded on large drives.
/// </summary>
public sealed class FileNode
{
    private readonly List<FileNode> _children = [];

    internal FileNode(string path, string name, bool isDirectory, FileCategory category,
        DateTime? lastWriteTimeUtc, DateTime? lastAccessTimeUtc, long size = 0, int fileCount = 0)
    {
        Path = path;
        Name = name;
        IsDirectory = isDirectory;
        Category = category;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LastAccessTimeUtc = lastAccessTimeUtc;
        Size = size;
        FileCount = fileCount;
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public FileCategory Category { get; }
    public DateTime? LastWriteTimeUtc { get; }
    public DateTime? LastAccessTimeUtc { get; }

    /// <summary>Bytes on disk at or below this node.</summary>
    public long Size { get; internal set; }

    /// <summary>Number of files at or below this node.</summary>
    public int FileCount { get; internal set; }

    /// <summary>Children, largest first.</summary>
    public IReadOnlyList<FileNode> Children => _children;

    public long SmallFilesSize { get; internal set; }
    public int SmallFilesCount { get; internal set; }
    public FileNode? Parent { get; internal set; }

    /// <summary>Whether the folder browser can open this node.</summary>
    public bool CanDrillDown => IsDirectory && (_children.Count > 0 || SmallFilesCount > 0);

    internal void AddChild(FileNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void SortChildren() => _children.Sort((a, b) => b.Size.CompareTo(a.Size));

    /// <summary>Adds bytes and files to this node and every ancestor.</summary>
    internal void AdjustTotals(long bytes, int files)
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            node.Size = Math.Max(0, node.Size + bytes);
            node.FileCount = Math.Max(0, node.FileCount + files);
        }
    }

    /// <summary>Detaches this node and subtracts its size from all ancestors.</summary>
    internal void RemoveFromParent()
    {
        if (Parent is not { } parent)
        {
            return;
        }
        parent._children.Remove(this);
        parent.AdjustTotals(-Size, -FileCount);
        Parent = null;
    }

    /// <summary>Accounts for a small (non-node) file removed from this folder.</summary>
    internal void RemoveSmallFile(long size)
    {
        SmallFilesSize = Math.Max(0, SmallFilesSize - size);
        SmallFilesCount = Math.Max(0, SmallFilesCount - 1);
        AdjustTotals(-size, -1);
    }
}

/// <summary>A file in a "largest files" list.</summary>
public sealed record FileEntry(
    string Path,
    long Size,
    FileCategory Category,
    DateTime? LastWriteTimeUtc = null,
    DateTime? LastAccessTimeUtc = null)
{
    public string Name => PathText.FileName(Path);
    public string Folder => PathText.Parent(Path) ?? string.Empty;
    public string KindName => Category.DisplayName();
}

/// <summary>Size and item count of a group of files.</summary>
public sealed class UsageStats
{
    public long Size { get; private set; }
    public int Count { get; private set; }

    internal void Add(long bytes)
    {
        Size += bytes;
        Count++;
    }

    internal void Remove(long bytes)
    {
        Size = Math.Max(0, Size - bytes);
        Count = Math.Max(0, Count - 1);
    }

    internal void Merge(UsageStats other)
    {
        Size += other.Size;
        Count += other.Count;
    }
}

public sealed record CategorySummary(FileCategory Category, long Size, int Count);

public sealed record ExtensionSummary(string Extension, FileCategory Category, long Size, int Count)
{
    public string DisplayName => Extension.Length == 0 ? "No extension" : "." + Extension;
}
