using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Application.Drives;
using DiskAnalyzer.Application.Duplicates;
using DiskAnalyzer.Application.Indexing;
using DiskAnalyzer.Application.Scanning;
using DiskAnalyzer.Application.Searching;
using DiskAnalyzer.Infrastructure.FileSystem;
using DiskAnalyzer.Infrastructure.Hashing;
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

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var worker = Services.GetService<WorkerClient>();
        if (worker is not null)
        {
            try { worker.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); }
            catch { }
        }
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IDriveService, DriveService>();
        services.AddSingleton<IHashService, HashService>();

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

        services.AddSingleton<ScanService>();
        services.AddSingleton<DuplicateFinderService>();
        services.AddSingleton<IndexingService>();
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