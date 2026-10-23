using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.ViewModels;

/// <summary>A table of files with Explorer and Recycle Bin actions, shared by Large Files and File Types.</summary>
public sealed partial class FileListViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public FileListViewModel(MainViewModel main)
    {
        _main = main;
        Rows = [];
        SummaryText = string.Empty;
    }

    [ObservableProperty]
    public partial IReadOnlyList<FileRow> Rows { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    public void SetEntries(IEnumerable<FileEntry> entries)
    {
        Rows = entries.Select(entry => new FileRow(entry)).ToList();
        SummaryText = $"{Rows.Count:N0} files, {ByteFormatting.Format(Rows.Sum(row => row.Size))}";
    }

    [RelayCommand]
    private void ShowInExplorer(IList? items) => ShellActions.ShowInExplorer(PathsOf(items));

    [RelayCommand]
    private void Open(IList? items)
    {
        if (PathsOf(items) is [var path])
        {
            ShellActions.Open(path);
        }
    }

    [RelayCommand]
    private void CopyPath(IList? items) => ShellActions.CopyPaths(PathsOf(items));

    [RelayCommand]
    private void MoveToRecycleBin(IList? items) => _main.MoveToRecycleBin(PathsOf(items));

    private static List<string> PathsOf(IList? items) => items?.OfType<FileRow>().Select(row => row.Path).ToList() ?? [];
}

