using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Sync;

public sealed class StorageSyncServiceTests : IAsyncLifetime
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

	[Fact]
	public async Task InitializeAsync_DefaultsToLocalStorage_WhenNoMetadata()
	{
		await using var service = CreateService();
		await service.InitializeAsync();

		Assert.Equal(StorageProviderType.LocalStorage, service.CurrentProvider.ProviderType);
		Assert.Equal(SyncEngineStatus.Idle, service.Status);
	}

	[Fact]
	public async Task InitializeAsync_RestoresActiveProviderFromMetadata()
	{
		await _dbFixture.DatabaseService.SetMetadataAsync(
			SyncMetadataKeys.ActiveProvider,
			StorageProviderType.GoogleDrive.ToString());

		await using var service = CreateService();
		await service.InitializeAsync();

		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);
	}

	[Fact]
	public async Task FlushPendingChangesAsync_WhenOnline_PushesDirtyRecordsAndMarksSynced()
	{
		var log1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "log-1", syncStatus: (int)SyncStatus.Pending);
		var log2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "log-2", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(log1);
		await _dbFixture.DatabaseService.InsertLogAsync(log2);

		await using var service = CreateService();
		await service.InitializeAsync();

		var result = await service.FlushPendingChangesAsync();

		Assert.True(result);
		Assert.Equal(1, _fakeLocalStorage.WritePayloadCallCount);
		Assert.NotNull(_fakeLocalStorage.StoredPayload);
		Assert.Equal(2, _fakeLocalStorage.StoredPayload.DecisionLogs.Count);

		// Verify records marked Synced in SQLite
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);

		var all = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.All(all, item => Assert.Equal((int)SyncStatus.Synced, item.SyncStatus));
	}

	[Fact]
	public async Task FlushPendingChangesAsync_WhenOffline_DoesNotWriteAndKeepsRecordsPending()
	{
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.None);

		var log1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "log-1", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(log1);

		await using var service = CreateService();
		await service.InitializeAsync();

		var result = await service.FlushPendingChangesAsync();

		Assert.False(result);
		Assert.Equal(0, _fakeLocalStorage.WritePayloadCallCount);

		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Single(pending);
	}

	[Fact]
	public async Task FlushPendingChangesAsync_WhenNoPendingRecords_ReturnsTrueWithoutWriting()
	{
		var log1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "log-1", syncStatus: (int)SyncStatus.Synced);
		await _dbFixture.DatabaseService.InsertLogAsync(log1);

		await using var service = CreateService();
		await service.InitializeAsync();

		var result = await service.FlushPendingChangesAsync();

		Assert.True(result);
		Assert.Equal(0, _fakeLocalStorage.WritePayloadCallCount);
	}

	[Fact]
	public async Task HandleAppSleepAsync_FlushesPendingChanges()
	{
		var log = SqliteTestDatabaseFixture.CreateSampleEntity(id: "sleep-log", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(log);

		await using var service = CreateService();
		await service.InitializeAsync();

		await service.HandleAppSleepAsync();

		Assert.Equal(1, _fakeLocalStorage.WritePayloadCallCount);
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
	}

	[Fact]
	public async Task HandleAppDestroyAsync_FlushesPendingChanges()
	{
		var log = SqliteTestDatabaseFixture.CreateSampleEntity(id: "destroy-log", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(log);

		await using var service = CreateService();
		await service.InitializeAsync();

		await service.HandleAppDestroyAsync();

		Assert.Equal(1, _fakeLocalStorage.WritePayloadCallCount);
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
	}

	[Fact]
	public async Task ConnectivityChanged_WhenRestoredToInternet_TriggersAutomaticFlush()
	{
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.None);

		var log = SqliteTestDatabaseFixture.CreateSampleEntity(id: "offline-log", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(log);

		await using var service = CreateService();
		await service.InitializeAsync();

		// Trigger internet restored event
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.Internet);

		// Wait briefly for background Task.Run to execute flush
		await Task.Delay(200);

		Assert.Equal(1, _fakeLocalStorage.WritePayloadCallCount);
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
	}

	[Fact]
	public async Task SwitchProviderAsync_UploadLocalToCloud_UploadsLocalToTargetAndUpdatesActiveProvider()
	{
		var log1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "mig-1", itemName: "Espresso");
		var log2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "mig-2", itemName: "Pour Over");
		await _dbFixture.DatabaseService.InsertLogAsync(log1);
		await _dbFixture.DatabaseService.InsertLogAsync(log2);

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		Assert.True(success);
		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);
		Assert.Equal(1, _fakeGoogleDrive.WritePayloadCallCount);
		Assert.NotNull(_fakeGoogleDrive.StoredPayload);
		Assert.Equal(2, _fakeGoogleDrive.StoredPayload.DecisionLogs.Count);

		// Verify metadata updated in SQLite
		var activeMeta = await _dbFixture.DatabaseService.GetMetadataAsync(SyncMetadataKeys.ActiveProvider);
		Assert.Equal(StorageProviderType.GoogleDrive.ToString(), activeMeta);
	}

	[Fact]
	public async Task SwitchProviderAsync_DownloadCloudToLocal_ReplacesLocalWithTargetAndUpdatesActiveProvider()
	{
		// Existing local record
		var localOld = SqliteTestDatabaseFixture.CreateSampleEntity(id: "old-local", itemName: "Old Local Item");
		await _dbFixture.DatabaseService.InsertLogAsync(localOld);

		// Remote payload in target OneDrive provider
		var cloudItem = SqliteTestDatabaseFixture.CreateSampleEntity(id: "cloud-item", itemName: "Cloud Item");
		_fakeOneDrive.StoredPayload = new StoragePayload
		{
			DecisionLogs = [cloudItem]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.OneDrive, MigrationOption.DownloadCloudToLocal);

		Assert.True(success);
		Assert.Equal(StorageProviderType.OneDrive, service.CurrentProvider.ProviderType);

		// Verify SQLite replaced with cloudItem
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(allLogs);
		Assert.Equal("cloud-item", allLogs[0].Id);
		Assert.Equal((int)SyncStatus.Synced, allLogs[0].SyncStatus);
	}

	[Fact]
	public async Task SwitchProviderAsync_TwoWayMergeBoth_MergesBothAndUpdatesActiveProvider()
	{
		var t1 = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var t2 = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);

		// Local has LogA and LogB (v1, older)
		var localA = SqliteTestDatabaseFixture.CreateSampleEntity(id: "item-A", timestamp: t1, lastModifiedUtc: t1, itemName: "Item A");
		var localB = SqliteTestDatabaseFixture.CreateSampleEntity(id: "item-B", timestamp: t1, lastModifiedUtc: t1, itemName: "Item B v1");
		await _dbFixture.DatabaseService.InsertLogAsync(localA);
		await _dbFixture.DatabaseService.InsertLogAsync(localB);

		// Target has LogB (v2, newer) and LogC
		var remoteB = SqliteTestDatabaseFixture.CreateSampleEntity(id: "item-B", timestamp: t1, lastModifiedUtc: t2, itemName: "Item B v2 (Newer)");
		var remoteC = SqliteTestDatabaseFixture.CreateSampleEntity(id: "item-C", timestamp: t2, lastModifiedUtc: t2, itemName: "Item C");
		_fakeGoogleDrive.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remoteB, remoteC]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);

		Assert.True(success);
		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);

		// Verify SQLite contains union of 3 items, with remoteB winning
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(3, allLogs.Count);
		var itemB = allLogs.First(x => x.Id == "item-B");
		Assert.Equal("Item B v2 (Newer)", itemB.ItemName);

		// Verify target provider also received union of 3 items
		Assert.NotNull(_fakeGoogleDrive.StoredPayload);
		Assert.Equal(3, _fakeGoogleDrive.StoredPayload.DecisionLogs.Count);
	}

	[Fact]
	public async Task SwitchProviderAsync_WhenTargetAuthenticationFails_AbortsAndPreservesCurrentProvider()
	{
		_fakeGoogleDrive.ShouldFailAuthentication = true;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		Assert.False(success);
		Assert.Equal(StorageProviderType.LocalStorage, service.CurrentProvider.ProviderType);
		Assert.Equal(SyncEngineStatus.Error, service.Status);
	}

	[Fact]
	public async Task SwitchProviderAsync_WhenTargetWriteFails_ReturnsFalseAndSetsErrorStatus()
	{
		_fakeGoogleDrive.ShouldFailWrite = true;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		Assert.False(success);
		Assert.Equal(SyncEngineStatus.Error, service.Status);
	}

	[Fact]
	public async Task SwitchProviderAsync_WhenOfflineAndTargetIsCloud_ReturnsFalse()
	{
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.None);

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);

		Assert.False(success);
		Assert.Equal(SyncEngineStatus.Offline, service.Status);
	}

	[Fact]
	public async Task PeriodicFlush_WithDirtyRecords_PushesPayloadAndMarksSynced()
	{
		// Arrange: 100ms flush interval for testing
		await using var service = CreateService(flushInterval: TimeSpan.FromMilliseconds(100));
		await service.InitializeAsync();

		var testEntity = new DecisionLogEntity
		{
			Id = "log-periodic-1",
			Decision = "YES",
			CaffeineAmountMg = 95,
			ItemName = "Espresso",
			SyncStatus = (int)SyncStatus.Pending
		};
		await _dbFixture.DatabaseService.InsertLogAsync(testEntity);

		// Act: Wait 300ms for periodic tick to fire
		await Task.Delay(300);

		// Assert
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);

		var all = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(all);
		Assert.Equal((int)SyncStatus.Synced, all[0].SyncStatus);

		Assert.True(_fakeLocalStorage.WritePayloadCallCount >= 1);
		Assert.NotNull(_fakeLocalStorage.StoredPayload);
		Assert.Single(_fakeLocalStorage.StoredPayload.DecisionLogs);
	}

	[Fact]
	public async Task OfflineResilience_WhenNetworkDrops_TransitionsToOfflineAndQueuesWrites()
	{
		// Arrange: Cloud provider requiring auth
		_fakeGoogleDrive.IsAuthenticated = true;
		_fakeGoogleDrive.RequiresAuthentication = true;

		await _dbFixture.DatabaseService.SetMetadataAsync(
			SyncMetadataKeys.ActiveProvider,
			StorageProviderType.GoogleDrive.ToString());

		await using var service = CreateService(flushInterval: TimeSpan.FromHours(1));
		await service.InitializeAsync();
		Assert.Equal(SyncEngineStatus.Idle, service.Status);

		// Act 1: Drop network connection
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.None);
		Assert.Equal(SyncEngineStatus.Offline, service.Status);

		// Act 2: Local write while offline
		await _dbFixture.DatabaseService.InsertLogAsync(new DecisionLogEntity
		{
			Id = "log-offline-1",
			Decision = "YES",
			SyncStatus = (int)SyncStatus.Pending
		});

		// Act 3: Try flushing while offline (should fail safely and keep pending)
		bool flushResult = await service.FlushPendingChangesAsync();
		Assert.False(flushResult);
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Single(pending);

		// Act 4: Restore network
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.Internet);
		await Task.Delay(200); // Allow background auto-flush to execute

		// Assert: Auto-flush completed and marked synced
		var pendingAfterRestore = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pendingAfterRestore);
	}
}
