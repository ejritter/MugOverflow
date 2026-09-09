using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Services.Sync;

/// <summary>
/// Thread-safe singleton synchronization service orchestrating two-way union hydration,
/// dirty record flushing, provider migrations, and offline queueing.
/// </summary>
public sealed class StorageSyncService : IStorageSyncService
{
	private readonly ISqliteDatabaseService _databaseService;
	private readonly IServiceProvider _serviceProvider;
	private readonly IConnectivity _connectivity;
	private readonly TimeSpan _periodicFlushInterval;
	private readonly SemaphoreSlim _syncLock = new(1, 1);

	private IStorageProvider _currentProvider;
	private SyncEngineStatus _status = SyncEngineStatus.Idle;
	private DateTime? _lastSuccessfulSyncUtc;
	private CancellationTokenSource? _timerCts;
	private Task? _periodicTimerTask;
	private bool _isInitialized;
	private bool _disposed;
	private readonly WeakEventManager _statusWeakEventManager = new();

	public SyncEngineStatus Status
	{
		get => _status;
		private set
		{
			if (_status != value)
			{
				_status = value;
				_statusWeakEventManager.HandleEvent(this, _status, nameof(StatusChanged));
			}
		}
	}

	public DateTime? LastSuccessfulSyncUtc => _lastSuccessfulSyncUtc;

	public IStorageProvider CurrentProvider => _currentProvider;

	public event EventHandler<SyncEngineStatus>? StatusChanged
	{
		add => _statusWeakEventManager.AddEventHandler(value);
		remove => _statusWeakEventManager.RemoveEventHandler(value);
	}

	/// <summary>
	/// Constructor for dependency injection and testing.
	/// </summary>
	public StorageSyncService(
		ISqliteDatabaseService databaseService,
		IServiceProvider serviceProvider,
		IConnectivity? connectivity = null,
		TimeSpan? periodicFlushInterval = null)
	{
		_databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_connectivity = connectivity ?? Connectivity.Current;
		_periodicFlushInterval = periodicFlushInterval ?? TimeSpan.FromMinutes(5);

		// Default fallback provider before initialization resolves metadata
		_currentProvider = ResolveProvider(StorageProviderType.LocalStorage);
	}

