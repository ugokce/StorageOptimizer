using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.ViewModels;

public sealed partial class CacheRowViewModel : ObservableObject
{
    public CacheRowViewModel(CacheTarget target, bool isElevated, long? knownSize, bool? wasSelected)
    {
        Target = target;
        IsAvailable = !target.RequiresAdmin || isElevated;
        Size = knownSize;
        IsMeasuring = knownSize is null;
        IsSelected = IsAvailable && (wasSelected ?? target.IsRecommended);
    }

    public CacheTarget Target { get; }
    public string Title => Target.Title;
    public string Detail => Target.Detail;
    public string LocationsText => Target.Action == CacheAction.EmptyRecycleBin
        ? "All drives"
        : string.Join("  ·  ", Target.Locations.Select(DisplayPath));

    /// <summary>False for administrator-only targets while the app isn't elevated.</summary>
    public bool IsAvailable { get; }

    public string? Badge => !IsAvailable ? "Needs administrator" : Target.Safety == CacheSafety.Caution ? "Review first" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    public partial long? Size { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    public partial bool IsMeasuring { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string SizeText => IsMeasuring ? "Measuring…" : ByteFormatting.Format(Size ?? 0);

    [RelayCommand]
    private void OpenFolder()
    {
        if (Target.Locations.Count > 0)
        {
            ShellActions.Open(Target.Locations[0]);
        }
    }

    private static string DisplayPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(profile) && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[profile.Length..]
            : path;
    }
}

public sealed class CacheGroupViewModel(string title, IReadOnlyList<CacheRowViewModel> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<CacheRowViewModel> Rows { get; } = rows;
    public bool IsProjects => Rows.Count > 0 && Rows[0].Target.Group == CacheGroup.Projects;
}

public sealed partial class CacheCleanerViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly CacheCleaner _cleaner;
    private IReadOnlyList<CacheTarget> _catalogTargets = [];
    private IReadOnlyList<ArtifactDirectory> _artifacts = [];
    private readonly List<CacheRowViewModel> _rows = [];

    public CacheCleanerViewModel(MainViewModel main) : base("Cache Cleaner", Icons.Clean)
    {
        _main = main;
        _cleaner = new CacheCleaner(main.Guard, SizeMode.Allocated, Drives.ClusterSize(Drives.SystemDriveRoot), new RecycleBin());
        Groups = [];
        SelectionText = string.Empty;
        RecommendedSizeText = "—";
    }

    public bool IsElevated => Elevation.IsElevated;

