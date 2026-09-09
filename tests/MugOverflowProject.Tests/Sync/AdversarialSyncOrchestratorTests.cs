using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Sync;

/// <summary>
/// Adversarial stress test harness challenging the Milestone 3 Synchronization Engine:
/// - 3-Way Provider Switching Migrations (UploadLocalToCloud, DownloadCloudToLocal, TwoWayMergeBoth)
///   across empty, populated, disjoint, and conflicting datasets.
/// - Lifecycle sleep/destroy flush under bounded time limits and slow/hung providers.
/// - High-concurrency synchronization, token cancellation recovery, and connectivity storm resilience.
/// </summary>
public sealed class AdversarialSyncOrchestratorTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeConnectivity _fakeConnectivity = new();
	private readonly FakeStorageProvider _fakeLocalStorage = new()
	{
		ProviderType = StorageProviderType.LocalStorage,
		DisplayName = "Fake Local"
	};
	private readonly FakeStorageProvider _fakeGoogleDrive = new()
	{
		ProviderType = StorageProviderType.GoogleDrive,
		DisplayName = "Fake Google Drive",
		RequiresAuthentication = true
	};
	private readonly FakeStorageProvider _fakeOneDrive = new()
	{
		ProviderType = StorageProviderType.OneDrive,
		DisplayName = "Fake OneDrive",
		RequiresAuthentication = true
	};

	private IServiceProvider _serviceProvider = null!;

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();

		var services = new ServiceCollection();
		services.AddSingleton<ISqliteDatabaseService>(_dbFixture.DatabaseService);
		services.AddSingleton<IConnectivity>(_fakeConnectivity);

		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, _fakeLocalStorage);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, _fakeGoogleDrive);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.OneDrive, _fakeOneDrive);

		_serviceProvider = services.BuildServiceProvider();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	private StorageSyncService CreateService(TimeSpan? flushInterval = null)
	{
		return new StorageSyncService(
			_dbFixture.DatabaseService,
			_serviceProvider,
			_fakeConnectivity,
			flushInterval ?? TimeSpan.FromMinutes(5));
	}

	#region 1. Provider Switching Migrations: UploadLocalToCloud

	[Fact]
	public async Task Migration_UploadLocalToCloud_EmptyLocalDataset_UploadsEmptyPayloadAndUpdatesMetadata()
	{
		// Arrange: Target OneDrive currently has old payload
		var oldCloudItem = SqliteTestDatabaseFixture.CreateSampleEntity(id: "old-cloud-item");
		_fakeOneDrive.StoredPayload = new StoragePayload { DecisionLogs = [oldCloudItem] };

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act: Upload empty local SQLite to cloud
		var success = await service.SwitchProviderAsync(StorageProviderType.OneDrive, MigrationOption.UploadLocalToCloud);

		// Assert
		Assert.True(success);
		Assert.Equal(StorageProviderType.OneDrive, service.CurrentProvider.ProviderType);
		Assert.NotNull(_fakeOneDrive.StoredPayload);
		Assert.Empty(_fakeOneDrive.StoredPayload.DecisionLogs);

		// SQLite remains empty
		var localLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Empty(localLogs);

		// Metadata updated
		var activeProvider = await _dbFixture.DatabaseService.GetMetadataAsync(SyncMetadataKeys.ActiveProvider);
		Assert.Equal(StorageProviderType.OneDrive.ToString(), activeProvider);
	}

	[Fact]
	public async Task Migration_UploadLocalToCloud_PopulatedLocal_OverwritesCloudAndMarksAllSynced()
	{
		// Arrange: 100 local records with mixed pending/synced states and special characters
		var localRecords = new List<DecisionLogEntity>();
		for (int i = 0; i < 100; i++)
		{
			localRecords.Add(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"local-mig-upload-{i}",
				itemName: $"Coffee #{i} ☕ 특수문자",
				syncStatus: i % 2 == 0 ? (int)SyncStatus.Pending : (int)SyncStatus.Synced));
		}
		await _dbFixture.DatabaseService.UpsertBatchAsync(localRecords);

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act: Switch to Google Drive with UploadLocalToCloud
		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		// Assert
		Assert.True(success);
		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);
		Assert.NotNull(_fakeGoogleDrive.StoredPayload);
		Assert.Equal(100, _fakeGoogleDrive.StoredPayload.DecisionLogs.Count);

		// All 100 records in SQLite must now be marked Synced
		var allInDb = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(100, allInDb.Count);
		Assert.All(allInDb, log => Assert.Equal((int)SyncStatus.Synced, log.SyncStatus));

		var pendingInDb = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pendingInDb);
	}

	[Fact]
	public async Task Migration_UploadLocalToCloud_WhenTargetWriteFails_AbortsAndPreservesPendingState()
	{
		var localRecord = SqliteTestDatabaseFixture.CreateSampleEntity(id: "keep-pending-1", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(localRecord);

		_fakeGoogleDrive.ShouldFailWrite = true;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		Assert.False(success);
		Assert.Equal(SyncEngineStatus.Error, service.Status);

		// Record remains pending in SQLite
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Single(pending);
		Assert.Equal("keep-pending-1", pending[0].Id);
	}

	#endregion

	#region 2. Provider Switching Migrations: DownloadCloudToLocal

	[Fact]
	public async Task Migration_DownloadCloudToLocal_EmptyCloudPayload_ClearsLocalData()
	{
		// Arrange: Local has 5 existing records
		for (int i = 0; i < 5; i++)
		{
			await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(id: $"old-loc-{i}"));
		}

		_fakeOneDrive.StoredPayload = new StoragePayload { DecisionLogs = [] }; // Empty cloud

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act
		var success = await service.SwitchProviderAsync(StorageProviderType.OneDrive, MigrationOption.DownloadCloudToLocal);

		// Assert: Local SQLite was wiped clean
		Assert.True(success);
		Assert.Equal(StorageProviderType.OneDrive, service.CurrentProvider.ProviderType);
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Empty(allLogs);
	}

	[Fact]
	public async Task Migration_DownloadCloudToLocal_PopulatedCloud_CompletelyReplacesLocalWithSyncedRecords()
	{
		// Arrange: Local has old records
		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(id: "local-to-be-deleted-1"));
		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(id: "local-to-be-deleted-2"));

		// Target Google Drive has 150 cloud records
		var cloudRecords = new List<DecisionLogEntity>();
		for (int i = 0; i < 150; i++)
		{
			cloudRecords.Add(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"cloud-item-{i}",
				itemName: $"Cloud Roaster {i}",
				syncStatus: (int)SyncStatus.Pending)); // Even if remote had pending flag, downloading sets it to Synced
		}
		_fakeGoogleDrive.StoredPayload = new StoragePayload { DecisionLogs = cloudRecords };

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act
		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.DownloadCloudToLocal);

		// Assert
		Assert.True(success);
		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);

		var allDbLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(150, allDbLogs.Count);
		Assert.DoesNotContain(allDbLogs, x => x.Id == "local-to-be-deleted-1");
		Assert.DoesNotContain(allDbLogs, x => x.Id == "local-to-be-deleted-2");
		Assert.All(allDbLogs, log => Assert.Equal((int)SyncStatus.Synced, log.SyncStatus));
	}

	[Fact]
	public async Task Migration_DownloadCloudToLocal_WhenCloudPayloadNull_ClearsLocalAndSucceeds()
	{
		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(id: "local-item-1"));

		_fakeGoogleDrive.StoredPayload = null; // No file in cloud yet

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.DownloadCloudToLocal);

		Assert.True(success);
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Empty(allLogs);
	}

	#endregion

	#region 3. Provider Switching Migrations: TwoWayMergeBoth

	[Fact]
	public async Task Migration_TwoWayMergeBoth_BothEmpty_SucceedsWithoutChanges()
	{
		_fakeOneDrive.StoredPayload = null;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.OneDrive, MigrationOption.TwoWayMergeBoth);

		Assert.True(success);
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Empty(allLogs);
		Assert.NotNull(_fakeOneDrive.StoredPayload);
		Assert.Empty(_fakeOneDrive.StoredPayload.DecisionLogs);
	}

	[Fact]
	public async Task Migration_TwoWayMergeBoth_DisjointPopulatedDatasets_MergesFullUnion()
	{
		var t0 = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Local has 50 records (L-0 .. L-49)
		for (int i = 0; i < 50; i++)
		{
			await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"L-{i}",
				timestamp: t0,
				lastModifiedUtc: t0,
				itemName: $"Local Brew {i}"));
		}

		// Remote has 50 distinct records (R-0 .. R-49)
		var remoteLogs = new List<DecisionLogEntity>();
		for (int i = 0; i < 50; i++)
		{
			remoteLogs.Add(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"R-{i}",
				timestamp: t0,
				lastModifiedUtc: t0,
				itemName: $"Remote Brew {i}"));
		}
		_fakeGoogleDrive.StoredPayload = new StoragePayload { DecisionLogs = remoteLogs };

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act
		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);

		// Assert: Union of 100 records in SQLite
		Assert.True(success);
		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(100, allLocal.Count);
		Assert.All(allLocal, log => Assert.Equal((int)SyncStatus.Synced, log.SyncStatus));

		// Assert: Target Google Drive also has all 100 records
		Assert.NotNull(_fakeGoogleDrive.StoredPayload);
		Assert.Equal(100, _fakeGoogleDrive.StoredPayload.DecisionLogs.Count);
	}

	[Fact]
	public async Task Migration_TwoWayMergeBoth_ComplexConflictingDatasets_AppliesLWWCorrectly()
	{
		var tBase = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var tOlder = tBase.AddMinutes(-30);
		var tNewer = tBase.AddMinutes(30);

		// 1. Conflict 1: Local is newer -> Local wins
		var local1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-1", timestamp: tBase, lastModifiedUtc: tNewer, itemName: "Local Winning Version");
		// 2. Conflict 2: Remote is newer -> Remote wins
		var local2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-2", timestamp: tBase, lastModifiedUtc: tOlder, itemName: "Local Losing Version");
		// 3. Conflict 3: Equal timestamps -> Retains data, marks synced
		var local3 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-3", timestamp: tBase, lastModifiedUtc: tBase, itemName: "Equal Timestamp Version", syncStatus: (int)SyncStatus.Pending);
		// 4. Local-only record
		var localOnly = SqliteTestDatabaseFixture.CreateSampleEntity(id: "loc-only", timestamp: tBase, lastModifiedUtc: tBase, itemName: "Local Only Record");

		await _dbFixture.DatabaseService.InsertLogAsync(local1);
		await _dbFixture.DatabaseService.InsertLogAsync(local2);
		await _dbFixture.DatabaseService.InsertLogAsync(local3);
		await _dbFixture.DatabaseService.InsertLogAsync(localOnly);

		// Remote dataset
		var remote1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-1", timestamp: tBase, lastModifiedUtc: tOlder, itemName: "Remote Losing Version");
		var remote2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-2", timestamp: tBase, lastModifiedUtc: tNewer, itemName: "Remote Winning Version");
		var remote3 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "conf-3", timestamp: tBase, lastModifiedUtc: tBase, itemName: "Equal Timestamp Version");
		var remoteOnly = SqliteTestDatabaseFixture.CreateSampleEntity(id: "rem-only", timestamp: tBase, lastModifiedUtc: tBase, itemName: "Remote Only Record");

		_fakeOneDrive.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote1, remote2, remote3, remoteOnly]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act
		var success = await service.SwitchProviderAsync(StorageProviderType.OneDrive, MigrationOption.TwoWayMergeBoth);

		// Assert
		Assert.True(success);
		var allDb = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(5, allDb.Count);

		var db1 = allDb.First(x => x.Id == "conf-1");
		Assert.Equal("Local Winning Version", db1.ItemName);

		var db2 = allDb.First(x => x.Id == "conf-2");
		Assert.Equal("Remote Winning Version", db2.ItemName);

		var db3 = allDb.First(x => x.Id == "conf-3");
		Assert.Equal("Equal Timestamp Version", db3.ItemName);
		Assert.Equal((int)SyncStatus.Synced, db3.SyncStatus);

		Assert.Contains(allDb, x => x.Id == "loc-only");
		Assert.Contains(allDb, x => x.Id == "rem-only");

		// Target cloud payload matches SQLite exactly
		Assert.NotNull(_fakeOneDrive.StoredPayload);
		Assert.Equal(5, _fakeOneDrive.StoredPayload.DecisionLogs.Count);
		var cloud1 = _fakeOneDrive.StoredPayload.DecisionLogs.First(x => x.Id == "conf-1");
		Assert.Equal("Local Winning Version", cloud1.ItemName);
		var cloud2 = _fakeOneDrive.StoredPayload.DecisionLogs.First(x => x.Id == "conf-2");
		Assert.Equal("Remote Winning Version", cloud2.ItemName);
	}

	[Fact]
	public async Task Migration_TwoWayMergeBoth_HandlesMalformedRemoteEntries_WithoutThrowing()
	{
		var t0 = DateTime.UtcNow;
		var local = SqliteTestDatabaseFixture.CreateSampleEntity(id: "valid-local", timestamp: t0, lastModifiedUtc: t0);
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		// Remote has empty string ID, whitespace ID, and duplicate ID with different timestamps
		var emptyId1 = new DecisionLogEntity { Id = "", ItemName = "Empty ID", Timestamp = t0, LastModifiedUtc = t0 };
		var emptyId2 = new DecisionLogEntity { Id = "   ", ItemName = "Whitespace ID", Timestamp = t0, LastModifiedUtc = t0 };
		var dupOld = SqliteTestDatabaseFixture.CreateSampleEntity(id: "dup-id", timestamp: t0, lastModifiedUtc: t0.AddMinutes(-5), itemName: "Dup Older");
		var dupNew = SqliteTestDatabaseFixture.CreateSampleEntity(id: "dup-id", timestamp: t0, lastModifiedUtc: t0.AddMinutes(5), itemName: "Dup Newer");

		_fakeGoogleDrive.StoredPayload = new StoragePayload
		{
			DecisionLogs = [emptyId1, emptyId2, dupOld, dupNew]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);

		Assert.True(success);
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2, allLogs.Count);
		Assert.Contains(allLogs, x => x.Id == "valid-local");
		Assert.Contains(allLogs, x => x.Id == "dup-id" && x.ItemName == "Dup Newer");
		Assert.DoesNotContain(allLogs, x => string.IsNullOrWhiteSpace(x.Id));
	}

	#endregion

	#region 4. Lifecycle Sleep / Destroy Flush under Bounded Time Limits

	[Fact]
	public async Task Lifecycle_HandleAppSleepAsync_BoundedFlush_CompletesWithinTimeBudget()
	{
		// Arrange: 10 pending dirty records
		for (int i = 0; i < 10; i++)
		{
			await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"sleep-stress-{i}",
				syncStatus: (int)SyncStatus.Pending));
		}

		await using var service = CreateService();
		await service.InitializeAsync();

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		await service.HandleAppSleepAsync();
		stopwatch.Stop();

		// Assert: Bounded execution (< 2 seconds under normal conditions)
		Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"App sleep took too long: {stopwatch.ElapsedMilliseconds}ms");

		// All flushed and marked Synced
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
		Assert.Equal(1, _fakeLocalStorage.WritePayloadCallCount);
	}

	[Fact]
	public async Task Lifecycle_HandleAppSleepAsync_WhenProviderHangs_AbortsCleanlyWithin5SecondsWithoutCrashing()
	{
		// Arrange: Provider simulates a 10-second hang (e.g. unresponsive cloud endpoint)
		_fakeLocalStorage.SimulatedDelayMs = 10000;

		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "sleep-hung-log",
			syncStatus: (int)SyncStatus.Pending));

		await using var service = CreateService();
		await service.InitializeAsync();

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		// Act: HandleAppSleepAsync has internal 5s timeout
		var sleepTask = service.HandleAppSleepAsync();
		await sleepTask;
		stopwatch.Stop();

		// Assert: Elapsed time should be bounded around 5000ms (+/- reasonable scheduling tolerance), NOT 10000ms
		Assert.True(stopwatch.ElapsedMilliseconds < 7500, $"App sleep did not enforce timeout: {stopwatch.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Lifecycle_HandleAppDestroyAsync_CancelsPeriodicTimerAndFlushesCleanly()
	{
		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "destroy-log-1",
			syncStatus: (int)SyncStatus.Pending));

		await using var service = CreateService(flushInterval: TimeSpan.FromMilliseconds(50));
		await service.InitializeAsync();

		// Act: Destroy lifecycle
		await service.HandleAppDestroyAsync();

		// Assert: Records flushed
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);

		// Subsequent periodic ticks should not throw or cause race conditions
		await Task.Delay(150);
	}

	[Fact]
	public async Task Lifecycle_HandleAppDestroyAsync_WhenProviderHangs_AbortsCleanlyWithinTimeout()
	{
		_fakeLocalStorage.SimulatedDelayMs = 10000;

		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "destroy-hung-log",
			syncStatus: (int)SyncStatus.Pending));

		await using var service = CreateService();
		await service.InitializeAsync();

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		await service.HandleAppDestroyAsync();
		stopwatch.Stop();

		Assert.True(stopwatch.ElapsedMilliseconds < 7500, $"App destroy did not enforce timeout: {stopwatch.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Lifecycle_ConcurrentSleepAndDestroyCalls_ExecuteSafelyWithoutDeadlock()
	{
		for (int i = 0; i < 20; i++)
		{
			await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"concurrent-life-{i}",
				syncStatus: (int)SyncStatus.Pending));
		}

		await using var service = CreateService();
		await service.InitializeAsync();

		// Launch concurrent lifecycle tasks
		var task1 = Task.Run(() => service.HandleAppSleepAsync());
		var task2 = Task.Run(() => service.HandleAppDestroyAsync());
		var task3 = Task.Run(() => service.HandleAppSleepAsync());

		await Task.WhenAll(task1, task2, task3);

		// Check consistency
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
	}

	#endregion

	#region 5. Concurrency, Contention & Cancellation Stress

	[Fact]
	public async Task Concurrency_RapidMixedSyncAndFlushCalls_ExecuteAtomicallyWithoutLockCollisions()
	{
		// Seed 50 items
		for (int i = 0; i < 50; i++)
		{
			await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"stress-mix-{i}",
				syncStatus: (int)SyncStatus.Pending));
		}

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act: Fire 30 parallel operations across sync, flush, and reads
		var tasks = new List<Task>();
		for (int i = 0; i < 10; i++)
		{
			tasks.Add(Task.Run(() => service.SynchronizeAsync()));
			tasks.Add(Task.Run(() => service.FlushPendingChangesAsync()));
			tasks.Add(Task.Run(async () =>
			{
				await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
					id: $"runtime-add-{Guid.NewGuid()}",
					syncStatus: (int)SyncStatus.Pending));
			}));
		}

		await Task.WhenAll(tasks);

		// Assert: System remained healthy, status returned to Idle
		Assert.Equal(SyncEngineStatus.Idle, service.Status);
	}

	[Fact]
	public async Task Concurrency_CancellationTokenAbortedMidOperation_ReleasesLockForSubsequentCalls()
	{
		_fakeLocalStorage.SimulatedDelayMs = 500;

		await _dbFixture.DatabaseService.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "cancel-test-item",
			syncStatus: (int)SyncStatus.Pending));

		await using var service = CreateService();
		await service.InitializeAsync();

		// Act 1: Call SynchronizeAsync with early cancellation
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await service.SynchronizeAsync(cts.Token);
		});

		// Act 2: Subsequent call without cancellation should succeed immediately (semaphore was released)
		_fakeLocalStorage.SimulatedDelayMs = 0;
		var nextCallResult = await service.SynchronizeAsync();

		Assert.True(nextCallResult);
		Assert.Equal(SyncEngineStatus.Idle, service.Status);
	}

	[Fact]
	public async Task Concurrency_NetworkStorm_RapidConnectivityChanges_HandledGracefully()
	{
		await using var service = CreateService();
		await service.InitializeAsync();

		// Simulate network rapid flapping 20 times
		for (int i = 0; i < 20; i++)
		{
			_fakeConnectivity.SetNetworkAccess(i % 2 == 0 ? NetworkAccess.None : NetworkAccess.Internet);
		}

		await Task.Delay(200);

		// Ensure system stabilizes
		Assert.True(service.Status == SyncEngineStatus.Idle || service.Status == SyncEngineStatus.Offline);
	}

	#endregion
}
