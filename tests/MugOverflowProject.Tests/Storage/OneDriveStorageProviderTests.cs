using System.Net;
using System.Text;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

public sealed class OneDriveStorageProviderTests
{
	private sealed class MockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, HttpResponseMessage> OnSend { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.OK);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(OnSend(request));
		}
	}

	[Fact]
	public void Properties_ReturnExpectedDefaults()
	{
		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>("token"));

		Assert.Equal(StorageProviderType.OneDrive, provider.ProviderType);
		Assert.Equal("Microsoft OneDrive", provider.DisplayName);
		Assert.True(provider.RequiresAuthentication);
	}

	[Fact]
	public async Task IsAuthenticatedAsync_WhenTokenProvided_ReturnsTrue()
	{
		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>("valid_token"));

		Assert.True(await provider.IsAuthenticatedAsync());
	}

	[Fact]
	public async Task IsAuthenticatedAsync_WhenTokenNull_ReturnsFalse()
	{
		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>(null));

		Assert.False(await provider.IsAuthenticatedAsync());
	}

	[Fact]
	public async Task ReadPayloadAsync_WhenAppRootFileNotFound404_ReturnsNull()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				Assert.Equal(HttpMethod.Get, req.Method);
				Assert.Equal(OneDriveStorageProvider.GraphFileContentEndpoint, req.RequestUri?.ToString());
				Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
				Assert.Equal("test_graph_token", req.Headers.Authorization?.Parameter);

				return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_graph_token"));

		var payload = await provider.ReadPayloadAsync();
		Assert.Null(payload);
	}

	[Fact]
	public async Task ReadPayloadAsync_WhenAppRootReturns200_DeserializesPayloadCorrectly()
	{
		var expectedPayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "onedrive-log-1",
					Timestamp = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc),
					LastModifiedUtc = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc),
					Decision = "YES",
					Reason = "Complied with Oracle",
					CaffeineAmountMg = 120.0,
					ItemName = "Americano",
					ItemCategory = "Coffee",
					IsComplianceOverride = false,
					SyncStatus = (int)SyncStatus.Synced
				}
			]
		};

		string jsonContent = expectedPayload.ToJson();

		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				Assert.Equal(HttpMethod.Get, req.Method);
				Assert.Equal(OneDriveStorageProvider.GraphFileContentEndpoint, req.RequestUri?.ToString());
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
				};
			}
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_graph_token"));

		var payload = await provider.ReadPayloadAsync();
		Assert.NotNull(payload);
		Assert.Equal("1.0.0", payload.AppVersion);
		Assert.Single(payload.DecisionLogs);
		Assert.Equal("onedrive-log-1", payload.DecisionLogs[0].Id);
		Assert.Equal("Americano", payload.DecisionLogs[0].ItemName);
		Assert.Equal(120.0, payload.DecisionLogs[0].CaffeineAmountMg);
	}

	[Fact]
	public async Task WritePayloadAsync_WhenGraphApiReturns200_UploadsSuccessfully()
	{
		string capturedPayload = string.Empty;

		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				Assert.Equal(HttpMethod.Put, req.Method);
				Assert.Equal(OneDriveStorageProvider.GraphFileContentEndpoint, req.RequestUri?.ToString());
				Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
				Assert.Equal("test_graph_token", req.Headers.Authorization?.Parameter);

				capturedPayload = req.Content?.ReadAsStringAsync().Result ?? string.Empty;

				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("{\"id\":\"file_id_123\"}", Encoding.UTF8, "application/json")
				};
			}
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_graph_token"));

		var payloadToSave = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs =
			[
				new DecisionLogEntity { Id = "save-1", ItemName = "Flat White", CaffeineAmountMg = 130.0 }
			]
		};

		bool result = await provider.WritePayloadAsync(payloadToSave);
		Assert.True(result);
		Assert.Contains("save-1", capturedPayload);
		Assert.Contains("Flat White", capturedPayload);
	}

	[Fact]
	public async Task WritePayloadAsync_WhenGraphApiReturns500_ReturnsFalse()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_graph_token"));

		var payloadToSave = new StoragePayload();
		bool result = await provider.WritePayloadAsync(payloadToSave);
		Assert.False(result);
	}

	[Fact]
	public async Task WritePayloadAsync_WhenThrowsArgumentNull_ThrowsException()
	{
		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>("token"));

		await Assert.ThrowsAsync<ArgumentNullException>(() => provider.WritePayloadAsync(null!));
	}

	[Fact]
	public async Task TestConnectionAsync_WhenAppRootAccessible_ReturnsTrue()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = req =>
			{
				Assert.Equal(HttpMethod.Get, req.Method);
				Assert.Equal(OneDriveStorageProvider.GraphAppRootEndpoint, req.RequestUri?.ToString());
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_token"));

		Assert.True(await provider.TestConnectionAsync());
	}

	[Fact]
	public async Task TestConnectionAsync_WhenApiReturns401_ReturnsFalse()
	{
		var mockHandler = new MockHttpHandler
		{
			OnSend = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
		};

		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(mockHandler), _ => Task.FromResult<string?>("test_token"));

		Assert.False(await provider.TestConnectionAsync());
	}

	[Fact]
	public async Task SignOutAsync_ClearsTokenCacheKeyInSecureStorage()
	{
		var secureStorage = new FakeSecureStorage();
		await secureStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "cached_token_bytes");

		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new MockHttpHandler()), _ => Task.FromResult<string?>(null));

		await provider.SignOutAsync();
		Assert.Null(await secureStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}
}
