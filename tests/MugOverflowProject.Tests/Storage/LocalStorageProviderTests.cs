using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

public sealed class LocalStorageProviderTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly LocalStorageProvider _provider;

	public LocalStorageProviderTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MugOverflow_LocalTest_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
		_provider = new LocalStorageProvider(_tempDirectory);
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_tempDirectory))
			{
				Directory.Delete(_tempDirectory, recursive: true);
			}
		}
		catch
		{
			// Best effort cleanup on test completion
		}
	}

	[Fact]
	public void Properties_ReturnExpectedDefaults()
	{
		Assert.Equal(StorageProviderType.LocalStorage, _provider.ProviderType);
		Assert.Equal("Local Storage", _provider.DisplayName);
		Assert.False(_provider.RequiresAuthentication);
		Assert.Contains(LocalStorageProvider.SubFolderName, _provider.TargetDirectory);
		Assert.EndsWith(LocalStorageProvider.FileName, _provider.FilePath);
	}

	[Fact]
	public async Task AuthenticationMethods_ReturnTrueOrComplete()
	{
		Assert.True(await _provider.IsAuthenticatedAsync());
		Assert.True(await _provider.AuthenticateAsync());
		await _provider.SignOutAsync();
		Assert.True(await _provider.TestConnectionAsync());
	}

	[Fact]
	public async Task ReadPayloadAsync_WhenDirectoryDoesNotExist_ReturnsNull()
	{
		var isolatedProvider = new LocalStorageProvider(Path.Combine(_tempDirectory, "non_existent"));
		var result = await isolatedProvider.ReadPayloadAsync();
		Assert.Null(result);
	}

	[Fact]
	public async Task ReadPayloadAsync_WhenFileDoesNotExist_ReturnsNull()
	{
		Directory.CreateDirectory(_provider.TargetDirectory);
		var result = await _provider.ReadPayloadAsync();
		Assert.Null(result);
	}

	[Fact]
	public async Task ReadPayloadAsync_WhenFileIsInvalidJson_ReturnsNull()
	{
		Directory.CreateDirectory(_provider.TargetDirectory);
		await File.WriteAllTextAsync(_provider.FilePath, "{ invalid: json [corrupt");

		var result = await _provider.ReadPayloadAsync();
		Assert.Null(result);
	}

	[Fact]
	public async Task WritePayloadAsync_WhenPayloadValid_CreatesFileAndRoundtrips()
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
					Id = "local-log-1",
					Timestamp = new DateTime(2026, 8, 28, 8, 30, 0, DateTimeKind.Utc),
					LastModifiedUtc = new DateTime(2026, 8, 28, 8, 30, 0, DateTimeKind.Utc),
					Decision = "YES",
					Reason = "Oracle compliant",
					CaffeineAmountMg = 95.0,
					ItemName = "Pour Over",
					ItemCategory = "Coffee",
					IsComplianceOverride = false,
					SyncStatus = (int)SyncStatus.Synced
				},
				new DecisionLogEntity
				{
					Id = "local-log-2",
					Timestamp = new DateTime(2026, 8, 28, 14, 0, 0, DateTimeKind.Utc),
					LastModifiedUtc = new DateTime(2026, 8, 28, 14, 0, 0, DateTimeKind.Utc),
					Decision = "NO",
					Reason = "Late afternoon limit reached",
					CaffeineAmountMg = 0.0,
					ItemName = "Decaf Coffee",
					ItemCategory = "Coffee",
					IsComplianceOverride = false,
					SyncStatus = (int)SyncStatus.Synced
				}
			]
		};

		bool writeSuccess = await _provider.WritePayloadAsync(payload);
		Assert.True(writeSuccess);
		Assert.True(File.Exists(_provider.FilePath));

		var readPayload = await _provider.ReadPayloadAsync();
		Assert.NotNull(readPayload);
		Assert.Equal("1.0.0", readPayload.AppVersion);
		Assert.Equal("1.0.0", readPayload.SchemaVersion);
		Assert.Equal(2, readPayload.DecisionLogs.Count);
		Assert.Equal("local-log-1", readPayload.DecisionLogs[0].Id);
		Assert.Equal("Pour Over", readPayload.DecisionLogs[0].ItemName);
		Assert.Equal(95.0, readPayload.DecisionLogs[0].CaffeineAmountMg);
		Assert.Equal("local-log-2", readPayload.DecisionLogs[1].Id);
	}

	[Fact]
	public async Task WritePayloadAsync_OverwritesExistingFileAtomically()
	{
		var p1 = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "v1", ItemName = "First" }]
		};
		var p2 = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "v2", ItemName = "Second" }]
		};

		Assert.True(await _provider.WritePayloadAsync(p1));
		Assert.True(await _provider.WritePayloadAsync(p2));

		var result = await _provider.ReadPayloadAsync();
		Assert.NotNull(result);
		Assert.Single(result.DecisionLogs);
		Assert.Equal("v2", result.DecisionLogs[0].Id);
		Assert.Equal("Second", result.DecisionLogs[0].ItemName);
	}

	[Fact]
	public async Task WritePayloadAsync_ThrowsArgumentNullException_WhenPayloadIsNull()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.WritePayloadAsync(null!));
	}

	[Fact]
	public async Task ConcurrentWrites_AreThreadSafeAndConsistent()
	{
		var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
		{
			var p = new StoragePayload
			{
				DecisionLogs = [new DecisionLogEntity { Id = $"concurrency-{i}", ItemName = $"Item-{i}" }]
			};
			return await _provider.WritePayloadAsync(p);
		}));

		var results = await Task.WhenAll(tasks);
		Assert.All(results, Assert.True);

		var finalPayload = await _provider.ReadPayloadAsync();
		Assert.NotNull(finalPayload);
		Assert.Single(finalPayload.DecisionLogs);
	}
}
