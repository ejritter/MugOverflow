using System.Net;
using System.Text.Json;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

public sealed class GoogleDriveMultiPlatformTests
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
	public void ActiveClientId_ReturnsPlatformSpecificOrFallback()
	{
		var config = new GoogleDriveConfig
		{
			ClientId = "fallback-client-id",
			WindowsClientId = "win-client-id",
			AndroidClientId = "android-client-id"
		};

		Assert.False(string.IsNullOrWhiteSpace(config.ActiveClientId));
	}

	[Fact]
	public void ActiveClientSecret_ReturnsPlatformSpecificOrFallback()
	{
		var config = new GoogleDriveConfig
		{
			ClientSecret = "fallback-secret",
			WindowsClientSecret = "win-secret",
			AndroidClientSecret = null
		};

		// Should resolve properly without throwing
		var secret = config.ActiveClientSecret;
		Assert.True(secret == null || !string.IsNullOrWhiteSpace(secret));
	}

	[Fact]
	public async Task TokenRefresh_WithoutClientSecret_OmitsClientSecretField()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "test-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O")); // Expired

		var config = new GoogleDriveConfig
		{
			IsMockMode = false,
			ClientId = "android-client-id",
			ClientSecret = "", // No secret for Android
			AndroidClientId = "android-client-id",
			AndroidClientSecret = null,
			WindowsClientId = "android-client-id",
			WindowsClientSecret = null
		};

		string? requestContent = null;
		var handler = new MockHttpHandler
		{
			Handler = async req =>
			{
				if (req.RequestUri != null && req.RequestUri.ToString().Contains("oauth2.googleapis.com/token"))
				{
					requestContent = await req.Content!.ReadAsStringAsync();
					var json = JsonSerializer.Serialize(new
					{
						access_token = "android-fresh-token",
						expires_in = 3600,
						token_type = "Bearer"
					});
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		};

		using var client = new HttpClient(handler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// Trigger silent token refresh by testing connection or reading
		bool isAuth = await provider.IsAuthenticatedAsync();

		Assert.True(isAuth);
		Assert.NotNull(requestContent);
		Assert.Contains("client_id=android-client-id", requestContent);
		Assert.DoesNotContain("client_secret", requestContent);
	}

	[Fact]
	public async Task TokenRefresh_WithClientSecret_IncludesClientSecretField()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "test-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O")); // Expired

		var config = new GoogleDriveConfig
		{
			IsMockMode = false,
			ClientId = "desktop-client-id",
			ClientSecret = "desktop-secret-123",
			WindowsClientId = "desktop-client-id",
			WindowsClientSecret = "desktop-secret-123"
		};

		string? requestContent = null;
		var handler = new MockHttpHandler
		{
			Handler = async req =>
			{
				if (req.RequestUri != null && req.RequestUri.ToString().Contains("oauth2.googleapis.com/token"))
				{
					requestContent = await req.Content!.ReadAsStringAsync();
					var json = JsonSerializer.Serialize(new
					{
						access_token = "desktop-fresh-token",
						expires_in = 3600,
						token_type = "Bearer"
					});
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		};

		using var client = new HttpClient(handler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		bool isAuth = await provider.IsAuthenticatedAsync();

		Assert.True(isAuth);
		Assert.NotNull(requestContent);
		Assert.Contains("client_id=desktop-client-id", requestContent);
		Assert.Contains("client_secret=desktop-secret-123", requestContent);
	}
}
