using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Database;

/// <summary>
/// High-performance SQLite persistence service implemented using Microsoft.Data.Sqlite and Dapper.
/// Supports connection pooling, WAL mode, pragmas for concurrent access, and micro-ORM mapping.
/// </summary>
public class DapperDatabaseService : IDatabaseService, IAsyncDisposable, IDisposable
{
	private readonly string _databasePath;
	private readonly string _connectionString;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private CancellationTokenSource? _checkpointCts;
	private Task? _checkpointLoopTask;
	private bool _isInitialized;
	private bool _disposed;

	static DapperDatabaseService()
	{
		// Register custom DateTime handler for SQLite to seamlessly handle both ISO8601 strings and integer ticks
		SqlMapper.AddTypeHandler(new SqliteDateTimeHandler());
	}

	public DapperDatabaseService()
		: this(Path.Combine(FileSystem.AppDataDirectory, "MugOverflow.db3"))
	{
	}

	public DapperDatabaseService(string databasePath)
	{
		_databasePath = string.IsNullOrWhiteSpace(databasePath)
			? Path.Combine(FileSystem.AppDataDirectory, "MugOverflow.db3")
			: databasePath;

		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = _databasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Private,
			DefaultTimeout = 5,
			ForeignKeys = true,
			Pooling = true
		}.ToString();
	}

	private async Task<SqliteConnection> GetOpenConnectionAsync(CancellationToken ct = default)
	{
		var conn = new SqliteConnection(_connectionString);
		await conn.OpenAsync(ct).ConfigureAwait(false);
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "PRAGMA synchronous = NORMAL;";
		await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
		return conn;
	}

	/// <inheritdoc />
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized) return;

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_isInitialized) return;

			var dir = Path.GetDirectoryName(_databasePath);
			if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

			await conn.ExecuteAsync(new CommandDefinition("""
				PRAGMA journal_mode = WAL;
				PRAGMA synchronous = NORMAL;
				PRAGMA busy_timeout = 5000;
				PRAGMA foreign_keys = ON;

				CREATE TABLE IF NOT EXISTS decision_logs (
					Id TEXT PRIMARY KEY NOT NULL,
					Timestamp TEXT NOT NULL,
					LastModifiedUtc TEXT NOT NULL,
					Decision TEXT NOT NULL,
					Reason TEXT NOT NULL,
					CaffeineAmountMg REAL NOT NULL,
					ItemName TEXT NOT NULL,
					ItemCategory TEXT NOT NULL,
					IsComplianceOverride INTEGER NOT NULL,
					SyncStatus INTEGER NOT NULL
				);
				CREATE INDEX IF NOT EXISTS IX_decision_logs_Timestamp ON decision_logs(Timestamp);
				CREATE INDEX IF NOT EXISTS IX_decision_logs_SyncStatus ON decision_logs(SyncStatus);

				CREATE TABLE IF NOT EXISTS sync_metadata (
					Key TEXT PRIMARY KEY NOT NULL,
					Value TEXT NOT NULL,
					LastUpdatedUtc TEXT NOT NULL
				);
				""", cancellationToken: cancellationToken)).ConfigureAwait(false);

			_isInitialized = true;
			StartPeriodicCheckpointLoop();
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<string> GetJournalModeAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		var mode = await conn.ExecuteScalarAsync<string>(new CommandDefinition("PRAGMA journal_mode;", cancellationToken: cancellationToken)).ConfigureAwait(false);
		return mode ?? string.Empty;
	}

	/// <inheritdoc />
	public async Task<int> InsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			entity.Id = Guid.NewGuid().ToString();
		}
		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = """
				INSERT INTO decision_logs (Id, Timestamp, LastModifiedUtc, Decision, Reason, CaffeineAmountMg, ItemName, ItemCategory, IsComplianceOverride, SyncStatus)
				VALUES (@Id, @Timestamp, @LastModifiedUtc, @Decision, @Reason, @CaffeineAmountMg, @ItemName, @ItemCategory, @IsComplianceOverride, @SyncStatus);
				""";
			return await conn.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> UpdateLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = """
				UPDATE decision_logs SET
					Timestamp = @Timestamp,
					LastModifiedUtc = @LastModifiedUtc,
					Decision = @Decision,
					Reason = @Reason,
					CaffeineAmountMg = @CaffeineAmountMg,
					ItemName = @ItemName,
					ItemCategory = @ItemCategory,
					IsComplianceOverride = @IsComplianceOverride,
					SyncStatus = @SyncStatus
				WHERE Id = @Id;
				""";
			return await conn.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> UpsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			entity.Id = Guid.NewGuid().ToString();
		}
		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = """
				INSERT OR REPLACE INTO decision_logs (Id, Timestamp, LastModifiedUtc, Decision, Reason, CaffeineAmountMg, ItemName, ItemCategory, IsComplianceOverride, SyncStatus)
				VALUES (@Id, @Timestamp, @LastModifiedUtc, @Decision, @Reason, @CaffeineAmountMg, @ItemName, @ItemCategory, @IsComplianceOverride, @SyncStatus);
				""";
			return await conn.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> DeleteLogAsync(string id, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(id)) return 0;
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = "DELETE FROM decision_logs WHERE Id = @id;";
			return await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<DecisionLogEntity?> GetLogByIdAsync(string id, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(id)) return null;
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		const string sql = "SELECT * FROM decision_logs WHERE Id = @id;";
		return await conn.QuerySingleOrDefaultAsync<DecisionLogEntity>(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<List<DecisionLogEntity>> GetAllLogsAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		const string sql = "SELECT * FROM decision_logs ORDER BY Timestamp DESC;";
		var result = await conn.QueryAsync<DecisionLogEntity>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
		return result.AsList();
	}

	/// <inheritdoc />
	public async Task<List<DecisionLogEntity>> GetPendingSyncLogsAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		const string sql = "SELECT * FROM decision_logs WHERE SyncStatus = 0 ORDER BY Timestamp ASC;";
		var result = await conn.QueryAsync<DecisionLogEntity>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
		return result.AsList();
	}

	/// <inheritdoc />
	public async Task<int> MarkLogsSyncedAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(ids);
		var idList = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
		if (idList.Count == 0) return 0;

		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = "UPDATE decision_logs SET SyncStatus = 1 WHERE Id IN @idList;";
			return await conn.ExecuteAsync(new CommandDefinition(sql, new { idList }, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> MarkAllLogsSyncedAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = "UPDATE decision_logs SET SyncStatus = 1 WHERE SyncStatus = 0;";
			return await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> UpsertBatchAsync(IEnumerable<DecisionLogEntity> entities, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entities);
		var list = entities.ToList();
		if (list.Count == 0) return 0;

		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			await using var trans = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

			const string sql = """
				INSERT OR REPLACE INTO decision_logs (Id, Timestamp, LastModifiedUtc, Decision, Reason, CaffeineAmountMg, ItemName, ItemCategory, IsComplianceOverride, SyncStatus)
				VALUES (@Id, @Timestamp, @LastModifiedUtc, @Decision, @Reason, @CaffeineAmountMg, @ItemName, @ItemCategory, @IsComplianceOverride, @SyncStatus);
				""";

			foreach (var entity in list)
			{
				if (string.IsNullOrWhiteSpace(entity.Id))
				{
					entity.Id = Guid.NewGuid().ToString();
				}
				if (entity.LastModifiedUtc == default)
				{
					entity.LastModifiedUtc = DateTime.UtcNow;
				}
			}

			int count = await conn.ExecuteAsync(new CommandDefinition(sql, list, transaction: trans, cancellationToken: cancellationToken)).ConfigureAwait(false);
			await trans.CommitAsync(cancellationToken).ConfigureAwait(false);
			return count;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<int> ClearAllLogsAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = "DELETE FROM decision_logs;";
			return await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<string> GetMetadataAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(key)) return defaultValue;
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		const string sql = "SELECT Value FROM sync_metadata WHERE Key = @key;";
		var value = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { key }, cancellationToken: cancellationToken)).ConfigureAwait(false);
		return value ?? defaultValue;
	}

	/// <inheritdoc />
	public async Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		await InitializeAsync(cancellationToken).ConfigureAwait(false);

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			const string sql = """
				INSERT OR REPLACE INTO sync_metadata (Key, Value, LastUpdatedUtc)
				VALUES (@key, @value, @now);
				""";
			await conn.ExecuteAsync(new CommandDefinition(sql, new { key, value = value ?? string.Empty, now = DateTime.UtcNow }, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private void StartPeriodicCheckpointLoop()
	{
		if (_checkpointCts != null) return;
		_checkpointCts = new CancellationTokenSource();
		_checkpointLoopTask = RunPeriodicCheckpointLoopAsync(_checkpointCts.Token);
	}

	private async Task RunPeriodicCheckpointLoopAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				if (_disposed || cancellationToken.IsCancellationRequested) break;

				try
				{
					await CheckpointWalAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch
				{
					// Suppress background passive checkpoint errors
				}
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// Safe teardown
		}
	}

	/// <inheritdoc />
	public async Task CheckpointWalAsync(CancellationToken cancellationToken = default)
	{
		if (_disposed) return;

		try
		{
			await using var conn = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			await conn.ExecuteAsync(new CommandDefinition("PRAGMA wal_checkpoint(PASSIVE);", cancellationToken: cancellationToken)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			// Best-effort passive checkpoint
		}
	}

	/// <inheritdoc />
	public async Task CloseConnectionAsync()
	{
		if (_disposed) return;

		if (_checkpointCts != null)
		{
			_checkpointCts.Cancel();
		}

		if (_checkpointLoopTask != null)
		{
			try
			{
				await _checkpointLoopTask.ConfigureAwait(false);
			}
			catch
			{
			}
			_checkpointLoopTask = null;
		}

		_checkpointCts?.Dispose();
		_checkpointCts = null;

		await _writeLock.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_isInitialized)
			{
				try
				{
					await using var conn = await GetOpenConnectionAsync().ConfigureAwait(false);
					await conn.ExecuteAsync("PRAGMA wal_checkpoint(PASSIVE);").ConfigureAwait(false);
				}
				catch
				{
				}
			}

			SqliteConnection.ClearAllPools();
			_isInitialized = false;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;

		await CloseConnectionAsync().ConfigureAwait(false);
		_disposed = true;
		_writeLock.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (_checkpointCts != null)
		{
			_checkpointCts.Cancel();
			if (_checkpointLoopTask?.IsCompleted == true)
			{
				_checkpointCts.Dispose();
			}
			_checkpointCts = null;
		}

		try
		{
			SqliteConnection.ClearAllPools();
			_isInitialized = false;
		}
		catch
		{
		}

		_writeLock.Dispose();
		GC.SuppressFinalize(this);
	}

	private sealed class SqliteDateTimeHandler : SqlMapper.TypeHandler<DateTime>
	{
		public override void SetValue(IDbDataParameter parameter, DateTime value)
		{
			parameter.Value = value.ToString("o", CultureInfo.InvariantCulture);
		}

		public override DateTime Parse(object value)
		{
			if (value is DateTime dt)
			{
				return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
			}
			if (value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
			{
				return parsed.Kind == DateTimeKind.Unspecified
					? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
					: parsed.ToUniversalTime();
			}
			if (value is long ticks) return new DateTime(ticks, DateTimeKind.Utc);
			return Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToUniversalTime();
		}
	}
}
