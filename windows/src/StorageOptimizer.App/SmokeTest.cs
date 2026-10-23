using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.App.ViewModels;

namespace StorageOptimizer.App;

/// <summary>
/// <c>StorageOptimizer.exe --smoke-test &lt;output folder&gt;</c>: opens the real window off-screen, scans a
/// generated fixture folder, shows every page at a normal and at the smallest window size, checks
/// layout invariants and binding errors, saves a screenshot of each page and exits with 0 or 1.
/// Used by CI, where nobody can look at the window.
/// </summary>
internal sealed class SmokeTest
{
    private readonly string _outputDirectory;
    private readonly List<string> _log = [];
    private int _failures;

    private SmokeTest(string outputDirectory) => _outputDirectory = outputDirectory;

    public static SmokeTest? TryCreate(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke-test");
        if (index < 0)
        {
            return null;
        }
        var output = index + 1 < args.Length ? args[index + 1] : Path.Join(Path.GetTempPath(), "StorageOptimizerSmokeTest");
        return new SmokeTest(Path.GetFullPath(output));
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_outputDirectory);
        // The app exits through App.OnStartup once the result is known, not when the window closes.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        Application.Current.DispatcherUnhandledException += (_, args) =>
        {
            Fail($"Unhandled exception: {args.Exception}");
            args.Handled = true;
        };

