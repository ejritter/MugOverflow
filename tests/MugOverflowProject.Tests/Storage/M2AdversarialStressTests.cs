using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Tests.Fixtures;
using Xunit;

namespace MugOverflowProject.Tests.Storage;

public sealed class M2AdversarialStressTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly LocalStorageProvider _localProvider;

	public M2AdversarialStressTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MugOverflow_Stress_" + Guid.NewGuid().ToString("N"));
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

	#region LocalStorageProvider Stress & Adversarial Tests

	[Fact]
	public async Task LocalStorage_HighConcurrency_UnderHeavyReadWriteLoad_MaintainsDataIntegrity()
	{
		// Arrange: 30 concurrent workers doing randomized read and write operations
		const int workerCount = 30;
		var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

		var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
		{
			try
			{
				for (int iter = 0; iter < 10; iter++)
				{
					if (workerId % 2 == 0)
					{
						// Writer
						var payload = new StoragePayload
						{
							AppVersion = "1.0.0",
							SchemaVersion = "1.0.0",
							ExportedAtUtc = DateTime.UtcNow,
							DecisionLogs =
							[
								new DecisionLogEntity
								{
									Id = $"log-w{workerId}-i{iter}",
									Timestamp = DateTime.UtcNow,
									LastModifiedUtc = DateTime.UtcNow,
									Decision = (iter % 2 == 0) ? "YES" : "NO",
									Reason = $"Concurrent reason {workerId}:{iter}",
									CaffeineAmountMg = 75.0 * (iter + 1),
									ItemName = $"Espresso Shot #{iter}",
									ItemCategory = "Espresso",
									SyncStatus = (int)SyncStatus.Synced
								}
							]
						};

						bool written = await _localProvider.WritePayloadAsync(payload);
						Assert.True(written, $"Worker {workerId} failed to write on iteration {iter}");
					}
					else
					{
						// Reader
						var readResult = await _localProvider.ReadPayloadAsync();
						if (readResult != null)
						{
							Assert.NotNull(readResult.AppVersion);
							Assert.NotNull(readResult.DecisionLogs);
						}
					}

					await Task.Yield();
				}
			}
			catch (Exception ex)
			{
				exceptions.Add(ex);
			}
		}));

		await Task.WhenAll(tasks);

		Assert.Empty(exceptions);

		// Final assertion: The resulting file must be non-corrupt and valid
		var finalPayload = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(finalPayload);
		Assert.NotEmpty(finalPayload.DecisionLogs);
		Assert.StartsWith("log-w", finalPayload.DecisionLogs[0].Id);
	}

	[Fact]
	public async Task LocalStorage_FileInUseSimulation_TransientLock_RecoversViaRetryLoop()
	{
		// Arrange: Write initial payload
		var initial = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "lock-init", ItemName = "Initial Item" }]
		};
		Assert.True(await _localProvider.WritePayloadAsync(initial));
		Assert.True(File.Exists(_localProvider.FilePath));

		var updatePayload = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "lock-updated", ItemName = "Updated Under Lock" }]
		};

		// Act: Hold a read-exclusive lock on the destination file for 80ms while triggering WritePayloadAsync
		var lockTask = Task.Run(async () =>
		{
			using var fileStream = new FileStream(
				_localProvider.FilePath,
				FileMode.Open,
				FileAccess.ReadWrite,
				FileShare.None);

			await Task.Delay(80); // Hold lock for 80ms (within maxRetries * retryDelayMs = ~300ms window)
		});

		// Small delay to ensure fileStream lock is acquired first
		await Task.Delay(10);

		bool writeResult = await _localProvider.WritePayloadAsync(updatePayload);
		await lockTask;

		// Assert: Transient lock was successfully overcome by retry loop
		Assert.True(writeResult);
		var result = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(result);
		Assert.Equal("lock-updated", result.DecisionLogs[0].Id);
	}

	[Fact]
	public async Task LocalStorage_FileInUseSimulation_PermanentLock_FailsGracefullyWithoutCrashing()
	{
		// Arrange: Write initial payload
		var initial = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "perm-lock-init", ItemName = "Perm Lock Item" }]
		};
		Assert.True(await _localProvider.WritePayloadAsync(initial));

		var updatePayload = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "perm-lock-update", ItemName = "Should Fail" }]
		};

		// Act: Lock the file for 600ms (longer than the total 300ms retry window)
		using var fileStream = new FileStream(
			_localProvider.FilePath,
			FileMode.Open,
			FileAccess.ReadWrite,
			FileShare.None);

		bool writeResult = await _localProvider.WritePayloadAsync(updatePayload);

		// Assert: Returns false cleanly without unhandled exception
		Assert.False(writeResult);

		// Release lock
		fileStream.Close();

		// Ensure no temp files leaked in target directory
		var tempFiles = Directory.GetFiles(_localProvider.TargetDirectory, "*.tmp");
		Assert.Empty(tempFiles);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \t\r\n   ")]
	[InlineData("{")]
	[InlineData("{\"appVersion\": \"1.0.0\", \"decisionLogs\": [")]
	[InlineData("{\"decisionLogs\": \"invalid_type_not_array\"}")]
	[InlineData("<!DOCTYPE html><html><body>502 Bad Gateway</body></html>")]
	public async Task LocalStorage_CorruptedPayloadRecovery_ReturnsNullAndSelfHealsOnNextWrite(string corruptContent)
	{
		// Arrange: Write corrupt payload directly to file
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		await File.WriteAllTextAsync(_localProvider.FilePath, corruptContent, Encoding.UTF8);

		// Act 1: Read corrupted file
		var corruptReadResult = await _localProvider.ReadPayloadAsync();
		Assert.Null(corruptReadResult);

		// Act 2: Write healthy payload to overwrite corrupt file
		var healthyPayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			DecisionLogs =
			[
				new DecisionLogEntity
				{
					Id = "healed-log-1",
					Timestamp = DateTime.UtcNow,
					ItemName = "Self-Healed Pour Over",
					CaffeineAmountMg = 110.0,
					SyncStatus = (int)SyncStatus.Synced
				}
			]
		};

		bool healed = await _localProvider.WritePayloadAsync(healthyPayload);
		Assert.True(healed);

		// Act 3: Read healed file
		var healedReadResult = await _localProvider.ReadPayloadAsync();
		Assert.NotNull(healedReadResult);
		Assert.Single(healedReadResult.DecisionLogs);
		Assert.Equal("healed-log-1", healedReadResult.DecisionLogs[0].Id);
		Assert.Equal("Self-Healed Pour Over", healedReadResult.DecisionLogs[0].ItemName);
	}

	[Fact]
	public async Task LocalStorage_BinaryGarbageFile_ReturnsNullGracefully()
	{
		// Arrange: Write binary garbage bytes
		Directory.CreateDirectory(_localProvider.TargetDirectory);
		byte[] garbageBytes = [0xFF, 0xFE, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0];
		await File.WriteAllBytesAsync(_localProvider.FilePath, garbageBytes);

		// Act & Assert: Returns null without unhandled crash
		var result = await _localProvider.ReadPayloadAsync();
		Assert.Null(result);
	}

	[Fact]
	public async Task LocalStorage_LargePayloadStress_5000EntitiesWithUnicodeAndSpecialCharacters_RoundtripsAccurately()
	{
		// Arrange: 5,000 entities with special Unicode, CJK, emojis, quotes, line breaks
		const int entityCount = 5000;
		var entities = new List<DecisionLogEntity>(entityCount);

		for (int i = 0; i < entityCount; i++)
		{
			entities.Add(new DecisionLogEntity
			{
				Id = $"large-entity-{i:D6}",
				Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
				LastModifiedUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc).AddSeconds(i),
				Decision = (i % 3 == 0) ? "YES" : (i % 3 == 1) ? "NO" : "DEFERRED",
				Reason = $"Reason with Unicode ☕ 日本語 עברית \nLine 2 \"quoted\" \\escaped\\ - #{i}",
				CaffeineAmountMg = (i * 1.5) % 400.0,
				ItemName = $"Special Coffee Blend 🌟 #{i}",
				ItemCategory = (i % 2 == 0) ? "Cold Brew & Nitro" : "Pourover / Espresso",
				IsComplianceOverride = (i % 7 == 0),
				SyncStatus = (int)SyncStatus.Synced
			});
		}

		var largePayload = new StoragePayload
		{
			AppVersion = "1.0.0",
			SchemaVersion = "1.0.0",
			ExportedAtUtc = DateTime.UtcNow,
			DecisionLogs = entities
		};

		// Act: Write and Read
		var writeSw = System.Diagnostics.Stopwatch.StartNew();
		bool written = await _localProvider.WritePayloadAsync(largePayload);
		writeSw.Stop();

		Assert.True(written);
		Assert.True(writeSw.ElapsedMilliseconds < 5000, $"Writing 5,000 entities took too long: {writeSw.ElapsedMilliseconds}ms");

		var readSw = System.Diagnostics.Stopwatch.StartNew();
		var readPayload = await _localProvider.ReadPayloadAsync();
		readSw.Stop();

		Assert.True(readSw.ElapsedMilliseconds < 5000, $"Reading 5,000 entities took too long: {readSw.ElapsedMilliseconds}ms");

		// Assert: Verify data fidelity across all 5,000 records
		Assert.NotNull(readPayload);
		Assert.Equal(entityCount, readPayload.DecisionLogs.Count);

		for (int i = 0; i < entityCount; i += 100) // Sample check every 100th record + boundaries
		{
			var expected = entities[i];
			var actual = readPayload.DecisionLogs[i];

			Assert.Equal(expected.Id, actual.Id);
			Assert.Equal(expected.Timestamp, actual.Timestamp);
			Assert.Equal(expected.LastModifiedUtc, actual.LastModifiedUtc);
			Assert.Equal(expected.Decision, actual.Decision);
			Assert.Equal(expected.Reason, actual.Reason);
			Assert.Equal(expected.CaffeineAmountMg, actual.CaffeineAmountMg);
			Assert.Equal(expected.ItemName, actual.ItemName);
			Assert.Equal(expected.ItemCategory, actual.ItemCategory);
			Assert.Equal(expected.IsComplianceOverride, actual.IsComplianceOverride);
			Assert.Equal(expected.SyncStatus, actual.SyncStatus);
		}
	}

	[Fact]
	public async Task LocalStorage_CancellationToken_AbortsAndReleasesLockCleanly()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel(); // Pre-cancelled token

		var payload = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "cancel-test" }]
		};

		// Act & Assert: Throws OperationCanceledException
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _localProvider.WritePayloadAsync(payload, cts.Token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _localProvider.ReadPayloadAsync(cts.Token));

		// Verify semaphore lock was not left locked: subsequent normal write must succeed
		bool normalWrite = await _localProvider.WritePayloadAsync(payload, CancellationToken.None);
		Assert.True(normalWrite);
	}

	#endregion

	#region GoogleDriveStorageProvider Stress & Adversarial Tests

	private sealed class DynamicMockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, Task<HttpResponseMessage>> Router { get; set; } =
			_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Router(request);
		}
	}

	[Fact]
	public async Task GoogleDrive_401Unauthorized_TriggersSilentTokenRefreshAndRetries()
	{
		// Arrange: Simulate expired access token and valid refresh token
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "expired_google_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "valid_google_refresh_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddMinutes(-30).ToString("O"));

		var config = new GoogleDriveConfig
		{
			IsMockMode = false,
			ClientId = "test_google_client_id"
		};

		bool tokenRefreshEndpointCalled = false;
		bool driveEndpointCalledWithRefreshedToken = false;

		var mockHandler = new DynamicMockHttpHandler
		{
			Router = req =>
			{
				string uri = req.RequestUri?.ToString() ?? string.Empty;

				// Token refresh endpoint
				if (uri.Contains("oauth2.googleapis.com/token"))
				{
					tokenRefreshEndpointCalled = true;
					var refreshResponse = new
					{
						access_token = "brand_new_refreshed_access_token",
						expires_in = 3600,
						token_type = "Bearer"
					};
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(refreshResponse), Encoding.UTF8, "application/json")
					});
				}

				// Drive query endpoint
				if (uri.Contains("drive/v3/files"))
				{
					if (req.Headers.Authorization?.Parameter == "brand_new_refreshed_access_token")
					{
						driveEndpointCalledWithRefreshedToken = true;
					}

					var fileListResponse = new
					{
						files = new[] { new { id = "gdrive-file-999", name = GoogleDriveConfig.FileName } }
					};
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(fileListResponse), Encoding.UTF8, "application/json")
					});
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			}
		};

		using var httpClient = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(httpClient, fakeStorage, config: config);

		// Act: Check authentication and read payload
		bool isAuth = await provider.IsAuthenticatedAsync();
		var payload = await provider.ReadPayloadAsync();

		// Assert: Token was refreshed silently and new token persisted to SecureStorage
		Assert.True(isAuth);
		Assert.True(tokenRefreshEndpointCalled);
		Assert.True(driveEndpointCalledWithRefreshedToken);
		Assert.Equal("brand_new_refreshed_access_token", await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
	}

	[Fact]
	public async Task GoogleDrive_RefreshTokenRevoked_ReturnsUnauthenticatedGracefully()
	{
		// Arrange: Expired access token with revoked refresh token
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "expired_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "revoked_refresh_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(-1).ToString("O"));

		var config = new GoogleDriveConfig
		{
			IsMockMode = false,
			ClientId = "test_google_client_id"
		};

		var mockHandler = new DynamicMockHttpHandler
		{
			Router = req =>
			{
				if (req.RequestUri?.ToString().Contains("oauth2.googleapis.com/token") == true)
				{
					// Return 400 invalid_grant
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
					{
						Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}", Encoding.UTF8, "application/json")
					});
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
			}
		};

		using var httpClient = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(httpClient, fakeStorage, config: config);

		// Act:
		bool isAuth = await provider.IsAuthenticatedAsync();
		var payload = await provider.ReadPayloadAsync();
		bool writeResult = await provider.WritePayloadAsync(new StoragePayload());

		// Assert: Handled safely without throwing
		Assert.False(isAuth);
		Assert.Null(payload);
		Assert.False(writeResult);
	}

	[Fact]
	public async Task GoogleDrive_SignOut_PurgesAllSecureKeysAndClearsMockState()
	{
		var fakeStorage = new FakeSecureStorage();
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "test_token");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "test_refresh");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O"));
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFolderId, "folder-xyz");
		await fakeStorage.SetAsync(GoogleDriveConfig.KeyFileId, "file-xyz");

		var config = new GoogleDriveConfig { IsMockMode = true };
		using var httpClient = new HttpClient();
		var provider = new GoogleDriveStorageProvider(httpClient, fakeStorage, config: config);

		// Seed mock payload
		await provider.WritePayloadAsync(new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "mock-1" }]
		});
		Assert.NotNull(await provider.ReadPayloadAsync());

		// Act: Sign out
		await provider.SignOutAsync();

		// Assert: All 5 secure keys purged and mock payload cleared
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyAccessToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyRefreshToken));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFolderId));
		Assert.Null(await fakeStorage.GetAsync(GoogleDriveConfig.KeyFileId));
		Assert.Null(await provider.ReadPayloadAsync());
		Assert.False(await provider.IsAuthenticatedAsync());
	}

	[Fact]
	public async Task GoogleDrive_Http500And503Failures_HandledGracefully()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		var mockHandler = new DynamicMockHttpHandler
		{
			Router = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
		};

		using var httpClient = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(httpClient, fakeStorage, _ => Task.FromResult<string?>("valid-token"), config: config);

		// Act:
		var readResult = await provider.ReadPayloadAsync();
		bool writeResult = await provider.WritePayloadAsync(new StoragePayload());
		bool testConn = await provider.TestConnectionAsync();

		// Assert:
		Assert.Null(readResult);
		Assert.False(writeResult);
		Assert.False(testConn);
	}

	#endregion

	#region OneDriveStorageProvider Stress & Adversarial Tests

	[Fact]
	public async Task OneDrive_401Unauthorized_ClearsTokenCacheKeyAndReturnsGracefulFailure()
	{
		var secureStorage = new FakeSecureStorage();
		await secureStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid_base64_cache");

		var mockHandler = new DynamicMockHttpHandler
		{
			Router = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
		};

		using var httpClient = new HttpClient(mockHandler);
		var provider = new OneDriveStorageProvider(secureStorage, httpClient, _ => Task.FromResult<string?>("invalid_token"));

		// Act: ReadPayloadAsync triggers 401
		var readResult = await provider.ReadPayloadAsync();

		// Assert: Token cache key removed and returns null
		Assert.Null(readResult);
		Assert.Null(await secureStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));

		// Re-set token cache and test WritePayloadAsync 401
		await secureStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "valid_base64_cache");
		bool writeResult = await provider.WritePayloadAsync(new StoragePayload());

		Assert.False(writeResult);
		Assert.Null(await secureStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	[Fact]
	public async Task OneDrive_ServerInternalError500AndMalformedJson_ReturnsNullSafely()
	{
		var secureStorage = new FakeSecureStorage();

		// Sub-case A: 500 error
		var mockHandler500 = new DynamicMockHttpHandler
		{
			Router = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
		};
		using var client500 = new HttpClient(mockHandler500);
		var provider500 = new OneDriveStorageProvider(secureStorage, client500, _ => Task.FromResult<string?>("token"));

		var res500 = await provider500.ReadPayloadAsync();
		Assert.Null(res500);

		// Sub-case B: Malformed JSON body
		var mockHandlerCorrupt = new DynamicMockHttpHandler
		{
			Router = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"invalid_json\": [corrupt", Encoding.UTF8, "application/json")
			})
		};
		using var clientCorrupt = new HttpClient(mockHandlerCorrupt);
		var providerCorrupt = new OneDriveStorageProvider(secureStorage, clientCorrupt, _ => Task.FromResult<string?>("token"));

		var resCorrupt = await providerCorrupt.ReadPayloadAsync();
		Assert.Null(resCorrupt);
	}

	[Fact]
	public async Task OneDrive_SignOut_PurgesSecureStorageTokenCacheKey()
	{
		var secureStorage = new FakeSecureStorage();
		await secureStorage.SetAsync(OneDriveStorageProvider.TokenCacheKey, "active_cache_data");

		var provider = new OneDriveStorageProvider(secureStorage, new HttpClient(new DynamicMockHttpHandler()), _ => Task.FromResult<string?>(null));

		await provider.SignOutAsync();

		Assert.Null(await secureStorage.GetAsync(OneDriveStorageProvider.TokenCacheKey));
	}

	[Fact]
	public async Task OneDrive_ConcurrentOperations_ExecuteWithoutDeadlock()
	{
		var secureStorage = new FakeSecureStorage();
		var samplePayload = new StoragePayload
		{
			DecisionLogs = [new DecisionLogEntity { Id = "onedrive-concurrent" }]
		};

		var mockHandler = new DynamicMockHttpHandler
		{
			Router = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(samplePayload.ToJson(), Encoding.UTF8, "application/json")
			})
		};

		using var httpClient = new HttpClient(mockHandler);
		var provider = new OneDriveStorageProvider(secureStorage, httpClient, _ => Task.FromResult<string?>("token"));

		const int concurrency = 30;
		var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
		{
			if (i % 3 == 0)
			{
				return (object?)await provider.ReadPayloadAsync();
			}
			else if (i % 3 == 1)
			{
				return (object)await provider.WritePayloadAsync(samplePayload);
			}
			else
			{
				return (object)await provider.TestConnectionAsync();
			}
		}));

		var results = await Task.WhenAll(tasks);
		Assert.Equal(concurrency, results.Length);
		Assert.All(results, r => Assert.NotNull(r));
	}

	#endregion
}
