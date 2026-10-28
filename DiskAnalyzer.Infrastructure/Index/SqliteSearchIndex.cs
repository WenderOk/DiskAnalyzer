using System.Globalization;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Models;
using Microsoft.Data.Sqlite;

namespace DiskAnalyzer.Infrastructure.Index;

public sealed class SqliteSearchIndex : ISearchIndex
{
    private readonly SqliteSearchIndexOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    public SqliteSearchIndex(SqliteSearchIndexOptions options)
    {
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is not null)
                return;

            var dir = Path.GetDirectoryName(_options.DatabasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
            await _connection.OpenAsync(ct);

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    full_path TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    extension TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    last_modified TEXT NOT NULL,
                    content_preview TEXT,
                    content_hash TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_files_name ON files(name);
                CREATE INDEX IF NOT EXISTS idx_files_extension ON files(extension);
                CREATE INDEX IF NOT EXISTS idx_files_size ON files(size);
                CREATE INDEX IF NOT EXISTS idx_files_hash ON files(content_hash);
            ";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task AddOrUpdateAsync(IReadOnlyList<IndexedFile> files, CancellationToken ct = default)
    {
        EnsureInitialized();
        if (files.Count == 0) return;

        await _gate.WaitAsync(ct);
        try
        {
            await using var tx = (SqliteTransaction)await _connection!.BeginTransactionAsync(ct);
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO files (full_path, name, extension, size, last_modified, content_preview, content_hash)
                VALUES ($path, $name, $ext, $size, $modified, $preview, $hash)
                ON CONFLICT(full_path) DO UPDATE SET
                    name = excluded.name,
                    extension = excluded.extension,
                    size = excluded.size,
                    last_modified = excluded.last_modified,
                    content_preview = excluded.content_preview,
                    content_hash = excluded.content_hash;
            ";

            var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
            var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
            var pModified = cmd.Parameters.Add("$modified", SqliteType.Text);
            var pPreview = cmd.Parameters.Add("$preview", SqliteType.Text);
            var pHash = cmd.Parameters.Add("$hash", SqliteType.Text);

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                pPath.Value = f.FullPath;
                pName.Value = f.Name;
                pExt.Value = f.Extension;
                pSize.Value = f.Size.Bytes;
                pModified.Value = f.LastModified.ToString("O", CultureInfo.InvariantCulture);
                pPreview.Value = (object?)f.ContentPreview ?? DBNull.Value;
                pHash.Value = (object?)f.ContentHash ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        EnsureInitialized();

        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection!.CreateCommand();
            var where = new List<string>();

            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                where.Add("(name LIKE $text OR content_preview LIKE $text)");
                cmd.Parameters.AddWithValue("$text", $"%{query.Text}%");
            }
            if (!string.IsNullOrWhiteSpace(query.NamePattern))
            {
                where.Add("name LIKE $name");
                cmd.Parameters.AddWithValue("$name", query.NamePattern);
            }
            if (!string.IsNullOrWhiteSpace(query.Extension))
            {
                where.Add("LOWER(extension) = LOWER($ext)");
                cmd.Parameters.AddWithValue("$ext", query.Extension);
            }
            if (query.MinSize.Bytes > 0)
            {
                where.Add("size >= $minSize");
                cmd.Parameters.AddWithValue("$minSize", query.MinSize.Bytes);
            }
            if (query.MaxSize is { } maxSize)
            {
                where.Add("size <= $maxSize");
                cmd.Parameters.AddWithValue("$maxSize", maxSize.Bytes);
            }
            if (query.ModifiedAfter is { } after)
            {
                where.Add("last_modified >= $after");
                cmd.Parameters.AddWithValue("$after", after.ToString("O", CultureInfo.InvariantCulture));
            }
            if (query.ModifiedBefore is { } before)
            {
                where.Add("last_modified <= $before");
                cmd.Parameters.AddWithValue("$before", before.ToString("O", CultureInfo.InvariantCulture));
            }

            var sql = "SELECT full_path, name, extension, size, last_modified, content_preview, content_hash FROM files";
            if (where.Count > 0)
                sql += " WHERE " + string.Join(" AND ", where);
            sql += " LIMIT $limit";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$limit", query.MaxResults);

            var results = new List<SearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var file = new IndexedFile
                {
                    FullPath = reader.GetString(0),
                    Name = reader.GetString(1),
                    Extension = reader.GetString(2),
                    Size = new FileSize(reader.GetInt64(3)),
                    LastModified = DateTime.Parse(
                        reader.GetString(4), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    ContentPreview = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ContentHash = reader.IsDBNull(6) ? null : reader.GetString(6)
                };
                results.Add(new SearchResult
                {
                    File = file,
                    MatchedSnippet = null,
                    Score = 100
                });
            }
            return results;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM files";
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM files";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        _gate.Dispose();
    }

    private void EnsureInitialized()
    {
        if (_connection is null)
            throw new InvalidOperationException(
                "Индекс не инициализирован. Вызовите InitializeAsync().");
    }
}