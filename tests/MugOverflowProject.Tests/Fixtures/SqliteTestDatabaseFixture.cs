using Dapper;
using Microsoft.Data.Sqlite;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using Xunit;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// Manages an isolated, temporary SQLite database instance for xUnit tests.
/// Ensures WAL mode is verified and connections are safely closed before file deletion
/// to prevent Windows file locking violations.
/// Standardized 100% on Microsoft.Data.Sqlite and Dapper.
/// </summary>
public sealed class SqliteTestDatabaseFixture : IAsyncLifetime, IDisposable
{
	static SqliteTestDatabaseFixture()
	{
		// Initialize the SQLitePCLRaw bundle_green native runtime for desktop test host
		SQLitePCL.Batteries_V2.Init();
	}

	public string DatabasePath { get; }
	public string ConnectionString { get; }
	public SqliteDatabaseService DatabaseService { get; private set; } = null!;

	public SqliteTestDatabaseFixture()
	{
		DatabasePath = Path.Combine(Path.GetTempPath(), $"MugOverflow_Test_{Guid.NewGuid():N}.db3");
		ConnectionString = new SqliteConnectionStringBuilder
		{
			DataSource = DatabasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Private,
			DefaultTimeout = 5,
			ForeignKeys = true,
			Pooling = true
		}.ToString();
	}

	public async Task InitializeAsync()
	{
		DatabaseService = new SqliteDatabaseService(DatabasePath);
		await DatabaseService.InitializeAsync();
	}

	private readonly List<SqliteConnection> _directConnections = [];
	private readonly object _directConnectionsLock = new();

	/// <summary>
	/// Creates a raw direct connection for low-level PRAGMA and table state validation.
	/// </summary>
	public SqliteConnection CreateDirectConnection()
	{
		var conn = new SqliteConnection(ConnectionString);
		conn.Open();
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
			cmd.ExecuteNonQuery();
		}

		lock (_directConnectionsLock)
		{
			_directConnections.Add(conn);
		}
		return conn;
	}

	/// <summary>
	/// Helper to generate a sample DecisionLogEntity instance for tests.
	/// </summary>
	public static DecisionLogEntity CreateSampleEntity(
		string? id = null,
		DateTime? timestamp = null,
		DateTime? lastModifiedUtc = null,
		string decision = "YES",
		string reason = "Complied with Oracle",
		double caffeineAmountMg = 95.0,
		string itemName = "Drip Coffee",
		string itemCategory = "drip",
		bool isComplianceOverride = false,
		int syncStatus = (int)SyncStatus.Pending)
	{
		return new DecisionLogEntity
		{
			Id = id ?? Guid.NewGuid().ToString(),
			Timestamp = timestamp ?? DateTime.UtcNow,
			LastModifiedUtc = lastModifiedUtc ?? DateTime.UtcNow,
			Decision = decision,
			Reason = reason,
			CaffeineAmountMg = caffeineAmountMg,
			ItemName = itemName,
			ItemCategory = itemCategory,
			IsComplianceOverride = isComplianceOverride,
			SyncStatus = syncStatus
		};
	}

	/// <summary>
	/// Helper to generate a collection of sample DecisionLogEntity instances.
	/// </summary>
	public static List<DecisionLogEntity> CreateSampleEntities(int count, int syncStatus = (int)SyncStatus.Pending)
	{
		var baseTime = DateTime.UtcNow.AddHours(-count);
		var list = new List<DecisionLogEntity>(count);

		for (int i = 0; i < count; i++)
		{
			list.Add(new DecisionLogEntity
			{
				Id = Guid.NewGuid().ToString(),
				Timestamp = baseTime.AddHours(i),
				LastModifiedUtc = DateTime.UtcNow,
				Decision = i % 2 == 0 ? "YES" : "NO",
				Reason = i % 2 == 0 ? "Complied with Oracle" : "Rebelled against Oracle",
				CaffeineAmountMg = i % 2 == 0 ? 64.0 : 95.0,
				ItemName = i % 2 == 0 ? "Espresso" : "Drip Coffee",
				ItemCategory = i % 2 == 0 ? "espresso" : "drip",
				IsComplianceOverride = i % 2 != 0,
				SyncStatus = syncStatus
			});
		}

		return list;
	}

	public async Task DisposeAsync()
	{
		if (DatabaseService is not null)
		{
			await DatabaseService.CloseConnectionAsync();
		}

		List<SqliteConnection> conns;
		lock (_directConnectionsLock)
		{
			conns = [.. _directConnections];
			_directConnections.Clear();
		}

		foreach (var conn in conns)
		{
			try
			{
				await conn.CloseAsync();
				await conn.DisposeAsync();
			}
			catch
			{
				// Ignore if already closed
			}
		}

		SqliteConnection.ClearAllPools();

		// Ensure garbage collector releases any unmanaged SQLite handles on Windows
		GC.Collect();
		GC.WaitForPendingFinalizers();

		DeleteDatabaseFilesWithRetry(DatabasePath);
	}

	public void Dispose()
	{
		DisposeAsync().GetAwaiter().GetResult();
	}

	private static void DeleteDatabaseFilesWithRetry(string basePath, int maxRetries = 5, int delayMs = 50)
	{
		var filesToDelete = new[]
		{
			basePath,
			$"{basePath}-wal",
			$"{basePath}-shm",
			$"{basePath}-journal"
		};

		foreach (var filePath in filesToDelete)
		{
			if (!File.Exists(filePath)) continue;

			for (int attempt = 0; attempt < maxRetries; attempt++)
			{
				try
				{
					File.Delete(filePath);
					break;
				}
				catch (IOException)
				{
					if (attempt == maxRetries - 1)
					{
						break;
					}
					Thread.Sleep(delayMs);
				}
				catch (UnauthorizedAccessException)
				{
					break;
				}
			}
		}
	}
}
