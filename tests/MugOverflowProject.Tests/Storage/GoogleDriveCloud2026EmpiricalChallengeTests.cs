using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

/// <summary>
/// Empirical challenge test harness by Challenger 2:
/// Tests Background Sync Decoupling (R2), Direct Stream Serialization (R4),
/// and appDataFolder cloud modernization under Google Drive v3 REST API constraints.
/// </summary>
public sealed class GoogleDriveCloud2026EmpiricalChallengeTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeConnectivity _fakeConnectivity = new()
	{
		NetworkAccess = Microsoft.Maui.Networking.NetworkAccess.Internet
	};

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	private sealed class InterceptingHttpHandler : HttpMessageHandler
	{
		public List<HttpRequestMessage> RecordedRequests { get; } = new();
		public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } =
			_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RecordedRequests.Add(request);
			return await Handler(request);
		}
	}

	#region 1. Challenge: Background Sync Decoupling & Silent Auth

	[Fact]
	public async Task Challenge_BackgroundSync_WhenCredentialsExpired_NeverInvokesAuthenticateAsync_AndKeepsSQLitePending()
	{
		// Arrange
		var fakeDrive = new FakeGoogleDriveStorageProvider
		{
			IsAuthenticated = false,
			RequiresAuthentication = true
		};

		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, fakeDrive);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, new LocalStorageProvider(Path.GetTempPath()));
		var serviceProvider = services.BuildServiceProvider();

		var syncService = new StorageSyncService(_dbFixture.DatabaseService, serviceProvider, _fakeConnectivity);
		await syncService.InitializeAsync();

		// Switch provider to Google Drive while authenticated initially
		fakeDrive.IsAuthenticated = true;
		await syncService.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);
		Assert.Equal(0, fakeDrive.AuthenticateCallCount); // No auth prompt needed when already authenticated

		// Now simulate token expiry / credentials missing
		fakeDrive.IsAuthenticated = false;
		fakeDrive.AuthenticateCallCount = 0;

		// Add multiple uncommitted records to SQLite
		var record1 = SqliteTestDatabaseFixture.CreateSampleEntity("pending-challenger-1", syncStatus: (int)SyncStatus.Pending);
		var record2 = SqliteTestDatabaseFixture.CreateSampleEntity("pending-challenger-2", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.UpsertLogAsync(record1);
		await _dbFixture.DatabaseService.UpsertLogAsync(record2);

		var capturedStatuses = new List<SyncEngineStatus>();
		syncService.StatusChanged += (s, status) => capturedStatuses.Add(status);

		// Act 1: Trigger background flush
		bool flushResult = await syncService.FlushPendingChangesAsync();

		// Assert 1:
		Assert.False(flushResult);
		Assert.Equal(0, fakeDrive.AuthenticateCallCount); // MUST NEVER CALL AuthenticateAsync
		Assert.Equal(SyncEngineStatus.AuthRequired, syncService.Status);
		Assert.Contains(SyncEngineStatus.AuthRequired, capturedStatuses);

		// Verify records in SQLite remain untouched and pending
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Equal(2, pending.Count);
		Assert.All(pending, p => Assert.Equal((int)SyncStatus.Pending, p.SyncStatus));

		// Act 2: Trigger background synchronize
		bool syncResult = await syncService.SynchronizeAsync();

		// Assert 2:
		Assert.False(syncResult);
		Assert.Equal(0, fakeDrive.AuthenticateCallCount); // Still NEVER called
		Assert.Equal(SyncEngineStatus.AuthRequired, syncService.Status);

		// Records remain pending
		var pendingAfterSync = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Equal(2, pendingAfterSync.Count);
	}

	#endregion

	#region 2. Challenge: Direct Stream Serialization (No LOH String Buffering)

	[Fact]
	public async Task Challenge_DirectStreamSerialization_LargePayload_StreamsWithoutMaterializingFullString()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		bool streamContentObserved = false;
		long streamContentLength = 0;

		// Generate 1,000 logs (~250 KB payload, which would trigger LOH if converted to string)
		var largePayload = new StoragePayload
		{
			AppVersion = "2.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = DateTime.UtcNow,
			DecisionLogs = new List<DecisionLogEntity>()
		};

		for (int i = 0; i < 1000; i++)
		{
			largePayload.DecisionLogs.Add(new DecisionLogEntity
			{
				Id = $"bulk-log-{i:D4}",
				ItemName = $"Espresso Double Shot Super Grand Roast #{i}",
				Decision = "YES",
				Reason = "Testing Large Object Heap avoidance via direct stream serialization",
				CaffeineAmountMg = 150,
				Timestamp = DateTime.UtcNow.AddMinutes(-i),
				LastModifiedUtc = DateTime.UtcNow.AddMinutes(-i)
			});
		}

		var mockHandler = new InterceptingHttpHandler
		{
			Handler = async req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");

				// Handle folder check/creation if called
				if (url.Contains("mimeType = 'application/vnd.google-apps.folder'"))
				{
					var folderResult = new { files = new[] { new { id = "folder-stream-1", name = GoogleDriveConfig.FolderName } } };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(folderResult), Encoding.UTF8, "application/json")
					};
				}

				if (url.Contains("spaces=appDataFolder") && url.Contains("name = 'caffeine_consumption.json'"))
				{
					var fileResult = new { files = new[] { new { id = "file-stream-1", name = GoogleDriveConfig.FileName } } };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(fileResult), Encoding.UTF8, "application/json")
					};
				}

				if (req.Method == HttpMethod.Patch && url.Contains("file-stream-1"))
				{
					// Verify that req.Content is StreamContent (not StringContent)
					if (req.Content is StreamContent && !(req.Content is StringContent))
					{
						streamContentObserved = true;
						using var ms = new MemoryStream();
						await req.Content.CopyToAsync(ms);
						streamContentLength = ms.Length;
					}

					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				if (req.Method == HttpMethod.Get && url.Contains("file-stream-1?alt=media"))
				{
					byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(largePayload, StoragePayload.JsonOptions);
					var response = new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new ByteArrayContent(jsonBytes)
					};
					return response;
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("test-token"), config: config);

		// Act: Write large payload
		bool writeSuccess = await provider.WritePayloadAsync(largePayload);
		Assert.True(writeSuccess);
		Assert.True(streamContentObserved, "WritePayloadAsync MUST use StreamContent directly, NOT StringContent");
		Assert.True(streamContentLength > 85000, $"Payload size was {streamContentLength} bytes (exceeding 85KB LOH threshold)");

		// Act: Read large payload
		var readPayload = await provider.ReadPayloadAsync();
		Assert.NotNull(readPayload);
		Assert.Equal(1000, readPayload.DecisionLogs.Count);
		Assert.Equal("bulk-log-0000", readPayload.DecisionLogs[0].Id);
	}

	#endregion

	#region 3. Challenge: appDataFolder Scope & Forbidden spaces=drive Requests

	[Fact]
	public async Task Challenge_AppDataFolder_DriveApiRestrictsSpacesDrive_RevealsZombieFolderCall()
	{
		// In production, when an application uses OAuth scope https://www.googleapis.com/auth/drive.appdata,
		// Google Drive v3 REST API rejects any file listing with spaces=drive or folder creation in root drive with HTTP 403 Forbidden.
		// Let us simulate this real Google Drive API security boundary.

		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig
		{
			IsMockMode = false,
			ClientId = "test-client-appdata",
			Scope = "https://www.googleapis.com/auth/drive.appdata"
		};

		var requestsToSpacesDrive = new List<string>();

		var mockHandler = new InterceptingHttpHandler
		{
			Handler = async req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");

				// REAL GOOGLE DRIVE BEHAVIOR:
				// If scope is drive.appdata and spaces=drive is queried:
				if (url.Contains("spaces=drive"))
				{
					requestsToSpacesDrive.Add(url);
					// Real Google Drive returns 403 Forbidden for spaces=drive when only drive.appdata scope is granted:
					return new HttpResponseMessage(HttpStatusCode.Forbidden)
					{
						Content = new StringContent("{\"error\": {\"code\": 403, \"message\": \"Insufficient Permission: Request had insufficient authentication scopes for spaces=drive.\"}}")
					};
				}

				// If attempting to create a folder in root Drive (no parents=['appDataFolder']):
				if (req.Method == HttpMethod.Post && url.Contains("drive/v3/files"))
				{
					string body = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
					if (body.Contains("application/vnd.google-apps.folder") && !body.Contains("appDataFolder"))
					{
						requestsToSpacesDrive.Add("POST root folder: " + body);
						return new HttpResponseMessage(HttpStatusCode.Forbidden)
						{
							Content = new StringContent("{\"error\": {\"code\": 403, \"message\": \"Insufficient Permission: Cannot create folder in user root drive under drive.appdata scope.\"}}")
						};
					}

					// Creating file inside appDataFolder is ALLOWED:
					if (body.Contains("appDataFolder"))
					{
						var fileCreated = new { id = "appdata-file-1", name = GoogleDriveConfig.FileName };
						return new HttpResponseMessage(HttpStatusCode.OK)
						{
							Content = new StringContent(JsonSerializer.Serialize(fileCreated), Encoding.UTF8, "application/json")
						};
					}
				}

				// Querying files in appDataFolder is ALLOWED:
				if (url.Contains("spaces=appDataFolder"))
				{
					var fileResult = new { files = new[] { new { id = "appdata-file-1", name = GoogleDriveConfig.FileName } } };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(fileResult), Encoding.UTF8, "application/json")
					};
				}

				// Uploading/Patching file:
				if (req.Method == HttpMethod.Patch && url.Contains("appdata-file-1"))
				{
					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				// Downloading file:
				if (req.Method == HttpMethod.Get && url.Contains("appdata-file-1?alt=media"))
				{
					var payload = new StoragePayload
					{
						AppVersion = "2.0.0",
						DecisionLogs = [new DecisionLogEntity { Id = "appdata-log-1", ItemName = "Nitro Cold Brew" }]
					};
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, StoragePayload.JsonOptions))
					};
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("test-token"), config: config);

		var payload = new StoragePayload
		{
			AppVersion = "2.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "appdata-log-1", ItemName = "Nitro Cold Brew" }]
		};

		// EMPIRICAL TEST:
		// When WritePayloadAsync is executed against a strict drive.appdata server:
		bool writeResult = await provider.WritePayloadAsync(payload);

		// EMPIRICAL OBSERVATION & ASSERTIONS:
		// Verify GoogleDriveStorageProvider NEVER attempts to query or create in spaces=drive or root drive:
		Assert.Empty(requestsToSpacesDrive);
		Assert.True(writeResult, "WritePayloadAsync must succeed directly in appDataFolder without querying spaces=drive.");

		// Verify direct read from appDataFolder succeeds:
		var readPayload = await provider.ReadPayloadAsync();
		Assert.NotNull(readPayload);
		Assert.Single(readPayload.DecisionLogs);
		Assert.Equal("appdata-log-1", readPayload.DecisionLogs[0].Id);
		Assert.Empty(requestsToSpacesDrive);
	}

	#endregion

	private sealed class FakeGoogleDriveStorageProvider : IStorageProvider
	{
		public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;
		public string DisplayName => "Google Drive Fake";
		public bool RequiresAuthentication { get; set; } = true;
		public bool IsAuthenticated { get; set; } = false;
		public int AuthenticateCallCount { get; set; } = 0;
		public StoragePayload? StoredPayload { get; set; }

		public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(IsAuthenticated);

		public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
		{
			AuthenticateCallCount++;
			IsAuthenticated = true;
			return Task.FromResult(true);
		}

		public Task SignOutAsync(CancellationToken cancellationToken = default)
		{
			IsAuthenticated = false;
			return Task.CompletedTask;
		}

		public Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(StoredPayload);

		public Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default)
		{
			StoredPayload = payload;
			return Task.FromResult(true);
		}

		public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(IsAuthenticated);
	}
}
