using LuckyStats.Core;
using Microsoft.Data.Sqlite;

namespace LuckyStats.Infrastructure;

public sealed record CaptureAccountData(
    string Scope,
    string? SteamId,
    string Label,
    IReadOnlyDictionary<string, long> Values,
    string RawJson,
    bool Available = true,
    string? Error = null);

public sealed record CaptureBatchData(
    long Id,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyDictionary<string, long> GlobalValues,
    IReadOnlyList<AccountFacts> Accounts);

public sealed class SqliteSnapshotStore
{
    private readonly string _connectionString;

    public SqliteSnapshotStore(string databaseFile)
    {
        var directory = Path.GetDirectoryName(databaseFile)
                        ?? throw new ArgumentException("数据库路径缺少目录。", nameof(databaseFile));
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS capture_batch (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_at_utc TEXT NOT NULL,
                app_id INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS snapshot (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                batch_id INTEGER NOT NULL REFERENCES capture_batch(id) ON DELETE CASCADE,
                scope TEXT NOT NULL,
                steam_id TEXT NULL,
                label TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                available INTEGER NOT NULL DEFAULT 1,
                error TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS fact_value (
                snapshot_id INTEGER NOT NULL REFERENCES snapshot(id) ON DELETE CASCADE,
                api_name TEXT NOT NULL,
                value INTEGER NOT NULL,
                PRIMARY KEY(snapshot_id, api_name)
            );
            CREATE INDEX IF NOT EXISTS ix_capture_batch_time ON capture_batch(captured_at_utc);
            CREATE INDEX IF NOT EXISTS ix_snapshot_batch_scope ON snapshot(batch_id, scope);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> SaveBatchAsync(
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<CaptureAccountData> captures,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var batchCommand = connection.CreateCommand();
        batchCommand.Transaction = transaction;
        batchCommand.CommandText = "INSERT INTO capture_batch(captured_at_utc, app_id) VALUES ($at, $app); SELECT last_insert_rowid();";
        batchCommand.Parameters.AddWithValue("$at", capturedAtUtc.ToUniversalTime().ToString("O"));
        batchCommand.Parameters.AddWithValue("$app", ProjectPaths.PlaytestAppId);
        var batchId = (long)(await batchCommand.ExecuteScalarAsync(cancellationToken)
                             ?? throw new InvalidOperationException("无法创建快照批次。"));

        foreach (var capture in captures)
        {
            var snapshotCommand = connection.CreateCommand();
            snapshotCommand.Transaction = transaction;
            snapshotCommand.CommandText = """
                INSERT INTO snapshot(batch_id, scope, steam_id, label, raw_json, available, error)
                VALUES ($batch, $scope, $steam, $label, $raw, $available, $error);
                SELECT last_insert_rowid();
                """;
            snapshotCommand.Parameters.AddWithValue("$batch", batchId);
            snapshotCommand.Parameters.AddWithValue("$scope", capture.Scope);
            snapshotCommand.Parameters.AddWithValue("$steam", (object?)capture.SteamId ?? DBNull.Value);
            snapshotCommand.Parameters.AddWithValue("$label", capture.Label);
            snapshotCommand.Parameters.AddWithValue("$raw", capture.RawJson);
            snapshotCommand.Parameters.AddWithValue("$available", capture.Available ? 1 : 0);
            snapshotCommand.Parameters.AddWithValue("$error", (object?)capture.Error ?? DBNull.Value);
            var snapshotId = (long)(await snapshotCommand.ExecuteScalarAsync(cancellationToken)
                                    ?? throw new InvalidOperationException("无法创建快照。"));

            foreach (var value in capture.Values)
            {
                var factCommand = connection.CreateCommand();
                factCommand.Transaction = transaction;
                factCommand.CommandText = "INSERT INTO fact_value(snapshot_id, api_name, value) VALUES ($snapshot, $api, $value);";
                factCommand.Parameters.AddWithValue("$snapshot", snapshotId);
                factCommand.Parameters.AddWithValue("$api", value.Key);
                factCommand.Parameters.AddWithValue("$value", value.Value);
                await factCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return batchId;
    }

    public async Task<CaptureBatchData?> GetLatestGlobalBatchAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id, b.captured_at_utc
            FROM capture_batch b
            JOIN snapshot s ON s.batch_id = b.id AND s.scope = 'global'
            ORDER BY b.captured_at_utc DESC LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var id = reader.GetInt64(0);
        var capturedAt = DateTimeOffset.Parse(reader.GetString(1));
        await reader.DisposeAsync();
        return await LoadBatchAsync(connection, id, capturedAt, cancellationToken);
    }

    public async Task<CaptureBatchData?> GetGlobalBatchAtOrBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id, b.captured_at_utc
            FROM capture_batch b
            JOIN snapshot s ON s.batch_id = b.id AND s.scope = 'global'
            WHERE b.captured_at_utc <= $cutoff
            ORDER BY b.captured_at_utc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.ToUniversalTime().ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var id = reader.GetInt64(0);
        var capturedAt = DateTimeOffset.Parse(reader.GetString(1));
        await reader.DisposeAsync();
        return await LoadBatchAsync(connection, id, capturedAt, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(
        string apiName,
        CancellationToken cancellationToken = default)
    {
        var points = new List<HistoryPoint>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id, b.captured_at_utc
            FROM capture_batch b
            JOIN snapshot s ON s.batch_id = b.id AND s.scope = 'global'
            ORDER BY b.captured_at_utc;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var batches = new List<(long Id, DateTimeOffset At)>();
        while (await reader.ReadAsync(cancellationToken))
            batches.Add((reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1))));
        await reader.DisposeAsync();

        foreach (var batch in batches)
        {
            var data = await LoadBatchAsync(connection, batch.Id, batch.At, cancellationToken);
            var global = data.GlobalValues.GetValueOrDefault(apiName);
            var excluded = data.Accounts.Where(x => x.Available).Sum(x => x.Values.GetValueOrDefault(apiName));
            points.Add(new HistoryPoint(batch.At, apiName, global, excluded, global - excluded));
        }
        return points;
    }

    private static async Task<CaptureBatchData> LoadBatchAsync(
        SqliteConnection connection,
        long batchId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.scope, s.steam_id, s.label, s.available, s.error, f.api_name, f.value
            FROM snapshot s
            LEFT JOIN fact_value f ON f.snapshot_id = s.id
            WHERE s.batch_id = $batch
            ORDER BY s.id, f.api_name;
            """;
        command.Parameters.AddWithValue("$batch", batchId);

        var snapshots = new Dictionary<long, MutableSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshotId = reader.GetInt64(0);
            if (!snapshots.TryGetValue(snapshotId, out var snapshot))
            {
                snapshot = new MutableSnapshot(
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4) != 0,
                    reader.IsDBNull(5) ? null : reader.GetString(5));
                snapshots[snapshotId] = snapshot;
            }
            if (!reader.IsDBNull(6))
                snapshot.Values[reader.GetString(6)] = reader.GetInt64(7);
        }

        var global = snapshots.Values.FirstOrDefault(x => x.Scope == "global")?.Values
                     ?? new Dictionary<string, long>(StringComparer.Ordinal);
        var accounts = snapshots.Values
            .Where(x => x.Scope == "excluded")
            .Select(x => new AccountFacts(x.SteamId ?? string.Empty, x.Label, x.Values, x.Available, x.Error))
            .ToArray();
        return new CaptureBatchData(batchId, capturedAt, global, accounts);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private sealed class MutableSnapshot(
        string scope,
        string? steamId,
        string label,
        bool available,
        string? error)
    {
        public string Scope { get; } = scope;
        public string? SteamId { get; } = steamId;
        public string Label { get; } = label;
        public bool Available { get; } = available;
        public string? Error { get; } = error;
        public Dictionary<string, long> Values { get; } = new(StringComparer.Ordinal);
    }
}
