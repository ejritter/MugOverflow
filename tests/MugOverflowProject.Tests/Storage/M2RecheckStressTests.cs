using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

/// <summary>
/// Empirical challenge stress tests for Milestone 2 recheck.
/// Tests multi-threaded provider operations, provider switching under concurrent load,
/// comprehensive token eviction under all auth/404 failure codes, and concurrent token refresh serialization.
/// </summary>
public sealed class M2RecheckStressTests : IDisposable
{
	private readonly string _tempDirectory;

	public M2RecheckStressTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MugOverflow_Recheck_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
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
			// Best-effort cleanup
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

	#region 1. Token Eviction Matrix Under Auth & 404 Failures

	[Fact]
	public async Task GoogleDrive_ReadPayloadAsync_401Unauthorized_EvictsAccessToken()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "stale-access-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "file-123");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("/files/file-123?alt=media"))
				{
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		var payload = await provider.ReadPayloadAsync();

		Assert.Null(payload);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task GoogleDrive_WritePayloadAsync_401Unauthorized_EvictsAccessToken()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "stale-access-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "file-123");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains("/upload/drive/v3/files/file-123"))
				{
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool success = await provider.WritePayloadAsync(new StoragePayload { AppVersion = "1.0.0" });

		Assert.False(success);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task GoogleDrive_TestConnectionAsync_401Unauthorized_EvictsAccessToken()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "stale-access-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("/about"))
				{
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool success = await provider.TestConnectionAsync();

		Assert.False(success);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.Unauthorized)]
	public async Task GoogleDrive_SilentTokenRefresh_AuthFailure_EvictsAllTokens(HttpStatusCode failureCode)
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "expired-access-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "revoked-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O"));

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("oauth2.googleapis.com/token"))
				{
					return new HttpResponseMessage(failureCode)
					{
						Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool isAuth = await provider.IsAuthenticatedAsync();

		Assert.False(isAuth);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyRefreshToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry));
	}

	[Fact]
	public async Task GoogleDrive_ReadPayloadAsync_404NotFound_EvictsFileId()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "valid-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-id");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("/files/stale-file-id?alt=media"))
				{
					return new HttpResponseMessage(HttpStatusCode.NotFound);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		var payload = await provider.ReadPayloadAsync();

		Assert.Null(payload);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
		Assert.Equal("folder-123", await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
	}

	[Fact]
	public async Task GoogleDrive_WritePayloadAsync_Retry404NotFound_EvictsFolderAndFileIds()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "valid-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "stale-folder-id");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-id");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.Method == HttpMethod.Patch)
				{
					return new HttpResponseMessage(HttpStatusCode.NotFound);
				}
				if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/drive/v3/files"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent("{\"id\":\"new-file-id\"}", Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool success = await provider.WritePayloadAsync(new StoragePayload { AppVersion = "1.0.0" });

		Assert.False(success);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
	}

	[Fact]
	public async Task OneDrive_ReadPayloadAsync_401Unauthorized_EvictsTokenCacheKey()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid-cache-base64");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("caffeine_consumption.json:/content"))
				{
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var provider = new OneDriveStorageProvider(
			fakeStorage,
			client,
			customTokenProvider: _ => Task.FromResult<string?>("mock-onedrive-token"));

		var payload = await provider.ReadPayloadAsync();

		Assert.Null(payload);
		Assert.Null(await fakeStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	[Fact]
	public async Task OneDrive_WritePayloadAsync_401Unauthorized_EvictsTokenCacheKey()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid-cache-base64");

		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.Method == HttpMethod.Put)
				{
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var provider = new OneDriveStorageProvider(
			fakeStorage,
			client,
			customTokenProvider: _ => Task.FromResult<string?>("mock-onedrive-token"));

		bool success = await provider.WritePayloadAsync(new StoragePayload { AppVersion = "1.0.0" });

		Assert.False(success);
		Assert.Null(await fakeStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	#endregion

	#region 2. Multi-Threaded Provider Switching & Concurrent Multi-Provider Operations

	[Fact]
	public async Task MultiProvider_ConcurrentOperations_MaintainThreadSafety()
	{
		// Arrange: instantiate all three providers
		var localProvider = new LocalStorageProvider(_tempDirectory);

		var gDriveStorage = new FakeSecureStorage();
		var gDriveConfig = new GoogleDriveConfig { IsMockMode = true };
		using var gClient = new HttpClient();
		var gDriveProvider = new GoogleDriveStorageProvider(gClient, gDriveStorage, config: gDriveConfig);
		await gDriveProvider.AuthenticateAsync();

		var oneDriveStorage = new FakeSecureStorage();
		var oneDriveStore = new StoragePayload { AppVersion = "1.0.0", DecisionLogs = [new() { Id = "o-1", Decision = "NO" }] };
		var oneDriveHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("caffeine_consumption.json:/content"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(oneDriveStore.ToJson(), Encoding.UTF8, "application/json")
					};
				}
				if (req.Method == HttpMethod.Put)
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}
				if (req.RequestUri!.ToString().Contains("special/approot"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};
		using var oClient = new HttpClient(oneDriveHandler);
		var oneDriveProvider = new OneDriveStorageProvider(oneDriveStorage, oClient, customTokenProvider: _ => Task.FromResult<string?>("o-token"));

		IStorageProvider[] providers = [localProvider, gDriveProvider, oneDriveProvider];

		// Act: 30 concurrent workers executing randomized operations across all providers
		var exceptions = new ConcurrentBag<Exception>();
		var tasks = Enumerable.Range(0, 30).Select(workerId => Task.Run(async () =>
		{
			try
			{
				for (int i = 0; i < 15; i++)
				{
					var provider = providers[(workerId + i) % providers.Length];

					// Write operation
					var payload = new StoragePayload
					{
						AppVersion = "1.0.0",
						DecisionLogs = [new DecisionLogEntity { Id = $"log-{workerId}-{i}", Decision = "YES", CaffeineAmountMg = 100 }]
					};
					bool written = await provider.WritePayloadAsync(payload);
					Assert.True(written);

					// Read operation
					var read = await provider.ReadPayloadAsync();
					Assert.NotNull(read);

					// Test connection
					bool conn = await provider.TestConnectionAsync();
					Assert.True(conn);
				}
			}
			catch (Exception ex)
			{
				exceptions.Add(ex);
			}
		}));

		await Task.WhenAll(tasks);

		Assert.Empty(exceptions);
	}

	[Fact]
	public async Task ProviderSwitching_SimulatedConcurrentAccess_NoDeadlockOrCorruption()
	{
		var localProvider = new LocalStorageProvider(_tempDirectory);
		var gDriveStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient();
		var gDriveProvider = new GoogleDriveStorageProvider(client, gDriveStorage, config: config);
		await gDriveProvider.AuthenticateAsync();

		var activeProviderRef = new StrongBox<IStorageProvider>(localProvider);
		var switchLock = new object();
		var exceptions = new ConcurrentBag<Exception>();

		// 20 reader/writer tasks + 5 switcher tasks
		var tasks = new List<Task>();

		for (int w = 0; w < 20; w++)
		{
			int workerId = w;
			tasks.Add(Task.Run(async () =>
			{
				try
				{
					for (int i = 0; i < 20; i++)
					{
						IStorageProvider current;
						lock (switchLock)
						{
							current = activeProviderRef.Value!;
						}

						var p = new StoragePayload
						{
							AppVersion = "1.0.0",
							DecisionLogs = [new DecisionLogEntity { Id = $"switch-{workerId}-{i}", Decision = "MAYBE" }]
						};

						await current.WritePayloadAsync(p);
						var read = await current.ReadPayloadAsync();
						if (read != null)
						{
							Assert.NotNull(read.AppVersion);
						}

						await Task.Yield();
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(ex);
				}
			}));
		}

		for (int s = 0; s < 5; s++)
		{
			tasks.Add(Task.Run(async () =>
			{
				try
				{
					for (int i = 0; i < 10; i++)
					{
						lock (switchLock)
						{
							activeProviderRef.Value = (activeProviderRef.Value == localProvider) ? gDriveProvider : localProvider;
						}
						await Task.Delay(5);
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(ex);
				}
			}));
		}

		await Task.WhenAll(tasks);
		Assert.Empty(exceptions);
	}

	private sealed class StrongBox<T> where T : class
	{
		public T? Value { get; set; }
		public StrongBox(T? val) => Value = val;
	}

	#endregion

	#region 3. Concurrency Serialization on Google Drive Token Refresh

	[Fact]
	public async Task GoogleDrive_30ConcurrentRequests_WithExpiredToken_RefreshesExactlyOnce()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "expired-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "valid-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O"));

		int tokenEndpointCallCount = 0;
		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("oauth2.googleapis.com/token"))
				{
					Interlocked.Increment(ref tokenEndpointCallCount);
					string json = "{\"access_token\":\"new-fresh-access-token\",\"expires_in\":3600,\"refresh_token\":\"new-refresh-token\"}";
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					};
				}

				if (req.RequestUri!.ToString().Contains("/about"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// Act: 30 concurrent callers testing connection (triggering token check)
		var tasks = Enumerable.Range(0, 30).Select(_ => provider.TestConnectionAsync());
		var results = await Task.WhenAll(tasks);

		Assert.All(results, Assert.True);
		Assert.Equal(1, tokenEndpointCallCount);
		Assert.Equal("new-fresh-access-token", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task GoogleDrive_TokenExpiryDateParsing_HandlesOffsetsAndNearExpiryCorrectly()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "offset-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "refresh-token");

		// Stored with explicit +05:00 offset equivalent to UtcNow + 10 minutes
		var futureWithOffset = DateTimeOffset.UtcNow.AddMinutes(10).ToOffset(TimeSpan.FromHours(5));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, futureWithOffset.ToString("O"));

		int refreshCalls = 0;
		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("oauth2.googleapis.com/token"))
				{
					Interlocked.Increment(ref refreshCalls);
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool isAuth = await provider.IsAuthenticatedAsync();

		Assert.True(isAuth);
		Assert.Equal(0, refreshCalls); // Should not refresh because token is valid for 10 minutes
	}

	[Fact]
	public async Task GoogleDrive_MalformedTokenExpiry_FallsBackToRefreshCleanly()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "token-with-bad-expiry");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "valid-refresh");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, "GARBAGE_NOT_A_DATE");

		int refreshCalls = 0;
		var handler = new MockHttpHandler
		{
			OnSend = req =>
			{
				if (req.RequestUri!.ToString().Contains("oauth2.googleapis.com/token"))
				{
					Interlocked.Increment(ref refreshCalls);
					string json = "{\"access_token\":\"refreshed-token\",\"expires_in\":3600,\"refresh_token\":\"valid-refresh\"}";
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(handler);
		var config = new GoogleDriveConfig { ClientId = "test-client-id" };
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool isAuth = await provider.IsAuthenticatedAsync();

		Assert.True(isAuth);
		Assert.Equal(1, refreshCalls);
		Assert.Equal("refreshed-token", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task AllProviders_SignOutAsync_PurgesAllSecureStorageKeysConcurrently()
	{
		var fakeStorage = new FakeSecureStorage();

		// Populate Google keys
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "g-access");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "g-refresh");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "g-folder");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "g-file");

		// Populate OneDrive keys
		await fakeStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "o-cache");

		using var gClient = new HttpClient(new MockHttpHandler());
		var gProvider = new GoogleDriveStorageProvider(gClient, fakeStorage, config: new GoogleDriveConfig { ClientId = "test" });
		var oProvider = new OneDriveStorageProvider(fakeStorage, gClient, _ => Task.FromResult<string?>("o-token"));

		// Act: Sign out concurrently
		await Task.WhenAll(
			gProvider.SignOutAsync(),
			oProvider.SignOutAsync()
		);

		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyRefreshToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
		Assert.Null(await fakeStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	#endregion

	#region 4. SecureStorageService Concurrency & Resilience

	[Fact]
	public async Task SecureStorageService_HighConcurrency_ConcurrentMixedOperations_MaintainsConsistency()
	{
		var secureService = new SecureStorageService();
		var exceptions = new ConcurrentBag<Exception>();

		var tasks = Enumerable.Range(0, 40).Select(workerId => Task.Run(async () =>
		{
			try
			{
				for (int i = 0; i < 25; i++)
				{
					string key = $"key_{workerId}_{i % 5}";
					string val = $"val_{workerId}_{i}";

					await secureService.SetAsync(key, val);
					var readVal = await secureService.GetAsync(key);
					Assert.NotNull(readVal);

					if (i % 3 == 0)
					{
						secureService.Remove(key);
					}
				}
			}
			catch (Exception ex)
			{
				exceptions.Add(ex);
			}
		}));

		await Task.WhenAll(tasks);
		Assert.Empty(exceptions);
	}

	#endregion
}
