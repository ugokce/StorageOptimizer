using System.Windows.Media;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.ViewModels;

/// <summary>A file in a file table.</summary>
public sealed class FileRow(FileEntry entry)
{
    public FileEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    public long Size => Entry.Size;
    public string SizeText => ByteFormatting.Format(Entry.Size);
    public string Kind => Entry.KindName;
    public string Glyph => Icons.For(Entry.Category);
    public Brush CategoryBrush => CategoryPalette.Brush(Entry.Category);
    public DateTime? Modified => Entry.LastWriteTimeUtc?.ToLocalTime();
    public DateTime? Accessed => Entry.LastAccessTimeUtc?.ToLocalTime();
}

/// <summary>A child of the current folder in the folder browser (or the "smaller files" summary row).</summary>
public sealed class FolderRow
{
    private FolderRow(FileNode? node, string name, long size, long parentSize, bool isFolder)
    {
        Node = node;
        Name = name;
        Size = size;
        Fraction = parentSize > 0 ? Math.Clamp((double)size / parentSize, 0, 1) : 0;
        IsFolder = isFolder;
    }

    public static FolderRow For(FileNode node, long parentSize) => new(node, node.Name, node.Size, parentSize, node.IsDirectory);

    public static FolderRow SmallFiles(int count, long size, long parentSize) =>
        new(null, $"{count:N0} smaller files", size, parentSize, isFolder: false);

    public FileNode? Node { get; }
    public string Name { get; }
    public long Size { get; }
    public string SizeText => ByteFormatting.Format(Size);
    public double Fraction { get; }
    public string PercentText => Fraction.ToString("P1", System.Globalization.CultureInfo.CurrentCulture);
    public bool IsFolder { get; }
    public bool IsSmallFiles => Node is null;
    public bool CanOpen => Node?.CanDrillDown == true;
    public string Glyph => IsSmallFiles ? Icons.Files : IsFolder ? Icons.Folder : Icons.For(Node!.Category);
    public Brush BarBrush => IsFolder || Node is null ? CategoryPalette.Brush(FileCategory.Document) : CategoryPalette.Brush(Node.Category);
}

/// <summary>A category with its share of the scanned data.</summary>
public sealed class CategoryRow(CategorySummary summary, long total)
{
    public FileCategory Category => summary.Category;
    public string Name => summary.Category.DisplayName();
    public string Glyph => Icons.For(summary.Category);
    public Brush Brush => CategoryPalette.Brush(summary.Category);
    public long Size => summary.Size;
    public string SizeText => ByteFormatting.Format(summary.Size);
    public double Fraction => total > 0 ? Math.Clamp((double)summary.Size / total, 0, 1) : 0;
    public string DetailText => $"{summary.Count:N0} files · {Fraction.ToString("P1", System.Globalization.CultureInfo.CurrentCulture)}";
}

/// <summary>One colored segment of the drive usage bar, also used for its legend.</summary>
public sealed class UsageSegment(string label, long size, Brush brush)
{
    public string Label { get; } = label;
    public long Size { get; } = size;
    public double Weight => Size;
    public string SizeText => ByteFormatting.Format(Size);
    public Brush Brush { get; } = brush;
    public string ToolTip => $"{Label}: {SizeText}";
}

public sealed record ExtensionChip(string Name, string SizeText, string CountText);

public sealed record SizeOption(string Title, long Bytes)
{
    public override string ToString() => Title;
}

public sealed record CategoryOption(string Title, FileCategory? Category)
{
    public override string ToString() => Title;
}

public sealed record ScanTargetOption(string Title, string Path, bool IsDrive)
{
    public override string ToString() => Title;
}

public sealed record Crumb(string Title, FileNode Node);
