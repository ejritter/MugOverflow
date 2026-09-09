using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Database;

public sealed class SqliteDatabaseServiceTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _fixture = new();

	public async Task InitializeAsync()
	{
		await _fixture.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		await _fixture.DisposeAsync();
	}

	[Fact]
	public async Task InitializeAsync_CreatesTablesAndEnablesWalMode()
	{
		var service = _fixture.DatabaseService;

		// 1. Verify journal_mode is WAL
		var journalMode = await service.GetJournalModeAsync();
		Assert.Equal("wal", journalMode, ignoreCase: true);

		// 2. Verify tables exist by querying them
		var logs = await service.GetAllLogsAsync();
		Assert.NotNull(logs);
		Assert.Empty(logs);

		var metadata = await service.GetMetadataAsync("NonExistentKey");
		Assert.Equal(string.Empty, metadata);
	}

	[Fact]
	public async Task InitializeAsync_IsIdempotent()
	{
		var service = _fixture.DatabaseService;

		// Re-initialize multiple times
		await service.InitializeAsync();
		await service.InitializeAsync();

		var journalMode = await service.GetJournalModeAsync();
		Assert.Equal("wal", journalMode, ignoreCase: true);
	}

	[Fact]
	public async Task InsertLogAsync_InsertsRecordWithPendingStatus()
	{
		var service = _fixture.DatabaseService;
		var testEntity = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "test-log-123",
			timestamp: new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
			lastModifiedUtc: new DateTime(2026, 8, 28, 12, 0, 1, DateTimeKind.Utc),
			decision: "YES",
			reason: "Complied with Oracle",
			caffeineAmountMg: 215.0,
			itemName: "Nitro Cold Brew",
			itemCategory: "nitro-cold-brew",
			isComplianceOverride: false,
			syncStatus: (int)SyncStatus.Pending);

		var rowsAffected = await service.InsertLogAsync(testEntity);
		Assert.Equal(1, rowsAffected);

		var fetched = await service.GetLogByIdAsync("test-log-123");
		Assert.NotNull(fetched);
		Assert.Equal("test-log-123", fetched.Id);
		Assert.Equal(new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc), fetched.Timestamp);
		Assert.Equal("YES", fetched.Decision);
		Assert.Equal("Complied with Oracle", fetched.Reason);
		Assert.Equal(215.0, fetched.CaffeineAmountMg);
		Assert.Equal("Nitro Cold Brew", fetched.ItemName);
		Assert.Equal("nitro-cold-brew", fetched.ItemCategory);
		Assert.False(fetched.IsComplianceOverride);
		Assert.Equal((int)SyncStatus.Pending, fetched.SyncStatus);
		Assert.Equal(new DateTime(2026, 8, 28, 12, 0, 1, DateTimeKind.Utc), fetched.LastModifiedUtc);
	}

	[Fact]
	public async Task GetLogByIdAsync_WhenRecordDoesNotExist_ReturnsNull()
	{
		var service = _fixture.DatabaseService;

		var nonExistent = await service.GetLogByIdAsync("non-existent-guid");
		Assert.Null(nonExistent);
	}

	[Fact]
	public async Task UpdateLogAsync_UpdatesPropertiesAndPersistsChanges()
	{
		var service = _fixture.DatabaseService;
		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "log-update-1",
			itemName: "Original Coffee",
			caffeineAmountMg: 95.0);
		await service.InsertLogAsync(entity);

		entity.ItemName = "Updated Espresso";
		entity.CaffeineAmountMg = 128.0;
		entity.LastModifiedUtc = DateTime.UtcNow.AddMinutes(5);
		entity.SyncStatus = (int)SyncStatus.Pending; // Marked dirty again

		var rowsAffected = await service.UpdateLogAsync(entity);
		Assert.Equal(1, rowsAffected);

		var fetched = await service.GetLogByIdAsync("log-update-1");
		Assert.NotNull(fetched);
		Assert.Equal("Updated Espresso", fetched.ItemName);
		Assert.Equal(128.0, fetched.CaffeineAmountMg);
		Assert.Equal((int)SyncStatus.Pending, fetched.SyncStatus);
	}

	[Fact]
	public async Task DeleteLogAsync_RemovesRecordSuccessfully()
	{
		var service = _fixture.DatabaseService;
		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(id: "log-delete-1");
		await service.InsertLogAsync(entity);

		var rowsAffected = await service.DeleteLogAsync("log-delete-1");
		Assert.Equal(1, rowsAffected);

		var fetched = await service.GetLogByIdAsync("log-delete-1");
		Assert.Null(fetched);
	}

	[Fact]
	public async Task DeleteLogAsync_WhenIdDoesNotExist_ReturnsZero()
	{
		var service = _fixture.DatabaseService;

		var rowsAffected = await service.DeleteLogAsync("unknown-id-12345");
		Assert.Equal(0, rowsAffected);
	}

	[Fact]
	public async Task UpsertLogAsync_WhenRecordIsNew_InsertsRecord()
	{
		var service = _fixture.DatabaseService;
		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(id: "upsert-new-1", itemName: "New Item");

		var rowsAffected = await service.UpsertLogAsync(entity);
		Assert.True(rowsAffected >= 1);

		var fetched = await service.GetLogByIdAsync("upsert-new-1");
		Assert.NotNull(fetched);
		Assert.Equal("New Item", fetched.ItemName);
	}

	[Fact]
	public async Task UpsertLogAsync_WhenRecordExists_UpdatesRecord()
	{
		var service = _fixture.DatabaseService;
		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(id: "upsert-existing-1", itemName: "Initial Item");
		await service.InsertLogAsync(entity);

		entity.ItemName = "Upserted Item";
		var rowsAffected = await service.UpsertLogAsync(entity);
		Assert.True(rowsAffected >= 1);

		var allLogs = await service.GetAllLogsAsync();
		Assert.Single(allLogs);
		Assert.Equal("Upserted Item", allLogs[0].ItemName);
	}

	[Fact]
	public async Task UpsertBatchAsync_InsertsMultipleNewRecordsInTransaction()
	{
		var service = _fixture.DatabaseService;
		var batch = SqliteTestDatabaseFixture.CreateSampleEntities(count: 50, syncStatus: (int)SyncStatus.Pending);

		var rowsAffected = await service.UpsertBatchAsync(batch);
		Assert.Equal(50, rowsAffected);

		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(50, allLogs.Count);
	}

	[Fact]
	public async Task UpsertBatchAsync_UpdatesExistingRecordsAndInsertsNewOnes()
	{
		var service = _fixture.DatabaseService;
		var initialBatch = SqliteTestDatabaseFixture.CreateSampleEntities(count: 10, syncStatus: (int)SyncStatus.Synced);
		await service.UpsertBatchAsync(initialBatch);

		// Modify first 5 records, keep IDs
		var updateBatch = new List<DecisionLogEntity>();
		for (int i = 0; i < 5; i++)
		{
			var existing = initialBatch[i];
			updateBatch.Add(new DecisionLogEntity
			{
				Id = existing.Id,
				Timestamp = existing.Timestamp,
				Decision = "YES",
				Reason = "Complied with Oracle",
				CaffeineAmountMg = 200.0,
				ItemName = $"Updated Item {i}",
				ItemCategory = "updated_type",
				IsComplianceOverride = false,
				SyncStatus = (int)SyncStatus.Pending,
				LastModifiedUtc = DateTime.UtcNow
			});
		}

		// Add 5 completely new records
		var newEntities = SqliteTestDatabaseFixture.CreateSampleEntities(count: 5, syncStatus: (int)SyncStatus.Pending);
		updateBatch.AddRange(newEntities);

		var rowsAffected = await service.UpsertBatchAsync(updateBatch);
		Assert.Equal(10, rowsAffected);

		var totalLogs = await service.GetAllLogsAsync();
		Assert.Equal(15, totalLogs.Count);

		var updatedItem0 = await service.GetLogByIdAsync(initialBatch[0].Id);
		Assert.NotNull(updatedItem0);
		Assert.Equal("Updated Item 0", updatedItem0.ItemName);
		Assert.Equal(200.0, updatedItem0.CaffeineAmountMg);
	}

	[Fact]
	public async Task UpsertBatchAsync_EmptyCollection_ReturnsZero()
	{
		var service = _fixture.DatabaseService;

		var rowsAffected = await service.UpsertBatchAsync([]);
		Assert.Equal(0, rowsAffected);
	}

	[Fact]
	public async Task GetPendingSyncLogsAsync_ReturnsOnlyDirtyRecords()
	{
		var service = _fixture.DatabaseService;

		var dirtyLogs = SqliteTestDatabaseFixture.CreateSampleEntities(count: 3, syncStatus: (int)SyncStatus.Pending);
		var cleanLogs = SqliteTestDatabaseFixture.CreateSampleEntities(count: 2, syncStatus: (int)SyncStatus.Synced);

		await service.UpsertBatchAsync(dirtyLogs);
		await service.UpsertBatchAsync(cleanLogs);

		var pending = await service.GetPendingSyncLogsAsync();
		Assert.Equal(3, pending.Count);
		Assert.All(pending, log => Assert.Equal((int)SyncStatus.Pending, log.SyncStatus));
	}

	[Fact]
	public async Task MarkLogsSyncedAsync_UpdatesSyncStatusToSyncedForSpecifiedIds()
	{
		var service = _fixture.DatabaseService;
		var dirtyLogs = SqliteTestDatabaseFixture.CreateSampleEntities(count: 5, syncStatus: (int)SyncStatus.Pending);
		await service.UpsertBatchAsync(dirtyLogs);

		var idsToMark = new[] { dirtyLogs[0].Id, dirtyLogs[1].Id };
		var updatedCount = await service.MarkLogsSyncedAsync(idsToMark);
		Assert.Equal(2, updatedCount);

		var remainingPending = await service.GetPendingSyncLogsAsync();
		Assert.Equal(3, remainingPending.Count);

		var item0 = await service.GetLogByIdAsync(dirtyLogs[0].Id);
		Assert.NotNull(item0);
		Assert.Equal((int)SyncStatus.Synced, item0.SyncStatus);
	}

	[Fact]
	public async Task MarkAllLogsSyncedAsync_UpdatesAllRecordsToSynced()
	{
		var service = _fixture.DatabaseService;
		var dirtyLogs = SqliteTestDatabaseFixture.CreateSampleEntities(count: 8, syncStatus: (int)SyncStatus.Pending);
		await service.UpsertBatchAsync(dirtyLogs);

		var rowsUpdated = await service.MarkAllLogsSyncedAsync();
		Assert.Equal(8, rowsUpdated);

		var remainingPending = await service.GetPendingSyncLogsAsync();
		Assert.Empty(remainingPending);

		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(8, allLogs.Count);
		Assert.All(allLogs, log => Assert.Equal((int)SyncStatus.Synced, log.SyncStatus));
	}

	[Fact]
	public async Task ClearAllLogsAsync_RemovesAllDecisionLogs()
	{
		var service = _fixture.DatabaseService;
		var logs = SqliteTestDatabaseFixture.CreateSampleEntities(count: 10);
		await service.UpsertBatchAsync(logs);

		await service.SetMetadataAsync("PreservedKey", "PreservedValue");

		var clearedCount = await service.ClearAllLogsAsync();
		Assert.Equal(10, clearedCount);

		var remainingLogs = await service.GetAllLogsAsync();
		Assert.Empty(remainingLogs);

		// Verify metadata was not cleared by ClearAllLogsAsync
		var metaVal = await service.GetMetadataAsync("PreservedKey");
		Assert.Equal("PreservedValue", metaVal);
	}

	[Fact]
	public async Task GetAndSetMetadataAsync_ManagesKeyValuePairs()
	{
		var service = _fixture.DatabaseService;

		// 1. Default value when missing
		var missing = await service.GetMetadataAsync("MissingKey", defaultValue: "Default");
		Assert.Equal("Default", missing);

		// 2. Set key
		await service.SetMetadataAsync("ActiveProvider", "GoogleDrive");
		var val = await service.GetMetadataAsync("ActiveProvider");
		Assert.Equal("GoogleDrive", val);

		// 3. Update key
		await service.SetMetadataAsync("ActiveProvider", "OneDrive");
		var updatedVal = await service.GetMetadataAsync("ActiveProvider");
		Assert.Equal("OneDrive", updatedVal);
	}

	[Fact]
	public async Task CloseConnectionAsync_ReleasesFileLock_AllowsFileDeletion()
	{
		// Create a separate isolated fixture instance
		var isolatedFixture = new SqliteTestDatabaseFixture();
		await isolatedFixture.InitializeAsync();

		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(id: "file-lock-test-1");
		await isolatedFixture.DatabaseService.InsertLogAsync(entity);

		// Act: explicitly close connection
		await isolatedFixture.DatabaseService.CloseConnectionAsync();

		// Clean up GC handles
		GC.Collect();
		GC.WaitForPendingFinalizers();

		// Assert: File can be deleted immediately on Windows without IOException
		var exception = Record.Exception(() =>
		{
			if (File.Exists(isolatedFixture.DatabasePath))
				File.Delete(isolatedFixture.DatabasePath);
		});

		Assert.Null(exception);
	}

	[Fact]
	public void DecisionLogEntity_FromAndToItem_PreservesAllFields()
	{
		var item = new DecisionLogItem
		{
			Id = "custom-item-id-1",
			Timestamp = new DateTime(2026, 8, 28, 10, 30, 0, DateTimeKind.Utc),
			Decision = "YES",
			Reason = "Energy boost",
			CaffeineAmountMg = 150.0,
			ItemName = "Cold Brew",
			ItemCategory = "cold_brew",
			IsComplianceOverride = false
		};

		var entity = DecisionLogEntity.FromItem(item, SyncStatus.Synced);
		Assert.Equal("custom-item-id-1", entity.Id);
		Assert.Equal(item.Timestamp, entity.Timestamp);
		Assert.Equal("YES", entity.Decision);
		Assert.Equal("Energy boost", entity.Reason);
		Assert.Equal(150.0, entity.CaffeineAmountMg);
		Assert.Equal("Cold Brew", entity.ItemName);
		Assert.Equal("cold_brew", entity.ItemCategory);
		Assert.False(entity.IsComplianceOverride);
		Assert.Equal((int)SyncStatus.Synced, entity.SyncStatus);

		var backToItem = entity.ToItem();
		Assert.Equal(item.Id, backToItem.Id);
		Assert.Equal(item.Timestamp, backToItem.Timestamp);
		Assert.Equal(item.Decision, backToItem.Decision);
		Assert.Equal(item.Reason, backToItem.Reason);
		Assert.Equal(item.CaffeineAmountMg, backToItem.CaffeineAmountMg);
		Assert.Equal(item.ItemName, backToItem.ItemName);
		Assert.Equal(item.ItemCategory, backToItem.ItemCategory);
		Assert.Equal(item.IsComplianceOverride, backToItem.IsComplianceOverride);
	}

	[Fact]
	public void DecisionLogEntity_FromAndToOracleLog_PreservesFieldsAndCalculatedState()
	{
		var oracleLog = new OracleDecisionLog
		{
			Id = 1724859000123L,
			Timestamp = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc),
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "espresso_single",
			CoffeeName = "Single Espresso",
			CaffeineMg = 63
		};

		var entity = DecisionLogEntity.FromOracleLog(oracleLog, SyncStatus.Pending);
		Assert.Equal("1724859000123", entity.Id);
		Assert.Equal(oracleLog.Timestamp, entity.Timestamp);
		Assert.Equal("YES", entity.Decision);
		Assert.Equal(63.0, entity.CaffeineAmountMg);
		Assert.Equal("Single Espresso", entity.ItemName);
		Assert.Equal("espresso_single", entity.ItemCategory);
		Assert.False(entity.IsComplianceOverride);
		Assert.Equal((int)SyncStatus.Pending, entity.SyncStatus);

		var backToOracle = entity.ToOracleLog();
		Assert.Equal(oracleLog.Id, backToOracle.Id);
		Assert.Equal(oracleLog.Timestamp, backToOracle.Timestamp);
		Assert.True(backToOracle.OracleSaidYes);
		Assert.True(backToOracle.UserDrank);
		Assert.True(backToOracle.FollowedAdvice);
		Assert.Equal("espresso_single", backToOracle.CoffeeTypeId);
		Assert.Equal("Single Espresso", backToOracle.CoffeeName);
		Assert.Equal(63, backToOracle.CaffeineMg);
	}

	[Fact]
	public void SyncMetadataEntity_FromAndAsDateTime_RoundTripsIso8601()
	{
		var nowUtc = new DateTime(2026, 8, 28, 15, 45, 30, DateTimeKind.Utc);
		var meta = SyncMetadataEntity.FromDateTime(SyncMetadataKeys.LastSuccessfulSyncUtc, nowUtc);

		Assert.Equal(SyncMetadataKeys.LastSuccessfulSyncUtc, meta.Key);
		Assert.NotNull(meta.Value);

		var parsed = meta.AsDateTime();
		Assert.NotNull(parsed);
		Assert.Equal(nowUtc, parsed.Value);
	}

	[Fact]
	public void StoragePayload_ToJsonAndFromJson_RoundTripsPayloadCorrectly()
	{
		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "payload-test-1",
					Timestamp = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc),
					LastModifiedUtc = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc),
					Decision = "YES",
					Reason = "Complied with Oracle",
					CaffeineAmountMg = 95.0,
					ItemName = "Drip Coffee",
					ItemCategory = "drip",
					IsComplianceOverride = false,
					SyncStatus = (int)SyncStatus.Synced
				}
			]
		};

		var json = payload.ToJson();
		Assert.Contains("\"appVersion\": \"1.0.0\"", json);
		Assert.Contains("\"decisionLogs\":", json);

		var deserialized = StoragePayload.FromJson(json);
		Assert.NotNull(deserialized);
		Assert.Equal("1.0.0", deserialized.AppVersion);
		Assert.Equal("1.0.0", deserialized.SchemaVersion);
		Assert.Single(deserialized.DecisionLogs);
		Assert.Equal("payload-test-1", deserialized.DecisionLogs[0].Id);
		Assert.Equal(95.0, deserialized.DecisionLogs[0].CaffeineAmountMg);
	}
}