    public ObservableCollection<CacheGroupViewModel> Groups { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CleanCommand))]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CleanCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial string SelectionText { get; set; }

    /// <summary>Size of the safe, recommended caches (shown on the Overview).</summary>
    [ObservableProperty]
    public partial string RecommendedSizeText { get; set; }

    [ObservableProperty]
    public partial bool IsMeasuring { get; set; }

    [ObservableProperty]
    public partial string? RunningAppsWarning { get; set; }

    [ObservableProperty]
    public partial bool ShowAdminBanner { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    private bool CanRefresh() => !IsRefreshing && !IsCleaning;

    /// <summary>Finds cache folders again and measures them in the background.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            _catalogTargets = await Task.Run(() => CacheCatalog.AvailableTargets(_main.Folders, includeRecycleBin: true));
            Rebuild(remeasure: true);
            await MeasureAsync(_rows.Where(row => row.IsMeasuring).Select(row => row.Target).ToList());
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Adds the rebuildable project folders found by the latest scan.</summary>
    public void SetProjectArtifacts(IReadOnlyList<ArtifactDirectory> artifacts)
    {
        _artifacts = artifacts;
        Rebuild(remeasure: false);
        if (!IsRefreshing)
        {
            _ = MeasureAsync(_rows.Where(row => row.IsMeasuring).Select(row => row.Target).ToList());
        }
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var row in _rows)
        {
            row.IsSelected = row.IsAvailable && row.Target.IsRecommended;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        if (!Elevation.RestartElevated())
        {
            _main.Dialogs.Inform("Restart as administrator", "Storage Optimizer is still running as a normal user.");
        }
    }

    private bool CanClean() => !IsCleaning && !IsRefreshing && _rows.Any(row => row.IsSelected && row.IsAvailable);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = _rows.Where(row => row.IsSelected && row.IsAvailable).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var size = selected.Sum(row => row.Size ?? 0);
        var running = ShellActions.RunningProcesses(selected.SelectMany(row => row.Target.ProcessNames));
        var message = $"This permanently deletes about {ByteFormatting.Format(size)} from {selected.Count} locations. It can't be undone.";
        if (selected.Any(row => row.Target.Safety == CacheSafety.Caution))
        {
            message += "\n\nSome selected items are marked \"Review first\".";
        }
        if (running.Count > 0)
        {
            message += $"\n\nThese apps are running and may keep some files in use: {string.Join(", ", running)}.";
        }
        if (!_main.Dialogs.Confirm("Clean selected caches", message))
        {
            return;
        }

        IsCleaning = true;
        CleanReport report;
        try
        {
            var targets = selected.Select(row => row.Target).ToList();
            report = await Task.Run(() => _cleaner.Clean(targets));
        }
        finally
        {
            IsCleaning = false;
        }

        if (report.RemovedItems.Count > 0)
        {
            var removed = new HashSet<string>(report.RemovedItems.Select(PathText.Normalize), StringComparer.OrdinalIgnoreCase);
            _artifacts = _artifacts.Where(artifact => !removed.Contains(PathText.Normalize(artifact.Path))).ToList();
            _main.ApplyRemovals(report.RemovedItems);
        }
        _main.RefreshDrive();

        var summary = $"Freed {ByteFormatting.Format(report.FreedBytes)}.";
        if (report.FailureCount > 0)
        {
            summary += $"\n\n{report.FailureCount:N0} items couldn't be removed; they're usually in use by a running app or protected by Windows:\n"
                + string.Join("\n", report.Failures.Take(6).Select(failure => "• " + failure.Path));
        }
        _main.Dialogs.Inform("Cleaning finished", summary + "\n\nScan again to update the other pages.");
        await RefreshAsync();
    }

    private void Rebuild(bool remeasure)
    {
        var previous = _rows.ToDictionary(row => row.Target.Id, StringComparer.OrdinalIgnoreCase);
        var artifactSizes = _artifacts.ToDictionary(artifact => CacheTarget.ArtifactId(artifact.Path), artifact => artifact.Size, StringComparer.OrdinalIgnoreCase);
        var targets = CacheCatalog.ResolveOverlaps(_catalogTargets.Concat(_artifacts.Select(CacheTarget.ForArtifact)));

        foreach (var row in _rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }
        _rows.Clear();
        foreach (var target in targets)
        {
            previous.TryGetValue(target.Id, out var old);
            long? size = target.Group == CacheGroup.Projects
                ? artifactSizes.GetValueOrDefault(target.Id)
                : remeasure || old is null || old.IsMeasuring ? null : old.Size;
            var row = new CacheRowViewModel(target, IsElevated, size, old?.IsSelected);
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }

        Groups.Clear();
        foreach (var group in Enum.GetValues<CacheGroup>())
        {
            var rows = _rows.Where(row => row.Target.Group == group).ToList();
            if (rows.Count > 0)
            {
                Groups.Add(new CacheGroupViewModel(group.Title(), rows));
            }
        }
        IsEmpty = _rows.Count == 0;
        ShowAdminBanner = !IsElevated && _rows.Any(row => !row.IsAvailable);
        UpdateSummary();
    }

    /// <summary>Measures targets in parallel and updates whichever row currently shows each target.</summary>
    private async Task MeasureAsync(IReadOnlyList<CacheTarget> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }
        IsMeasuring = true;
        UpdateSummary();
        var dispatcher = App.Current.Dispatcher;
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) };
        await Task.Run(() => Parallel.ForEach(targets, options, target =>
        {
            var size = _cleaner.Measure(target);
            dispatcher.Invoke(() =>
            {
                if (_rows.FirstOrDefault(row => row.Target.Id == target.Id) is { } row)
                {
                    row.Size = size;
                    row.IsMeasuring = false;
                }
            });
        }));
        IsMeasuring = _rows.Any(row => row.IsMeasuring);
        UpdateSummary();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CacheRowViewModel.IsSelected) or nameof(CacheRowViewModel.Size))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var selected = _rows.Where(row => row.IsSelected && row.IsAvailable).ToList();
        SelectionText = $"{selected.Count} selected · {ByteFormatting.Format(selected.Sum(row => row.Size ?? 0))}";
        RecommendedSizeText = ByteFormatting.Format(_rows
            .Where(row => row.IsAvailable && row.Target.IsRecommended && row.Target.Action != CacheAction.EmptyRecycleBin)
            .Sum(row => row.Size ?? 0));
        var running = ShellActions.RunningProcesses(selected.SelectMany(row => row.Target.ProcessNames));
        RunningAppsWarning = running.Count > 0
            ? $"Close {string.Join(", ", running)} before cleaning, so their caches aren't in use."
            : null;
        CleanCommand.NotifyCanExecuteChanged();
    }
}
