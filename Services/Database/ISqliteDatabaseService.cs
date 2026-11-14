using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Database;

/// <summary>
/// Defines the asynchronous SQLite database service operations for local persistence,
/// batch transactional upserts, dirty-state synchronization tracking, and metadata management.
/// </summary>
public interface ISqliteDatabaseService : IDisposable
{
	/// <summary>
	/// Asynchronously initializes the SQLite connection, enables WAL mode, and verifies table schemas.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Inserts a new decision log entity. Sets timestamps if not already populated.
	/// </summary>
	Task<int> InsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates an existing decision log entity and updates its LastModifiedUtc timestamp.
	/// </summary>
	Task<int> UpdateLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default);

	/// <summary>
	/// Inserts or replaces a single decision log entity.
	/// </summary>
	Task<int> UpsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a decision log entity by its unique ID.
	/// </summary>
	Task<int> DeleteLogAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves a single decision log by its unique ID, or null if not found.
	/// </summary>
	Task<DecisionLogEntity?> GetLogByIdAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves all decision logs ordered by timestamp descending.
	/// </summary>
	Task<List<DecisionLogEntity>> GetAllLogsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves all decision logs that have not yet been synchronized to cloud storage (SyncStatus == 0).
	/// </summary>
	Task<List<DecisionLogEntity>> GetPendingSyncLogsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically marks a batch of log IDs as synchronized (SyncStatus = 1) inside a transaction.
	/// </summary>
	Task<int> MarkLogsSyncedAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically marks all pending logs as synchronized (SyncStatus = 1).
	/// </summary>
	Task<int> MarkAllLogsSyncedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically upserts (inserts or replaces) a batch of decision logs within a single transaction.
	/// </summary>
	Task<int> UpsertBatchAsync(IEnumerable<DecisionLogEntity> entities, CancellationToken cancellationToken = default);

	/// <summary>
	/// Clears all records from the decision_logs table without removing metadata.
	/// </summary>
	Task<int> ClearAllLogsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves a metadata value by key, returning the default value if key does not exist.
	/// </summary>
	Task<string> GetMetadataAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default);

	/// <summary>
	/// Persists or updates a metadata key-value pair.
	/// </summary>
	Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves the current SQLite journal mode (e.g. "wal").
	/// </summary>
	Task<string> GetJournalModeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Performs a passive WAL checkpoint (PRAGMA wal_checkpoint(PASSIVE)) to flush committed frames
	/// to the main database file without blocking readers or writers.
	/// </summary>
	Task CheckpointWalAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Closes the database connection cleanly to allow file operations (migration, backup, deletion).
	/// </summary>
	Task CloseConnectionAsync();
}