	/// <inheritdoc />
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_isInitialized)
		{
			return;
		}

		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_isInitialized)
			{
				return;
			}

			// 1. Initialize SQLite database tables & WAL mode
			await _databaseService.InitializeAsync(cancellationToken).ConfigureAwait(false);

			// 2. Restore active provider from sync_metadata table
			var savedProviderKey = await _databaseService.GetMetadataAsync(
				SyncMetadataKeys.ActiveProvider,
				StorageProviderType.LocalStorage.ToString(),
				cancellationToken).ConfigureAwait(false);

			if (Enum.TryParse<StorageProviderType>(savedProviderKey, out var providerType))
			{
				_currentProvider = ResolveProvider(providerType);
			}
			else
			{
				_currentProvider = ResolveProvider(StorageProviderType.LocalStorage);
			}

			// 3. Restore last successful sync timestamp
			var savedSyncUtc = await _databaseService.GetMetadataAsync(
				SyncMetadataKeys.LastSuccessfulSyncUtc,
				string.Empty,
				cancellationToken).ConfigureAwait(false);

			if (DateTime.TryParse(savedSyncUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
			{
				_lastSuccessfulSyncUtc = parsedDate;
			}

			// 4. Subscribe to connectivity change events
			_connectivity.ConnectivityChanged -= OnConnectivityChanged;
			_connectivity.ConnectivityChanged += OnConnectivityChanged;

			// 5. Initial status check
			if (!IsOnline() && _currentProvider.RequiresAuthentication)
			{
				Status = SyncEngineStatus.Offline;
			}
			else
			{
				Status = SyncEngineStatus.Idle;
			}

			// 6. Start background periodic flush timer
			_timerCts = new CancellationTokenSource();
			_periodicTimerTask = StartPeriodicFlushLoopAsync(_timerCts.Token);

			_isInitialized = true;
		}
		finally
		{
			_syncLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<bool> SynchronizeAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (!IsOnline() && _currentProvider.RequiresAuthentication)
		{
			Status = SyncEngineStatus.Offline;
			return false;
		}

		if (!IsOnline())
		{
			Status = SyncEngineStatus.Offline;
			return false;
		}

		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			Status = SyncEngineStatus.Syncing;

			// 1. Ensure provider is authenticated if required (silent only in background sync)
			if (_currentProvider.RequiresAuthentication)
			{
				var isAuth = await _currentProvider.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
				if (!isAuth)
				{
					Status = SyncEngineStatus.AuthRequired;
					return false;
				}
			}

			// 2. Read remote payload from active provider
			var remotePayload = await _currentProvider.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);

			// 3. Fetch all local SQLite logs
			var localLogs = await _databaseService.GetAllLogsAsync(cancellationToken).ConfigureAwait(false);

			// 4. Execute Two-Way Union Hydration & LWW Conflict Resolution
			var localMap = localLogs.ToDictionary(x => x.Id, x => x);
			var remoteLogs = (remotePayload?.DecisionLogs ?? [])
				.Where(r => !string.IsNullOrWhiteSpace(r.Id))
				.GroupBy(r => r.Id, StringComparer.Ordinal)
				.Select(g => g.OrderByDescending(x => x.LastModifiedUtc).First())
				.ToList();
			var remoteMap = remoteLogs.ToDictionary(x => x.Id, x => x);

			var localUpserts = new List<DecisionLogEntity>();
			var mergedUnion = new Dictionary<string, DecisionLogEntity>();
			bool remoteNeedsUpdate = false;

			// Check all remote records against local state
			foreach (var remoteEntry in remoteLogs)
			{
				if (localMap.TryGetValue(remoteEntry.Id, out var localEntry))
				{
					// Record exists in both local SQLite and remote storage: LWW resolution
					if (remoteEntry.LastModifiedUtc > localEntry.LastModifiedUtc)
					{
						// Remote is strictly newer -> overwrite local SQLite
						var updatedLocal = CloneEntityWithStatus(remoteEntry, SyncStatus.Synced);
						localUpserts.Add(updatedLocal);
						mergedUnion[remoteEntry.Id] = remoteEntry;
					}
					else if (localEntry.LastModifiedUtc > remoteEntry.LastModifiedUtc)
					{
						// Local is strictly newer -> keep local, update remote
						mergedUnion[localEntry.Id] = localEntry;
						remoteNeedsUpdate = true;
					}
					else
					{
						// Timestamps are equal -> converged state
						mergedUnion[localEntry.Id] = localEntry;
						if (localEntry.SyncStatus != (int)SyncStatus.Synced)
						{
							localEntry.SyncStatus = (int)SyncStatus.Synced;
							localUpserts.Add(localEntry);
						}
					}
				}
				else
				{
					// Remote-only record: Insert into local SQLite as Synced
					var newLocal = CloneEntityWithStatus(remoteEntry, SyncStatus.Synced);
					localUpserts.Add(newLocal);
					mergedUnion[remoteEntry.Id] = remoteEntry;
				}
			}

			// Check for local-only records
			foreach (var localEntry in localLogs)
			{
				if (!remoteMap.ContainsKey(localEntry.Id))
				{
					// Local-only record: Keep local and mark for remote export
					mergedUnion[localEntry.Id] = localEntry;
					remoteNeedsUpdate = true;
				}
			}

			// 5. Apply local SQLite batch upserts inside transaction
			if (localUpserts.Count > 0)
			{
				await _databaseService.UpsertBatchAsync(localUpserts, cancellationToken).ConfigureAwait(false);
			}

			// 6. Write back merged payload to cloud if remote was missing records or had older records
			if (remoteNeedsUpdate || remotePayload == null)
			{
				var newPayload = new StoragePayload
				{
					AppVersion = "1.0.0",
					SchemaVersion = "1.0.0",
					ExportedAtUtc = DateTime.UtcNow,
					DecisionLogs = mergedUnion.Values.OrderByDescending(x => x.Timestamp).ToList()
				};

				var writeOk = await _currentProvider.WritePayloadAsync(newPayload, cancellationToken).ConfigureAwait(false);
				if (!writeOk)
				{
					Status = SyncEngineStatus.Error;
					return false;
				}

				// Mark all local records as Synced after successful write
				await _databaseService.MarkAllLogsSyncedAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				// Mark all local records as Synced
				await _databaseService.MarkAllLogsSyncedAsync(cancellationToken).ConfigureAwait(false);
			}

			// 7. Update sync metadata timestamps
			var nowUtc = DateTime.UtcNow;
			_lastSuccessfulSyncUtc = nowUtc;
			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.LastSuccessfulSyncUtc,
				nowUtc.ToString("o"),
				cancellationToken).ConfigureAwait(false);
			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.LastHydrationUtc,
				nowUtc.ToString("o"),
				cancellationToken).ConfigureAwait(false);

			Status = SyncEngineStatus.Success;
			Status = SyncEngineStatus.Idle;
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			Status = SyncEngineStatus.Error;
			return false;
		}
		finally
		{
			_syncLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<bool> FlushPendingChangesAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (!IsOnline())
		{
			return false;
		}

		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			// 1. Check for pending dirty records
			var pendingLogs = await _databaseService.GetPendingSyncLogsAsync(cancellationToken).ConfigureAwait(false);
			if (pendingLogs.Count == 0)
			{
				return true;
			}

			Status = SyncEngineStatus.Syncing;

			// 2. Ensure provider authentication (silent only in background sync)
			if (_currentProvider.RequiresAuthentication)
			{
				var isAuth = await _currentProvider.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
				if (!isAuth)
				{
					Status = SyncEngineStatus.AuthRequired;
					return false;
				}
			}

			// 3. Read current remote payload
			var remotePayload = await _currentProvider.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
			var remoteLogs = (remotePayload?.DecisionLogs ?? [])
				.Where(r => !string.IsNullOrWhiteSpace(r.Id))
				.GroupBy(r => r.Id, StringComparer.Ordinal)
				.Select(g => g.OrderByDescending(x => x.LastModifiedUtc).First())
				.ToDictionary(x => x.Id, x => x);

			// 4. Merge pending logs into remote payload with LWW
			foreach (var pending in pendingLogs)
			{
				if (remoteLogs.TryGetValue(pending.Id, out var existingRemote))
				{
					if (pending.LastModifiedUtc >= existingRemote.LastModifiedUtc)
					{
						remoteLogs[pending.Id] = pending;
					}
				}
				else
				{
					remoteLogs[pending.Id] = pending;
				}
			}

			var updatedPayload = new StoragePayload
			{
				AppVersion = remotePayload?.AppVersion ?? "1.0.0",
				SchemaVersion = remotePayload?.SchemaVersion ?? "1.0.0",
				ExportedAtUtc = DateTime.UtcNow,
				DecisionLogs = remoteLogs.Values.OrderByDescending(x => x.Timestamp).ToList()
			};

			// 5. Write updated payload to active provider
			var writeOk = await _currentProvider.WritePayloadAsync(updatedPayload, cancellationToken).ConfigureAwait(false);
			if (!writeOk)
			{
				Status = SyncEngineStatus.Error;
				return false;
			}

			// 6. Atomically mark flushed logs as Synced in SQLite
			var flushedIds = pendingLogs.Select(x => x.Id);
			await _databaseService.MarkLogsSyncedAsync(flushedIds, cancellationToken).ConfigureAwait(false);

			// 7. Update sync metadata
			var nowUtc = DateTime.UtcNow;
			_lastSuccessfulSyncUtc = nowUtc;
			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.LastSuccessfulSyncUtc,
				nowUtc.ToString("o"),
				cancellationToken).ConfigureAwait(false);

			Status = SyncEngineStatus.Success;
			Status = SyncEngineStatus.Idle;
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			Status = SyncEngineStatus.Error;
			return false;
		}
		finally
		{
			_syncLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<bool> SwitchProviderAsync(
		StorageProviderType targetType,
		MigrationOption migrationOption,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			Status = SyncEngineStatus.Syncing;

			// 1. Resolve target provider instance
			var targetProvider = ResolveProvider(targetType);

			// 2. Validate connectivity for remote target providers
			if (targetType != StorageProviderType.LocalStorage && !IsOnline())
			{
				Status = SyncEngineStatus.Offline;
				return false;
			}

			// 3. Authenticate target provider if required
			if (targetProvider.RequiresAuthentication)
			{
				var isAuth = await targetProvider.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
				if (!isAuth)
				{
					var authOk = await targetProvider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
					if (!authOk)
					{
						Status = SyncEngineStatus.Error;
						return false;
					}
				}
			}

			// 4. Execute Migration based on option
			switch (migrationOption)
			{
				case MigrationOption.UploadLocalToCloud:
				{
					var localLogs = await _databaseService.GetAllLogsAsync(cancellationToken).ConfigureAwait(false);
					var payload = new StoragePayload
					{
						AppVersion = "1.0.0",
						SchemaVersion = "1.0.0",
						ExportedAtUtc = DateTime.UtcNow,
						DecisionLogs = localLogs
					};

					var writeOk = await targetProvider.WritePayloadAsync(payload, cancellationToken).ConfigureAwait(false);
					if (!writeOk)
					{
						Status = SyncEngineStatus.Error;
						return false;
					}

					await _databaseService.MarkAllLogsSyncedAsync(cancellationToken).ConfigureAwait(false);
					break;
				}

				case MigrationOption.DownloadCloudToLocal:
				{
					var remotePayload = await targetProvider.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
					await _databaseService.ClearAllLogsAsync(cancellationToken).ConfigureAwait(false);

					if (remotePayload != null && remotePayload.DecisionLogs.Count > 0)
					{
						var recordsToInsert = remotePayload.DecisionLogs
							.Select(x => CloneEntityWithStatus(x, SyncStatus.Synced))
							.ToList();

						await _databaseService.UpsertBatchAsync(recordsToInsert, cancellationToken).ConfigureAwait(false);
					}
					break;
				}

				case MigrationOption.TwoWayMergeBoth:
				{
					var remotePayload = await targetProvider.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
					var localLogs = await _databaseService.GetAllLogsAsync(cancellationToken).ConfigureAwait(false);

					var localMap = localLogs.ToDictionary(x => x.Id, x => x);
					var remoteLogs = (remotePayload?.DecisionLogs ?? [])
						.Where(r => !string.IsNullOrWhiteSpace(r.Id))
						.GroupBy(r => r.Id, StringComparer.Ordinal)
						.Select(g => g.OrderByDescending(x => x.LastModifiedUtc).First())
						.ToDictionary(x => x.Id, x => x);

					var localUpserts = new List<DecisionLogEntity>();
					var mergedUnion = new Dictionary<string, DecisionLogEntity>();

					foreach (var (remoteId, remoteEntry) in remoteLogs)
					{
						if (localMap.TryGetValue(remoteId, out var localEntry))
						{
							if (remoteEntry.LastModifiedUtc > localEntry.LastModifiedUtc)
							{
								var updated = CloneEntityWithStatus(remoteEntry, SyncStatus.Synced);
								localUpserts.Add(updated);
								mergedUnion[remoteId] = remoteEntry;
							}
							else
							{
								mergedUnion[remoteId] = localEntry;
								if (localEntry.SyncStatus != (int)SyncStatus.Synced)
								{
									localEntry.SyncStatus = (int)SyncStatus.Synced;
									localUpserts.Add(localEntry);
								}
							}
						}
						else
						{
							var newLocal = CloneEntityWithStatus(remoteEntry, SyncStatus.Synced);
							localUpserts.Add(newLocal);
							mergedUnion[remoteId] = remoteEntry;
						}
					}

					foreach (var localEntry in localLogs)
					{
						if (!remoteLogs.ContainsKey(localEntry.Id))
						{
							mergedUnion[localEntry.Id] = localEntry;
						}
					}

					if (localUpserts.Count > 0)
					{
						await _databaseService.UpsertBatchAsync(localUpserts, cancellationToken).ConfigureAwait(false);
					}

					var consolidatedPayload = new StoragePayload
					{
						AppVersion = "1.0.0",
						SchemaVersion = "1.0.0",
						ExportedAtUtc = DateTime.UtcNow,
						DecisionLogs = mergedUnion.Values.OrderByDescending(x => x.Timestamp).ToList()
					};

					var writeOk = await targetProvider.WritePayloadAsync(consolidatedPayload, cancellationToken).ConfigureAwait(false);
					if (!writeOk)
					{
						Status = SyncEngineStatus.Error;
						return false;
					}

					await _databaseService.MarkAllLogsSyncedAsync(cancellationToken).ConfigureAwait(false);
					break;
				}

				default:
					throw new ArgumentOutOfRangeException(nameof(migrationOption), migrationOption, "Unknown migration option.");
			}

			// 5. Update ActiveProvider and timestamps in sync_metadata table
			_currentProvider = targetProvider;
			var nowUtc = DateTime.UtcNow;
			_lastSuccessfulSyncUtc = nowUtc;

			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.ActiveProvider,
				targetType.ToString(),
				cancellationToken).ConfigureAwait(false);

			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.LastSuccessfulSyncUtc,
				nowUtc.ToString("o"),
				cancellationToken).ConfigureAwait(false);

			await _databaseService.SetMetadataAsync(
				SyncMetadataKeys.LastHydrationUtc,
				nowUtc.ToString("o"),
				cancellationToken).ConfigureAwait(false);

			Status = SyncEngineStatus.Success;
			Status = SyncEngineStatus.Idle;
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			Status = SyncEngineStatus.Error;
			return false;
		}
		finally
		{
			_syncLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task HandleAppSleepAsync(CancellationToken cancellationToken = default)
	{
		if (_disposed) return;

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(TimeSpan.FromSeconds(5));

		try
		{
			await FlushPendingChangesAsync(cts.Token).ConfigureAwait(false);
		}
		catch
		{
			// Safe fall-through during app sleep
		}
	}

	/// <inheritdoc />
	public async Task HandleAppDestroyAsync(CancellationToken cancellationToken = default)
	{
		if (_disposed) return;

		_timerCts?.Cancel();

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(TimeSpan.FromSeconds(5));

		try
		{
			await FlushPendingChangesAsync(cts.Token).ConfigureAwait(false);
		}
		catch
		{
			// Safe fall-through during app teardown
		}
	}

	private async Task StartPeriodicFlushLoopAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_periodicFlushInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				if (_disposed || cancellationToken.IsCancellationRequested) break;

				if (IsOnline())
				{
					try
					{
						await FlushPendingChangesAsync(cancellationToken).ConfigureAwait(false);
					}
					catch
					{
						// Catch unhandled errors so periodic timer loop never terminates unexpectedly
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Normal loop cancellation on shutdown
		}
	}

	private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		bool isNowOnline = e.NetworkAccess == NetworkAccess.Internet;

		if (isNowOnline)
		{
			if (Status == SyncEngineStatus.Offline)
			{
				Status = SyncEngineStatus.Idle;
			}

			// Automatically trigger flush of pending offline mutations
			_ = Task.Run(async () =>
			{
				try
				{
					await FlushPendingChangesAsync(CancellationToken.None).ConfigureAwait(false);
				}
				catch
				{
					// Safe background completion
				}
			});
		}
		else
		{
			if (_currentProvider.RequiresAuthentication)
			{
				Status = SyncEngineStatus.Offline;
			}
			else
			{
				Status = SyncEngineStatus.Offline;
			}
		}
	}

	private IStorageProvider ResolveProvider(StorageProviderType type)
	{
		var provider = _serviceProvider.GetKeyedService<IStorageProvider>(type);
		if (provider != null)
		{
			return provider;
		}

		return type switch
		{
			StorageProviderType.LocalStorage => _serviceProvider.GetRequiredService<LocalStorageProvider>(),
			StorageProviderType.GoogleDrive => _serviceProvider.GetRequiredService<GoogleDriveStorageProvider>(),
			StorageProviderType.OneDrive => _serviceProvider.GetRequiredService<OneDriveStorageProvider>(),
			_ => throw new InvalidOperationException($"Unsupported storage provider type: {type}")
		};
	}

	private bool IsOnline()
	{
		return _connectivity.NetworkAccess == NetworkAccess.Internet;
	}

	private static DecisionLogEntity CloneEntityWithStatus(DecisionLogEntity source, SyncStatus status)
	{
		return new DecisionLogEntity
		{
			Id = source.Id,
			Timestamp = source.Timestamp,
			LastModifiedUtc = source.LastModifiedUtc,
			Decision = source.Decision,
			Reason = source.Reason,
			CaffeineAmountMg = source.CaffeineAmountMg,
			ItemName = source.ItemName,
			ItemCategory = source.ItemCategory,
			IsComplianceOverride = source.IsComplianceOverride,
			SyncStatus = (int)status
		};
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_connectivity.ConnectivityChanged -= OnConnectivityChanged;
		_timerCts?.Cancel();
		_timerCts?.Dispose();
		_syncLock.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;

		_connectivity.ConnectivityChanged -= OnConnectivityChanged;
		if (_timerCts != null)
		{
			_timerCts.Cancel();
			if (_periodicTimerTask != null)
			{
				try
				{
					await _periodicTimerTask.ConfigureAwait(false);
				}
				catch
				{
					// Ignore loop cancellation
				}
			}
			_timerCts.Dispose();
		}

		_syncLock.Dispose();
	}
}
