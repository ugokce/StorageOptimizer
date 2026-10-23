namespace StorageOptimizer.Core.Tests;

public sealed class DiskScannerTests
{
    private static DiskScanner Scanner(int topFileCount = 2_000, IReadOnlyCollection<string>? skipPaths = null) => new(new ScanOptions
    {
        SizeMode = SizeMode.Logical,
        DetailThreshold = 1_000_000,
        TopFileCount = topFileCount,
        SkipPaths = skipPaths ?? [],
    });

    private static TempDir BasicFixture()
    {
        var directory = new TempDir();
        directory.MakeFile("big.mov", 3_000_000);
        directory.MakeFile(Path.Join("docs", "report.pdf"), 2_000_000);
        directory.MakeFile(Path.Join("docs", "notes.txt"), 100);
        directory.MakeFile(Path.Join("docs", "todo.txt"), 200);
        return directory;
    }

    [Fact]
    public async Task AggregatesSizesAndSmallFiles()
    {
        using var directory = BasicFixture();
        var result = await Scanner().ScanAsync(directory.Path);

        Assert.Equal(5_000_300, result.TotalSize);
        Assert.Equal(4, result.FileCount);
        Assert.Equal(["big.mov", "docs"], result.Root.Children.Select(child => child.Name));

        var docs = result.Root.Children.Single(child => child.Name == "docs");
        Assert.Equal(2_000_300, docs.Size);
        Assert.Equal(3, docs.FileCount);
        Assert.Equal(["report.pdf"], docs.Children.Select(child => child.Name));
        Assert.Equal(2, docs.SmallFilesCount);
        Assert.Equal(300, docs.SmallFilesSize);
        Assert.Same(docs, docs.Children[0].Parent);
        Assert.Same(result.Root, docs.Parent);
    }

    [Fact]
    public async Task CollectsLargestFilesAndCategoryStatistics()
    {
        using var directory = BasicFixture();
        var result = await Scanner().ScanAsync(directory.Path);

        Assert.Equal(["big.mov", "report.pdf", "todo.txt", "notes.txt"], result.LargestFiles.Select(file => file.Name));
        Assert.Equal(3_000_000, result.CategoryStats[FileCategory.Video].Size);
        Assert.Equal(1, result.CategoryStats[FileCategory.Video].Count);
        Assert.Equal(2_000_300, result.CategoryStats[FileCategory.Document].Size);
        Assert.Equal(3, result.CategoryStats[FileCategory.Document].Count);
        Assert.Equal("report.pdf", result.FilesOf(FileCategory.Document)[0].Name);
        Assert.Equal([FileCategory.Video, FileCategory.Document], result.CategorySummaries().Select(summary => summary.Category));
        Assert.Equal(["pdf", "txt"], result.ExtensionSummaries(FileCategory.Document).Select(summary => summary.Extension));
    }

    [Fact]
    public async Task BoundsTheLargestFilesList()
    {
        using var directory = new TempDir();
        for (var index = 0; index < 20; index++)
        {
            directory.MakeFile($"file{index}.bin", 1_000 + index);
        }
        var result = await Scanner(topFileCount: 5).ScanAsync(directory.Path);

        Assert.Equal([1_019L, 1_018L, 1_017L, 1_016L, 1_015L], result.LargestFiles.Select(file => file.Size));
        Assert.Equal(20, result.FileCount);
    }

    [Fact]
    public async Task RoundsUpToClustersInAllocatedMode()
    {
        using var directory = new TempDir();
        directory.MakeFile("tiny.txt", 1);
        var scanner = new DiskScanner(new ScanOptions { SizeMode = SizeMode.Allocated, ClusterSize = 4096 });
        var result = await scanner.ScanAsync(directory.Path);

        Assert.Equal(4096, result.TotalSize);
    }

    [Fact]
    public async Task DoesNotFollowDirectoryLinks()
    {
        using var directory = new TempDir();
        using var outside = new TempDir();
        outside.MakeFile("movie.mov", 2_000_000);
        directory.MakeFile(Path.Join("data", "song.mp3"), 1_000);
        if (!directory.TryMakeDirectoryLink("linked", outside.Path))
        {
            return; // Creating links needs Developer Mode or administrator rights on Windows.
        }

        var result = await Scanner().ScanAsync(directory.Path);

        Assert.Equal(1_000, result.TotalSize);
        Assert.DoesNotContain(result.Root.Children, child => child.Name == "linked");
    }

