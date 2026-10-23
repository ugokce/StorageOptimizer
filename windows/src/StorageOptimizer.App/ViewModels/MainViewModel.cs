using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.ViewModels;

public abstract class PageViewModel(string title, string glyph) : ObservableObject
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
}

/// <summary>Disk capacity of the drive being looked at.</summary>
public sealed record DriveSummary(string Name, string Root, long Total, long Free)
{
    public long Used => Math.Max(0, Total - Free);
    public double UsedFraction => Total > 0 ? (double)Used / Total : 0;

    public static DriveSummary? For(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            var drive = new DriveInfo(root);
            return drive.IsReady ? new DriveSummary(Drives.DisplayName(drive), drive.Name, drive.TotalSize, drive.AvailableFreeSpace) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>State shared by the whole window: what to scan, the scan itself and its result.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _progressTimer;
    private CancellationTokenSource? _scanCancellation;
    private ScanProgress? _progress;

    public MainViewModel(IDialogs dialogs)
    {
        Dialogs = dialogs;
        Folders = KnownFolders.Current();
        var systemDrive = PathText.Normalize(Drives.SystemDriveRoot);
        Guard = new DeletionGuard(new DeletionRules
        {
            UserProfile = Folders.UserProfile,
            DataDriveRoots = Drives.LocalDrives()
                .Select(drive => drive.RootDirectory.FullName)
                .Where(root => !PathText.Normalize(root).Equals(systemDrive, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            CacheRoots = CacheCatalog.AllLocations(Folders),
        });

        ScanTargets = new ObservableCollection<ScanTargetOption>(BuildScanTargets());
        SelectedScanTarget = ScanTargets[0];
        ProgressText = string.Empty;
        StatusText = "Not scanned yet.";

        Overview = new OverviewViewModel(this);
        LargeFiles = new LargeFilesViewModel(this);
        FolderBrowser = new FoldersViewModel(this);
        FileTypes = new FileTypesViewModel(this);
        CacheCleaner = new CacheCleanerViewModel(this);
        Pages = [Overview, LargeFiles, FolderBrowser, FileTypes, CacheCleaner];
        SelectedPage = Overview;

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => UpdateProgressText();
        RefreshDrive();
    }

    public IDialogs Dialogs { get; }
    public KnownFolders Folders { get; }
    public DeletionGuard Guard { get; }

    public OverviewViewModel Overview { get; }
    public LargeFilesViewModel LargeFiles { get; }
    public FoldersViewModel FolderBrowser { get; }
    public FileTypesViewModel FileTypes { get; }
    public CacheCleanerViewModel CacheCleaner { get; }
    public IReadOnlyList<PageViewModel> Pages { get; }

    public ObservableCollection<ScanTargetOption> ScanTargets { get; }

    [ObservableProperty]
    public partial PageViewModel SelectedPage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial ScanTargetOption SelectedScanTarget { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(StopCommand), nameof(ChooseFolderCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>A one-line description of the last scan, or why there is none.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial ScanResult? Result { get; set; }

    [ObservableProperty]
    public partial DriveSummary? Drive { get; set; }

    /// <summary>Raised after a scan finishes and after items are removed from the result.</summary>
    public event EventHandler? ResultChanged;

    partial void OnSelectedScanTargetChanged(ScanTargetOption value)
    {
        if (!IsScanning)
        {
            RefreshDrive();
        }
    }

    private bool CanScan() => !IsScanning && SelectedScanTarget is not null;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanAsync() => ScanPathAsync(SelectedScanTarget);

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void Stop() => _scanCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ChooseFolderAsync()
    {
        if (Dialogs.PickFolder() is not { } folder)
        {
            return;
        }
        var existing = ScanTargets.FirstOrDefault(target => !target.IsDrive && PathText.AreEqual(target.Path, folder));
        if (existing is null)
        {
            existing = new ScanTargetOption(PathText.FileName(folder) is { Length: > 0 } name ? name : folder, folder, IsDrive: false);
            ScanTargets.Add(existing);
        }
        SelectedScanTarget = existing;
        await ScanPathAsync(existing);
    }

    /// <summary>Scans a target and publishes the result. Also used by the smoke test.</summary>
    public async Task ScanPathAsync(ScanTargetOption target)
    {
        if (IsScanning)
        {
            return;
        }
        IsScanning = true;
        _scanCancellation = new CancellationTokenSource();
        _progress = new ScanProgress();
        ProgressText = "Starting…";
        _progressTimer.Start();

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(target.Path)) ?? target.Path;
            var options = new ScanOptions
            {
                ClusterSize = Drives.ClusterSize(root),
                SkipPaths = target.IsDrive ? ScanOptions.DriveRootSkipPaths(root) : [],
            };
            var result = await new DiskScanner(options).ScanAsync(target.Path, _progress, _scanCancellation.Token);

            Result = result;
            StatusText = $"{target.Title}: {ByteFormatting.Format(result.TotalSize)} in {result.FileCount:N0} files, scanned in {FormatDuration(result.Duration)}"
                + (result.InaccessibleCount > 0 ? $" · {result.InaccessibleCount:N0} folders couldn't be read" : string.Empty);
            RefreshDrive();
            ResultChanged?.Invoke(this, EventArgs.Empty);
            CacheCleaner.SetProjectArtifacts(result.ArtifactDirectories);
        }
        catch (OperationCanceledException)
        {
            StatusText = "The scan was stopped.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = "The scan failed.";
            Dialogs.Inform("Scan failed", exception.Message, isWarning: true);
        }
        finally
        {
            _progressTimer.Stop();
            ProgressText = string.Empty;
            IsScanning = false;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>Asks for confirmation, then moves the items to the Recycle Bin and updates the result.</summary>
    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }
        var size = paths.Sum(path => Result?.FindNode(path)?.Size ?? Result?.LargestFiles.FirstOrDefault(file => file.Path == path)?.Size ?? 0);
        var what = paths.Count == 1 ? $"\"{PathText.FileName(paths[0])}\"" : $"{paths.Count:N0} items";
        if (!Dialogs.Confirm("Move to Recycle Bin",
                $"Move {what} ({ByteFormatting.Format(size)}) to the Recycle Bin?\n\nYou can restore them from the Recycle Bin. The space is freed when you empty it."))
        {
            return;
        }

        var removed = new List<string>();
        var failures = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                Guard.Validate(path);
                if (Drives.IsNetworkPath(path))
                {
                    throw new IOException("Network locations have no Recycle Bin, so this item wasn't deleted.");
                }
                RecycleBin.Send(path);
                removed.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or DeletionRefusedException or OperationCanceledException)
            {
                failures.Add($"{PathText.FileName(path)}: {exception.Message}");
            }
        }

        ApplyRemovals(removed);
        if (failures.Count > 0)
        {
            Dialogs.Inform("Some items weren't moved", string.Join("\n", failures.Take(8)), isWarning: true);
        }
    }

    /// <summary>Updates the scan result after items were deleted and tells every page.</summary>
    public void ApplyRemovals(IEnumerable<string> paths)
    {
        var any = false;
        foreach (var path in paths)
        {
            Result?.ApplyRemoval(path);
            any = true;
        }
        RefreshDrive();
        if (any && Result is not null)
        {
            ResultChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ShowInFolders(FileNode node)
    {
        FolderBrowser.Show(node);
        SelectedPage = FolderBrowser;
    }

    public void RefreshDrive() => Drive = DriveSummary.For(Result?.Root.Path ?? SelectedScanTarget.Path);

    private void UpdateProgressText()
    {
        if (_progress is null)
        {
            return;
        }
        var snapshot = _progress.Read();
        ProgressText = $"{snapshot.FilesScanned:N0} files · {ByteFormatting.Format(snapshot.BytesScanned)} · {snapshot.CurrentPath}";
    }

    private IEnumerable<ScanTargetOption> BuildScanTargets()
    {
        if (!string.IsNullOrEmpty(Folders.UserProfile))
        {
            yield return new ScanTargetOption($"Your user folder ({PathText.FileName(Folders.UserProfile)})", Folders.UserProfile, IsDrive: false);
        }
        foreach (var drive in Drives.LocalDrives())
        {
            yield return new ScanTargetOption(Drives.DisplayName(drive), drive.RootDirectory.FullName, IsDrive: true);
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds} s" : $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} s";
}
