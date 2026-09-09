using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

/// <summary>
/// Empirical Challenger Stress Harness for GoogleDriveStorageProvider:
/// Stress-tests high-concurrency token refresh serialization, 404 recovery workflows,
/// cancellation under lock contention, and cascading folder/file deletion self-healing.
/// </summary>
public sealed class GoogleDriveEmpiricalChallengeTests
{
	private sealed class LatencyMockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Router { get; set; } =
			(_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested(); return Router(request, cancellationToken);
		}
	}

	[Fact]
	public async Task EmpiricalChallenge_HighContentionTokenRefresh_100ConcurrentCallers_SingleTokenExchange()
	{
		// Arrange: Expired token with valid refresh token in storage
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "valid-refresh-token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-30).ToString("O"));
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client-id" };

		int tokenEndpointCallCount = 0;
		var mockHandler = new LatencyMockHttpHandler
		{
			Router = async (req, cancellationToken) =>
			{
				string uri = req.RequestUri?.ToString() ?? "";

				if (uri.Contains("oauth2.googleapis.com/token"))
				{
					Interlocked.Increment(ref tokenEndpointCallCount);
					// Add latency to widen the concurrency window
					await Task.Delay(40);

					var tokenJson = JsonSerializer.Serialize(new
					{
						access_token = "fresh-access-token-100",
						expires_in = 3600,
						token_type = "Bearer"
					});

					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(tokenJson, Encoding.UTF8, "application/json")
					};
				}

				if (uri.Contains("drive/v3/about"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// Act: Launch 100 concurrent calls across different methods that trigger GetValidAccessTokenAsync
		const int concurrency = 100;
		var exceptions = new ConcurrentBag<Exception>();

		var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
		{
			try
			{
				if (i % 2 == 0)
				{
					bool isAuth = await provider.IsAuthenticatedAsync();
					Assert.True(isAuth);
				}
				else
				{
					bool conn = await provider.TestConnectionAsync();
					Assert.True(conn);
				}
			}
			catch (Exception ex)
			{
				exceptions.Add(ex);
			}
		})).ToArray();

		await Task.WhenAll(tasks);

		// Assert: No exceptions and exactly 1 call made to Google's token endpoint
		Assert.Empty(exceptions);
		Assert.Equal(1, tokenEndpointCallCount);
		Assert.Equal("fresh-access-token-100", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task EmpiricalChallenge_RemoteFolderAndFile404CascadingRecovery()
	{
		// Arrange: Pre-populate cached folder and file IDs
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "stale-folder-id-1");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "stale-file-id-1");
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client-id" };

		string currentFileId = "recreated-file-2";
		int createCallCount = 0;
		string uploadedContent = "";

		var mockHandler = new LatencyMockHttpHandler
		{
			Router = async (req, cancellationToken) =>
			{
				string uri = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
				string method = req.Method.Method;

				// 1. Stale File PATCH -> 404
				if (method == "PATCH" && uri.Contains("stale-file-id-1"))
				{
					return new HttpResponseMessage(HttpStatusCode.NotFound);
				}

				// 2. Search file in appDataFolder
				if (method == "GET" && uri.Contains("name = 'caffeine_consumption.json'"))
				{
					var result = new { files = createCallCount > 0 ? new[] { new { id = currentFileId, name = GoogleDriveConfig.FileName } } : Array.Empty<object>() };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json")
					};
				}

				// 3. POST drive/v3/files
				if (method == "POST" && uri.Contains("drive/v3/files"))
				{
					createCallCount++;
					// First creation attempt simulates 404/failure to trigger full eviction
					if (createCallCount == 1)
					{
						return new HttpResponseMessage(HttpStatusCode.NotFound);
					}

					var result = new { id = currentFileId, name = GoogleDriveConfig.FileName };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json")
					};
				}

				// 4. Successful PATCH to new file
				if (method == "PATCH" && uri.Contains(currentFileId))
				{
					uploadedContent = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("live-token"), config: config);

		var payload = new StoragePayload
		{
			AppVersion = "1.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "challenge-log-1", ItemName = "Cascading 404 Recovery Nitro" }]
		};

		// Act: First write triggers 404 on PATCH, 404 on CreateFileMetadata, clearing stale folder & file IDs
		bool firstAttempt = await provider.WritePayloadAsync(payload);
		Assert.False(firstAttempt);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));

		// Act 2: Second write now creates the file directly in appDataFolder
		bool secondAttempt = await provider.WritePayloadAsync(payload);
		Assert.True(secondAttempt);
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Equal(currentFileId, await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
		Assert.Contains("Cascading 404 Recovery Nitro", uploadedContent);
	}

	[Fact]
	public async Task EmpiricalChallenge_CancellationUnderContention_ReleasesLockAndAllowsSubsequentSuccess()
	{
		// Arrange:
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "refresh-token-cancellation");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-10).ToString("O"));
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new LatencyMockHttpHandler
		{
			Router = async (req, cancellationToken) =>
			{
				if (req.RequestUri?.ToString().Contains("oauth2.googleapis.com/token") == true)
				{
					await Task.Delay(100, cancellationToken); // 100ms delay to simulate work under lock
					var json = JsonSerializer.Serialize(new
					{
						access_token = "token-after-cancellation",
						expires_in = 3600,
						token_type = "Bearer"
					});
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(json, Encoding.UTF8, "application/json")
					};
				}
				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, config: config);

		// Act 1: Trigger call with a cancellation token that cancels quickly
		using var cts = new CancellationTokenSource(20);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.IsAuthenticatedAsync(cts.Token));

		// Act 2: Subsequent uncancelled call must NOT deadlock or fail due to abandoned semaphore
		bool isAuth = await provider.IsAuthenticatedAsync(CancellationToken.None);
		Assert.True(isAuth);
		Assert.Equal("token-after-cancellation", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}
}
