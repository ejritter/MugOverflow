using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using Xunit;

namespace MugOverflowProject.Tests.Database;

public sealed class DapperDatabaseServiceTests : IAsyncLifetime, IDisposable
{
	private readonly string _databasePath;
	private DapperDatabaseService _service = null!;

	public DapperDatabaseServiceTests()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"Dapper_Test_{Guid.NewGuid():N}.db3");
	}

	public async Task InitializeAsync()
	{
		_service = new DapperDatabaseService(_databasePath);
		await _service.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		if (_service != null)
		{
			await _service.DisposeAsync();
		}

		Dispose();
	}

	public void Dispose()
	{
		try
		{
			if (File.Exists(_databasePath))
			{
				File.Delete(_databasePath);
			}
			var shm = $"{_databasePath}-shm";
			if (File.Exists(shm)) File.Delete(shm);
			var wal = $"{_databasePath}-wal";
			if (File.Exists(wal)) File.Delete(wal);
		}
		catch
		{
			// Best-effort cleanup
		}
	}

	[Fact]
	public async Task InitializeAsync_SetsWalMode()
	{
		var mode = await _service.GetJournalModeAsync();
		Assert.Equal("wal", mode.ToLowerInvariant());
	}

	[Fact]
	public async Task InsertLogAsync_And_GetLogByIdAsync_RoundtripsAccurately()
	{
		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString(),
			Timestamp = DateTime.UtcNow,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = "YES",
			Reason = "Oracle test approval",
			CaffeineAmountMg = 95.0,
			ItemName = "Drip Coffee",
			ItemCategory = "drip",
			IsComplianceOverride = false,
			SyncStatus = (int)SyncStatus.Pending
		};

		var insertResult = await _service.InsertLogAsync(entity);
		Assert.Equal(1, insertResult);

		var fetched = await _service.GetLogByIdAsync(entity.Id);
		Assert.NotNull(fetched);
		Assert.Equal(entity.Id, fetched.Id);
		Assert.Equal("YES", fetched.Decision);
		Assert.Equal(95.0, fetched.CaffeineAmountMg);
		Assert.Equal((int)SyncStatus.Pending, fetched.SyncStatus);
	}

	[Fact]
	public async Task UpdateLogAsync_UpdatesProperties()
	{
		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString(),
			Timestamp = DateTime.UtcNow,
			Decision = "YES",
			Reason = "Initial",
			CaffeineAmountMg = 65.0,
			ItemName = "Latte",
			SyncStatus = (int)SyncStatus.Pending
		};
		await _service.InsertLogAsync(entity);

		entity.Decision = "NO";
		entity.Reason = "Modified";
		entity.CaffeineAmountMg = 0.0;
		var rows = await _service.UpdateLogAsync(entity);

		Assert.Equal(1, rows);
		var updated = await _service.GetLogByIdAsync(entity.Id);
		Assert.NotNull(updated);
		Assert.Equal("NO", updated.Decision);
		Assert.Equal("Modified", updated.Reason);
		Assert.Equal(0.0, updated.CaffeineAmountMg);
	}

	[Fact]
	public async Task UpsertLogAsync_InsertsThenUpdates()
	{
		var id = Guid.NewGuid().ToString();
		var entity = new DecisionLogEntity
		{
			Id = id,
			Timestamp = DateTime.UtcNow,
			Decision = "YES",
			CaffeineAmountMg = 100.0,
			SyncStatus = (int)SyncStatus.Pending
		};

		await _service.UpsertLogAsync(entity);
		var first = await _service.GetLogByIdAsync(id);
		Assert.NotNull(first);
		Assert.Equal(100.0, first.CaffeineAmountMg);

		entity.CaffeineAmountMg = 200.0;
		await _service.UpsertLogAsync(entity);
		var second = await _service.GetLogByIdAsync(id);
		Assert.NotNull(second);
		Assert.Equal(200.0, second.CaffeineAmountMg);
	}

	[Fact]
	public async Task DeleteLogAsync_RemovesEntity()
	{
		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString(),
			Timestamp = DateTime.UtcNow,
			Decision = "YES"
		};
		await _service.InsertLogAsync(entity);

		var deleted = await _service.DeleteLogAsync(entity.Id);
		Assert.Equal(1, deleted);

		var fetched = await _service.GetLogByIdAsync(entity.Id);
		Assert.Null(fetched);
	}

	[Fact]
	public async Task GetAllLogsAsync_ReturnsOrderedByTimestampDesc()
	{
		var baseTime = DateTime.UtcNow;
		var log1 = new DecisionLogEntity { Id = "1", Timestamp = baseTime.AddHours(-2), Decision = "YES" };
		var log2 = new DecisionLogEntity { Id = "2", Timestamp = baseTime.AddHours(-1), Decision = "YES" };
		var log3 = new DecisionLogEntity { Id = "3", Timestamp = baseTime, Decision = "YES" };

		await _service.InsertLogAsync(log1);
		await _service.InsertLogAsync(log2);
		await _service.InsertLogAsync(log3);

		var all = await _service.GetAllLogsAsync();
		Assert.Equal(3, all.Count);
		Assert.Equal("3", all[0].Id);
		Assert.Equal("2", all[1].Id);
		Assert.Equal("1", all[2].Id);
	}

	[Fact]
	public async Task MarkLogsSyncedAsync_And_MarkAllLogsSyncedAsync_WorkCorrectly()
	{
		var log1 = new DecisionLogEntity { Id = "A", Timestamp = DateTime.UtcNow, SyncStatus = (int)SyncStatus.Pending };
		var log2 = new DecisionLogEntity { Id = "B", Timestamp = DateTime.UtcNow, SyncStatus = (int)SyncStatus.Pending };
		var log3 = new DecisionLogEntity { Id = "C", Timestamp = DateTime.UtcNow, SyncStatus = (int)SyncStatus.Pending };

		await _service.InsertLogAsync(log1);
		await _service.InsertLogAsync(log2);
		await _service.InsertLogAsync(log3);

		var pendingBefore = await _service.GetPendingSyncLogsAsync();
		Assert.Equal(3, pendingBefore.Count);

		await _service.MarkLogsSyncedAsync(new[] { "A", "B" });
		var pendingAfter = await _service.GetPendingSyncLogsAsync();
		Assert.Single(pendingAfter);
		Assert.Equal("C", pendingAfter[0].Id);

		await _service.MarkAllLogsSyncedAsync();
		var pendingFinal = await _service.GetPendingSyncLogsAsync();
		Assert.Empty(pendingFinal);
	}

	[Fact]
	public async Task UpsertBatchAsync_AtomicallyInsertsMultipleEntities()
	{
		var entities = Enumerable.Range(1, 20).Select(i => new DecisionLogEntity
		{
			Id = $"batch_{i}",
			Timestamp = DateTime.UtcNow.AddMinutes(-i),
			Decision = "YES",
			CaffeineAmountMg = 80.0
		}).ToList();

		var count = await _service.UpsertBatchAsync(entities);
		Assert.Equal(20, count);

		var all = await _service.GetAllLogsAsync();
		Assert.Equal(20, all.Count);

		var cleared = await _service.ClearAllLogsAsync();
		Assert.Equal(20, cleared);

		var remaining = await _service.GetAllLogsAsync();
		Assert.Empty(remaining);
	}

	[Fact]
	public async Task MetadataOperations_SetAndGetCorrectly()
	{
		var initial = await _service.GetMetadataAsync("CustomKey", "default_val");
		Assert.Equal("default_val", initial);

		await _service.SetMetadataAsync("CustomKey", "PersistedValue");
		var fetched = await _service.GetMetadataAsync("CustomKey", "default_val");
		Assert.Equal("PersistedValue", fetched);
	}

	[Fact]
	public async Task DateTime_RoundtripsPreservesUtcKind()
	{
		var utcNow = DateTime.UtcNow;
		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString(),
			Timestamp = utcNow,
			LastModifiedUtc = utcNow,
			Decision = "YES",
			Reason = "DateTime Utc test",
			CaffeineAmountMg = 50.0
		};

		await _service.InsertLogAsync(entity);
		var fetched = await _service.GetLogByIdAsync(entity.Id);

		Assert.NotNull(fetched);
		Assert.Equal(DateTimeKind.Utc, fetched.Timestamp.Kind);
		Assert.Equal(DateTimeKind.Utc, fetched.LastModifiedUtc.Kind);
		Assert.Equal(utcNow.ToString("o"), fetched.Timestamp.ToString("o"));
	}

	[Fact]
	public async Task SqliteDateTimeHandler_ParsesStringsWithOffsetToUtc()
	{
		var rawId = Guid.NewGuid().ToString();
		var rawOffsetString = "2026-09-18T18:00:00.0000000-04:00";
		var entity = new DecisionLogEntity
		{
			Id = rawId,
			Timestamp = DateTime.UtcNow,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = "YES",
			Reason = "Offset test",
			CaffeineAmountMg = 40.0
		};
		await _service.InsertLogAsync(entity);

		// Directly update Timestamp in SQLite with raw offset string to test Parse logic
		using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}"))
		{
			await connection.OpenAsync();
			using var cmd = connection.CreateCommand();
			cmd.CommandText = "UPDATE decision_logs SET Timestamp = @ts WHERE Id = @id;";
			cmd.Parameters.AddWithValue("@ts", rawOffsetString);
			cmd.Parameters.AddWithValue("@id", rawId);
			await cmd.ExecuteNonQueryAsync();
		}

		var fetched = await _service.GetLogByIdAsync(rawId);
		Assert.NotNull(fetched);
		Assert.Equal(DateTimeKind.Utc, fetched.Timestamp.Kind);
		Assert.Equal(DateTime.Parse(rawOffsetString).ToUniversalTime(), fetched.Timestamp);
	}

	[Fact]
	public async Task CloseConnectionAsync_SafelyDrainsAndAllowsReinitialization()
	{
		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString(),
			Timestamp = DateTime.UtcNow,
			Decision = "YES",
			CaffeineAmountMg = 80.0
		};

		await _service.InsertLogAsync(entity);
		await _service.CloseConnectionAsync();

		// Should re-initialize on next query without errors
		var retrieved = await _service.GetLogByIdAsync(entity.Id);
		Assert.NotNull(retrieved);
		Assert.Equal("YES", retrieved.Decision);
	}

	[Fact]
	public async Task DisposeAsync_IsIdempotent_CanBeCalledRepeatedlyWithoutException()
	{
		var customPath = Path.Combine(Path.GetTempPath(), $"Dapper_Dispose_Test_{Guid.NewGuid():N}.db3");
		var customService = new DapperDatabaseService(customPath);
		await customService.InitializeAsync();

		await customService.DisposeAsync();
		var ex = await Record.ExceptionAsync(async () => await customService.DisposeAsync());
		Assert.Null(ex);

		// CloseConnectionAsync after DisposeAsync should also be safe
		var exClose = await Record.ExceptionAsync(async () => await customService.CloseConnectionAsync());
		Assert.Null(exClose);

		if (File.Exists(customPath)) File.Delete(customPath);
		var wal = $"{customPath}-wal";
		if (File.Exists(wal)) File.Delete(wal);
		var shm = $"{customPath}-shm";
		if (File.Exists(shm)) File.Delete(shm);
	}
}
