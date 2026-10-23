namespace StorageOptimizer.Core;

/// <summary>Standard Windows folders the cache catalog is built from.</summary>
public sealed record KnownFolders(
    string UserProfile,
    string LocalAppData,
    string RoamingAppData,
    string Temp,
    string WindowsDirectory,
    string ProgramData)
{
    public static KnownFolders Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
}

/// <summary>The built-in list of cache locations.</summary>
public static class CacheCatalog
{
    internal enum FolderBase
    {
        Profile,
        LocalAppData,
        RoamingAppData,
        Temp,
        Windows,
        ProgramData,
    }

    internal sealed record Definition(string Id, string Title, string Detail, CacheGroup Group, CacheSafety Safety,
        FolderBase Base, string[] Paths)
    {
        public CacheAction Action { get; init; } = CacheAction.DeleteContents;
        public string[] FilePatterns { get; init; } = [];
        public bool RequiresAdmin { get; init; }
        public string[] ProcessNames { get; init; } = [];
    }

    internal static IReadOnlyList<Definition> Definitions { get; } =
    [
        // Windows
        new("user-temp", "Temporary Files",
            "Files that apps and installers left in your Temp folder. Files still in use are skipped.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.Temp, [""]),
        new("windows-temp", "Windows Temporary Files",
            "Temporary files written by Windows and system services.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.Windows, ["Temp"]) { RequiresAdmin = true },
        new("windows-update", "Windows Update Downloads",
            "Update packages Windows has already installed. Windows downloads them again if it needs them.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.Windows, [@"SoftwareDistribution\Download"]) { RequiresAdmin = true },
        new("delivery-optimization", "Delivery Optimization Cache",
            "Update pieces Windows keeps to share with other PCs.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.Windows,
            [@"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"]) { RequiresAdmin = true },
        new("thumbnail-cache", "Thumbnail & Icon Cache",
            "Picture previews and icons File Explorer keeps. They're recreated as you browse; files in use are skipped.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.LocalAppData, [@"Microsoft\Windows\Explorer"])
        {
            FilePatterns = ["thumbcache_*.db", "iconcache_*.db"],
        },
        new("crash-dumps", "Crash Dumps",
            "Memory dumps saved when apps crashed.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.LocalAppData, ["CrashDumps"]),
        new("error-reports", "Error Reports",
            "Windows Error Reporting files for apps that crashed or stopped responding.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.LocalAppData,
            [@"Microsoft\Windows\WER\ReportArchive", @"Microsoft\Windows\WER\ReportQueue", @"Microsoft\Windows\WER\Temp"]),
        new("system-error-reports", "System Error Reports",
            "Windows Error Reporting files for system components and services.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.ProgramData,
            [@"Microsoft\Windows\WER\ReportArchive", @"Microsoft\Windows\WER\ReportQueue", @"Microsoft\Windows\WER\Temp"])
        {
            RequiresAdmin = true,
        },
        new("shader-cache", "Graphics Shader Caches",
            "Compiled shaders saved by DirectX and your graphics driver. Games rebuild them, so their next start may be slower.",
            CacheGroup.Windows, CacheSafety.Safe, FolderBase.LocalAppData,
            ["D3DSCache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\VkCache", @"Intel\ShaderCache"]),
        new("recycle-bin", "Recycle Bin",
            "Items you already deleted, on every drive. Emptying it can't be undone.",
            CacheGroup.Windows, CacheSafety.Caution, FolderBase.Profile, []) { Action = CacheAction.EmptyRecycleBin },

        // Browsers
        new("chrome-cache", "Google Chrome",
            "Cached pages, images and scripts for every Chrome profile. History, passwords and sign-ins are kept.",
            CacheGroup.Browsers, CacheSafety.Safe, FolderBase.LocalAppData,
            [@"Google\Chrome\User Data\*\Cache", @"Google\Chrome\User Data\*\Code Cache", @"Google\Chrome\User Data\*\GPUCache"])
        {
            ProcessNames = ["chrome"],
        },
        new("edge-cache", "Microsoft Edge",
            "Cached pages, images and scripts for every Edge profile. History, passwords and sign-ins are kept.",
            CacheGroup.Browsers, CacheSafety.Safe, FolderBase.LocalAppData,
            [@"Microsoft\Edge\User Data\*\Cache", @"Microsoft\Edge\User Data\*\Code Cache", @"Microsoft\Edge\User Data\*\GPUCache"])
        {
            ProcessNames = ["msedge"],
        },
        new("brave-cache", "Brave",
            "Cached pages, images and scripts for every Brave profile. History, passwords and sign-ins are kept.",
            CacheGroup.Browsers, CacheSafety.Safe, FolderBase.LocalAppData,
            [@"BraveSoftware\Brave-Browser\User Data\*\Cache", @"BraveSoftware\Brave-Browser\User Data\*\Code Cache",
             @"BraveSoftware\Brave-Browser\User Data\*\GPUCache"])
        {
            ProcessNames = ["brave"],
        },
        new("firefox-cache", "Mozilla Firefox",
            "Cached pages and images for every Firefox profile. History, passwords and sign-ins are kept.",
            CacheGroup.Browsers, CacheSafety.Safe, FolderBase.LocalAppData, [@"Mozilla\Firefox\Profiles\*\cache2"])
        {
            ProcessNames = ["firefox"],
        },

        // Developer tools
        new("unity-cache", "Unity Package Cache",
            "Packages Unity downloaded for your projects. Unity downloads them again when a project needs them.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"Unity\cache"]) { ProcessNames = ["Unity", "Unity Hub"] },
        new("unity-asset-store", "Unity Asset Store Downloads",
            "Asset Store packages you downloaded. You can download them again from My Assets.",
            CacheGroup.Developer, CacheSafety.Caution, FolderBase.RoamingAppData, [@"Unity\Asset Store-5.x"]),
        new("npm-cache", "npm",
            "The npm package cache. Packages are downloaded again on the next install.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"npm-cache\_cacache"]),
        new("yarn-cache", "Yarn",
            "The Yarn package cache.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"Yarn\Cache"]),
        new("pnpm-store", "pnpm Store",
            "Projects installed with pnpm link to this store and need pnpm install afterwards.",
            CacheGroup.Developer, CacheSafety.Caution, FolderBase.LocalAppData, [@"pnpm\store"]),
        new("pip-cache", "pip",
            "Downloaded Python wheels and packages.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"pip\Cache"]),
        new("nuget-http-cache", "NuGet HTTP Cache",
            "Package downloads cached by NuGet. Downloaded again when needed.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"NuGet\v3-cache", @"NuGet\plugins-cache"]),
        new("nuget-packages", "NuGet Packages",
            "Every NuGet package your .NET projects use. The next restore or build downloads them again.",
            CacheGroup.Developer, CacheSafety.Caution, FolderBase.Profile, [@".nuget\packages"]),
        new("visual-studio-cache", "Visual Studio Component Cache",
            "Extension and component caches Visual Studio rebuilds on its next start.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"Microsoft\VisualStudio\*\ComponentModelCache"])
        {
            ProcessNames = ["devenv"],
        },
        new("vscode-cache", "Visual Studio Code Caches",
            "Cached web content and compiled code from VS Code. Settings and extensions are kept.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.RoamingAppData,
            [@"Code\Cache", @"Code\CachedData", @"Code\Code Cache", @"Code\GPUCache"])
        {
            ProcessNames = ["Code"],
        },
        new("jetbrains-caches", "JetBrains IDE Caches",
            "Indexes and caches of Rider, IntelliJ IDEA and other JetBrains IDEs. They're rebuilt when a project opens.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, [@"JetBrains\*\caches"]),
        new("gradle-cache", "Gradle",
            "Downloaded dependencies and build caches. Downloaded again on the next build.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.Profile, [@".gradle\caches"]),
        new("go-build-cache", "Go Build Cache",
            "Compiled Go packages. Rebuilt on the next go build.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.LocalAppData, ["go-build"]),
        new("cargo-cache", "Cargo Registry Cache",
            "Downloaded Rust crates.",
            CacheGroup.Developer, CacheSafety.Safe, FolderBase.Profile, [@".cargo\registry\cache"]),
    ];

    /// <summary>The catalog targets whose folders exist, with overlapping locations resolved.</summary>
    public static IReadOnlyList<CacheTarget> AvailableTargets(KnownFolders folders, bool includeRecycleBin)
    {
        var targets = new List<CacheTarget>();
        foreach (var definition in Definitions)
        {
            if (definition.Action == CacheAction.EmptyRecycleBin)
            {
                if (includeRecycleBin)
                {
                    targets.Add(ToTarget(definition, []));
                }
                continue;
            }

            var basePath = BasePath(folders, definition.Base);
            if (string.IsNullOrEmpty(basePath))
            {
                continue;
            }
            var locations = definition.Paths
                .SelectMany(pattern => Expand(basePath, pattern))
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (locations.Count > 0)
            {
                targets.Add(ToTarget(definition, locations));
            }
        }
        return ResolveOverlaps(targets);
    }

    /// <summary>
    /// Prevents targets from measuring or deleting each other's data: when a location of one
    /// target lies inside a folder another target empties, the outer target excludes it. Project
    /// folders that already live inside a catalog location are dropped.
    /// </summary>
    public static IReadOnlyList<CacheTarget> ResolveOverlaps(IEnumerable<CacheTarget> targets)
    {
        var all = targets.ToList();
        var catalogPaths = all
            .Where(target => target.Group != CacheGroup.Projects)
            .SelectMany(target => target.Locations.Select(PathText.Normalize))
            .ToList();

        var kept = all
            .Where(target => target.Group != CacheGroup.Projects
                || !target.Locations.Any(location => catalogPaths.Any(path => PathText.IsInside(PathText.Normalize(location), path))))
            .ToList();

        var resolved = new List<CacheTarget>(kept.Count);
        foreach (var target in kept)
        {
            if (target.Action != CacheAction.DeleteContents)
            {
                resolved.Add(target with { ExcludedPaths = [] });
                continue;
            }
            var own = target.Locations.Select(PathText.Normalize).ToList();
            var excluded = kept
                .Where(other => !ReferenceEquals(other, target))
                .SelectMany(other => other.Locations)
                .Where(location =>
                {
                    var path = PathText.Normalize(location);
                    return own.Any(mine => !mine.Equals(path, StringComparison.OrdinalIgnoreCase) && PathText.IsInside(path, mine));
                })
                .ToList();
            resolved.Add(target with { ExcludedPaths = excluded });
        }
        return resolved;
    }

    /// <summary>Every folder a catalog target may clean inside, for <see cref="DeletionRules.CacheRoots"/>.</summary>
    public static IReadOnlyList<string> AllLocations(KnownFolders folders) =>
        Definitions
            .Where(definition => definition.Action != CacheAction.EmptyRecycleBin)
            .SelectMany(definition =>
            {
                var basePath = BasePath(folders, definition.Base);
                return string.IsNullOrEmpty(basePath) ? [] : definition.Paths.SelectMany(pattern => Expand(basePath, pattern));
            })
            .ToList();

    /// <summary>Expands a path below <paramref name="basePath"/> whose components may contain <c>*</c> wildcards.</summary>
    internal static IEnumerable<string> Expand(string basePath, string pattern)
    {
        IEnumerable<string> results = [basePath];
        foreach (var component in pattern.Split('\\', '/').Where(part => part.Length > 0))
        {
            if (component.Contains('*'))
            {
                results = results.SelectMany(directory => MatchingChildren(directory, component)).ToList();
            }
            else
            {
                results = results.Select(directory => Path.Join(directory, component)).ToList();
            }
        }
        return results;
    }

    private static IEnumerable<string> MatchingChildren(string directory, string component)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateDirectories(directory)
                    .Where(child => Wildcard.Matches(PathText.FileName(child), component))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string BasePath(KnownFolders folders, FolderBase folderBase) => folderBase switch
    {
        FolderBase.Profile => folders.UserProfile,
        FolderBase.LocalAppData => folders.LocalAppData,
        FolderBase.RoamingAppData => folders.RoamingAppData,
        FolderBase.Temp => folders.Temp,
        FolderBase.Windows => folders.WindowsDirectory,
        _ => folders.ProgramData,
    };

    private static CacheTarget ToTarget(Definition definition, IReadOnlyList<string> locations) => new()
    {
        Id = definition.Id,
        Title = definition.Title,
        Detail = definition.Detail,
        Group = definition.Group,
        Safety = definition.Safety,
        Locations = locations,
        Action = definition.Action,
        FilePatterns = definition.FilePatterns,
        RequiresAdmin = definition.RequiresAdmin,
        ProcessNames = definition.ProcessNames,
    };
}

/// <summary>Matches names against patterns where <c>*</c> stands for any run of characters (case-insensitive).</summary>
public static class Wildcard
{
    public static bool Matches(string name, string pattern)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var first = parts[0];
        var last = parts[^1];
        if (!name.StartsWith(first, StringComparison.OrdinalIgnoreCase) || name.Length < first.Length + last.Length)
        {
            return false;
        }

        var position = first.Length;
        foreach (var middle in parts[1..^1])
        {
            if (middle.Length == 0)
            {
                continue;
            }
            var index = name.IndexOf(middle, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }
            position = index + middle.Length;
        }
        return name.Length - position >= last.Length && name.EndsWith(last, StringComparison.OrdinalIgnoreCase);
    }
}