        var fixture = CreateFixture();
        try
        {
            var viewModel = new MainViewModel(new SilentDialogs(Log));
            var window = new MainWindow
            {
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                Width = 1240,
                Height = 820,
                ShowActivated = false,
            };
            Application.Current.MainWindow = window;
            window.Show();
            await Idle();

            await viewModel.CacheCleaner.RefreshAsync();
            Log($"Cache Cleaner found {viewModel.CacheCleaner.Groups.Sum(group => group.Rows.Count)} targets; recommended {viewModel.CacheCleaner.RecommendedSizeText}.");

            await viewModel.ScanPathAsync(new ScanTargetOption("Smoke test fixture", fixture, IsDrive: false));
            if (viewModel.Result is not { } result)
            {
                Fail("The scan produced no result.");
                return Finish(bindingErrors);
            }
            Log($"Scanned {result.FileCount} files, {result.TotalSize} bytes, {result.ArtifactDirectories.Count} project folders.");
            Expect(result.FileCount == 7, $"expected 7 files, found {result.FileCount}");
            Expect(result.ArtifactDirectories.Count == 2, $"expected 2 project folders, found {result.ArtifactDirectories.Count}");
            // The fixture's files are small, so list them all.
            viewModel.LargeFiles.SelectedSize = viewModel.LargeFiles.SizeOptions[0];
            Expect(viewModel.LargeFiles.Files.Rows.Count == 7, $"Large Files shows {viewModel.LargeFiles.Files.Rows.Count} rows instead of 7");

            await CheckAllPages(window, viewModel, "normal");

            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            await CheckAllPages(window, viewModel, "small");

#pragma warning disable WPF0001 // ThemeMode is marked experimental in some .NET versions.
            Application.Current.ThemeMode = Application.Current.ThemeMode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
            window.Width = 1240;
            window.Height = 820;
            viewModel.SelectedPage = viewModel.Overview;
            await Idle();
            Screenshot(window, "overview-other-theme");
        }
        catch (Exception exception)
        {
            Fail($"Smoke test crashed: {exception}");
        }
        finally
        {
            try
            {
                Directory.Delete(fixture, recursive: true);
            }
            catch (IOException)
            {
            }
        }
        return Finish(bindingErrors);
    }

    private async Task CheckAllPages(MainWindow window, MainViewModel viewModel, string size)
    {
        foreach (var page in viewModel.Pages)
        {
            viewModel.SelectedPage = page;
            await Idle();
            window.UpdateLayout();
            CheckLayout(window, $"{page.Title} ({size})");
            Screenshot(window, $"{page.Title.Replace(' ', '-').ToLowerInvariant()}-{size}");
        }
    }

    /// <summary>Everything must lie inside the window: the sidebar items and the page below the top bar.</summary>
    private void CheckLayout(MainWindow window, string context)
    {
        var root = (FrameworkElement)window.Content;
        var height = root.ActualHeight;
        var width = root.ActualWidth;
        Expect(height > 0 && width > 0, $"{context}: window content has no size");

        var navigation = (ListBox)window.FindName("Navigation");
        for (var index = 0; index < navigation.Items.Count; index++)
        {
            if (navigation.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement item)
            {
                Fail($"{context}: sidebar item {index} wasn't created");
                continue;
            }
            var top = item.TranslatePoint(new Point(0, 0), root).Y;
            Expect(item.ActualHeight > 0 && top >= 0 && top + item.ActualHeight <= height + 1,
                $"{context}: sidebar item {index} is outside the window (top {top:F0}, height {item.ActualHeight:F0}, window {height:F0})");
        }

        var host = (ContentControl)window.FindName("PageHost");
        var hostTop = host.TranslatePoint(new Point(0, 0), root).Y;
        Expect(hostTop > 0 && hostTop + host.ActualHeight <= height + 1,
            $"{context}: page area is outside the window (top {hostTop:F0}, height {host.ActualHeight:F0}, window {height:F0})");

        if (VisualTreeHelper.GetChildrenCount(host) > 0 && VisualTreeHelper.GetChild(host, 0) is FrameworkElement presenter
            && VisualTreeHelper.GetChildrenCount(presenter) > 0 && VisualTreeHelper.GetChild(presenter, 0) is FrameworkElement page)
        {
            var pageTop = page.TranslatePoint(new Point(0, 0), root).Y;
            Expect(pageTop >= hostTop - 1 && page.ActualHeight <= host.ActualHeight + 1 && page.ActualWidth <= host.ActualWidth + 1,
                $"{context}: page content is larger than its area ({page.ActualWidth:F0}x{page.ActualHeight:F0} in {host.ActualWidth:F0}x{host.ActualHeight:F0}, top {pageTop:F0})");
        }
        else
        {
            Fail($"{context}: no page content was created");
        }
    }

    private void Screenshot(Window window, string name)
    {
        if (WindowCapture.RenderContent(window, CategoryPalette.IsDarkTheme()) is { } image)
        {
            WindowCapture.SavePng(image, Path.Join(_outputDirectory, name + ".png"));
        }
    }

    /// <summary>A small folder with known contents, including two rebuildable project folders.</summary>
    private static string CreateFixture()
    {
        var root = Path.Join(Path.GetTempPath(), "StorageOptimizerFixture-" + Guid.NewGuid().ToString("N"));
        void Write(string relativePath, int size)
        {
            var path = Path.Join(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
        }
        Write(@"Videos\holiday.mp4", 3_000_000);
        Write(@"Documents\report.pdf", 1_500_000);
        Write(@"Documents\notes.txt", 2_000);
        Write(@"Downloads\installer.exe", 2_500_000);
        Write(@"Projects\web\package.json", 100);
        Write(@"Projects\web\node_modules\left-pad\index.js", 50_000);
        Write(@"Projects\game\Library\ShaderCache\cache.bin", 1_200_000);
        Directory.CreateDirectory(Path.Join(root, @"Projects\game\Assets"));
        Directory.CreateDirectory(Path.Join(root, @"Projects\game\ProjectSettings"));
        return root;
    }

    private static async Task Idle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private void Expect(bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    private void Fail(string message)
    {
        _failures++;
        Log("FAIL: " + message);
    }

    private void Log(string message) => _log.Add(message);

    private int Finish(BindingErrorListener bindingErrors)
    {
        foreach (var error in bindingErrors.Errors.Distinct())
        {
            // Path errors mean a binding names a property that doesn't exist: a real bug.
            if (error.Contains("BindingExpression path error", StringComparison.Ordinal))
            {
                Fail("Binding error: " + error);
            }
            else
            {
                Log("Binding warning: " + error);
            }
        }
        Log(_failures == 0 ? "PASSED" : $"FAILED with {_failures} problem(s)");
        File.WriteAllLines(Path.Join(_outputDirectory, "smoke-test.log"), _log);
        return _failures == 0 ? 0 : 1;
    }

    private sealed class BindingErrorListener : TraceListener
    {
        private readonly System.Text.StringBuilder _current = new();

        public List<string> Errors { get; } = [];

        public override void Write(string? message) => _current.Append(message);

        public override void WriteLine(string? message)
        {
            _current.Append(message);
            Errors.Add(_current.ToString());
            _current.Clear();
        }
    }

    /// <summary>Answers every question with No, so nothing is deleted. Also used by screenshot mode.</summary>
    internal sealed class SilentDialogs(Action<string> log) : IDialogs
    {
        public bool Confirm(string title, string message)
        {
            log($"Dialog (answered No): {title}");
            return false;
        }

        public void Inform(string title, string message, bool isWarning = false) => log($"Dialog: {title}: {message}");

        public string? PickFolder() => null;
    }
}
