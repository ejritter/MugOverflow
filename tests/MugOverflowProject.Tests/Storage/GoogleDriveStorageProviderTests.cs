using System.Net;
using System.Text;
using System.Text.Json;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

public sealed class GoogleDriveStorageProviderTests
{
	private sealed class MockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } =
			_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Handler(request);
		}
	}

	[Fact]
	public void Properties_ReturnExpectedDefaults()
	{
		var fakeStorage = new FakeSecureStorage();
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage);

		Assert.Equal(StorageProviderType.GoogleDrive, provider.ProviderType);
		Assert.Equal("Google Drive", provider.DisplayName);
		Assert.True(provider.RequiresAuthentication);
	}

	[Fact]
	public async Task MockMode_AuthenticateAsync_SetsTokensAndReturnsTrue()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool result = await provider.AuthenticateAsync();

		Assert.True(result);
		Assert.True(await provider.IsAuthenticatedAsync());
		Assert.NotNull(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task MockMode_WriteAndReadPayloadAsync_RoundtripsSuccessfully()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		await provider.AuthenticateAsync();

		var originalPayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = DateTime.UtcNow,
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "log-google-1",
					Timestamp = DateTime.UtcNow,
					Decision = "YES",
					Reason = "Need energy",
					CaffeineAmountMg = 150,
					ItemName = "Cold Brew",
					ItemCategory = "ColdBrew",
					SyncStatus = (int)SyncStatus.Synced
				}
			]
		};

		bool writeSuccess = await provider.WritePayloadAsync(originalPayload);
		Assert.True(writeSuccess);

		var retrievedPayload = await provider.ReadPayloadAsync();
		Assert.NotNull(retrievedPayload);
		Assert.Single(retrievedPayload.DecisionLogs);
		Assert.Equal("Cold Brew", retrievedPayload.DecisionLogs[0].ItemName);
	}

	[Fact]
	public async Task SignOutAsync_ClearsTokensFromSecureStorage()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		await provider.AuthenticateAsync();
		Assert.True(await provider.IsAuthenticatedAsync());

		await provider.SignOutAsync();
		Assert.False(await provider.IsAuthenticatedAsync());
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task ReadPayload_ReturnsNull_WhenNotAuthenticated()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		var payload = await provider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task WritePayload_ThrowsArgumentNullException_WhenPayloadNull()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = true };
		using var client = new HttpClient();
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		await Assert.ThrowsAsync<ArgumentNullException>(() => provider.WritePayloadAsync(null!));
	}

	[Fact]
	public async Task CustomToken_ReadPayloadAsync_QueriesGoogleDriveEndpoints()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var expectedPayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "gdrive-live-1", ItemName = "Espresso" }]
		};

		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
				if (url.Contains("mimeType = 'application/vnd.google-apps.folder'"))
				{
					// Return folder list
					var json = JsonSerializer.Serialize(new
					{
						files = new[] { new { id = "folder-123", name = GoogleDriveConfig.FolderName } }
					});
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				if (url.Contains("name = 'caffeine_consumption.json'"))
				{
					// Return file list
					var json = JsonSerializer.Serialize(new
					{
						files = new[] { new { id = "file-456", name = GoogleDriveConfig.FileName } }
					});
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				if (url.Contains("files/file-456?alt=media"))
				{
					// Return payload file content
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(expectedPayload.ToJson(), Encoding.UTF8, "application/json")
					});
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = await provider.ReadPayloadAsync();
		Assert.NotNull(payload);
		Assert.Single(payload.DecisionLogs);
		Assert.Equal("gdrive-live-1", payload.DecisionLogs[0].Id);
	}

	[Fact]
	public async Task CustomToken_TestConnectionAsync_ReturnsTrue_OnSuccess()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				if (req.RequestUri?.ToString().Contains("drive/v3/about") == true)
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
				}
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		bool connected = await provider.TestConnectionAsync();
		Assert.True(connected);
	}

	[Fact]
	public async Task CustomToken_WritePayloadAsync_UploadsSuccessfully()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		string uploadedContent = string.Empty;

		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
				if (url.Contains("mimeType = 'application/vnd.google-apps.folder'"))
				{
					var json = JsonSerializer.Serialize(new
					{
						files = new[] { new { id = "folder-123", name = GoogleDriveConfig.FolderName } }
					});
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				if (url.Contains("name = 'caffeine_consumption.json'"))
				{
					var json = JsonSerializer.Serialize(new
					{
						files = new[] { new { id = "file-456", name = GoogleDriveConfig.FileName } }
					});
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				if (req.Method == HttpMethod.Patch && url.Contains("upload/drive/v3/files/file-456"))
				{
					uploadedContent = req.Content?.ReadAsStringAsync().Result ?? "";
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "gdrive-save-1", ItemName = "Cold Brew Nitro" }]
		};

		bool written = await provider.WritePayloadAsync(payload);
		Assert.True(written);
		Assert.Contains("gdrive-save-1", uploadedContent);
		Assert.Contains("Cold Brew Nitro", uploadedContent);
	}

	[Fact]
	public async Task ReadPayloadAsync_InvalidatesCachedFileId_On404NotFound()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-404");
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				if (req.RequestUri?.ToString().Contains("files/stale-file-404") == true)
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
				}
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = await provider.ReadPayloadAsync();

		Assert.Null(payload);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
	}

	[Fact]
	public async Task WritePayloadAsync_InvalidatesStaleFileId_AndRetriesCreation_On404NotFound()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-999");
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		int patchCount = 0;
		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				string url = req.RequestUri?.ToString() ?? "";
				if (req.Method == HttpMethod.Patch && url.Contains("stale-file-999"))
				{
					patchCount++;
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
				}

				if (req.Method == HttpMethod.Post && url.Contains("drive/v3/files"))
				{
					var json = JsonSerializer.Serialize(new { id = "recreated-file-1000", name = GoogleDriveConfig.FileName });
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				if (req.Method == HttpMethod.Patch && url.Contains("recreated-file-1000"))
				{
					patchCount++;
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "retry-log-1", ItemName = "Cold Brew" }]
		};

		bool written = await provider.WritePayloadAsync(payload);

		Assert.True(written);
		Assert.Equal(2, patchCount);
		Assert.Equal("recreated-file-1000", await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
	}

	[Fact]
	public async Task WritePayloadAsync_EvictsFolderAndFileId_WhenRetryPatchFailsWith404()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "stale-folder-123");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-456");
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new MockHttpHandler
		{
			Handler = req =>
			{
				string url = req.RequestUri?.ToString() ?? "";
				if (req.Method == HttpMethod.Patch)
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
				}

				if (req.Method == HttpMethod.Post && url.Contains("drive/v3/files"))
				{
					var json = JsonSerializer.Serialize(new { id = "new-file-789", name = GoogleDriveConfig.FileName });
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					});
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "test-log-1", ItemName = "Espresso" }]
		};

		bool written = await provider.WritePayloadAsync(payload);

		Assert.False(written);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
	}

	[Fact]
	public async Task GetValidAccessTokenAsync_ConcurrentCallers_ExecuteSingleRefresh()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "valid-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O")); // Expired
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		int tokenEndpointCallCount = 0;
		var mockHandler = new MockHttpHandler
		{
			Handler = async req =>
			{
				if (req.RequestUri?.ToString().Contains("oauth2.googleapis.com/token") == true)
				{
					Interlocked.Increment(ref tokenEndpointCallCount);
					await Task.Delay(50); // Simulate network latency to trigger concurrency window
					var json = JsonSerializer.Serialize(new
					{
						access_token = "new-fresh-access-token",
						expires_in = 3600,
						token_type = "Bearer"
					});
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					};
				}

				if (req.RequestUri?.ToString().Contains("drive/v3/about") == true)
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// Launch 10 concurrent requests simultaneously
		var tasks = Enumerable.Range(0, 10).Select(_ => provider.TestConnectionAsync()).ToArray();
		var results = await Task.WhenAll(tasks);

		Assert.All(results, Assert.True);
		Assert.Equal(1, tokenEndpointCallCount); // Exactly one refresh call executed
	}
}
