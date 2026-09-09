using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Services.Sync;

/// <summary>
/// Represents the operational state of the sync orchestrator engine.
/// </summary>
public enum SyncEngineStatus
{
	/// <summary>Sync engine is idle and ready for operations.</summary>
	Idle = 0,

	/// <summary>Sync engine is actively synchronizing, flushing, or migrating data.</summary>
	Syncing = 1,

	/// <summary>Device is offline and network-dependent operations are paused.</summary>
	Offline = 2,

	/// <summary>The last sync operation encountered an unrecoverable error.</summary>
	Error = 3,

	/// <summary>The last sync operation completed successfully.</summary>
	Success = 4,

	/// <summary>
	/// Authentication credentials have expired or are missing. Interactive user login required.
	/// Background operations requiring authentication are paused non-modally.
	/// </summary>
	AuthRequired = 5
}

/// <summary>
/// Specifies the data migration strategy when switching the active storage provider.
/// </summary>
public enum MigrationOption
{
	/// <summary>
	/// Exports all local SQLite records to the target provider, overwriting remote storage.
	/// </summary>
	UploadLocalToCloud = 0,

	/// <summary>
	/// Downloads and replaces all local SQLite records with the target provider's payload.
	/// </summary>
	DownloadCloudToLocal = 1,

	/// <summary>
	/// Performs a two-way union merge with Last-Write-Wins (LWW) resolution between local SQLite and target provider.
	/// </summary>
	TwoWayMergeBoth = 2
}

/// <summary>
/// Contract for the synchronization orchestrator managing cold-start hydration,
/// periodic flushing, offline queueing, and provider migrations.
/// </summary>
public interface IStorageSyncService : IAsyncDisposable, IDisposable
{
	/// <summary>
	/// Gets the current operational status of the sync engine.
	/// </summary>
	SyncEngineStatus Status { get; }

	/// <summary>
	/// Gets the UTC timestamp of the most recent successful sync or hydration operation.
	/// </summary>
	DateTime? LastSuccessfulSyncUtc { get; }

	/// <summary>
	/// Gets the currently active storage provider.
	/// </summary>
	IStorageProvider CurrentProvider { get; }

	/// <summary>
	/// Occurs when the sync engine status changes (e.g. Syncing, Idle, Offline, Error, Success).
	/// </summary>
	event EventHandler<SyncEngineStatus>? StatusChanged;

	/// <summary>
	/// Initializes the sync orchestrator, verifies database schema, restores active provider from metadata,
	/// starts the periodic flush timer, and executes initial hydration if online.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Executes a full two-way union hydration between local SQLite and the active storage provider
	/// with Last-Write-Wins (LWW) conflict resolution.
	/// </summary>
	Task<bool> SynchronizeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Pushes any pending dirty records (SyncStatus == 0) from local SQLite to the active storage provider.
	/// </summary>
	Task<bool> FlushPendingChangesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Switches the active storage provider to the specified target type and executes data migration.
	/// Persists the new active provider in the sync_metadata table.
	/// </summary>
	Task<bool> SwitchProviderAsync(StorageProviderType targetType, MigrationOption migrationOption, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lifecycle hook called during application suspension (App.OnSleep). Performs immediate flush of dirty records.
	/// </summary>
	Task HandleAppSleepAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lifecycle hook called during application or window destruction. Flushes dirty records and cleans up resources.
	/// </summary>
	Task HandleAppDestroyAsync(CancellationToken cancellationToken = default);
}
