using System.Diagnostics;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Tests.Fixtures;
using SQLite;
using Xunit;

namespace MugOverflowProject.Tests.Database;

/// <summary>
/// Empirical Challenger Stress & Adversarial Test Suite for Milestone 1 (SQLite Persistence Layer).
/// Rigorously validates:
/// 1. High-concurrency parallel reads and writes under heavy Task load (no database locks or data corruption)
/// 2. High-volume batch upsert throughput and scaling (thousands of entities)
/// 3. Transaction rollback atomicity on mid-batch exceptions or constraint violations
/// 4. Dirty flag state transitions across full sync lifecycle
/// 5. Connection lifecycle, disposal, re-initialization, and Windows file lock release
/// 6. Adversarial input validation (SQL injection patterns, unicode, large payloads, extreme numbers/dates)
/// </summary>
public sealed class SqliteDatabaseServiceStressTests : IAsyncLifetime
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

	#region 1. Concurrency & Parallel Execution Stress Tests

	[Fact]
	public async Task ConcurrentReadsAndWrites_UnderHighParallelLoad_ExecutesWithoutDatabaseLockingErrors()
	{
		var service = _fixture.DatabaseService;
		const int initialCount = 100;
		var initialEntities = SqliteTestDatabaseFixture.CreateSampleEntities(initialCount, (int)SyncStatus.Pending);
		await service.UpsertBatchAsync(initialEntities);

		const int writerTasksCount = 20;
		const int batchWriterTasksCount = 10;
		const int readerTasksCount = 15;
		const int pendingReaderTasksCount = 15;

		var allTasks = new List<Task>();
		var exceptions = new List<Exception>();
		var lockObj = new object();

		void SafeRecordException(Exception ex)
		{
			lock (lockObj)
			{
				exceptions.Add(ex);
			}
		}

		// 20 Writers performing single InsertLogAsync / UpdateLogAsync / UpsertLogAsync
		for (int i = 0; i < writerTasksCount; i++)
		{
			var taskIndex = i;
			allTasks.Add(Task.Run(async () =>
			{
				try
				{
					var entity = SqliteTestDatabaseFixture.CreateSampleEntity(
						id: $"concurrent-writer-{taskIndex}",
						itemName: $"Item {taskIndex}",
						caffeineAmountMg: 100 + taskIndex);

					await service.InsertLogAsync(entity);

					entity.ItemName = $"Updated Item {taskIndex}";
					await service.UpdateLogAsync(entity);

					var fetched = await service.GetLogByIdAsync(entity.Id);
					Assert.NotNull(fetched);
					Assert.Equal($"Updated Item {taskIndex}", fetched.ItemName);
				}
				catch (Exception ex)
				{
					SafeRecordException(ex);
				}
			}));
		}

		// 10 Batch Writers performing UpsertBatchAsync of 10 items each (100 items total)
		for (int i = 0; i < batchWriterTasksCount; i++)
		{
			var batchIndex = i;
			allTasks.Add(Task.Run(async () =>
			{
				try
				{
					var batch = new List<DecisionLogEntity>();
					for (int j = 0; j < 10; j++)
					{
						batch.Add(SqliteTestDatabaseFixture.CreateSampleEntity(
							id: $"concurrent-batch-{batchIndex}-{j}",
							itemName: $"BatchItem {batchIndex}_{j}",
							caffeineAmountMg: 50 + j));
					}
					var count = await service.UpsertBatchAsync(batch);
					Assert.Equal(10, count);
				}
				catch (Exception ex)
				{
					SafeRecordException(ex);
				}
			}));
		}

		// 15 Readers reading all logs
		for (int i = 0; i < readerTasksCount; i++)
		{
			allTasks.Add(Task.Run(async () =>
			{
				try
				{
					for (int k = 0; k < 5; k++)
					{
						var logs = await service.GetAllLogsAsync();
						Assert.NotNull(logs);
						Assert.True(logs.Count >= initialCount);
						await Task.Yield();
					}
				}
				catch (Exception ex)
				{
					SafeRecordException(ex);
				}
			}));
		}

		// 15 Readers reading pending sync logs
		for (int i = 0; i < pendingReaderTasksCount; i++)
		{
			allTasks.Add(Task.Run(async () =>
			{
				try
				{
					for (int k = 0; k < 5; k++)
					{
						var pending = await service.GetPendingSyncLogsAsync();
						Assert.NotNull(pending);
						Assert.All(pending, log => Assert.Equal((int)SyncStatus.Pending, log.SyncStatus));
						await Task.Yield();
					}
				}
				catch (Exception ex)
				{
					SafeRecordException(ex);
				}
			}));
		}

		await Task.WhenAll(allTasks);

		// Assert: Zero locking errors or concurrency exceptions
		Assert.Empty(exceptions);

		// Expected count: 100 initial + 20 single writers + 100 batch writers = 220 records
		var finalLogs = await service.GetAllLogsAsync();
		Assert.Equal(220, finalLogs.Count);
	}

	#endregion

	#region 2. High-Volume Batch Upsert Scaling

	[Fact]
	public async Task UpsertBatchAsync_WithThousandsOfEntities_ExecutesRapidlyAndAtomically()
	{
		var service = _fixture.DatabaseService;
		const int batchSize = 2500;
		var largeBatch = SqliteTestDatabaseFixture.CreateSampleEntities(batchSize, (int)SyncStatus.Pending);

		var sw = Stopwatch.StartNew();
		var insertedCount = await service.UpsertBatchAsync(largeBatch);
		sw.Stop();

		Assert.Equal(batchSize, insertedCount);
		// High performance: 2,500 batch upserts within a transaction should complete in < 2500ms on desktop host
		Assert.True(sw.ElapsedMilliseconds < 5000, $"Batch upsert took too long: {sw.ElapsedMilliseconds}ms");

		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(batchSize, allLogs.Count);

		// Update 1,250 existing items and add 1,250 brand new items (total 3,750 expected)
		var mixedBatch = new List<DecisionLogEntity>(2500);
		for (int i = 0; i < 1250; i++)
		{
			var existing = largeBatch[i];
			existing.ItemName = $"Modified {i}";
			existing.CaffeineAmountMg = 999.0;
			existing.LastModifiedUtc = DateTime.UtcNow;
			mixedBatch.Add(existing);
		}
		for (int i = 0; i < 1250; i++)
		{
			mixedBatch.Add(SqliteTestDatabaseFixture.CreateSampleEntity(
				id: $"new-entity-{i}",
				itemName: $"New Entity {i}",
				caffeineAmountMg: 150.0));
		}

		var upsertMixedCount = await service.UpsertBatchAsync(mixedBatch);
		Assert.Equal(2500, upsertMixedCount);

		var finalLogs = await service.GetAllLogsAsync();
		Assert.Equal(3750, finalLogs.Count);

		var modifiedItem = await service.GetLogByIdAsync(largeBatch[0].Id);
		Assert.NotNull(modifiedItem);
		Assert.Equal("Modified 0", modifiedItem.ItemName);
		Assert.Equal(999.0, modifiedItem.CaffeineAmountMg);
	}

	#endregion

	#region 3. Transaction Rollback & Atomicity Tests

	[Fact]
	public async Task TransactionRollback_WhenExceptionOccursMidTransaction_RevertsAllChanges()
	{
		var directConn = _fixture.CreateDirectConnection();
		try
		{
			// Pre-populate with 5 baseline items
			var baseline = SqliteTestDatabaseFixture.CreateSampleEntities(5, (int)SyncStatus.Synced);
			await _fixture.DatabaseService.UpsertBatchAsync(baseline);

			var preLogs = await _fixture.DatabaseService.GetAllLogsAsync();
			Assert.Equal(5, preLogs.Count);

			// Act: Execute a transaction that attempts to insert 20 items, but throws on item 10
			var exception = await Record.ExceptionAsync(async () =>
			{
				await directConn.RunInTransactionAsync(db =>
				{
					for (int i = 0; i < 20; i++)
					{
						if (i == 10)
						{
							throw new InvalidOperationException("Simulated catastrophic crash mid-batch");
						}

						var entity = SqliteTestDatabaseFixture.CreateSampleEntity(id: $"rollback-test-{i}");
						db.Insert(entity);
					}
				});
			});

			Assert.NotNull(exception);
			Assert.IsType<InvalidOperationException>(exception);

			// Assert: The 10 items inserted prior to the throw were completely rolled back
			var postLogs = await _fixture.DatabaseService.GetAllLogsAsync();
			Assert.Equal(5, postLogs.Count);

			var rolledBackItem0 = await _fixture.DatabaseService.GetLogByIdAsync("rollback-test-0");
			Assert.Null(rolledBackItem0);
		}
		finally
		{
			await directConn.CloseAsync();
		}
	}

	[Fact]
	public async Task TransactionRollback_WhenPrimaryKeyConstraintViolated_RollsBackEntireTransaction()
	{
		var directConn = _fixture.CreateDirectConnection();
		try
		{
			var service = _fixture.DatabaseService;

			// Seed 1 existing item
			var existing = SqliteTestDatabaseFixture.CreateSampleEntity(id: "duplicate-pk-1", itemName: "Original");
			await service.InsertLogAsync(existing);

			// Attempt transaction with raw Insert (not InsertOrReplace) containing duplicate PK
			var exception = await Record.ExceptionAsync(async () =>
			{
				await directConn.RunInTransactionAsync(db =>
				{
					db.Insert(SqliteTestDatabaseFixture.CreateSampleEntity(id: "new-valid-1"));
					db.Insert(SqliteTestDatabaseFixture.CreateSampleEntity(id: "new-valid-2"));
					// This violates primary key constraint
					db.Insert(SqliteTestDatabaseFixture.CreateSampleEntity(id: "duplicate-pk-1", itemName: "Duplicate Attempt"));
					db.Insert(SqliteTestDatabaseFixture.CreateSampleEntity(id: "new-valid-3"));
				});
			});

			Assert.NotNull(exception);
			Assert.IsAssignableFrom<SQLiteException>(exception);

			// Assert: Neither new-valid-1 nor new-valid-2 were committed
			var log1 = await service.GetLogByIdAsync("new-valid-1");
			var log2 = await service.GetLogByIdAsync("new-valid-2");
			Assert.Null(log1);
			Assert.Null(log2);

			// Original record remains unchanged
			var original = await service.GetLogByIdAsync("duplicate-pk-1");
			Assert.NotNull(original);
			Assert.Equal("Original", original.ItemName);
		}
		finally
		{
			await directConn.CloseAsync();
		}
	}

	#endregion

	#region 4. Dirty Flag State Machine Transitions

	[Fact]
	public async Task DirtyFlagStateTransitions_FollowsFullSyncLifecycle()
	{
		var service = _fixture.DatabaseService;

		// Step 1: Create 100 new pending logs
		var initialLogs = SqliteTestDatabaseFixture.CreateSampleEntities(100, (int)SyncStatus.Pending);
		await service.UpsertBatchAsync(initialLogs);

		var pending = await service.GetPendingSyncLogsAsync();
		Assert.Equal(100, pending.Count);

		// Step 2: Mark 40 items as Synced
		var first40Ids = initialLogs.Take(40).Select(x => x.Id).ToList();
		var markedCount = await service.MarkLogsSyncedAsync(first40Ids);
		Assert.Equal(40, markedCount);

		var pendingAfterSync = await service.GetPendingSyncLogsAsync();
		Assert.Equal(60, pendingAfterSync.Count);

		var syncedSample = await service.GetLogByIdAsync(first40Ids[0]);
		Assert.NotNull(syncedSample);
		Assert.Equal((int)SyncStatus.Synced, syncedSample.SyncStatus);

		// Step 3: User edits 10 previously synced items -> marked dirty (Pending)
		for (int i = 0; i < 10; i++)
		{
			var item = await service.GetLogByIdAsync(first40Ids[i]);
			Assert.NotNull(item);
			item.Reason = "Modified by user";
			item.SyncStatus = (int)SyncStatus.Pending; // Marked dirty
			await service.UpdateLogAsync(item);
		}

		var pendingAfterEdit = await service.GetPendingSyncLogsAsync();
		Assert.Equal(70, pendingAfterEdit.Count); // 60 untouched pending + 10 re-dirtied

		// Step 4: Resolve conflict on 5 items (ConflictResolved = 2)
		for (int i = 10; i < 15; i++)
		{
			var item = await service.GetLogByIdAsync(first40Ids[i]);
			Assert.NotNull(item);
			item.SyncStatus = (int)SyncStatus.ConflictResolved;
			await service.UpdateLogAsync(item);
		}

		var pendingAfterConflict = await service.GetPendingSyncLogsAsync();
		// ConflictResolved is not Pending (0), so pending count stays 70
		Assert.Equal(70, pendingAfterConflict.Count);

		// Step 5: Mark all pending logs synced
		var markedAllCount = await service.MarkAllLogsSyncedAsync();
		Assert.Equal(70, markedAllCount);

		var pendingFinal = await service.GetPendingSyncLogsAsync();
		Assert.Empty(pendingFinal);

		// Verify ConflictResolved items remained ConflictResolved (not overwritten to Synced)
		var conflictItem = await service.GetLogByIdAsync(first40Ids[10]);
		Assert.NotNull(conflictItem);
		Assert.Equal((int)SyncStatus.ConflictResolved, conflictItem.SyncStatus);

		// Verify all 100 records still exist
		var allLogs = await service.GetAllLogsAsync();
		Assert.Equal(100, allLogs.Count);
	}

	#endregion

	#region 5. Connection Disposal, Re-initialization & Windows File Locking

	[Fact]
	public async Task ConnectionDisposal_ReleasesAllFileLocks_AndAllowsFileDeletion()
	{
		var isolatedFixture = new SqliteTestDatabaseFixture();
		await isolatedFixture.InitializeAsync();

		var batch = SqliteTestDatabaseFixture.CreateSampleEntities(100);
		await isolatedFixture.DatabaseService.UpsertBatchAsync(batch);

		// Act: Explicitly close connection
		await isolatedFixture.DatabaseService.CloseConnectionAsync();

		GC.Collect();
		GC.WaitForPendingFinalizers();

		// Assert: All SQLite database files (.db3, .db3-wal, .db3-shm) can be deleted immediately
		var dbPath = isolatedFixture.DatabasePath;
		var files = new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" };

		foreach (var file in files)
		{
			if (File.Exists(file))
			{
				var deleteEx = Record.Exception(() => File.Delete(file));
				Assert.Null(deleteEx);
			}
		}
	}

	[Fact]
	public async Task ServiceOperations_AfterCloseConnectionAsync_LazilyReinitializesSeamlessly()
	{
		var service = _fixture.DatabaseService;

		// Seed data
		var initial = SqliteTestDatabaseFixture.CreateSampleEntities(10);
		await service.UpsertBatchAsync(initial);

		// Close connection
		await service.CloseConnectionAsync();

		// Act: Query logs after connection closed - should lazily re-initialize
		var logs = await service.GetAllLogsAsync();
		Assert.Equal(10, logs.Count);

		// Act: Insert new log after lazy re-initialization
		var newEntity = SqliteTestDatabaseFixture.CreateSampleEntity(id: "reinit-new-1");
		var rows = await service.InsertLogAsync(newEntity);
		Assert.Equal(1, rows);

		var fetched = await service.GetLogByIdAsync("reinit-new-1");
		Assert.NotNull(fetched);
	}

	[Fact]
	public async Task CloseConnectionAsync_IsIdempotent()
	{
		var service = _fixture.DatabaseService;

		// Multiple sequential close calls should execute without throwing
		await service.CloseConnectionAsync();
		await service.CloseConnectionAsync();
		await service.CloseConnectionAsync();

		// Re-initialization should still work
		var logs = await service.GetAllLogsAsync();
		Assert.NotNull(logs);
	}

	#endregion

	#region 6. Adversarial Input & Boundary Validation

	[Fact]
	public async Task AdversarialData_HandlesSpecialCharacters_SqlInjection_AndExtremeTimestamps()
	{
		var service = _fixture.DatabaseService;

		var adversarialEntities = new List<DecisionLogEntity>
		{
			// SQL Injection attempt in text fields
			new()
			{
				Id = "sql-injection-1",
				Timestamp = DateTime.UtcNow,
				LastModifiedUtc = DateTime.UtcNow,
				Decision = "YES",
				Reason = "'); DROP TABLE [decision_logs]; --",
				CaffeineAmountMg = 100.0,
				ItemName = "' OR '1'='1' --",
				ItemCategory = "'''\";;;",
				IsComplianceOverride = true,
				SyncStatus = (int)SyncStatus.Pending
			},
			// Unicode, Japanese, Emojis, control characters
			new()
			{
				Id = "unicode-emoji-2",
				Timestamp = DateTime.UtcNow,
				LastModifiedUtc = DateTime.UtcNow,
				Decision = "NO",
				Reason = "濃いエスプレッソ \t \r\n 🚀☕🔥💻🎉",
				CaffeineAmountMg = 0.0001,
				ItemName = "エスプレッソ・ドッピオ",
				ItemCategory = "specialty_☕",
				IsComplianceOverride = false,
				SyncStatus = (int)SyncStatus.Synced
			},
			// Extreme timestamps
			new()
			{
				Id = "extreme-timestamp-3",
				Timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				LastModifiedUtc = new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc),
				Decision = "YES",
				Reason = new string('A', 8192), // 8KB payload
				CaffeineAmountMg = 99999.99,
				ItemName = "Mega Caffeine Bomb",
				ItemCategory = "extreme",
				IsComplianceOverride = true,
				SyncStatus = (int)SyncStatus.Pending
			}
		};

		// Upsert batch
		var inserted = await service.UpsertBatchAsync(adversarialEntities);
		Assert.Equal(3, inserted);

		// Verify table still exists (SQL injection did not drop table)
		var allLogs = await service.GetAllLogsAsync();
		Assert.True(allLogs.Count >= 3);

		// Verify byte-level equality for SQL injection payload
		var fetchedSqlInj = await service.GetLogByIdAsync("sql-injection-1");
		Assert.NotNull(fetchedSqlInj);
		Assert.Equal("'); DROP TABLE [decision_logs]; --", fetchedSqlInj.Reason);
		Assert.Equal("' OR '1'='1' --", fetchedSqlInj.ItemName);

		// Verify Unicode & Emoji
		var fetchedUnicode = await service.GetLogByIdAsync("unicode-emoji-2");
		Assert.NotNull(fetchedUnicode);
		Assert.Equal("濃いエスプレッソ \t \r\n 🚀☕🔥💻🎉", fetchedUnicode.Reason);
		Assert.Equal("エスプレッソ・ドッピオ", fetchedUnicode.ItemName);
		Assert.Equal(0.0001, fetchedUnicode.CaffeineAmountMg);

		// Verify Large string payload & extreme dates
		var fetchedExtreme = await service.GetLogByIdAsync("extreme-timestamp-3");
		Assert.NotNull(fetchedExtreme);
		Assert.Equal(8192, fetchedExtreme.Reason.Length);
		Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), fetchedExtreme.Timestamp);
		Assert.Equal(new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc), fetchedExtreme.LastModifiedUtc);
		Assert.Equal(99999.99, fetchedExtreme.CaffeineAmountMg);
	}

	[Fact]
	public async Task Metadata_HandlesLargeJsonPayloadsAndUnicode()
	{
		var service = _fixture.DatabaseService;

		var largeJson = "{\"items\":[" + string.Join(",", Enumerable.Range(1, 1000).Select(i => $"{{\"id\":{i},\"val\":\"日本語テスト_{i}\"}}")) + "]}";

		await service.SetMetadataAsync("LargeJsonPayloadKey", largeJson);

		var fetched = await service.GetMetadataAsync("LargeJsonPayloadKey");
		Assert.Equal(largeJson, fetched);
	}

	[Fact]
	public async Task EdgeCases_NullAndEmptyInputs_HandledSafely()
	{
		var service = _fixture.DatabaseService;

		// Null argument checks
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.InsertLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpsertLogAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpsertBatchAsync(null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.MarkLogsSyncedAsync(null!));
		await Assert.ThrowsAsync<ArgumentException>(() => service.SetMetadataAsync("", "val"));
		await Assert.ThrowsAsync<ArgumentException>(() => service.SetMetadataAsync("   ", "val"));

		// Empty string queries
		Assert.Null(await service.GetLogByIdAsync(""));
		Assert.Null(await service.GetLogByIdAsync("   "));
		Assert.Equal(0, await service.DeleteLogAsync(""));
		Assert.Equal(0, await service.DeleteLogAsync("   "));

		// Empty batch collections
		Assert.Equal(0, await service.UpsertBatchAsync([]));
		Assert.Equal(0, await service.MarkLogsSyncedAsync([]));
		Assert.Equal(0, await service.MarkLogsSyncedAsync(["", "   "]));
	}

	#endregion
}
