using SQLite;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Database;

/// <summary>
/// Thread-safe singleton SQLite database service managing local persistence for MugOverflow.
/// Features Write-Ahead Logging (WAL), transactional batch operations, and clean resource management.
/// </summary>
public sealed class SqliteDatabaseService : ISqliteDatabaseService, IAsyncDisposable
{
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private readonly string _databasePath;
	private readonly SQLiteOpenFlags _flags;
	private SQLiteAsyncConnection? _connection;
	private bool _isInitialized;

	/// <summary>
	/// Default constructor utilizing standard MAUI app data directory.
	/// </summary>
	public SqliteDatabaseService()
		: this(Path.Combine(FileSystem.AppDataDirectory, "MugOverflow.db3"))
	{
	}

	/// <summary>
	/// Constructor accepting custom database path (for isolated testing or custom locations).
	/// </summary>
	public SqliteDatabaseService(string databasePath)
	{
		_databasePath = string.IsNullOrWhiteSpace(databasePath)
			? Path.Combine(FileSystem.AppDataDirectory, "MugOverflow.db3")
			: databasePath;

		_flags = SQLiteOpenFlags.ReadWrite |
		         SQLiteOpenFlags.Create;
	}

	/// <inheritdoc />
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized && _connection is not null)
		{
			return;
		}

		await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_isInitialized && _connection is not null)
			{
				return;
			}

			// Ensure native SQLite provider batteries are initialized
			SQLitePCL.Batteries_V2.Init();

			// Ensure target directory exists
			var directory = Path.GetDirectoryName(_databasePath);
			if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			_connection = new SQLiteAsyncConnection(_databasePath, _flags);

			// Enable Write-Ahead Logging (WAL) for concurrency & non-blocking reads
			await _connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;").ConfigureAwait(false);

			// Create required tables asynchronously
			await _connection.CreateTableAsync<DecisionLogEntity>().ConfigureAwait(false);
			await _connection.CreateTableAsync<SyncMetadataEntity>().ConfigureAwait(false);

			_isInitialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}

	private async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
	{
		if (!_isInitialized || _connection is null)
		{
			await InitializeAsync(cancellationToken).ConfigureAwait(false);
		}

		return _connection!;
	}

	/// <inheritdoc />
	public async Task<string> GetJournalModeAsync(CancellationToken cancellationToken = default)
	{
		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode;").ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> InsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			entity.Id = Guid.NewGuid().ToString();
		}

		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.InsertAsync(entity).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> UpdateLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.UpdateAsync(entity).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> UpsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			entity.Id = Guid.NewGuid().ToString();
		}

		if (entity.LastModifiedUtc == default)
		{
			entity.LastModifiedUtc = DateTime.UtcNow;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.InsertOrReplaceAsync(entity).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> DeleteLogAsync(string id, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return 0;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.DeleteAsync<DecisionLogEntity>(id).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<DecisionLogEntity?> GetLogByIdAsync(string id, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.FindAsync<DecisionLogEntity>(id).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<List<DecisionLogEntity>> GetAllLogsAsync(CancellationToken cancellationToken = default)
	{
		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.Table<DecisionLogEntity>()
		                 .OrderByDescending(x => x.Timestamp)
		                 .ToListAsync()
		                 .ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<List<DecisionLogEntity>> GetPendingSyncLogsAsync(CancellationToken cancellationToken = default)
	{
		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.Table<DecisionLogEntity>()
		                 .Where(x => x.SyncStatus == 0)
		                 .OrderBy(x => x.Timestamp)
		                 .ToListAsync()
		                 .ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> MarkLogsSyncedAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(ids);

		var idList = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
		if (idList.Count == 0)
		{
			return 0;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		var updatedCount = 0;

		await conn.RunInTransactionAsync(db =>
		{
			var mapping = db.GetMapping<DecisionLogEntity>();
			var sql = $"UPDATE [{mapping.TableName}] SET SyncStatus = 1 WHERE Id = ?";
			foreach (var id in idList)
			{
				updatedCount += db.Execute(sql, id);
			}
		}).ConfigureAwait(false);

		return updatedCount;
	}

	/// <inheritdoc />
	public async Task<int> MarkAllLogsSyncedAsync(CancellationToken cancellationToken = default)
	{
		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.ExecuteAsync("UPDATE [decision_logs] SET SyncStatus = 1 WHERE SyncStatus = 0;").ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<int> UpsertBatchAsync(IEnumerable<DecisionLogEntity> entities, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entities);

		var list = entities.ToList();
		if (list.Count == 0)
		{
			return 0;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		var count = 0;

		await conn.RunInTransactionAsync(db =>
		{
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

				db.InsertOrReplace(entity);
				count++;
			}
		}).ConfigureAwait(false);

		return count;
	}

	/// <inheritdoc />
	public async Task<int> ClearAllLogsAsync(CancellationToken cancellationToken = default)
	{
		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		return await conn.DeleteAllAsync<DecisionLogEntity>().ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<string> GetMetadataAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return defaultValue;
		}

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		var meta = await conn.FindAsync<SyncMetadataEntity>(key).ConfigureAwait(false);
		return meta?.Value ?? defaultValue;
	}

	/// <inheritdoc />
	public async Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
		var entity = new SyncMetadataEntity
		{
			Key = key,
			Value = value ?? string.Empty,
			LastUpdatedUtc = DateTime.UtcNow
		};

		await conn.InsertOrReplaceAsync(entity).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task CloseConnectionAsync()
	{
		await _initLock.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_connection is not null)
			{
				await _connection.CloseAsync().ConfigureAwait(false);
				_connection = null;
			}
			_isInitialized = false;
		}
		finally
		{
			_initLock.Release();
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		await CloseConnectionAsync().ConfigureAwait(false);
		_initLock.Dispose();
	}
}
