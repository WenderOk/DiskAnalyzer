using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Application.Duplicates;
using DiskAnalyzer.Application.Indexing;
using DiskAnalyzer.Application.Scanning;
using DiskAnalyzer.Infrastructure.FileSystem;
using DiskAnalyzer.Infrastructure.Hashing;
using DiskAnalyzer.Infrastructure.Index;
using DiskAnalyzer.Worker;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSingleton<IFileSystemService, FileSystemService>();
services.AddSingleton<IDriveService, DriveService>();
services.AddSingleton<IHashService, HashService>();

services.AddSingleton<ScanService>();
services.AddSingleton<DuplicateFinderService>();
services.AddSingleton<IndexingService>();

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

services.AddSingleton<WorkerHost>();

await using var provider = services.BuildServiceProvider();
await provider.GetRequiredService<WorkerHost>().RunAsync();