public sealed partial class OverviewViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public OverviewViewModel(MainViewModel main) : base("Overview", Icons.HardDrive)
    {
        _main = main;
        Segments = [];
        Categories = [];
        TopItems = [];
        TopFiles = [];
        main.ResultChanged += (_, _) => Refresh();
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.Drive))
            {
                Refresh();
            }
        };
        Refresh();
    }

    public MainViewModel Main => _main;
    public CacheCleanerViewModel CacheCleaner => _main.CacheCleaner;

    [ObservableProperty]
    public partial IReadOnlyList<UsageSegment> Segments { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CategoryRow> Categories { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FolderRow> TopItems { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FileRow> TopFiles { get; set; }

    [ObservableProperty]
    public partial string? TopItemsTitle { get; set; }

    public bool HasResult => _main.Result is not null;
    public string DriveTitle => _main.Drive?.Name ?? "Drive";
    public string UsedText => _main.Drive is { } drive ? $"{ByteFormatting.Format(drive.Used)} of {ByteFormatting.Format(drive.Total)} used" : string.Empty;
    public string FreeText => _main.Drive is { } drive ? $"{ByteFormatting.Format(drive.Free)} free" : string.Empty;
    public bool IsNearlyFull => _main.Drive?.UsedFraction > 0.9;
    public double DriveTotal => _main.Drive?.Total ?? 0;

    public void Refresh()
    {
        var result = _main.Result;
        var drive = _main.Drive;
        var segments = new List<UsageSegment>();
        long accounted = 0;
        if (result is not null && drive is not null)
        {
            foreach (var category in FileCategories.All)
            {
                if (result.CategoryStats.TryGetValue(category, out var stats) && stats.Size > 0)
                {
                    segments.Add(new UsageSegment(category.DisplayName(), stats.Size, CategoryPalette.Brush(category)));
                    accounted += stats.Size;
                }
            }
        }
        if (drive is not null)
        {
            var label = result is null ? "Used" : PathText.IsRoot(PathText.Normalize(result.Root.Path)) ? "System & unreadable" : "Outside the scanned folder";
            segments.Add(new UsageSegment(label, Math.Max(0, drive.Used - accounted), CategoryPalette.Unscanned));
        }
        Segments = segments;

        if (result is not null)
        {
            Categories = result.CategorySummaries().Select(summary => new CategoryRow(summary, result.TotalSize)).ToList();
            TopItems = result.Root.Children.Take(8).Select(node => FolderRow.For(node, result.Root.Size)).ToList();
            TopItemsTitle = $"Largest items in {result.Root.Name}";
            TopFiles = result.LargestFiles.Take(8).Select(file => new FileRow(file)).ToList();
        }
        else
        {
            Categories = [];
            TopItems = [];
            TopFiles = [];
            TopItemsTitle = null;
        }

        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(DriveTitle));
        OnPropertyChanged(nameof(UsedText));
        OnPropertyChanged(nameof(FreeText));
        OnPropertyChanged(nameof(IsNearlyFull));
        OnPropertyChanged(nameof(DriveTotal));
    }

    [RelayCommand]
    private void OpenItem(FolderRow? row)
    {
        if (row?.Node is not { } node)
        {
            return;
        }
        if (node.CanDrillDown)
        {
            _main.ShowInFolders(node);
        }
        else
        {
            ShellActions.ShowInExplorer([node.Path]);
        }
    }

    [RelayCommand]
    private void ShowFile(FileRow? row)
    {
        if (row is not null)
        {
            ShellActions.ShowInExplorer([row.Path]);
        }
    }

    [RelayCommand]
    private void GoTo(PageViewModel? page)
    {
        if (page is not null)
        {
            _main.SelectedPage = page;
        }
    }

}

public sealed partial class LargeFilesViewModel : PageViewModel
{
    private const long MB = 1024 * 1024;
    private readonly MainViewModel _main;

    public LargeFilesViewModel(MainViewModel main) : base("Large Files", Icons.Search)
    {
        _main = main;
        Files = new FileListViewModel(main);
        SizeOptions =
        [
            new("Any size", 0),
            new("10 MB or more", 10 * MB),
            new("100 MB or more", 100 * MB),
            new("500 MB or more", 500 * MB),
            new("1 GB or more", 1024 * MB),
        ];
        CategoryOptions = [new("All types", null), .. FileCategories.All.Select(category => new CategoryOption(category.DisplayName(), category))];
        SelectedSize = SizeOptions[2];
        SelectedCategory = CategoryOptions[0];
        SearchText = string.Empty;
        main.ResultChanged += (_, _) => Refresh();
    }

    public FileListViewModel Files { get; }
    public IReadOnlyList<SizeOption> SizeOptions { get; }
    public IReadOnlyList<CategoryOption> CategoryOptions { get; }
    public bool HasResult => _main.Result is not null;

    [ObservableProperty]
    public partial SizeOption SelectedSize { get; set; }

    [ObservableProperty]
    public partial CategoryOption SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial bool OnlyNotOpenedRecently { get; set; }

    partial void OnSelectedSizeChanged(SizeOption value) => Refresh();
    partial void OnSelectedCategoryChanged(CategoryOption value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnOnlyNotOpenedRecentlyChanged(bool value) => Refresh();

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasResult));
        if (_main.Result is not { } result || SelectedSize is null || SelectedCategory is null)
        {
            Files.SetEntries([]);
            return;
        }

        var cutoff = DateTime.UtcNow.AddMonths(-6);
        var query = SearchText.Trim();
        Files.SetEntries(result.LargestFiles.Where(entry =>
            entry.Size >= SelectedSize.Bytes
            && (SelectedCategory.Category is null || entry.Category == SelectedCategory.Category)
            && (!OnlyNotOpenedRecently || (entry.LastAccessTimeUtc ?? entry.LastWriteTimeUtc ?? DateTime.MinValue) <= cutoff)
            && (query.Length == 0 || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))));
    }
}

