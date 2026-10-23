namespace StorageOptimizer.Core.Tests;

public sealed class CacheCleanerTests
{
    /// <summary>A fake user profile with a few caches in their usual Windows places.</summary>
    private sealed class Fixture : IDisposable
    {
        public TempDir Root { get; } = new();

        public Fixture()
        {
            Root.MakeFile(Local("Temp", "setup.tmp"), 1_000);
            Root.MakeFile(Local("Google", "Chrome", "User Data", "Default", "Cache", "data_1"), 500);
            Root.MakeFile(Local("Google", "Chrome", "User Data", "Profile 1", "Cache", "data_1"), 300);
            Root.MakeFile(Local("Google", "Chrome", "User Data", "Default", "History"), 900);
            Root.MakeFile(Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db"), 200);
            Root.MakeFile(Local("Microsoft", "Windows", "Explorer", "ExplorerStartupLog.etl"), 50);
            Root.MakeFile(Local("Unity", "cache", "packages", "com.unity.ugui.tgz"), 100);
            Root.MakeFile(Local("npm-cache", "_cacache", "index"), 10);
            Root.MakeDirectory("Windows");
            Root.MakeDirectory("ProgramData");
        }

        public string Profile => Root["home"];

        public KnownFolders Folders => new(
            Profile,
            Root[Local()],
            Root[Path.Join("home", "AppData", "Roaming")],
            Root[Local("Temp")],
            Root["Windows"],
            Root["ProgramData"]);

        public DeletionGuard Guard => new(new DeletionRules { UserProfile = Profile, CacheRoots = CacheCatalog.AllLocations(Folders) });

        public CacheCleaner Cleaner(IRecycleBin? recycleBin = null) => new(Guard, SizeMode.Logical, recycleBin: recycleBin);

        public static string Local(params string[] parts) => Path.Join(["home", "AppData", "Local", .. parts]);

        public void Dispose() => Root.Dispose();
    }

    private sealed class FakeRecycleBin(long size) : IRecycleBin
    {
        public long Size { get; private set; } = size;
        public long QuerySize() => Size;
        public void Empty() => Size = 0;
    }

    [Fact]
    public void FindsExistingLocations()
    {
        using var fixture = new Fixture();
        var targets = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: false);

        Assert.Equal(
            ["chrome-cache", "npm-cache", "thumbnail-cache", "unity-cache", "user-temp"],
            targets.Select(target => target.Id).Order());

        var chrome = targets.Single(target => target.Id == "chrome-cache");
        Assert.Equal(["Default", "Profile 1"], chrome.Locations.Select(location => PathText.FileName(PathText.Parent(location)!)));
        Assert.Equal(["chrome"], chrome.ProcessNames);
    }

    [Fact]
    public void MeasuresOnlyCacheData()
    {
        using var fixture = new Fixture();
        var targets = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: false);
        var cleaner = fixture.Cleaner();

