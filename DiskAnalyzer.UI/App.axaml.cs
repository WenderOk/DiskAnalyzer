using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Application.Drives;
using DiskAnalyzer.Application.Searching;
using DiskAnalyzer.Infrastructure.FileSystem;
using DiskAnalyzer.Infrastructure.Index;
using DiskAnalyzer.UI.ViewModels;
using DiskAnalyzer.UI.Views;
using DiskAnalyzer.Worker.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DiskAnalyzer.UI;

public sealed partial class App : Avalonia.Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool _shutdownStarted;

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        e.Cancel = true;

        // Нельзя блокировать UI-поток ожиданием Worker: даём ему корректно
        // отменить операции и завершиться, после чего закрываем приложение.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var worker = Services.GetService<WorkerClient>();
                if (worker is not null)
                    await worker.DisposeAsync();
            }
            catch { }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDriveService, DriveService>();

        services.AddSingleton<ISearchIndex>(_ =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiskAnalyzer");
            Directory.CreateDirectory(dir);
            return new SqliteSearchIndex(new SqliteSearchIndexOptions
            {
                DatabasePath = Path.Combine(dir, "index.db")
            });
        });

        services.AddSingleton<SearchService>();
        services.AddSingleton<DriveCatalogService>();

        services.AddSingleton<WorkerClient>(_ =>
        {
            var exe = Path.Combine(
                AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "DiskAnalyzer.Worker.exe" : "DiskAnalyzer.Worker");
            return new WorkerClient(new WorkerClientOptions { WorkerExecutablePath = exe });
        });

        services.AddSingleton<DriveListViewModel>();
        services.AddSingleton<ScannerViewModel>();
        services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<IndexingViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}