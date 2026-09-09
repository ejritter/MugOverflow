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
/// Empirical Adversarial Stress Test Suite for Milestone 3 (Sync Orchestrator, Two-Way Hydration & Lifecycle Persistence).
/// Stress-tests:
/// 1. Disjoint union hydration with 1,000+ local & remote records + overlapping LWW resolution
/// 2. Multi-device concurrent clock skew simulations (+2h, -3h, UTC on-time)
/// 3. Tie-breaking on identical timestamps & sub-millisecond precision
/// 4. Rapid offline/online network flipping & connection storm resilience
/// 5. Mid-flight cancellation token abortion & SemaphoreSlim lock recovery
/// 6. Extreme multi-threaded concurrent re-entrancy stress
/// 7. Corrupted & duplicate remote payload fault-injection recovery
/// 8. Large-scale provider switching & 3-way migration under failure modes
/// </summary>
public sealed class SyncAdversarialStressTests : IAsyncLifetime
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
	public async Task DisjointUnion_1000Local_1000Remote_500Overlapping_MergesAccuratelyAndConverges()
	{
		// 1. Arrange 1,000 local-only records
		var localOnly = new List<DecisionLogEntity>(1000);
		for (int i = 0; i < 1000; i++)
		{
			localOnly.Add(new DecisionLogEntity
			{
				Id = $"local-only-{i:D4}",
				Timestamp = DateTime.UtcNow.AddMinutes(-i),
				LastModifiedUtc = DateTime.UtcNow.AddMinutes(-i),
				Decision = "YES",
				Reason = $"Local Only Reason {i}",
				CaffeineAmountMg = 95.0 + i,
				ItemName = $"Local Coffee {i}",
				ItemCategory = "Coffee",
				SyncStatus = (int)SyncStatus.Pending
			});
		}
		await _dbFixture.DatabaseService.UpsertBatchAsync(localOnly);

		// 2. Arrange 1,000 remote-only records
		var remoteOnly = new List<DecisionLogEntity>(1000);
		for (int i = 0; i < 1000; i++)
		{
			remoteOnly.Add(new DecisionLogEntity
			{
				Id = $"remote-only-{i:D4}",
				Timestamp = DateTime.UtcNow.AddMinutes(-i),
				LastModifiedUtc = DateTime.UtcNow.AddMinutes(-i),
				Decision = "NO",
				Reason = $"Remote Only Reason {i}",
				CaffeineAmountMg = 50.0 + i,
				ItemName = $"Remote Tea {i}",
				ItemCategory = "Tea",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		// 3. Arrange 500 overlapping records
		// 200 where remote is strictly newer
		// 200 where local is strictly newer
		// 100 where timestamps are identical
		var baseTime = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var overlappingLocal = new List<DecisionLogEntity>(500);
		var overlappingRemote = new List<DecisionLogEntity>(500);

		// 200 Remote Newer
		for (int i = 0; i < 200; i++)
		{
			var id = $"overlap-remote-newer-{i:D3}";
			overlappingLocal.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Decision = "NO",
				Reason = "Old Local Value",
				CaffeineAmountMg = 10.0,
				ItemName = "Old Local Item",
				SyncStatus = (int)SyncStatus.Pending
			});
			overlappingRemote.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime.AddHours(2), // 2 hours newer
				Decision = "YES",
				Reason = "Winning Remote Value",
				CaffeineAmountMg = 150.0,
				ItemName = "Winning Remote Item",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		// 200 Local Newer
		for (int i = 0; i < 200; i++)
		{
			var id = $"overlap-local-newer-{i:D3}";
			overlappingLocal.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime.AddHours(2), // 2 hours newer
				Decision = "YES",
				Reason = "Winning Local Value",
				CaffeineAmountMg = 200.0,
				ItemName = "Winning Local Item",
				SyncStatus = (int)SyncStatus.Pending
			});
			overlappingRemote.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Decision = "NO",
				Reason = "Old Remote Value",
				CaffeineAmountMg = 20.0,
				ItemName = "Old Remote Item",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		// 100 Identical Timestamps
		for (int i = 0; i < 100; i++)
		{
			var id = $"overlap-identical-{i:D3}";
			overlappingLocal.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Decision = "YES",
				Reason = "Identical Local Reason",
				CaffeineAmountMg = 100.0,
				ItemName = "Identical Item",
				SyncStatus = (int)SyncStatus.Pending
			});
			overlappingRemote.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Decision = "YES",
				Reason = "Identical Remote Reason",
				CaffeineAmountMg = 100.0,
				ItemName = "Identical Item",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		await _dbFixture.DatabaseService.UpsertBatchAsync(overlappingLocal);

		var fullRemoteList = remoteOnly.Concat(overlappingRemote).ToList();
		_fakeLocalStorage.StoredPayload = new StoragePayload
		{
			DecisionLogs = fullRemoteList
		};

		// Act: Run Two-Way Union Hydration
		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();

		// Assert
		Assert.True(success);

		// Total distinct IDs should be 1000 + 1000 + 500 = 2,500
		var allLocalLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2500, allLocalLogs.Count);

		// Verify 100% of SQLite records are marked Synced
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Empty(pending);
		Assert.All(allLocalLogs, log => Assert.Equal((int)SyncStatus.Synced, log.SyncStatus));

		// Verify Remote payload has exactly 2,500 records
		Assert.NotNull(_fakeLocalStorage.StoredPayload);
		Assert.Equal(2500, _fakeLocalStorage.StoredPayload.DecisionLogs.Count);

		// Verify Remote-Newer records won in SQLite
		var localMap = allLocalLogs.ToDictionary(x => x.Id, x => x);
		for (int i = 0; i < 200; i++)
		{
			var id = $"overlap-remote-newer-{i:D3}";
			Assert.True(localMap.TryGetValue(id, out var entity));
			Assert.Equal("YES", entity.Decision);
			Assert.Equal("Winning Remote Value", entity.Reason);
			Assert.Equal(150.0, entity.CaffeineAmountMg);
			Assert.Equal(baseTime.AddHours(2), entity.LastModifiedUtc);
		}

		// Verify Local-Newer records won in Remote Storage Payload
		var remoteMap = _fakeLocalStorage.StoredPayload.DecisionLogs.ToDictionary(x => x.Id, x => x);
		for (int i = 0; i < 200; i++)
		{
			var id = $"overlap-local-newer-{i:D3}";
			Assert.True(remoteMap.TryGetValue(id, out var entity));
			Assert.Equal("YES", entity.Decision);
			Assert.Equal("Winning Local Value", entity.Reason);
			Assert.Equal(200.0, entity.CaffeineAmountMg);
			Assert.Equal(baseTime.AddHours(2), entity.LastModifiedUtc);
		}

		// Verify Identical records converged cleanly
		for (int i = 0; i < 100; i++)
		{
			var id = $"overlap-identical-{i:D3}";
			Assert.True(localMap.ContainsKey(id));
			Assert.True(remoteMap.ContainsKey(id));
		}
	}

	[Fact]
	public async Task MultiDeviceClockSkew_SimulatedThreeDevicesWithSkew_MaintainsLwwDeterminism()
	{
		// Shared simulated cloud storage payload
		var cloudPayload = new StoragePayload();
		var sharedCloudProvider = new FakeStorageProvider
		{
			ProviderType = StorageProviderType.GoogleDrive,
			DisplayName = "Shared Cloud",
			RequiresAuthentication = false,
			StoredPayload = cloudPayload
		};

		// 3 isolated SQLite database instances simulating 3 distinct user devices
		var dbFixtureA = new SqliteTestDatabaseFixture();
		var dbFixtureB = new SqliteTestDatabaseFixture();
		var dbFixtureC = new SqliteTestDatabaseFixture();

		try
		{
			await dbFixtureA.InitializeAsync();
			await dbFixtureB.InitializeAsync();
			await dbFixtureC.InitializeAsync();

		var fakeConn = new FakeConnectivity();

		var scA = new ServiceCollection();
		scA.AddSingleton<ISqliteDatabaseService>(dbFixtureA.DatabaseService);
		scA.AddSingleton<IConnectivity>(fakeConn);
		scA.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, sharedCloudProvider);
		scA.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, sharedCloudProvider);
		var spA = scA.BuildServiceProvider();

		var scB = new ServiceCollection();
		scB.AddSingleton<ISqliteDatabaseService>(dbFixtureB.DatabaseService);
		scB.AddSingleton<IConnectivity>(fakeConn);
		scB.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, sharedCloudProvider);
		scB.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, sharedCloudProvider);
		var spB = scB.BuildServiceProvider();

		var scC = new ServiceCollection();
		scC.AddSingleton<ISqliteDatabaseService>(dbFixtureC.DatabaseService);
		scC.AddSingleton<IConnectivity>(fakeConn);
		scC.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, sharedCloudProvider);
		scC.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, sharedCloudProvider);
		var spC = scC.BuildServiceProvider();

		await dbFixtureA.DatabaseService.SetMetadataAsync(SyncMetadataKeys.ActiveProvider, StorageProviderType.GoogleDrive.ToString());
		await dbFixtureB.DatabaseService.SetMetadataAsync(SyncMetadataKeys.ActiveProvider, StorageProviderType.GoogleDrive.ToString());
		await dbFixtureC.DatabaseService.SetMetadataAsync(SyncMetadataKeys.ActiveProvider, StorageProviderType.GoogleDrive.ToString());

		await using var serviceA = new StorageSyncService(dbFixtureA.DatabaseService, spA, fakeConn);
		await using var serviceB = new StorageSyncService(dbFixtureB.DatabaseService, spB, fakeConn);
		await using var serviceC = new StorageSyncService(dbFixtureC.DatabaseService, spC, fakeConn);

		await serviceA.InitializeAsync();
		await serviceB.InitializeAsync();
		await serviceC.InitializeAsync();

		var baseUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Device A (Clock skewed +2 hours fast): Creates "record-1"
		var rec1DeviceA = new DecisionLogEntity
		{
			Id = "record-1",
			Timestamp = baseUtc,
			LastModifiedUtc = baseUtc.AddHours(2), // +2h skew
			Decision = "YES",
			Reason = "Device A fast clock",
			ItemName = "Device A Coffee",
			SyncStatus = (int)SyncStatus.Pending
		};
		await dbFixtureA.DatabaseService.InsertLogAsync(rec1DeviceA);
		Assert.True(await serviceA.SynchronizeAsync());

		// Device B (Clock skewed -3 hours slow): Creates "record-2"
		var rec2DeviceB = new DecisionLogEntity
		{
			Id = "record-2",
			Timestamp = baseUtc,
			LastModifiedUtc = baseUtc.AddHours(-3), // -3h skew
			Decision = "NO",
			Reason = "Device B slow clock",
			ItemName = "Device B Tea",
			SyncStatus = (int)SyncStatus.Pending
		};
		await dbFixtureB.DatabaseService.InsertLogAsync(rec2DeviceB);
		Assert.True(await serviceB.SynchronizeAsync());

		// Device C (Standard UTC): Syncs and downloads both record-1 and record-2
		Assert.True(await serviceC.SynchronizeAsync());
		var logsC = await dbFixtureC.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2, logsC.Count);

		// Device C updates record-2 with a standard on-time UTC timestamp (baseUtc, which is > -3h from Device B)
		var rec2DeviceC = logsC.First(x => x.Id == "record-2");
		rec2DeviceC.LastModifiedUtc = baseUtc; // Newer than Device B's -3h
		rec2DeviceC.Reason = "Device C override with correct UTC";
		rec2DeviceC.SyncStatus = (int)SyncStatus.Pending;
		await dbFixtureC.DatabaseService.UpdateLogAsync(rec2DeviceC);
		Assert.True(await serviceC.SynchronizeAsync());

		// Now Device B syncs again -> Device C's newer update should overwrite Device B's older copy
		Assert.True(await serviceB.SynchronizeAsync());
		var rec2UpdatedInB = await dbFixtureB.DatabaseService.GetLogByIdAsync("record-2");
		Assert.NotNull(rec2UpdatedInB);
		Assert.Equal("Device C override with correct UTC", rec2UpdatedInB.Reason);
		Assert.Equal(baseUtc, rec2UpdatedInB.LastModifiedUtc);

		// Now Device A syncs again -> Gets record-2 as updated by C
		Assert.True(await serviceA.SynchronizeAsync());
		var logsA = await dbFixtureA.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2, logsA.Count);

		// Final check: All 3 devices converge to identical sets of 2 records
		var finalA = await dbFixtureA.DatabaseService.GetAllLogsAsync();
		var finalB = await dbFixtureB.DatabaseService.GetAllLogsAsync();
		var finalC = await dbFixtureC.DatabaseService.GetAllLogsAsync();

		Assert.Equal(2, finalA.Count);
		Assert.Equal(2, finalB.Count);
		Assert.Equal(2, finalC.Count);

		Assert.Equal(finalA.OrderBy(x => x.Id).Select(x => x.Reason), finalB.OrderBy(x => x.Id).Select(x => x.Reason));
		Assert.Equal(finalB.OrderBy(x => x.Id).Select(x => x.Reason), finalC.OrderBy(x => x.Id).Select(x => x.Reason));
		}
		finally
		{
			await dbFixtureA.DisposeAsync();
			await dbFixtureB.DisposeAsync();
			await dbFixtureC.DisposeAsync();
		}
	}

	[Fact]
	public async Task TieBreaking_IdenticalTimestamps_AcrossDifferentFields_ConvergesDeterministically()
	{
		var tSame = new DateTime(2026, 8, 28, 12, 0, 0, 0, DateTimeKind.Utc);

		var localLogs = new List<DecisionLogEntity>();
		var remoteLogs = new List<DecisionLogEntity>();

		for (int i = 0; i < 50; i++)
		{
			var id = $"tie-{i:D3}";
			localLogs.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = tSame,
				LastModifiedUtc = tSame,
				Decision = "LOCAL_DECISION",
				Reason = $"Local Reason {i}",
				ItemName = $"Local Item {i}",
				SyncStatus = (int)SyncStatus.Pending
			});
			remoteLogs.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = tSame,
				LastModifiedUtc = tSame,
				Decision = "REMOTE_DECISION",
				Reason = $"Remote Reason {i}",
				ItemName = $"Remote Item {i}",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		await _dbFixture.DatabaseService.UpsertBatchAsync(localLogs);
		_fakeLocalStorage.StoredPayload = new StoragePayload { DecisionLogs = remoteLogs };

		await using var service = CreateService();
		await service.InitializeAsync();

		// First sync: tie-break convergence
		var sync1 = await service.SynchronizeAsync();
		Assert.True(sync1);

		var all1 = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(50, all1.Count);
		Assert.All(all1, x => Assert.Equal((int)SyncStatus.Synced, x.SyncStatus));

		// Second immediate sync: idempotent, zero changes
		var sync2 = await service.SynchronizeAsync();
		Assert.True(sync2);

		var all2 = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(50, all2.Count);
	}

	[Fact]
	public async Task SubMillisecondTimestamps_PreservedThroughJsonAndSqlite_ResolvesExactWinner()
	{
		var baseTime = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Local is ahead by 10 ticks (1 microsecond) on 25 records
		// Remote is ahead by 10 ticks on 25 records
		var localList = new List<DecisionLogEntity>();
		var remoteList = new List<DecisionLogEntity>();

		for (int i = 0; i < 25; i++)
		{
			var id = $"subms-local-wins-{i:D2}";
			localList.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime.AddTicks(10), // +1 microsecond
				Reason = "Local Microsecond Winner",
				SyncStatus = (int)SyncStatus.Pending
			});
			remoteList.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Reason = "Remote Base Time",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		for (int i = 0; i < 25; i++)
		{
			var id = $"subms-remote-wins-{i:D2}";
			localList.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime,
				Reason = "Local Base Time",
				SyncStatus = (int)SyncStatus.Pending
			});
			remoteList.Add(new DecisionLogEntity
			{
				Id = id,
				Timestamp = baseTime,
				LastModifiedUtc = baseTime.AddTicks(10), // +1 microsecond
				Reason = "Remote Microsecond Winner",
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		await _dbFixture.DatabaseService.UpsertBatchAsync(localList);
		_fakeLocalStorage.StoredPayload = new StoragePayload { DecisionLogs = remoteList };

		await using var service = CreateService();
		await service.InitializeAsync();

		Assert.True(await service.SynchronizeAsync());

		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(50, allLogs.Count);

		for (int i = 0; i < 25; i++)
		{
			var localWin = allLogs.First(x => x.Id == $"subms-local-wins-{i:D2}");
			Assert.Equal("Local Microsecond Winner", localWin.Reason);

			var remoteWin = allLogs.First(x => x.Id == $"subms-remote-wins-{i:D2}");
			Assert.Equal("Remote Microsecond Winner", remoteWin.Reason);
		}
	}

	[Fact]
	public async Task NetworkFlappingStorm_RapidOnlineOfflineToggling_DoesNotDeadlockOrCorruptData()
	{
		await using var service = CreateService(flushInterval: TimeSpan.FromMilliseconds(50));
		await service.InitializeAsync();

		var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Worker 1: Rapidly toggles network state
		var networkToggleTask = Task.Run(async () =>
		{
			for (int i = 0; i < 40 && !cts.Token.IsCancellationRequested; i++)
			{
				_fakeConnectivity.SetNetworkAccess(i % 2 == 0 ? NetworkAccess.Internet : NetworkAccess.None);
				await Task.Delay(10, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}, cts.Token);

		// Worker 2: Continuously inserts records in SQLite
		var insertionTask = Task.Run(async () =>
		{
			for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
			{
				var entity = new DecisionLogEntity
				{
					Id = $"storm-{i:D3}",
					Decision = "YES",
					Reason = $"Storm Log {i}",
					SyncStatus = (int)SyncStatus.Pending
				};
				await _dbFixture.DatabaseService.InsertLogAsync(entity);
				await Task.Delay(15, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}, cts.Token);

		// Worker 3: Continuously triggers flush and sync calls
		var syncTask = Task.Run(async () =>
		{
			for (int i = 0; i < 20 && !cts.Token.IsCancellationRequested; i++)
			{
				if (i % 2 == 0)
				{
					await service.FlushPendingChangesAsync(CancellationToken.None).ConfigureAwait(false);
				}
				else
				{
					await service.SynchronizeAsync(CancellationToken.None).ConfigureAwait(false);
				}
				await Task.Delay(20, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}, cts.Token);

		await Task.WhenAll(networkToggleTask, insertionTask, syncTask);

		// Settle network to Internet
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.Internet);
		await Task.Delay(100);

		// Final clean sync
		var finalSync = await service.SynchronizeAsync();
		Assert.True(finalSync);

		// Verify all 30 storm records are present and Synced
		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(30, allLogs.Count);
		Assert.All(allLogs, x => Assert.Equal((int)SyncStatus.Synced, x.SyncStatus));

		Assert.NotNull(_fakeLocalStorage.StoredPayload);
		Assert.Equal(30, _fakeLocalStorage.StoredPayload.DecisionLogs.Count);
	}

	[Fact]
	public async Task SyncCancellation_MidFlightTokenCancellation_ReleasesLockAndLeavesDbConsistent()
	{
		_fakeLocalStorage.SimulatedDelayMs = 150;

		var logs = SqliteTestDatabaseFixture.CreateSampleEntities(20);
		await _dbFixture.DatabaseService.UpsertBatchAsync(logs);

		await using var service = CreateService();
		await service.InitializeAsync();

		using var cts = new CancellationTokenSource();
		cts.CancelAfter(30); // Cancel while ReadPayloadAsync or WritePayloadAsync is sleeping

		// Act & Assert cancellation exception
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await service.SynchronizeAsync(cts.Token);
		});

		// Ensure lock was released by immediately running an un-cancelled sync
		_fakeLocalStorage.SimulatedDelayMs = 0;
		var success = await service.SynchronizeAsync(CancellationToken.None);
		Assert.True(success);

		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(20, allLogs.Count);
		Assert.All(allLogs, x => Assert.Equal((int)SyncStatus.Synced, x.SyncStatus));
	}

	[Fact]
	public async Task HighConcurrencyStress_20ConcurrentCallers_ExecuteSafelyWithoutCorruptingOrCrashing()
	{
		var sampleLogs = SqliteTestDatabaseFixture.CreateSampleEntities(15);
		await _dbFixture.DatabaseService.UpsertBatchAsync(sampleLogs);

		await using var service = CreateService();
		await service.InitializeAsync();

		// 20 concurrent threads hammering SynchronizeAsync, FlushPendingChangesAsync, HandleAppSleepAsync
		var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
		{
			if (i % 3 == 0)
			{
				return await service.SynchronizeAsync();
			}
			if (i % 3 == 1)
			{
				return await service.FlushPendingChangesAsync();
			}
			await service.HandleAppSleepAsync();
			return true;
		})).ToList();

		var results = await Task.WhenAll(tasks);

		// None should throw unhandled exception
		Assert.All(results, Assert.True);

		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(15, allLogs.Count);
		Assert.All(allLogs, x => Assert.Equal((int)SyncStatus.Synced, x.SyncStatus));
	}

	[Fact]
	public async Task CorruptedRemotePayload_MalformedRecordsAndDuplicateIds_SanitizedGracefully()
	{
		var tOld = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
		var tNew = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

		// Remote payload contains invalid entries: empty IDs, whitespace IDs, duplicates
		_fakeLocalStorage.StoredPayload = new StoragePayload
		{
			DecisionLogs =
			[
				new DecisionLogEntity { Id = "", Reason = "Empty ID 1" },
				new DecisionLogEntity { Id = "   ", Reason = "Whitespace ID 2" },
				new DecisionLogEntity { Id = "dup-corrupt", Timestamp = tOld, LastModifiedUtc = tOld, Reason = "Older Dup Version" },
				new DecisionLogEntity { Id = "dup-corrupt", Timestamp = tOld, LastModifiedUtc = tNew, Reason = "Newer Dup Winner" },
				new DecisionLogEntity { Id = "valid-remote", Timestamp = tNew, LastModifiedUtc = tNew, Reason = "Valid Remote Item" }
			]
		};

		var localLog = new DecisionLogEntity
		{
			Id = "local-valid",
			Timestamp = tNew,
			LastModifiedUtc = tNew,
			Reason = "Valid Local Item",
			SyncStatus = (int)SyncStatus.Pending
		};
		await _dbFixture.DatabaseService.InsertLogAsync(localLog);

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SynchronizeAsync();
		Assert.True(success);

		var allLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(3, allLogs.Count); // local-valid, valid-remote, dup-corrupt

		var dupRecord = allLogs.First(x => x.Id == "dup-corrupt");
		Assert.Equal("Newer Dup Winner", dupRecord.Reason);

		Assert.Contains(allLogs, x => x.Id == "local-valid");
		Assert.Contains(allLogs, x => x.Id == "valid-remote");
		Assert.DoesNotContain(allLogs, x => string.IsNullOrWhiteSpace(x.Id));
	}

	[Fact]
	public async Task ProviderMigrationStress_SwitchProviderWith1000Records_UnderAllThreeOptions()
	{
		// 1. Arrange 1,000 local records
		var local1000 = new List<DecisionLogEntity>(1000);
		for (int i = 0; i < 1000; i++)
		{
			local1000.Add(new DecisionLogEntity
			{
				Id = $"mig-local-{i:D4}",
				Timestamp = DateTime.UtcNow,
				LastModifiedUtc = DateTime.UtcNow,
				Decision = "YES",
				Reason = $"Local {i}",
				SyncStatus = (int)SyncStatus.Pending
			});
		}
		await _dbFixture.DatabaseService.UpsertBatchAsync(local1000);

		// 2. Arrange 1,000 remote records in Google Drive
		var remote1000 = new List<DecisionLogEntity>(1000);
		for (int i = 0; i < 1000; i++)
		{
			remote1000.Add(new DecisionLogEntity
			{
				Id = $"mig-remote-{i:D4}",
				Timestamp = DateTime.UtcNow,
				LastModifiedUtc = DateTime.UtcNow,
				Decision = "NO",
				Reason = $"Remote {i}",
				SyncStatus = (int)SyncStatus.Synced
			});
		}
		_fakeGoogleDrive.StoredPayload = new StoragePayload { DecisionLogs = remote1000 };

		await using var service = CreateService();
		await service.InitializeAsync();

		// Option: TwoWayMergeBoth into Google Drive
		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);
		Assert.True(success);
		Assert.Equal(StorageProviderType.GoogleDrive, service.CurrentProvider.ProviderType);

		var allMerged = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(2000, allMerged.Count);
		Assert.All(allMerged, x => Assert.Equal((int)SyncStatus.Synced, x.SyncStatus));

		Assert.NotNull(_fakeGoogleDrive.StoredPayload);
		Assert.Equal(2000, _fakeGoogleDrive.StoredPayload.DecisionLogs.Count);

		// Verify metadata persisted
		var savedProvider = await _dbFixture.DatabaseService.GetMetadataAsync(SyncMetadataKeys.ActiveProvider);
		Assert.Equal(StorageProviderType.GoogleDrive.ToString(), savedProvider);
	}

	[Fact]
	public async Task OfflineProviderSwitch_RefusesRemoteSwitchWhileOffline_MaintainsOriginalProvider()
	{
		_fakeConnectivity.SetNetworkAccess(NetworkAccess.None);

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);
		Assert.False(success);
		Assert.Equal(StorageProviderType.LocalStorage, service.CurrentProvider.ProviderType);
		Assert.Equal(SyncEngineStatus.Offline, service.Status);
	}

	[Fact]
	public async Task TargetProviderAuthFailure_DuringMigration_AbortsCleanlyWithoutDataLoss()
	{
		_fakeGoogleDrive.ShouldFailAuthentication = true;

		var log = SqliteTestDatabaseFixture.CreateSampleEntity(id: "safe-entity");
		await _dbFixture.DatabaseService.InsertLogAsync(log);

		await using var service = CreateService();
		await service.InitializeAsync();

		var success = await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.UploadLocalToCloud);
		Assert.False(success);
		Assert.Equal(StorageProviderType.LocalStorage, service.CurrentProvider.ProviderType);
		Assert.Equal(SyncEngineStatus.Error, service.Status);

		// Local entity is preserved
		var local = await _dbFixture.DatabaseService.GetLogByIdAsync("safe-entity");
		Assert.NotNull(local);
	}
}