    [Fact]
    public async Task IgnoresSkipPaths()
    {
        using var directory = new TempDir();
        directory.MakeFile(Path.Join("keep", "a.bin"), 1_000);
        directory.MakeFile(Path.Join("skip", "b.bin"), 5_000);
        var result = await Scanner(skipPaths: [directory["skip"]]).ScanAsync(directory.Path);

        Assert.Equal(1_000, result.TotalSize);
        Assert.Equal(["keep"], result.Root.Children.Select(child => child.Name));
    }

    [Fact]
    public void DriveScansSkipSystemVolumeInformation() =>
        Assert.Contains(ScanOptions.DriveRootSkipPaths(@"C:\"), path => PathText.AreEqual(path, @"C:\System Volume Information"));

    [Fact]
    public async Task DetectsOutermostProjectBuildFolders()
    {
        using var directory = new TempDir();
        directory.MakeFile(Path.Join("web", "package.json"), 10);
        directory.MakeFile(Path.Join("web", "node_modules", "left-pad", "index.js"), 100);
        directory.MakeFile(Path.Join("web", "node_modules", "left-pad", "package.json"), 10);
        directory.MakeFile(Path.Join("web", "node_modules", "left-pad", "node_modules", "dep", "index.js"), 50);
        directory.MakeFile(Path.Join("loose", "node_modules", "orphan.js"), 10);
        directory.MakeFile(Path.Join("game", "Assets", "Player.cs"), 10);
        directory.MakeDirectory(Path.Join("game", "ProjectSettings"));
        directory.MakeFile(Path.Join("game", "Library", "ShaderCache", "a.bin"), 700);
        directory.MakeFile(Path.Join("game", "Library", "Artifacts", "b.bin"), 300);
        directory.MakeFile(Path.Join("app", "App.csproj"), 10);
        directory.MakeFile(Path.Join("app", "obj", "project.assets.json"), 40);
        directory.MakeFile(Path.Join("app", "bin", "Debug", "App.dll"), 60);
        directory.MakeFile(Path.Join("photos", "Library", "picture.jpg"), 10);
        directory.MakeFile(Path.Join("rust", "Cargo.toml"), 10);
        directory.MakeFile(Path.Join("rust", "target", "debug", "app.exe"), 300);

        var result = await Scanner().ScanAsync(directory.Path);
        var found = result.ArtifactDirectories
            .Select(artifact => (artifact.ProjectName, artifact.Kind, artifact.Size))
            .OrderBy(item => item.ProjectName).ThenBy(item => item.Size)
            .ToList();

        Assert.Equal(
        [
            ("app", ArtifactKind.DotNetBuild, 40L),
            ("app", ArtifactKind.DotNetBuild, 60L),
            ("game", ArtifactKind.UnityLibrary, 1_000L),
            ("rust", ArtifactKind.CargoTarget, 300L),
            ("web", ArtifactKind.NodeModules, 160L),
        ], found);
    }

    [Fact]
    public async Task CancelledScanThrows()
    {
        using var directory = BasicFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scanner().ScanAsync(directory.Path, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ScanningAFileThrows()
    {
        using var directory = new TempDir();
        var file = directory.MakeFile("a.bin", 10);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Scanner().ScanAsync(file));
    }

    [Fact]
    public async Task ReportsProgress()
    {
        using var directory = BasicFixture();
        var progress = new ScanProgress();
        await Scanner().ScanAsync(directory.Path, progress);

        var snapshot = progress.Read();
        Assert.Equal(4, snapshot.FilesScanned);
        Assert.Equal(5_000_300, snapshot.BytesScanned);
    }

    [Fact]
    public async Task ApplyRemovalUpdatesTreeAndStatistics()
    {
        using var directory = BasicFixture();
        var result = await Scanner().ScanAsync(directory.Path);

        result.ApplyRemoval(directory["big.mov"]);
        Assert.Equal(2_000_300, result.TotalSize);
        Assert.DoesNotContain(result.LargestFiles, file => file.Name == "big.mov");
        Assert.Equal(0, result.CategoryStats[FileCategory.Video].Size);

        // A small file has no node of its own; its folder's totals must still shrink.
        result.ApplyRemoval(directory[Path.Join("docs", "notes.txt")]);
        var docs = result.FindNode(directory["docs"]);
        Assert.NotNull(docs);
        Assert.Equal(1, docs.SmallFilesCount);
        Assert.Equal(2_000_200, docs.Size);
        Assert.Equal(2_000_200, result.TotalSize);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(2_000_200, result.CategoryStats[FileCategory.Document].Size);
        Assert.Equal(2, result.CategoryStats[FileCategory.Document].Count);

        // Removing a folder removes everything listed inside it.
        result.ApplyRemoval(directory["docs"]);
        Assert.Equal(0, result.TotalSize);
        Assert.Empty(result.LargestFiles);
        Assert.Empty(result.Root.Children);
    }
}
