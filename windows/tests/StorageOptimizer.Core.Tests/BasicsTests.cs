using System.Globalization;

namespace StorageOptimizer.Core.Tests;

public sealed class FileCategoryTests
{
    [Theory]
    [InlineData("mp4", FileCategory.Video)]
    [InlineData(".MOV", FileCategory.Video)]
    [InlineData("heic", FileCategory.Image)]
    [InlineData("flac", FileCategory.Audio)]
    [InlineData("pdf", FileCategory.Document)]
    [InlineData("pst", FileCategory.Document)]
    [InlineData("zip", FileCategory.Archive)]
    [InlineData("iso", FileCategory.DiskImage)]
    [InlineData("vhdx", FileCategory.DiskImage)]
    [InlineData("exe", FileCategory.Application)]
    [InlineData("msi", FileCategory.Application)]
    [InlineData("cs", FileCategory.Developer)]
    [InlineData("dll", FileCategory.Developer)]
    [InlineData("", FileCategory.Other)]
    [InlineData("qwertyunknown", FileCategory.Other)]
    public void CategorizesByExtension(string extension, FileCategory expected) =>
        Assert.Equal(expected, FileCategories.ForExtension(extension));
}

public sealed class TopFilesCollectorTests
{
    private static FileEntry Entry(long size) => new($"/tmp/file-{size}", size, FileCategory.Other);

    [Fact]
    public void KeepsLargestEntries()
    {
        var collector = new TopFilesCollector(10);
        foreach (var size in Enumerable.Range(1, 100).OrderBy(_ => Random.Shared.Next()))
        {
            collector.Add(Entry(size));
        }
        Assert.Equal(10, collector.Count);
        Assert.Equal(Enumerable.Range(91, 10).Reverse().Select(size => (long)size), collector.SortedDescending().Select(entry => entry.Size));
    }

    [Fact]
    public void AcceptsReflectsCurrentMinimum()
    {
        var collector = new TopFilesCollector(2);
        Assert.True(collector.Accepts(1));
        collector.Add(Entry(10));
        collector.Add(Entry(20));
        Assert.False(collector.Accepts(5));
        Assert.True(collector.Accepts(15));
    }

    [Fact]
    public void Merges()
    {
        var first = new TopFilesCollector(3);
        var second = new TopFilesCollector(3);
        foreach (var size in new long[] { 1, 5, 9 })
        {
            first.Add(Entry(size));
        }
        foreach (var size in new long[] { 2, 7, 8 })
        {
            second.Add(Entry(size));
        }
        first.Merge(second);
        Assert.Equal([9L, 8L, 7L], first.SortedDescending().Select(entry => entry.Size));
    }

    [Fact]
    public void ZeroCapacityKeepsNothing()
    {
        var collector = new TopFilesCollector(0);
        collector.Add(Entry(1));
        Assert.Equal(0, collector.Count);
        Assert.False(collector.Accepts(100));
    }
}

public sealed class ByteFormattingTests
{
    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(1023L, "1,023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(11_010_048L, "10.5 MB")]
    [InlineData(157_286_400L, "150 MB")]
    [InlineData(1_073_741_824L, "1 GB")]
    [InlineData(1_320_702_444L, "1.23 GB")]
    public void FormatsLikeFileExplorer(long bytes, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, ByteFormatting.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}

public sealed class PathTextTests
{
    [Theory]
    [InlineData("C:/Users/Me/../Me/Documents/", @"C:\Users\Me\Documents")]
    [InlineData(@"c:\", @"C:\")]
    [InlineData(@"C:\Windows\.\System32", @"C:\Windows\System32")]
    [InlineData(@"\\server\share\folder\", @"\\server\share\folder")]
    public void Normalizes(string path, string expected) => Assert.Equal(expected, PathText.Normalize(path));

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"D:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\folder", false)]
    [InlineData(@"C:\Users", false)]
    public void DetectsRoots(string path, bool expected) => Assert.Equal(expected, PathText.IsRoot(PathText.Normalize(path)));

    [Fact]
    public void ComparesCaseInsensitivelyOnComponentBoundaries()
    {
        Assert.True(PathText.IsInside(@"C:\Users\me\file.txt", PathText.Normalize(@"c:\users\ME")));
        Assert.True(PathText.IsInside(@"C:\Users\me", @"C:\Users\me"));
        Assert.False(PathText.IsInside(@"C:\Users\meow", @"C:\Users\me"));
        Assert.True(PathText.IsInside(@"C:\Users", @"C:\"));
    }

    [Fact]
    public void SplitsNamesParentsAndExtensions()
    {
        Assert.Equal("file.txt", PathText.FileName(@"C:\Users\me\file.txt"));
        Assert.Equal(@"C:\Users\me", PathText.Parent(@"C:\Users\me\file.txt"));
        Assert.Equal(@"C:\", PathText.Parent(@"C:\Users"));
        Assert.Equal("gz", PathText.Extension("archive.tar.gz"));
        Assert.Equal(string.Empty, PathText.Extension(".gitignore"));
        Assert.Equal(string.Empty, PathText.Extension("README"));
    }
}

public sealed class FileSizingTests
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x0040_0000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x0004_0000;

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 4096L)]
    [InlineData(4096L, 4096L)]
    [InlineData(4097L, 8192L)]
    public void RoundsUpToClusters(long length, long expected) =>
        Assert.Equal(expected, FileSizing.SizeOf(length, FileAttributes.Normal, SizeMode.Allocated, 4096));

    [Fact]
    public void LogicalModeUsesTheLength() =>
        Assert.Equal(4097, FileSizing.SizeOf(4097, FileAttributes.Normal, SizeMode.Logical, 4096));

    [Fact]
    public void CloudOnlyFilesTakeNoSpace()
    {
        Assert.Equal(0, FileSizing.SizeOf(5_000_000, FileAttributes.ReparsePoint | RecallOnDataAccess, SizeMode.Allocated, 4096));
        Assert.Equal(0, FileSizing.SizeOf(5_000_000, RecallOnOpen, SizeMode.Allocated, 4096));
        Assert.Equal(0, FileSizing.SizeOf(5_000_000, FileAttributes.Offline, SizeMode.Allocated, 4096));
        Assert.True(FileSizing.IsCloudPlaceholder(RecallOnDataAccess));
        Assert.False(FileSizing.IsCloudPlaceholder(FileAttributes.Archive | FileAttributes.ReparsePoint));
    }
}
