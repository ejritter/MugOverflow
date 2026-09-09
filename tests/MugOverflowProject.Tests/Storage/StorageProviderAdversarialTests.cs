using System.Net;
using System.Text;
using System.Text.Json;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

/// <summary>
/// Empirical Challenger Adversarial Test Harness for Milestone 2 (Storage Providers & OAuth Authentication).
/// Thoroughly tests edge cases: malformed JSON, empty storage payloads, unauthenticated read/write rejections,
/// special characters, emojis, SQL injection, unicode text, secure storage corruption/recovery, and concurrency.
/// </summary>
public sealed class StorageProviderAdversarialTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly LocalStorageProvider _localProvider;

	public StorageProviderAdversarialTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MugOverflow_AdvStorage_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
		_localProvider = new LocalStorageProvider(_tempDirectory);
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
			// Best effort cleanup
		}
	}

	private sealed class MockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, HttpResponseMessage> OnSend { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.OK);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(OnSend(request));
		}
	}

	#region Dimension 1: Malformed JSON, Truncation & Corrupted Payloads

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\r\n\t")]
	[InlineData("{")]
	[InlineData("{\"appVersion\":")]
	[InlineData("{\"appVersion\": \"1.0.0\", \"decisionLogs\": [")]
	[InlineData("{\"decisionLogs\": [ { \"id\": \"1\", \"decision\": ")]
	[InlineData("[1, 2, 3, 4]")]
	[InlineData("\"bare string\"")]
	[InlineData("{\"decisionLogs\": [{\"caffeineAmountMg\": \"not-a-number\"}]}")]
	[InlineData("{\"exportedAtUtc\": \"not-a-date\"}")]
	[InlineData("{\"decisionLogs\": \"not-an-array\"}")]
	[InlineData("<html><body>502 Bad Gateway</body></html>")]
	public void Dimension1_StoragePayload_FromJson_HandlesMalformedJsonSafely(string? malformedJson)
	{
		var result = StoragePayload.FromJson(malformedJson!);
		Assert.Null(result);
	}

	[Fact]
	public async Task Dimension1_LocalStorage_ReadPayloadAsync_WhenFileContainsCorruptedJson_ReturnsNull()
	{
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		await File.WriteAllTextAsync(_localProvider.FilePath, "{\"appVersion\": \"1.0.0\", \"decisionLogs\": [ { incomplete...");

		var payload = await _localProvider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task Dimension1_LocalStorage_ReadPayloadAsync_WhenFileContainsBinaryGarbage_ReturnsNull()
	{
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		byte[] garbage = [0xFF, 0xFE, 0x00, 0x12, 0x34, 0xAA, 0xBB, 0xCC, 0x00, 0x01];
		await File.WriteAllBytesAsync(_localProvider.FilePath, garbage);

		var payload = await _localProvider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task Dimension1_GoogleDrive_ReadPayloadAsync_WhenHttpReturnsCorruptedJson_ReturnsNull()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
				if (url.Contains("mimeType = 'application/vnd.google-apps.folder'"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent("{\"files\":[{\"id\":\"f1\",\"name\":\"" + GoogleDriveConfig.FolderName + "\"}]}", Encoding.UTF8, "application/json")
					};
				}
				if (url.Contains("name = 'caffeine_consumption.json'"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent("{\"files\":[{\"id\":\"file1\",\"name\":\"" + GoogleDriveConfig.FileName + "\"}]}", Encoding.UTF8, "application/json")
					};
				}
				if (url.Contains("files/file1?alt=media"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent("<!DOCTYPE html><html>Broken Cloud Response</html>", Encoding.UTF8, "text/html")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("valid-token"), config: config);

		var payload = await provider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task Dimension1_OneDrive_ReadPayloadAsync_WhenHttpReturnsCorruptedJson_ReturnsNull()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{ \"appVersion\": \"1.0.0\", \"decisionLogs\": [ unclosed json ...", Encoding.UTF8, "application/json")
			}
		};

		var fakeStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(fakeStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("token"));

		var payload = await provider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	#endregion

	#region Dimension 2: Empty Payloads, Zero Records & Schema Invariants

	[Fact]
	public async Task Dimension2_EmptyPayload_RoundtripsThroughAllProviders()
	{
		var emptyPayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = DateTime.UtcNow,
			DecisionLogs = []
		};

		// 1. Local storage round-trip
		Assert.True(await _localProvider.WritePayloadAsync(emptyPayload));
		var localRead = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(localRead);
		Assert.Empty(localRead.DecisionLogs);

		// 2. Google Drive mock round-trip
		var fakeSecureStorage = new FakeSecureStorage();
		var gdrive = new GoogleDriveStorageProvider(
			new HttpClient(new MockHttpHandler()),
			fakeSecureStorage,
			config: new GoogleDriveConfig { IsMockMode = true });
		await gdrive.AuthenticateAsync();

		Assert.True(await gdrive.WritePayloadAsync(emptyPayload));
		var gdriveRead = await gdrive.ReadPayloadAsync();
		Assert.NotNull(gdriveRead);
		Assert.Empty(gdriveRead.DecisionLogs);

		// 3. OneDrive round-trip
		string uploadedJson = string.Empty;
		var oneDriveHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.Method == HttpMethod.Put)
				{
					uploadedJson = req.Content?.ReadAsStringAsync().Result ?? "";
					return new HttpResponseMessage(HttpStatusCode.OK);
				}
				if (req.Method == HttpMethod.Get)
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(uploadedJson, Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		var oneDrive = new OneDriveStorageProvider(fakeSecureStorage, new HttpClient(oneDriveHandler), _ => Task.FromResult<string?>("token"));
		Assert.True(await oneDrive.WritePayloadAsync(emptyPayload));
		var oneDriveRead = await oneDrive.ReadPayloadAsync();
		Assert.NotNull(oneDriveRead);
		Assert.Empty(oneDriveRead.DecisionLogs);
	}

	[Fact]
	public async Task Dimension2_LocalStorage_ReadPayloadAsync_WhenFileIsEmptyZeroBytes_ReturnsNull()
	{
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		await File.WriteAllBytesAsync(_localProvider.FilePath, []);

		var payload = await _localProvider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task Dimension2_LocalStorage_ReadPayloadAsync_WhenFileIsWhitespaceOnly_ReturnsNull()
	{
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		await File.WriteAllTextAsync(_localProvider.FilePath, "   \r\n\t  \n  ");

		var payload = await _localProvider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task Dimension2_OneDrive_ReadPayloadAsync_WhenHttpReturnsEmptyString_ReturnsNull()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("", Encoding.UTF8, "application/json")
			}
		};

		var fakeStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(fakeStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("token"));

		var payload = await provider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public void Dimension2_StoragePayload_FromJson_WhenJsonObjectIsEmpty_InitializesDefaultCollections()
	{
		var payload = StoragePayload.FromJson("{}");
		Assert.NotNull(payload);
		Assert.NotNull(payload.DecisionLogs);
		Assert.Empty(payload.DecisionLogs);
		Assert.Equal("1.0.0", payload.AppVersion);
	}

	#endregion

	#region Dimension 3: Unauthenticated Read / Write Rejections & Credential Failures

	[Fact]
	public async Task Dimension3_LocalStorage_AuthenticationMethods_ComplyWithContract()
	{
		Assert.False(_localProvider.RequiresAuthentication);
		Assert.True(await _localProvider.IsAuthenticatedAsync());
		Assert.True(await _localProvider.AuthenticateAsync());
		await _localProvider.SignOutAsync();
		Assert.True(await _localProvider.TestConnectionAsync());
	}

	[Fact]
	public async Task Dimension3_GoogleDrive_UnauthenticatedOperations_RejectGracefully()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };
		using var client = new HttpClient(new MockHttpHandler());
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		Assert.False(await provider.IsAuthenticatedAsync());

		// Read without token returns null
		var readPayload = await provider.ReadPayloadAsync();
		Assert.Null(readPayload);

		// Write without token returns false
		var writeSuccess = await provider.WritePayloadAsync(new StoragePayload());
		Assert.False(writeSuccess);

		// TestConnection without token returns false
		var testConn = await provider.TestConnectionAsync();
		Assert.False(testConn);
	}

	[Fact]
	public async Task Dimension3_GoogleDrive_ExpiredTokenWithFailedRefresh_ReturnsNullAndFalse()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "expired_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(-1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "bad_refresh_token");

		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri?.ToString().Contains("oauth2.googleapis.com/token") == true)
				{
					// Refresh fails with 400 Bad Request
					return new HttpResponseMessage(HttpStatusCode.BadRequest);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };
		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		Assert.False(await provider.IsAuthenticatedAsync());
		Assert.Null(await provider.ReadPayloadAsync());
		Assert.False(await provider.WritePayloadAsync(new StoragePayload()));
	}

	[Fact]
	public async Task Dimension3_GoogleDrive_SignOutAsync_ClearsAllSecureStorageKeys()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "token123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "refresh123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "file123");

		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient(new MockHttpHandler());
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		await provider.SignOutAsync();

		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyRefreshToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
	}

	[Fact]
	public async Task Dimension3_OneDrive_UnauthenticatedOperations_RejectGracefully()
	{
		var fakeStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(fakeStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>(null));

		Assert.False(await provider.IsAuthenticatedAsync());
		Assert.Null(await provider.ReadPayloadAsync());
		Assert.False(await provider.WritePayloadAsync(new StoragePayload()));
		Assert.False(await provider.TestConnectionAsync());
	}

	[Fact]
	public async Task Dimension3_OneDrive_Http401Unauthorized_ClearsTokenCacheAndReturnsNullOrFalse()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid_cached_token");

		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
		};

		var provider = new OneDriveStorageProvider(fakeStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("rejected_token"));

		// 1. Read on 401 returns null and purges token cache
		var readResult = await provider.ReadPayloadAsync();
		Assert.Null(readResult);
		Assert.Null(await fakeStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));

		// Repopulate token cache to test write
		await fakeStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid_cached_token");

		// 2. Write on 401 returns false and purges token cache
		var writeResult = await provider.WritePayloadAsync(new StoragePayload());
		Assert.False(writeResult);
		Assert.Null(await fakeStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	[Fact]
	public async Task Dimension3_OneDrive_Http500InternalServerError_ReturnsNullOrFalse()
	{
		var fakeStorage = new FakeSecureStorage();
		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
		};

		var provider = new OneDriveStorageProvider(fakeStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("token"));

		Assert.Null(await provider.ReadPayloadAsync());
		Assert.False(await provider.WritePayloadAsync(new StoragePayload()));
	}

	#endregion

	#region Dimension 4: Special Characters, Unicode, SQL Injection & Extreme Payloads

	[Theory]
	[InlineData("Robert'); DROP TABLE decision_logs; --")]
	[InlineData("<script>alert('XSS')</script><img src=x onerror=alert(1)>")]
	[InlineData("Double\"Quotes\" and Back\\Slashes/ and \r\n\t Whitespaces")]
	[InlineData("☕️ 🔥 ⚡️ 💀 🚀 🧋 🧪 💯 \ud83d\ude00 \ud83c\udf75")]
	[InlineData("Chinese: 拿铁咖啡 Japanese: カプチーノ Arabic: قهوة مثلجة Hebrew: קפה שחור Russian: Эспрессо Greek: Ελληνικός")]
	public async Task Dimension4_SpecialCharactersAndUnicode_PersistsAndRoundtripsFidelity(string specialContent)
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
					Id = $"id-{Guid.NewGuid():N}",
					Timestamp = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc),
					LastModifiedUtc = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc),
					Decision = specialContent,
					Reason = specialContent,
					CaffeineAmountMg = 185.75,
					ItemName = specialContent,
					ItemCategory = specialContent,
					IsComplianceOverride = true,
					SyncStatus = (int)SyncStatus.Pending
				}
			]
		};

		// 1. Test JSON Serialization
		string json = payload.ToJson();
		var jsonDeserialized = StoragePayload.FromJson(json);
		Assert.NotNull(jsonDeserialized);
		Assert.Equal(specialContent, jsonDeserialized.DecisionLogs[0].ItemName);
		Assert.Equal(specialContent, jsonDeserialized.DecisionLogs[0].Reason);

		// 2. Test Local Storage Provider Round-trip
		Assert.True(await _localProvider.WritePayloadAsync(payload));
		var localRead = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(localRead);
		Assert.Equal(specialContent, localRead.DecisionLogs[0].ItemName);
		Assert.Equal(specialContent, localRead.DecisionLogs[0].Reason);
		Assert.Equal(specialContent, localRead.DecisionLogs[0].Decision);

		// 3. Test Mock Google Drive Provider Round-trip
		var fakeStorage = new FakeSecureStorage();
		var gdrive = new GoogleDriveStorageProvider(
			new HttpClient(new MockHttpHandler()),
			fakeStorage,
			config: new GoogleDriveConfig { IsMockMode = true });
		await gdrive.AuthenticateAsync();

		Assert.True(await gdrive.WritePayloadAsync(payload));
		var gdriveRead = await gdrive.ReadPayloadAsync();
		Assert.NotNull(gdriveRead);
		Assert.Equal(specialContent, gdriveRead.DecisionLogs[0].ItemName);
		Assert.Equal(specialContent, gdriveRead.DecisionLogs[0].Reason);
	}

	[Fact]
	public async Task Dimension4_MassivePayloadStrings_RoundtripsSuccessfully()
	{
		string massiveReason = new('X', 50_000);
		string massiveItemName = new('Y', 10_000);

		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "massive-1",
					Reason = massiveReason,
					ItemName = massiveItemName,
					CaffeineAmountMg = 500.0
				}
			]
		};

		Assert.True(await _localProvider.WritePayloadAsync(payload));
		var retrieved = await _localProvider.ReadPayloadAsync();

		Assert.NotNull(retrieved);
		Assert.Equal(massiveReason, retrieved.DecisionLogs[0].Reason);
		Assert.Equal(massiveItemName, retrieved.DecisionLogs[0].ItemName);
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-50.0)]
	[InlineData(0.00001)]
	[InlineData(999999.999)]
	public async Task Dimension4_BoundaryCaffeineValues_PersistsAccurately(double caffeineMg)
	{
		var payload = new StoragePayload
		{
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "caffeine-boundary",
					CaffeineAmountMg = caffeineMg
				}
			]
		};

		Assert.True(await _localProvider.WritePayloadAsync(payload));
		var retrieved = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(retrieved);
		Assert.Equal(caffeineMg, retrieved.DecisionLogs[0].CaffeineAmountMg);
	}

	#endregion

	#region Dimension 5: SecureStorage Resilience, Corruption & Recovery

	[Fact]
	public async Task Dimension5_SecureStorageService_FallbackDictionary_MaintainsIntegrityOnException()
	{
		// Test SecureStorageService fallback dictionary operations
		var service = new SecureStorageService();

		// Setting and getting key safely via fallback if default is not available in test runner
		await service.SetAsync("test_key", "test_val");
		var val = await service.GetAsync("test_key");
		Assert.Equal("test_val", val);

		bool removed = service.Remove("test_key");
		Assert.True(removed);

		var afterRemove = await service.GetAsync("test_key");
		Assert.Null(afterRemove);

		// RemoveAll test
		await service.SetAsync("k1", "v1");
		await service.SetAsync("k2", "v2");
		service.RemoveAll();
		Assert.Null(await service.GetAsync("k1"));
		Assert.Null(await service.GetAsync("k2"));
	}

	[Fact]
	public async Task Dimension5_SecureStorageService_InputValidation_RejectsNullOrWhitespaceKeys()
	{
		var service = new SecureStorageService();

		await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(""));
		await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync("   "));
		await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("", "val"));
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetAsync("valid_key", null!));
		Assert.Throws<ArgumentException>(() => service.Remove(""));
	}

	[Fact]
	public async Task Dimension5_GoogleDrive_CorruptTokenExpiryFormat_HandlesGracefullyWithoutCrashing()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "active_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, "NOT-A-VALID-DATETIME-FORMAT");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "refresh_token");

		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri?.ToString().Contains("oauth2.googleapis.com/token") == true)
				{
					// Refresh returns fresh token
					var responseJson = JsonSerializer.Serialize(new
					{
						access_token = "refreshed_valid_token",
						expires_in = 3600,
						token_type = "Bearer"
					});
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };
		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// IsAuthenticated should successfully refresh token instead of crashing on invalid expiry string
		bool isAuth = await provider.IsAuthenticatedAsync();
		Assert.True(isAuth);
		Assert.Equal("refreshed_valid_token", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	#endregion

	#region Dimension 6: High-Concurrency, Atomic File I/O Under Load & Cancellation

	[Fact]
	public async Task Dimension6_LocalStorage_RapidParallelReadsAndWrites_MaintainAtomicConsistency()
	{
		const int parallelTasks = 30;
		var writeTasks = Enumerable.Range(0, parallelTasks).Select(i => Task.Run(async () =>
		{
			var p = new StoragePayload
			{
				AppVersion = "1.0.0",
				DecisionLogs =
				[
					new DecisionLogEntity
					{
						Id = $"concurrency-task-{i}",
						ItemName = $"Coffee-{i}",
						CaffeineAmountMg = 100.0 + i
					}
				]
			};

			bool writeOk = await _localProvider.WritePayloadAsync(p);
			Assert.True(writeOk);

			var read = await _localProvider.ReadPayloadAsync();
			Assert.NotNull(read);
			Assert.Single(read.DecisionLogs);
		}));

		await Task.WhenAll(writeTasks);

		// Final check: file exists and has valid payload
		var final = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(final);
		Assert.Single(final.DecisionLogs);

		// Verify no leaked .tmp files in target directory
		var tmpFiles = Directory.GetFiles(_localProvider.TargetDirectory, "*.tmp");
		Assert.Empty(tmpFiles);
	}

	[Fact]
	public async Task Dimension6_LocalStorage_WhenTargetDirectoryDeleted_CreatesDirectoryOnWrite()
	{
		if (Directory.Exists(_localProvider.TargetDirectory))
		{
			Directory.Delete(_localProvider.TargetDirectory, recursive: true);
		}

		var payload = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "dir-recreate-1", ItemName = "Cold Brew" }]
		};

		bool written = await _localProvider.WritePayloadAsync(payload);
		Assert.True(written);
		Assert.True(Directory.Exists(_localProvider.TargetDirectory));
		Assert.True(File.Exists(_localProvider.FilePath));
	}

	[Fact]
	public async Task Dimension6_CancellationToken_PropagatesCancellationCorrectly()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// LocalStorage throws OperationCanceledException (or derived TaskCanceledException)
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			_localProvider.ReadPayloadAsync(cts.Token));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			_localProvider.WritePayloadAsync(new StoragePayload(), cts.Token));

		// Google Drive throws OperationCanceledException
		var fakeStorage = new FakeSecureStorage();
		var gdrive = new GoogleDriveStorageProvider(
			new HttpClient(new MockHttpHandler()),
			fakeStorage,
			_ => Task.FromResult<string?>("token"),
			config: new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" });

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			gdrive.ReadPayloadAsync(cts.Token));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			gdrive.WritePayloadAsync(new StoragePayload(), cts.Token));

		// OneDrive throws OperationCanceledException
		var onedrive = new OneDriveStorageProvider(
			fakeStorage,
			new HttpClient(new MockHttpHandler()),
			_ => Task.FromResult<string?>("token"));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			onedrive.ReadPayloadAsync(cts.Token));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			onedrive.WritePayloadAsync(new StoragePayload(), cts.Token));
	}

	#endregion
}
