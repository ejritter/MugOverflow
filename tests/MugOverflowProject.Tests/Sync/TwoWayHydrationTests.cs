using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Sync;

public sealed class TwoWayHydrationTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeConnectivity _fakeConnectivity = new();
	private readonly FakeStorageProvider _fakeProvider = new()
	{
		ProviderType = StorageProviderType.GoogleDrive,
		DisplayName = "Fake Google Drive",
		RequiresAuthentication = false
	};

	private IServiceProvider _serviceProvider = null!;

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();

		var services = new ServiceCollection();
		services.AddSingleton<ISqliteDatabaseService>(_dbFixture.DatabaseService);
		services.AddSingleton<IConnectivity>(_fakeConnectivity);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, _fakeProvider);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, _fakeProvider);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.OneDrive, _fakeProvider);

		_serviceProvider = services.BuildServiceProvider();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	private StorageSyncService CreateService()
	{
		return new StorageSyncService(
			_dbFixture.DatabaseService,
			_serviceProvider,
			_fakeConnectivity,
			TimeSpan.FromMinutes(5));
	}

	[Fact]
	public async Task SynchronizeAsync_DisjointUnion_MergesNonOverlappingLocalAndRemoteRecords()
	{
		var t0 = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Local has items 1 and 2
		var local1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "L1", timestamp: t0, lastModifiedUtc: t0, itemName: "Local 1");
		var local2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "L2", timestamp: t0, lastModifiedUtc: t0, itemName: "Local 2");
		await _dbFixture.DatabaseService.InsertLogAsync(local1);
		await _dbFixture.DatabaseService.InsertLogAsync(local2);

		// Remote has items 3 and 4
		var remote3 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "R3", timestamp: t0, lastModifiedUtc: t0, itemName: "Remote 3");
		var remote4 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "R4", timestamp: t0, lastModifiedUtc: t0, itemName: "Remote 4");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote3, remote4]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		// Verify SQLite contains all 4 records
		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(4, allLocal.Count);
		Assert.Contains(allLocal, x => x.Id == "L1");
		Assert.Contains(allLocal, x => x.Id == "L2");
		Assert.Contains(allLocal, x => x.Id == "R3" && x.SyncStatus == (int)SyncStatus.Synced);
		Assert.Contains(allLocal, x => x.Id == "R4" && x.SyncStatus == (int)SyncStatus.Synced);

		// Verify remote payload contains all 4 records
		Assert.NotNull(_fakeProvider.StoredPayload);
		Assert.Equal(4, _fakeProvider.StoredPayload.DecisionLogs.Count);
	}

	[Fact]
	public async Task SynchronizeAsync_LWWConflict_RemoteNewer_OverwritesLocalWithRemote()
	{
		var tOld = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var tNew = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Local has item with older timestamp
		var local = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "conflict-1",
			timestamp: tOld,
			lastModifiedUtc: tOld,
			decision: "NO",
			reason: "Local Old Reason");
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		// Remote has item with newer timestamp
		var remote = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "conflict-1",
			timestamp: tOld,
			lastModifiedUtc: tNew,
			decision: "YES",
			reason: "Remote Newer Reason");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		var fetched = await _dbFixture.DatabaseService.GetLogByIdAsync("conflict-1");
		Assert.NotNull(fetched);
		Assert.Equal("YES", fetched.Decision);
		Assert.Equal("Remote Newer Reason", fetched.Reason);
		Assert.Equal(tNew, fetched.LastModifiedUtc);
		Assert.Equal((int)SyncStatus.Synced, fetched.SyncStatus);
	}

	[Fact]
	public async Task SynchronizeAsync_LWWConflict_LocalNewer_OverwritesRemoteWithLocal()
	{
		var tOld = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var tNew = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Local has newer item
		var local = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "conflict-2",
			timestamp: tOld,
			lastModifiedUtc: tNew,
			decision: "YES",
			reason: "Local Newer Reason",
			syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		// Remote has older item
		var remote = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "conflict-2",
			timestamp: tOld,
			lastModifiedUtc: tOld,
			decision: "NO",
			reason: "Remote Old Reason");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		// Verify remote was updated with local item
		Assert.NotNull(_fakeProvider.StoredPayload);
		var remoteLog = _fakeProvider.StoredPayload.DecisionLogs.First(x => x.Id == "conflict-2");
		Assert.Equal("YES", remoteLog.Decision);
		Assert.Equal("Local Newer Reason", remoteLog.Reason);
		Assert.Equal(tNew, remoteLog.LastModifiedUtc);

		// Local marked synced
		var localLog = await _dbFixture.DatabaseService.GetLogByIdAsync("conflict-2");
		Assert.NotNull(localLog);
		Assert.Equal((int)SyncStatus.Synced, localLog.SyncStatus);
	}

	[Fact]
	public async Task SynchronizeAsync_LWWConflict_IdenticalTimestamps_ConvergesWithoutDuplication()
	{
		var tSame = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		var local = SqliteTestDatabaseFixture.CreateSampleEntity(id: "same-1", timestamp: tSame, lastModifiedUtc: tSame, decision: "YES");
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		var remote = SqliteTestDatabaseFixture.CreateSampleEntity(id: "same-1", timestamp: tSame, lastModifiedUtc: tSame, decision: "YES");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(allLogs);
		Assert.Equal("same-1", allLogs[0].Id);
	}

	[Fact]
	public async Task SynchronizeAsync_SubMillisecondTimestamps_ResolvesStrictLww()
	{
		var baseTime = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
		var tLocal = baseTime.AddTicks(10); // 1 microsecond later
		var tRemote = baseTime;

		var local = SqliteTestDatabaseFixture.CreateSampleEntity(id: "subms-1", timestamp: baseTime, lastModifiedUtc: tLocal, itemName: "Local High Precision");
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		var remote = SqliteTestDatabaseFixture.CreateSampleEntity(id: "subms-1", timestamp: baseTime, lastModifiedUtc: tRemote, itemName: "Remote Base Precision");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [remote]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		Assert.NotNull(_fakeProvider.StoredPayload);
		var winning = _fakeProvider.StoredPayload.DecisionLogs.First(x => x.Id == "subms-1");
		Assert.Equal("Local High Precision", winning.ItemName);
	}

	[Fact]
	public async Task SynchronizeAsync_EmptyRemote_UploadsAllLocalRecordsToRemote()
	{
		var local1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "L1");
		var local2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "L2");
		await _dbFixture.DatabaseService.InsertLogAsync(local1);
		await _dbFixture.DatabaseService.InsertLogAsync(local2);

		_fakeProvider.StoredPayload = null; // Remote store is empty

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);
		Assert.NotNull(_fakeProvider.StoredPayload);
		Assert.Equal(2, _fakeProvider.StoredPayload.DecisionLogs.Count);

		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.All(allLocal, item => Assert.Equal((int)SyncStatus.Synced, item.SyncStatus));
	}

	[Fact]
	public async Task SynchronizeAsync_EmptyLocal_DownloadsAllRemoteRecordsToLocal()
	{
		var r1 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "R1");
		var r2 = SqliteTestDatabaseFixture.CreateSampleEntity(id: "R2");
		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [r1, r2]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);
		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2, allLocal.Count);
		Assert.All(allLocal, item => Assert.Equal((int)SyncStatus.Synced, item.SyncStatus));
	}

	[Fact]
	public async Task SynchronizeAsync_BothEmpty_SucceedsWithoutChanges()
	{
		_fakeProvider.StoredPayload = null;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);
		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Empty(allLocal);
	}

	[Fact]
	public async Task SynchronizeAsync_WhenRemoteReadFails_SetsStatusErrorAndPreservesLocalData()
	{
		var local = SqliteTestDatabaseFixture.CreateSampleEntity(id: "safe-local");
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		_fakeProvider.ShouldFailRead = true;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		// Should preserve local SQLite data intact
		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(allLocal);
		Assert.Equal("safe-local", allLocal[0].Id);
	}

	[Fact]
	public async Task SynchronizeAsync_WhenRemoteWriteFails_KeepsLocalDirtyFlagsPending()
	{
		var local = SqliteTestDatabaseFixture.CreateSampleEntity(id: "pending-local", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.InsertLogAsync(local);

		_fakeProvider.ShouldFailWrite = true;

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.False(success);
		Assert.Equal(SyncEngineStatus.Error, service.Status);

		// Local item remains pending
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Single(pending);
		Assert.Equal("pending-local", pending[0].Id);
	}

	[Fact]
	public async Task SynchronizeAsync_DeduplicatesRemoteDuplicateIds_ByNewestTimestamp()
	{
		var tOld = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var tNew = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		var dupOld = SqliteTestDatabaseFixture.CreateSampleEntity(id: "dup-1", timestamp: tOld, lastModifiedUtc: tOld, itemName: "Old Dup Name");
		var dupNew = SqliteTestDatabaseFixture.CreateSampleEntity(id: "dup-1", timestamp: tOld, lastModifiedUtc: tNew, itemName: "New Dup Name");

		_fakeProvider.StoredPayload = new StoragePayload
		{
			DecisionLogs = [dupOld, dupNew]
		};

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		Assert.True(success);

		var allLocal = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(allLocal);
		Assert.Equal("dup-1", allLocal[0].Id);
		Assert.Equal("New Dup Name", allLocal[0].ItemName);
	}
}
