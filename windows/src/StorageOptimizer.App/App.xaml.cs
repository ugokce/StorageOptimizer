using System.Windows;
using StorageOptimizer.App.Infrastructure;
using StorageOptimizer.App.Services;
using StorageOptimizer.App.ViewModels;

namespace StorageOptimizer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CategoryPalette.Initialize();

        if (SmokeTest.TryCreate(e.Args) is { } smokeTest)
        {
            Shutdown(await smokeTest.RunAsync());
            return;
        }
        if (ScreenshotRunner.TryCreate(e.Args) is { } screenshots)
        {
            Shutdown(await screenshots.RunAsync());
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Storage Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var viewModel = new MainViewModel(new WindowsDialogs());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.CacheCleaner.RefreshAsync();
    }
}
