using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Tests.Fixtures;
using SQLite;
using System.Text.Json;
using Xunit;

namespace MugOverflowProject.Tests.Database;

/// <summary>
/// Adversarial stress harness and empirical challenge suite for Milestone 1 (SQLite Persistence Layer).
/// Rigorously tests edge cases: large payloads, special characters, SQL injection patterns,
/// corrupt metadata, WAL mode pragma validation, and high-concurrency thread safety.
/// </summary>
public sealed class SqliteDatabaseAdversarialTests : IAsyncLifetime
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

	#region Dimension 1: WAL Mode Pragma Validation & Direct Inspection

	[Fact]
	public async Task WalMode_IsActiveAndEnforcedOnUnderlyingDatabase()
	{
		var service = _fixture.DatabaseService;

		// 1. Service reports WAL mode
		var mode = await service.GetJournalModeAsync();
		Assert.Equal("wal", mode, ignoreCase: true);

		// 2. Direct connection verifies PRAGMA journal_mode is WAL
		var directConn = _fixture.CreateDirectConnection();
		try
		{
			var directMode = await directConn.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
			Assert.Equal("wal", directMode, ignoreCase: true);
		}
		finally
		{
			await directConn.CloseAsync();
		}
	}

	[Fact]
	public async Task WalMode_ConcurrentReadsDoNotBlockDuringTransaction()
	{
		var service = _fixture.DatabaseService;
		await service.UpsertBatchAsync(SqliteTestDatabaseFixture.CreateSampleEntities(count: 20));

		// Start a direct connection that reads while the service writes
		var directConn = _fixture.CreateDirectConnection();
		try
		{
			var writeTask = Task.Run(async () =>
			{
				var batch = SqliteTestDatabaseFixture.CreateSampleEntities(count: 100);
				return await service.UpsertBatchAsync(batch);
			});

			var readTask = Task.Run(async () =>
			{
				var logs = await directConn.Table<DecisionLogEntity>().ToListAsync();
				return logs.Count;
			});

			await Task.WhenAll(writeTask, readTask);

			Assert.Equal(100, await writeTask);
			Assert.True(await readTask >= 20); // Successfully read without locking exception
		}
		finally
		{
			await directConn.CloseAsync();
		}
	}

	#endregion

	#region Dimension 2: Large Payloads & Stress Scale

	[Fact]
	public async Task LargePayload_BatchUpsert2000Entities_ExecutesWithinTransaction()
	{
		var service = _fixture.DatabaseService;
		const int entityCount = 2000;
		var largeBatch = SqliteTestDatabaseFixture.CreateSampleEntities(entityCount);

		var count = await service.UpsertBatchAsync(largeBatch);
		Assert.Equal(entityCount, count);

		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(entityCount, allLogs.Count);
	}

	[Fact]
	public async Task LargePayload_MassiveStringValues_PersistsAndRetrievesIntact()
	{
		var service = _fixture.DatabaseService;

		// 50 KB Reason string, 10 KB ItemName
		string massiveReason = new('A', 50_000);
		string massiveItemName = new('B', 10_000);
		string massiveCategory = new('C', 5_000);

		var entity = new DecisionLogEntity
		{
			Id = "massive-payload-log-1",
			Timestamp = DateTime.UtcNow,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = "YES",
			Reason = massiveReason,
			CaffeineAmountMg = 450.5,
			ItemName = massiveItemName,
			ItemCategory = massiveCategory,
			IsComplianceOverride = true,
			SyncStatus = (int)SyncStatus.Pending
		};

		var insertResult = await service.InsertLogAsync(entity);
		Assert.Equal(1, insertResult);

		var retrieved = await service.GetLogByIdAsync("massive-payload-log-1");
		Assert.NotNull(retrieved);
		Assert.Equal(massiveReason, retrieved.Reason);
		Assert.Equal(massiveItemName, retrieved.ItemName);
		Assert.Equal(massiveCategory, retrieved.ItemCategory);
		Assert.Equal(450.5, retrieved.CaffeineAmountMg);
	}

	[Fact]
	public async Task LargePayload_MassiveMetadataString_PersistsAndRetrieves()
	{
		var service = _fixture.DatabaseService;
		string massiveValue = new('X', 100_000);

		await service.SetMetadataAsync("MassiveKey", massiveValue);
		var retrieved = await service.GetMetadataAsync("MassiveKey");

		Assert.Equal(massiveValue, retrieved);
		Assert.Equal(100_000, retrieved.Length);
	}

	[Fact]
	public void LargePayload_StoragePayload_JsonRoundTripWith1000Entities()
	{
		var entities = SqliteTestDatabaseFixture.CreateSampleEntities(1000);
		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = DateTime.UtcNow,
			DecisionLogs = entities
		};

		var json = payload.ToJson();
		Assert.False(string.IsNullOrWhiteSpace(json));

		var deserialized = StoragePayload.FromJson(json);
		Assert.NotNull(deserialized);
		Assert.Equal(1000, deserialized.DecisionLogs.Count);
		Assert.Equal(entities[500].Id, deserialized.DecisionLogs[500].Id);
	}

	#endregion

	#region Dimension 3: Adversarial Input, Special Characters, SQL Injection & Unicode

	[Theory]
	[InlineData("Robert'); DROP TABLE decision_logs; --")]
	[InlineData("' OR '1'='1' --")]
	[InlineData("UNION SELECT 1, 'admin', datetime('now'), 'YES', 'Pwned', 999, 'Hacked', 'exploit', 0, 0 --")]
	[InlineData("DoubleQuotes\"\"And''SingleQuotes`Backticks`")]
	[InlineData("Emojis: ☕️ 🔥 ⚡️ 💀 🚀 \ud83d\ude00 \ud83d\udca9")]
	[InlineData("MultiLanguage: \u041f\u0440\u0438\u0432\u0435\u0442 \u4e16\u754c \u0645\u0631\u062d\u0627 \u05e9\u05dc\u05d5\u05dd")]
	[InlineData("WhiteSpaces:\r\n\t\t\n  End")]
	public async Task AdversarialInput_SpecialCharactersInFields_SafelyStoredWithoutSqlInjection(string injectionOrUnicode)
	{
		var service = _fixture.DatabaseService;
		string id = $"id-{Guid.NewGuid():N}";

		var entity = new DecisionLogEntity
		{
			Id = id,
			Timestamp = DateTime.UtcNow,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = injectionOrUnicode,
			Reason = injectionOrUnicode,
			CaffeineAmountMg = 120.0,
			ItemName = injectionOrUnicode,
			ItemCategory = injectionOrUnicode,
			IsComplianceOverride = false,
			SyncStatus = (int)SyncStatus.Pending
		};

		// 1. Insert entity with hostile string
		var inserted = await service.InsertLogAsync(entity);
		Assert.Equal(1, inserted);

		// 2. Retrieve entity and verify table was not dropped or corrupted
		var retrieved = await service.GetLogByIdAsync(id);
		Assert.NotNull(retrieved);
		Assert.Equal(injectionOrUnicode, retrieved.Decision);
		Assert.Equal(injectionOrUnicode, retrieved.Reason);
		Assert.Equal(injectionOrUnicode, retrieved.ItemName);
		Assert.Equal(injectionOrUnicode, retrieved.ItemCategory);

		// 3. Mark synced with hostile ID
		var synced = await service.MarkLogsSyncedAsync([id]);
		Assert.Equal(1, synced);

		// 4. Update entity with hostile string
		entity.Reason = "Updated: " + injectionOrUnicode;
		var updated = await service.UpdateLogAsync(entity);
		Assert.Equal(1, updated);

		// 5. Delete entity
		var deleted = await service.DeleteLogAsync(id);
		Assert.Equal(1, deleted);
	}

	[Theory]
	[InlineData("id-with-'quotes'-and-';drop-table")]
	[InlineData("id-with-\"double\"-quotes")]
	[InlineData("id with spaces and symbols !@#$%^&*()_+={}|[]\\:\";'<>?,./~`")]
	[InlineData("id-with-unicode-\u041a\u043e\u0444\u0435-\u2615")]
	public async Task AdversarialInput_HostileIdsInMarkLogsSynced_HandledSafely(string hostileId)
	{
		var service = _fixture.DatabaseService;

		var entity = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: hostileId,
			syncStatus: (int)SyncStatus.Pending);

		await service.InsertLogAsync(entity);

		// Mark logs synced via parameterized SQL
		var rowsMarked = await service.MarkLogsSyncedAsync([hostileId]);
		Assert.Equal(1, rowsMarked);

		var fetched = await service.GetLogByIdAsync(hostileId);
		Assert.NotNull(fetched);
		Assert.Equal((int)SyncStatus.Synced, fetched.SyncStatus);
	}

	[Fact]
	public async Task AdversarialInput_SpecialCharactersInMetadataKeysAndValues()
	{
		var service = _fixture.DatabaseService;

		string hostileKey = "Key'; DROP TABLE sync_metadata; --";
		string hostileVal = "Value' OR '1'='1' \r\n\t <tag>json:{\"a\":1}</tag>";

		await service.SetMetadataAsync(hostileKey, hostileVal);

		var fetched = await service.GetMetadataAsync(hostileKey);
		Assert.Equal(hostileVal, fetched);

		// Verify table still exists
		var keys = await service.GetMetadataAsync(SyncMetadataKeys.ActiveProvider, defaultValue: "fallback");
		Assert.Equal("fallback", keys);
	}

	#endregion

	#region Dimension 4: Corrupt Metadata, Deserialization Robustness & Null/Boundary Handling

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-a-datetime")]
	[InlineData("2026-13-45T99:99:99Z")]
	[InlineData("0000000000")]
	[InlineData("{\"date\": \"2026-08-28\"}")]
	public void CorruptMetadata_AsDateTime_ReturnsNullOnMalformedInput(string? malformedValue)
	{
		var meta = new SyncMetadataEntity
		{
			Key = "CorruptKey",
			Value = malformedValue!
		};

		var parsed = meta.AsDateTime();
		Assert.Null(parsed);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("{invalid json")]
	[InlineData("{\"appVersion\": 123}")] // Type mismatch
	[InlineData("[1, 2, 3]")]
	[InlineData("\"just a string\"")]
	public void StoragePayload_FromJson_ReturnsNullOnCorruptedJson(string? corruptJson)
	{
		var payload = StoragePayload.FromJson(corruptJson!);
		Assert.Null(payload);
	}

	[Fact]
	public void StoragePayload_FromJson_ToleratesMissingOrExtraFields()
	{
		// Payload with missing schemaVersion and extra unknown properties
		string jsonWithExtras = """
		{
			"appVersion": "2.0.0",
			"extraField": "should be ignored",
			"decisionLogs": [
				{
					"id": "item-with-extras",
					"decision": "YES",
					"extraNested": 42
				}
			]
		}
		""";

		var payload = StoragePayload.FromJson(jsonWithExtras);
		Assert.NotNull(payload);
		Assert.Equal("2.0.0", payload.AppVersion);
		Assert.Single(payload.DecisionLogs);
		Assert.Equal("item-with-extras", payload.DecisionLogs[0].Id);
		Assert.Equal("YES", payload.DecisionLogs[0].Decision);
	}

	[Theory]
	[InlineData(-1L, true, true, 0.0)]
	[InlineData(0L, false, false, 50.0)]
	[InlineData(long.MaxValue, true, false, 95.0)]
	public void DecisionLogEntity_FromOracleLog_HandlesBoundaryIdsAndComplianceStates(
		long oracleId, bool oracleSaidYes, bool followedAdvice, double caffeineMg)
	{
		var oracleLog = new OracleDecisionLog
		{
			Id = oracleId,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = oracleSaidYes,
			UserDrank = caffeineMg > 0,
			FollowedAdvice = followedAdvice,
			CoffeeTypeId = "espresso",
			CoffeeName = "Espresso Shot",
			CaffeineMg = (int)caffeineMg
		};

		var entity = DecisionLogEntity.FromOracleLog(oracleLog);
		Assert.Equal(oracleId.ToString(), entity.Id);
		Assert.Equal(oracleSaidYes ? "YES" : "NO", entity.Decision);
		Assert.Equal(!followedAdvice, entity.IsComplianceOverride);

		var backToOracle = entity.ToOracleLog();
		Assert.Equal(oracleId, backToOracle.Id);
		Assert.Equal(oracleSaidYes, backToOracle.OracleSaidYes);
		Assert.Equal(followedAdvice, backToOracle.FollowedAdvice);
	}

	[Fact]
	public async Task BoundaryHandling_NullAndEmptyParameters_ThrowsOrReturnsDefaultsSafely()
	{
		var service = _fixture.DatabaseService;

		// Null entity throws ArgumentNullException
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.InsertLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpsertLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpsertBatchAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.MarkLogsSyncedAsync(null!));

		// Empty/null string keys in metadata
		await Assert.ThrowsAsync<ArgumentException>(() => service.SetMetadataAsync("", "val"));
		await Assert.ThrowsAsync<ArgumentException>(() => service.SetMetadataAsync("  ", "val"));

		var defaultVal = await service.GetMetadataAsync("", defaultValue: "fallback");
		Assert.Equal("fallback", defaultVal);

		// Non-existent or empty string IDs
		var nullLog = await service.GetLogByIdAsync("");
		Assert.Null(nullLog);

		var delCount = await service.DeleteLogAsync("");
		Assert.Equal(0, delCount);

		var markEmpty = await service.MarkLogsSyncedAsync([]);
		Assert.Equal(0, markEmpty);
	}

	#endregion

	#region Dimension 5: High-Concurrency & Thread Safety Stress Harness

	[Fact]
	public async Task Concurrency_RapidParallelOperations_ExecuteWithoutDeadlocksOrCorruption()
	{
		var service = _fixture.DatabaseService;
		const int concurrentTasks = 40;
		var tasks = new List<Task>();

		for (int i = 0; i < concurrentTasks; i++)
		{
			int index = i;
			tasks.Add(Task.Run(async () =>
			{
				string logId = $"concurrent-log-{index}";

				// 1. Insert log
				var entity = SqliteTestDatabaseFixture.CreateSampleEntity(
					id: logId,
					itemName: $"Coffee {index}",
					syncStatus: (int)SyncStatus.Pending);
				await service.InsertLogAsync(entity);

				// 2. Set metadata
				await service.SetMetadataAsync($"ThreadKey_{index}", $"ThreadVal_{index}");

				// 3. Update log
				entity.CaffeineAmountMg = 100.0 + index;
				await service.UpdateLogAsync(entity);

				// 4. Query pending
				var pending = await service.GetPendingSyncLogsAsync();
				Assert.NotNull(pending);

				// 5. Read metadata
				var val = await service.GetMetadataAsync($"ThreadKey_{index}");
				Assert.Equal($"ThreadVal_{index}", val);

				// 6. Mark synced
				await service.MarkLogsSyncedAsync([logId]);

				// 7. Get by ID
				var fetched = await service.GetLogByIdAsync(logId);
				Assert.NotNull(fetched);
				Assert.Equal((int)SyncStatus.Synced, fetched.SyncStatus);
			}));
		}

		await Task.WhenAll(tasks);

		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(concurrentTasks, allLogs.Count);
		Assert.All(allLogs, l => Assert.Equal((int)SyncStatus.Synced, l.SyncStatus));
	}

	[Fact]
	public async Task Concurrency_ParallelBatchUpsertsAndQueries_MaintainConsistency()
	{
		var service = _fixture.DatabaseService;
		const int batchesCount = 10;
		const int batchSize = 50;

		var tasks = Enumerable.Range(0, batchesCount).Select(batchIdx => Task.Run(async () =>
		{
			var batch = Enumerable.Range(0, batchSize).Select(itemIdx => new DecisionLogEntity
			{
				Id = $"batch-{batchIdx}-item-{itemIdx}",
				Timestamp = DateTime.UtcNow,
				LastModifiedUtc = DateTime.UtcNow,
				Decision = "YES",
				Reason = "Concurrency batch test",
				CaffeineAmountMg = 95.0,
				ItemName = $"Batch Item {batchIdx}_{itemIdx}",
				ItemCategory = "drip",
				IsComplianceOverride = false,
				SyncStatus = (int)SyncStatus.Pending
			}).ToList();

			await service.UpsertBatchAsync(batch);
		})).ToList();

		await Task.WhenAll(tasks);

		var totalLogs = await service.GetAllLogsAsync();
		Assert.Equal(batchesCount * batchSize, totalLogs.Count);
	}

	[Fact]
	public async Task Concurrency_ParallelInitializeAsync_ExecutesSafelyViaSemaphore()
	{
		// Test multiple parallel initializations on fresh uninitialized service instance
		var testPath = Path.Combine(Path.GetTempPath(), $"ParallelInitTest_{Guid.NewGuid():N}.db3");
		var freshService = new SqliteDatabaseService(testPath);

		try
		{
			var initTasks = Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
			{
				await freshService.InitializeAsync();
			}));

			await Task.WhenAll(initTasks);

			var journalMode = await freshService.GetJournalModeAsync();
			Assert.Equal("wal", journalMode, ignoreCase: true);

			var logs = await freshService.GetAllLogsAsync();
			Assert.Empty(logs);
		}
		finally
		{
			await freshService.CloseConnectionAsync();
			if (File.Exists(testPath))
			{
				File.Delete(testPath);
			}
		}
	}

	#endregion
}