        Assert.Equal(800, cleaner.Measure(targets.Single(target => target.Id == "chrome-cache")));
        Assert.Equal(200, cleaner.Measure(targets.Single(target => target.Id == "thumbnail-cache")));
        Assert.Equal(1_000, cleaner.Measure(targets.Single(target => target.Id == "user-temp")));
    }

    [Fact]
    public void CleansContentsButKeepsFoldersAndOtherFiles()
    {
        using var fixture = new Fixture();
        var targets = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: false);
        var report = fixture.Cleaner().Clean(targets.Where(target => target.Id is "chrome-cache" or "thumbnail-cache" or "user-temp"));

        Assert.True(fixture.Root.Exists(Fixture.Local("Temp")));
        Assert.False(fixture.Root.Exists(Fixture.Local("Temp", "setup.tmp")));
        Assert.True(fixture.Root.Exists(Fixture.Local("Google", "Chrome", "User Data", "Default", "Cache")));
        Assert.False(fixture.Root.Exists(Fixture.Local("Google", "Chrome", "User Data", "Default", "Cache", "data_1")));
        Assert.True(fixture.Root.Exists(Fixture.Local("Google", "Chrome", "User Data", "Default", "History")));
        Assert.False(fixture.Root.Exists(Fixture.Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db")));
        Assert.True(fixture.Root.Exists(Fixture.Local("Microsoft", "Windows", "Explorer", "ExplorerStartupLog.etl")));
        Assert.Equal(2_000, report.FreedBytes);
        Assert.Equal(0, report.FailureCount);
    }

    [Fact]
    public void DeletesReadOnlyFiles()
    {
        using var fixture = new Fixture();
        var file = fixture.Root.MakeFile(Fixture.Local("Temp", "locked", "readonly.tmp"), 10);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var temp = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: false).Single(target => target.Id == "user-temp");

        var report = fixture.Cleaner().Clean([temp]);

        Assert.False(File.Exists(file));
        Assert.Equal(0, report.FailureCount);
    }

    [Fact]
    public void RemovesLinksWithoutTouchingTheirTargets()
    {
        using var fixture = new Fixture();
        using var outside = new TempDir();
        outside.MakeFile("precious.docx", 10);
        if (!fixture.Root.TryMakeDirectoryLink(Fixture.Local("Temp", "link"), outside.Path))
        {
            return; // Creating links needs Developer Mode or administrator rights on Windows.
        }
        var temp = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: false).Single(target => target.Id == "user-temp");

        fixture.Cleaner().Clean([temp]);

        Assert.False(fixture.Root.Exists(Fixture.Local("Temp", "link")));
        Assert.True(outside.Exists("precious.docx"));
    }

    [Fact]
    public void PreservesNestedExclusions()
    {
        using var root = new TempDir();
        var cache = root.MakeDirectory(Path.Join("home", "cache"));
        root.MakeFile(Path.Join("home", "cache", "top.dat"), 10);
        root.MakeFile(Path.Join("home", "cache", "vendor", "other", "file.dat"), 20);
        var keep = root.MakeDirectory(Path.Join("home", "cache", "vendor", "keep"));
        root.MakeFile(Path.Join("home", "cache", "vendor", "keep", "file.dat"), 30);

        var outer = new CacheTarget { Id = "outer", Title = "Outer", Detail = "", Locations = [cache] };
        var inner = new CacheTarget { Id = "inner", Title = "Inner", Detail = "", Locations = [keep] };
        var resolved = CacheCatalog.ResolveOverlaps([outer, inner]);
        var cleaner = new CacheCleaner(new DeletionGuard(new DeletionRules { UserProfile = root["home"] }), SizeMode.Logical);

        Assert.Equal(30, cleaner.Measure(resolved[0]));
        var report = cleaner.Clean([resolved[0]]);

        Assert.False(root.Exists(Path.Join("home", "cache", "top.dat")));
        Assert.False(root.Exists(Path.Join("home", "cache", "vendor", "other")));
        Assert.True(root.Exists(Path.Join("home", "cache", "vendor", "keep", "file.dat")));
        Assert.Equal(30, report.FreedBytes);
    }

    [Fact]
    public void DeletesProjectFolders()
    {
        using var root = new TempDir();
        root.MakeFile(Path.Join("home", "Projects", "web", "package.json"), 10);
        var modules = root.MakeDirectory(Path.Join("home", "Projects", "web", "node_modules"));
        root.MakeFile(Path.Join("home", "Projects", "web", "node_modules", "a.js"), 100);

        var target = CacheTarget.ForArtifact(new ArtifactDirectory(modules, ArtifactKind.NodeModules, 100));
        var cleaner = new CacheCleaner(new DeletionGuard(new DeletionRules { UserProfile = root["home"] }), SizeMode.Logical);
        var report = cleaner.Clean([target]);

        Assert.False(root.Exists(Path.Join("home", "Projects", "web", "node_modules")));
        Assert.True(root.Exists(Path.Join("home", "Projects", "web", "package.json")));
        Assert.Equal([modules], report.RemovedItems);
        Assert.Equal(100, report.FreedBytes);
    }

    [Fact]
    public void GuardBlocksLocationsOutsideTheProfile()
    {
        using var root = new TempDir();
        root.MakeDirectory("home");
        var outside = root.MakeDirectory(Path.Join("elsewhere", "cache"));
        root.MakeFile(Path.Join("elsewhere", "cache", "data"), 10);

        var target = new CacheTarget { Id = "x", Title = "X", Detail = "", Locations = [outside] };
        var cleaner = new CacheCleaner(new DeletionGuard(new DeletionRules { UserProfile = root["home"] }), SizeMode.Logical);
        var report = cleaner.Clean([target]);

        Assert.True(root.Exists(Path.Join("elsewhere", "cache", "data")));
        Assert.Equal(1, report.FailureCount);
        Assert.Equal(0, report.FreedBytes);
    }

    [Fact]
    public void EmptiesTheRecycleBin()
    {
        using var fixture = new Fixture();
        var recycleBin = new FakeRecycleBin(5_000);
        var cleaner = fixture.Cleaner(recycleBin);
        var target = CacheCatalog.AvailableTargets(fixture.Folders, includeRecycleBin: true).Single(target => target.Id == "recycle-bin");

        Assert.Equal(5_000, cleaner.Measure(target));
        var report = cleaner.Clean([target]);

        Assert.Equal(0, recycleBin.Size);
        Assert.Equal(5_000, report.FreedBytes);
    }

    [Fact]
    public void DropsProjectFoldersInsideCatalogLocations()
    {
        var caches = new CacheTarget { Id = "caches", Title = "Caches", Detail = "", Locations = [@"C:\Users\me\AppData\Local\Temp"] };
        var inside = CacheTarget.ForArtifact(new ArtifactDirectory(@"C:\Users\me\AppData\Local\Temp\tool\node_modules", ArtifactKind.NodeModules, 1));
        var project = CacheTarget.ForArtifact(new ArtifactDirectory(@"C:\Users\me\Projects\web\node_modules", ArtifactKind.NodeModules, 1));

        var resolved = CacheCatalog.ResolveOverlaps([caches, inside, project]);

        Assert.Equal(["caches", project.Id], resolved.Select(target => target.Id));
    }

    [Theory]
    [InlineData("thumbcache_256.db", "thumbcache_*.db", true)]
    [InlineData("THUMBCACHE_idx.DB", "thumbcache_*.db", true)]
    [InlineData("thumbcache.txt", "thumbcache_*.db", false)]
    [InlineData("abc", "a*c", true)]
    [InlineData("ab", "ab*b", false)]
    [InlineData("anything", "*", true)]
    [InlineData("exact", "exact", true)]
    public void MatchesWildcards(string name, string pattern, bool expected) => Assert.Equal(expected, Wildcard.Matches(name, pattern));
}
