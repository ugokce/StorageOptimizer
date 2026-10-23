using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.App.ViewModels;
using StorageOptimizer.Core;

namespace StorageOptimizer.App;

/// <summary>
/// <c>StorageOptimizer.exe --screenshots &lt;output folder&gt;</c>: scans a generated demo folder, saves a
/// screenshot of every page in light and dark mode, writes <c>screenshots.log</c> and exits with 0 or 1.
/// The Screenshots workflow uses it to keep the README's images current.
/// </summary>
internal sealed class ScreenshotRunner
{
    private const int WindowWidth = 1240;
    private const int WindowHeight = 820;

    private readonly string _outputDirectory;
    private readonly List<string> _log = [];
    private int _failures;

    private ScreenshotRunner(string outputDirectory) => _outputDirectory = outputDirectory;

    public static ScreenshotRunner? TryCreate(string[] args)
    {
        var index = Array.IndexOf(args, "--screenshots");
        if (index < 0)
        {
            return null;
        }
        var output = index + 1 < args.Length ? args[index + 1] : Path.Join(Path.GetTempPath(), "StorageOptimizerScreenshots");
        return new ScreenshotRunner(Path.GetFullPath(output));
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_outputDirectory);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Application.Current.DispatcherUnhandledException += (_, args) =>
        {
            Fail($"Unhandled exception: {args.Exception}");
            args.Handled = true;
        };

        var demo = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Demo");
        var createdDemo = false;
        try
        {
            DemoFolder.Create(demo);
            createdDemo = true;
            await CapturePagesAsync(demo);
        }
        catch (Exception exception)
        {
            Fail($"Screenshot run crashed: {exception}");
        }
        finally
        {
            try
            {
                if (createdDemo)
                {
                    Directory.Delete(demo, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        Log(_failures == 0 ? "PASSED" : $"FAILED with {_failures} problem(s)");
        File.WriteAllLines(Path.Join(_outputDirectory, "screenshots.log"), _log);
        return _failures == 0 ? 0 : 1;
    }

    private async Task CapturePagesAsync(string demo)
    {
        var viewModel = new MainViewModel(new SmokeTest.SilentDialogs(Log));
        var window = new MainWindow
        {
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = WindowWidth,
            Height = WindowHeight,
        };
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowLargerThanScreen);
        Application.Current.MainWindow = window;
        window.Show();
        window.Activate();
        await Idle();

        await viewModel.CacheCleaner.RefreshAsync();
        await viewModel.ScanPathAsync(viewModel.SelectFolderTarget(demo));
        if (viewModel.Result is not { } result)
        {
            Fail("The scan produced no result.");
            return;
        }
        Log($"Scanned {result.FileCount:N0} files, {ByteFormatting.Format(result.TotalSize)}, {result.ArtifactDirectories.Count} project folders.");
        if (!await WaitUntilAsync(TimeSpan.FromMinutes(4), () => !viewModel.CacheCleaner.IsRefreshing && !viewModel.CacheCleaner.IsMeasuring))
        {
            Log("Cache Cleaner was still measuring after 4 minutes.");
        }
        Log($"Cache Cleaner found {viewModel.CacheCleaner.Groups.Sum(group => group.Rows.Count)} targets.");
        Log($"Screen {SystemParameters.PrimaryScreenWidth:F0}x{SystemParameters.PrimaryScreenHeight:F0}, window {window.ActualWidth:F0}x{window.ActualHeight:F0}.");

#pragma warning disable WPF0001 // ThemeMode is marked experimental in some .NET versions.
        foreach (var (theme, dark, suffix) in new[] { (ThemeMode.Light, false, "light"), (ThemeMode.Dark, true, "dark") })
        {
            Application.Current.ThemeMode = theme;
#pragma warning restore WPF0001
            CategoryPalette.UseTheme(dark);
            foreach (var page in viewModel.Pages)
            {
                viewModel.SelectedPage = page;
                await Idle();
                await Task.Delay(700);
                await Idle();
                Capture(window, $"{page.Title.Replace(' ', '-').ToLowerInvariant()}-{suffix}", dark);
            }
        }
    }

    /// <summary>Prefers the window as Windows shows it; falls back to WPF's own rendering of the content.</summary>
    private void Capture(Window window, string name, bool dark)
    {
        if (SmokeTest.FindColumnGap(window) is { } gap)
        {
            Fail($"{name}: {gap}");
        }
        var path = Path.Join(_outputDirectory, name + ".png");
        if (WindowCapture.CaptureWindow(window) is { } image)
        {
            WindowCapture.SavePng(image, path);
            Log($"{name}: PrintWindow, {image.PixelWidth}x{image.PixelHeight}");
        }
        else if (WindowCapture.RenderContent(window, dark) is { } content)
        {
            WindowCapture.SavePng(content, path);
            Log($"{name}: RenderTargetBitmap (PrintWindow gave no usable image), {content.PixelWidth}x{content.PixelHeight}");
        }
        else
        {
            Fail($"{name}: couldn't capture the window");
        }
    }

    /// <summary>CI screens can be smaller than the window; this stops Windows from shrinking it.</summary>
    private static IntPtr AllowLargerThanScreen(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            info.ptMaxTrackSize = new NativeMethods.POINT { X = 8192, Y = 8192 };
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }
        return IntPtr.Zero;
    }

    private static async Task<bool> WaitUntilAsync(TimeSpan timeout, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }
            await Task.Delay(200);
        }
        return true;
    }

    private static async Task Idle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private void Fail(string message)
    {
        _failures++;
        Log("FAIL: " + message);
    }

    private void Log(string message) => _log.Add(message);
}