public sealed partial class FoldersViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public FoldersViewModel(MainViewModel main) : base("Folders", Icons.Folder)
    {
        _main = main;
        Crumbs = [];
        Rows = [];
        SummaryText = string.Empty;
        main.ResultChanged += (_, _) => Refresh(resetPath: false);
    }

    public bool HasResult => _main.Result is not null;

    [ObservableProperty]
    public partial IReadOnlyList<Crumb> Crumbs { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FolderRow> Rows { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    public partial FileNode? Current { get; set; }

    /// <summary>Opens a folder of the scan, e.g. from the Overview.</summary>
    public void Show(FileNode node)
    {
        Current = node;
        Refresh(resetPath: false);
    }

    private void Refresh(bool resetPath)
    {
        OnPropertyChanged(nameof(HasResult));
        if (_main.Result is not { } result)
        {
            Current = null;
            Crumbs = [];
            Rows = [];
            SummaryText = string.Empty;
            return;
        }

        // Stay in the current folder if it is still part of this result.
        var current = resetPath || Current is null || result.FindNode(Current.Path) is not { } node || !ReferenceEquals(node, Current)
            ? result.Root
            : Current;
        Current = current;

        var crumbs = new List<Crumb>();
        for (var item = current; item is not null; item = item.Parent)
        {
            crumbs.Insert(0, new Crumb(item.Name, item));
        }
        Crumbs = crumbs;

        var rows = current.Children.Select(child => FolderRow.For(child, current.Size)).ToList();
        if (current.SmallFilesCount > 0)
        {
            rows.Add(FolderRow.SmallFiles(current.SmallFilesCount, current.SmallFilesSize, current.Size));
        }
        Rows = rows;
        SummaryText = $"{current.Name}: {ByteFormatting.Format(current.Size)} in {current.FileCount:N0} files";
    }

    [RelayCommand]
    private void Open(FolderRow? row)
    {
        if (row?.Node is not { } node)
        {
            return;
        }
        if (node.CanDrillDown)
        {
            Current = node;
            Refresh(resetPath: false);
        }
        else
        {
            ShellActions.ShowInExplorer([node.Path]);
        }
    }

    private bool CanGoUp() => Current?.Parent is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up()
    {
        if (Current?.Parent is { } parent)
        {
            Current = parent;
            Refresh(resetPath: false);
        }
    }

    [RelayCommand]
    private void Navigate(Crumb? crumb)
    {
        if (crumb is not null)
        {
            Current = crumb.Node;
            Refresh(resetPath: false);
        }
    }

    [RelayCommand]
    private void ShowInExplorer(IList? items)
    {
        var paths = NodesOf(items).Select(node => node.Path).ToList();
        ShellActions.ShowInExplorer(paths.Count > 0 ? paths : Current is { } current ? [current.Path] : []);
    }

    [RelayCommand]
    private void CopyPath(IList? items) => ShellActions.CopyPaths(NodesOf(items).Select(node => node.Path).ToList());

    [RelayCommand]
    private void MoveToRecycleBin(IList? items) => _main.MoveToRecycleBin(NodesOf(items).Select(node => node.Path).ToList());

    private static IEnumerable<FileNode> NodesOf(IList? items) =>
        items?.OfType<FolderRow>().Select(row => row.Node).OfType<FileNode>() ?? [];
}

public sealed partial class FileTypesViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public FileTypesViewModel(MainViewModel main) : base("File Types", Icons.List)
    {
        _main = main;
        Files = new FileListViewModel(main);
        Categories = [];
        Extensions = [];
        main.ResultChanged += (_, _) => Refresh();
    }

    public FileListViewModel Files { get; }
    public bool HasResult => _main.Result is not null;

    [ObservableProperty]
    public partial IReadOnlyList<CategoryRow> Categories { get; set; }

    [ObservableProperty]
    public partial CategoryRow? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ExtensionChip> Extensions { get; set; }

    partial void OnSelectedCategoryChanged(CategoryRow? value) => ShowCategory(value);

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasResult));
        var previous = SelectedCategory?.Category;
        if (_main.Result is not { } result)
        {
            Categories = [];
            SelectedCategory = null;
            return;
        }
        Categories = result.CategorySummaries().Select(summary => new CategoryRow(summary, result.TotalSize)).ToList();
        SelectedCategory = Categories.FirstOrDefault(row => row.Category == previous) ?? Categories.FirstOrDefault();
    }

    private void ShowCategory(CategoryRow? row)
    {
        if (row is null || _main.Result is not { } result)
        {
            Extensions = [];
            Files.SetEntries([]);
            return;
        }
        Extensions = result.ExtensionSummaries(row.Category)
            .Take(12)
            .Select(summary => new ExtensionChip(summary.DisplayName, ByteFormatting.Format(summary.Size), $"{summary.Count:N0} files"))
            .ToList();
        Files.SetEntries(result.FilesOf(row.Category));
    }
}