/// <summary>
/// A folder of sparse files that looks like a typical, rather full user folder, so every page and
/// file type has something to show. The files take almost no real disk space.
/// </summary>
internal static class DemoFolder
{
    /// <summary>Refuses to touch a folder that already exists, since it is deleted again afterwards.</summary>
    public static void Create(string root)
    {
        if (Path.Exists(root))
        {
            throw new IOException($"{root} already exists. Move or rename it, then try again.");
        }
        try
        {
            Populate(root);
        }
        catch when (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private static void Populate(string root)
    {
        var index = 0;
        foreach (var (relativePath, megabytes, daysAgo) in Entries())
        {
            var path = Path.Join(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Without the sparse flag, NTFS would reserve the full size on disk.
                if (!NativeMethods.DeviceIoControl(stream.SafeFileHandle, NativeMethods.FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                {
                    throw new IOException($"Couldn't make {path} a sparse file (error {Marshal.GetLastWin32Error()}).");
                }
                stream.SetLength((long)(megabytes * 1_000_000));
            }

            // Spread the dates over two years so "last opened" and the 6-month filter have variety.
            var date = DateTime.Now.AddDays(-(daysAgo ?? index * 37 % 720));
            File.SetCreationTime(path, date);
            File.SetLastWriteTime(path, date);
            File.SetLastAccessTime(path, date);
            index++;
        }
    }

    private static IEnumerable<(string Path, double Megabytes, int? DaysAgo)> Entries()
    {
        yield return (@"Videos\Lisbon Trip 2025.mp4", 8_600, 40);
        yield return (@"Videos\Captures\Game Clip 2025-03-14.mp4", 2_400, 220);
        yield return (@"Videos\Drone Footage\DJI_0042.mp4", 1_900, 410);
        yield return (@"Videos\Drone Footage\DJI_0043.mp4", 1_600, 410);
        yield return (@"Pictures\Design\Festival Poster.psd", 780, 300);
        yield return (@"Downloads\ubuntu-24.04.2-desktop-amd64.iso", 6_100, 260);
        yield return (@"Downloads\Docker Desktop Installer.exe", 620, 90);
        yield return (@"Downloads\blender-4.4-windows-x64.msi", 380, 200);
        yield return (@"Downloads\Fonts.zip", 220, 330);
        yield return (@"Downloads\Invoice-2025-08.pdf", 0.4, 70);
        yield return (@"Documents\Backups\Website backup 2023.zip", 3_400, 600);
        yield return (@"Documents\Quarterly Review.pptx", 640, 30);
        yield return (@"Documents\Thesis.pdf", 48, 500);
        yield return (@"Documents\Taxes 2024.pdf", 12, 150);
        yield return (@"Virtual Machines\Ubuntu Server.vhdx", 9_800, 380);
        yield return (@"WSL\Ubuntu\ext4.vhdx", 7_200, 3);
        yield return (@"Games\Indie Game\IndieGame.exe", 180, 45);
        yield return (@"Games\Indie Game\UnityPlayer.dll", 30, 45);
        yield return (@"Games\Indie Game\IndieGame_Data\resources.assets", 2_800, 45);
        yield return (@"Developer\website\package.json", 0.002, 5);
        yield return (@"Developer\api\Cargo.toml", 0.001, 12);
        yield return (@"Developer\api\src\main.rs", 0.02, 12);
        yield return (@"Developer\api\target\debug\api.exe", 180, 12);
        yield return (@"Developer\game\Assets\Scenes\Main.unity", 2, 8);
        yield return (@"Developer\game\ProjectSettings\ProjectSettings.asset", 0.05, 8);
        yield return (@"Developer\desktop-app\DesktopApp.csproj", 0.002, 20);
        yield return (@"Developer\desktop-app\MainWindow.xaml.cs", 0.01, 20);
        yield return (@"Developer\ml-experiments\.venv\pyvenv.cfg", 0.001, 300);
        yield return (@"Developer\ml-experiments\.venv\Lib\site-packages\torch\lib\torch_cpu.dll", 1_400, 300);
        yield return (@"Developer\ml-experiments\train.py", 0.01, 300);
        for (var photo = 1; photo <= 320; photo++)
        {
            yield return ($@"Pictures\Camera Roll\IMG_{4000 + photo}.jpg", 12, null);
        }
        for (var raw = 1; raw <= 120; raw++)
        {
            yield return ($@"Pictures\RAW\DSC_{1000 + raw}.nef", 45, null);
        }
        for (var album = 1; album <= 4; album++)
        {
            for (var track = 1; track <= 12; track++)
            {
                yield return ($@"Music\Lossless\Album {album}\Track {track}.flac", 32, null);
            }
        }
        for (var episode = 1; episode <= 30; episode++)
        {
            yield return ($@"Music\Podcasts\Episode {episode}.mp3", 60, null);
        }
        for (var note = 1; note <= 150; note++)
        {
            yield return ($@"Documents\Notes\Note {note}.md", 0.008, null);
        }
        for (var year = 2021; year <= 2025; year++)
        {
            yield return ($@"Documents\Spreadsheets\Budget {year}.xlsx", 2, null);
        }
        for (var page = 1; page <= 20; page++)
        {
            yield return ($@"Developer\website\src\app\page{page}.tsx", 0.01, 5);
        }
        for (var package = 1; package <= 40; package++)
        {
            yield return ($@"Developer\website\node_modules\package-{package}\index.js", 6, 5);
            yield return ($@"Developer\website\node_modules\package-{package}\dist\bundle.js", 18, 5);
        }
        for (var pack = 1; pack <= 12; pack++)
        {
            yield return ($@"Developer\website\.next\cache\webpack\client-production\{pack}.pack", 45, 5);
        }
        for (var crate = 1; crate <= 60; crate++)
        {
            yield return ($@"Developer\api\target\debug\deps\libcrate_{crate}.rlib", 42, 12);
        }
        for (var shader = 1; shader <= 40; shader++)
        {
            yield return ($@"Developer\game\Library\ShaderCache\shader_{shader}.bin", 60, 8);
        }
        for (var artifact = 1; artifact <= 200; artifact++)
        {
            yield return ($@"Developer\game\Library\Artifacts\{artifact:x2}\asset_{artifact}.info", 8, 8);
        }
        for (var assembly = 1; assembly <= 20; assembly++)
        {
            yield return ($@"Developer\desktop-app\bin\Debug\net10.0\Dependency{assembly}.dll", 12, 20);
        }
        for (var cache = 1; cache <= 10; cache++)
        {
            yield return ($@"Developer\desktop-app\obj\Debug\net10.0\build{cache}.cache", 20, 20);
        }
    }
}
