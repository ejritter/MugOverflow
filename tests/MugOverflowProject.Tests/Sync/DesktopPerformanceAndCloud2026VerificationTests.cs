using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.Sync;

public sealed class DesktopPerformanceAndCloud2026VerificationTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeConnectivity _fakeConnectivity = new();
	private readonly FakeStorageProvider _fakeLocalStorage = new()
	{
		ProviderType = StorageProviderType.LocalStorage,
		DisplayName = "Fake Local Storage",
		RequiresAuthentication = false
	};
	private readonly FakeStorageProvider _fakeGoogleDrive = new()
	{
		ProviderType = StorageProviderType.GoogleDrive,
		DisplayName = "Fake Google Drive",
		RequiresAuthentication = true
	};

	private IServiceProvider _serviceProvider = null!;

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();

		var services = new ServiceCollection();
		services.AddSingleton<ISqliteDatabaseService>(_dbFixture.DatabaseService);
		services.AddSingleton<IConnectivity>(_fakeConnectivity);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, _fakeLocalStorage);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, _fakeGoogleDrive);
		services.AddSingleton<StorageSelectionViewModel>();

		_serviceProvider = services.BuildServiceProvider();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	#region 1. Memory & WeakEventManager Verification

	[Fact]
	public void WeakEventManager_AllowsSubscriberToBeCollectedByGC_WithoutUnsubscribing()
	{
		var service = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		WeakReference weakSubscriber = CreateAndAttachWeakSubscriber(service);

		// Force garbage collection
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		// The subscriber was not unsubscribed, but WeakEventManager must allow it to be collected
		Assert.False(weakSubscriber.IsAlive, "Subscriber must be garbage collected despite active subscription on singleton service.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndAttachWeakSubscriber(StorageSyncService service)
	{
		var subscriber = new TransientStatusSubscriber();
		service.StatusChanged += subscriber.OnStatusChanged;
		return new WeakReference(subscriber);
	}

	private sealed class TransientStatusSubscriber
	{
		public int EventCount { get; private set; }
		public void OnStatusChanged(object? sender, SyncEngineStatus status)
		{
			EventCount++;
		}
	}

	[Fact]
	public void StorageSelectionViewModel_CollectedByGC_AfterCleanUp()
	{
		var service = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		WeakReference weakVm = CreateAndCleanUpViewModel(service, _serviceProvider);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.False(weakVm.IsAlive, "StorageSelectionViewModel must be garbage collected after CleanUp/Dispose.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndCleanUpViewModel(IStorageSyncService syncService, IServiceProvider sp)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		vm.RequestClose = () => { };
		// Simulate popup CleanUp
		vm.RequestClose = null;
		vm.Dispose();
		return new WeakReference(vm);
	}

	#endregion

	#region 2. Background Sync Decoupling & AuthRequired Verification

	[Fact]
	public async Task FlushPendingChangesAsync_WhenNotAuthenticated_TransitionsToAuthRequired_WithoutCallingAuthenticateAsync()
	{
		// Arrange: Set up Google Drive as active provider, then simulate unauthenticated/expired session
		_fakeGoogleDrive.IsAuthenticated = true;
		var service = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);
		await service.InitializeAsync();
		await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);

		// Token expires or revoked
		_fakeGoogleDrive.IsAuthenticated = false;

		// Add pending record in SQLite
		var entity = SqliteTestDatabaseFixture.CreateSampleEntity("pending-test-1", syncStatus: (int)SyncStatus.Pending);
		await _dbFixture.DatabaseService.UpsertLogAsync(entity);

		// Act: Background flush
		bool result = await service.FlushPendingChangesAsync();

		// Assert:
		Assert.False(result);
		Assert.Equal(0, _fakeGoogleDrive.AuthenticateCallCount); // NEVER called interactive auth
		Assert.Equal(SyncEngineStatus.AuthRequired, service.Status);

		// Record in SQLite remains Pending (not lost or overwritten)
		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Single(pending);
		Assert.Equal("pending-test-1", pending[0].Id);
	}

	[Fact]
	public async Task SynchronizeAsync_WhenNotAuthenticated_TransitionsToAuthRequired_WithoutCallingAuthenticateAsync()
	{
		// Arrange: Switch to Google Drive, then simulate expired session
		_fakeGoogleDrive.IsAuthenticated = true;
		var service = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);
		await service.InitializeAsync();
		await service.SwitchProviderAsync(StorageProviderType.GoogleDrive, MigrationOption.TwoWayMergeBoth);

		_fakeGoogleDrive.IsAuthenticated = false;

		// Act: Synchronization
		bool result = await service.SynchronizeAsync();

		// Assert
		Assert.False(result);
		Assert.Equal(0, _fakeGoogleDrive.AuthenticateCallCount);
		Assert.Equal(SyncEngineStatus.AuthRequired, service.Status);
	}

	#endregion

	#region 3. HistoryLogs In-Place Diffing & Layout Stability Verification

	[Fact]
	public void SynchronizeHistoryLogsInPlace_WhenLogsAreIdentical_FiresZeroCollectionChangedEvents()
	{
		var vm = CreateMainViewModel();

		var log1 = new OracleDecisionLog { Id = 101L, CaffeineMg = 95, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow };
		var log2 = new OracleDecisionLog { Id = 102L, CaffeineMg = 150, UserDrank = false, OracleSaidYes = false, Timestamp = DateTime.UtcNow.AddMinutes(-30) };

		vm.HistoryLogs.Add(log1);
		vm.HistoryLogs.Add(log2);

		int collectionChangedCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) => collectionChangedCount++;

		// Act: Pass identical logs
		var identicalLogs = new List<OracleDecisionLog>
		{
			new OracleDecisionLog { Id = 101L, CaffeineMg = 95, UserDrank = true, OracleSaidYes = true, Timestamp = log1.Timestamp },
			new OracleDecisionLog { Id = 102L, CaffeineMg = 150, UserDrank = false, OracleSaidYes = false, Timestamp = log2.Timestamp }
		};

		vm.SynchronizeHistoryLogsInPlace(identicalLogs);

		// Assert: Zero layout thrashing
		Assert.Equal(0, collectionChangedCount);
		Assert.Equal(2, vm.HistoryLogs.Count);
	}

	[Fact]
	public void SynchronizeHistoryLogsInPlace_WhenItemAdded_FiresSingleAddEvent_AndPreservesExistingItems()
	{
		var vm = CreateMainViewModel();

		var existingLog = new OracleDecisionLog { Id = 101L, CaffeineMg = 95, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow.AddHours(-1) };
		vm.HistoryLogs.Add(existingLog);

		int resetCount = 0;
		int addCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) =>
		{
			if (e.Action == NotifyCollectionChangedAction.Reset) resetCount++;
			if (e.Action == NotifyCollectionChangedAction.Add) addCount++;
		};

		var newLog = new OracleDecisionLog { Id = 102L, CaffeineMg = 65, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow };
		var newLogs = new List<OracleDecisionLog> { newLog, existingLog };

		vm.SynchronizeHistoryLogsInPlace(newLogs);

		Assert.Equal(0, resetCount);
		Assert.Equal(1, addCount);
		Assert.Equal(2, vm.HistoryLogs.Count);
		Assert.Equal(102L, vm.HistoryLogs[0].Id);
		Assert.Same(existingLog, vm.HistoryLogs[1]); // Existing instance preserved!
	}

	[Fact]
	public void SynchronizeHistoryLogsInPlace_WhenItemRemoved_FiresSingleRemoveEvent()
	{
		var vm = CreateMainViewModel();

		var keepLog = new OracleDecisionLog { Id = 101L, CaffeineMg = 95, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow };
		var deleteLog = new OracleDecisionLog { Id = 102L, CaffeineMg = 50, UserDrank = false, OracleSaidYes = false, Timestamp = DateTime.UtcNow.AddHours(-1) };

		vm.HistoryLogs.Add(keepLog);
		vm.HistoryLogs.Add(deleteLog);

		int resetCount = 0;
		int removeCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) =>
		{
			if (e.Action == NotifyCollectionChangedAction.Reset) resetCount++;
			if (e.Action == NotifyCollectionChangedAction.Remove) removeCount++;
		};

		var newLogs = new List<OracleDecisionLog> { keepLog };

		vm.SynchronizeHistoryLogsInPlace(newLogs);

		Assert.Equal(0, resetCount);
		Assert.Equal(1, removeCount);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal(101L, vm.HistoryLogs[0].Id);
	}

	private MainViewModel CreateMainViewModel()
	{
		return new MainViewModel(
			new FakeCoffeeDictionaryService(),
			new FakeOracleEngineService(),
			new FakeCaffeineMetabolismService(),
			new FakeDataPersistenceService(),
			new FakeAppPopupService(),
			_dbFixture.DatabaseService,
			new FakeStorageSyncService());
	}

	#endregion

	#region 4. Google Drive 2026 Standards Verification

	[Fact]
	public void GoogleDriveConfig_Scope_TargetsAppDataFolder()
	{
		var config = new GoogleDriveConfig();
		Assert.Equal("https://www.googleapis.com/auth/drive.appdata", config.Scope);
	}

	[Fact]
	public async Task GoogleDriveStorageProvider_WriteAndReadPayload_UsesDirectStreamingAndAppDataFolder()
	{
		var fakeStorage = new FakeSecureStorage();
		var config = new GoogleDriveConfig { IsMockMode = false, ClientId = "test-client" };

		bool queriedAppDataFolder = false;
		bool createdWithAppDataFolderParent = false;
		bool usedStreamContentForUpload = false;

		var mockHandler = new MockHttpHandler
		{
			Handler = async req =>
			{
				string url = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");

				// Enforce: Zero queries to spaces=drive or folder MIME types
				if (url.Contains("spaces=drive") || url.Contains("application/vnd.google-apps.folder"))
				{
					return new HttpResponseMessage(HttpStatusCode.Forbidden)
					{
						Content = new StringContent("{\"error\": {\"code\": 403, \"message\": \"Forbidden under drive.appdata scope.\"}}")
					};
				}

				// 2. Search for caffeine_consumption.json in appDataFolder
				if (url.Contains("spaces=appDataFolder") && url.Contains("name = 'caffeine_consumption.json'"))
				{
					queriedAppDataFolder = true;
					var fileResult = new { files = Array.Empty<object>() };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(fileResult), Encoding.UTF8, "application/json")
					};
				}

				// 3. Create file metadata
				if (req.Method == HttpMethod.Post && url.Contains("drive/v3/files"))
				{
					string body = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
					if (body.Contains("appDataFolder"))
					{
						createdWithAppDataFolderParent = true;
					}

					var fileCreated = new { id = "file-appdata-99", name = GoogleDriveConfig.FileName };
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(JsonSerializer.Serialize(fileCreated), Encoding.UTF8, "application/json")
					};
				}

				// 4. Upload payload (PATCH)
				if (req.Method == HttpMethod.Patch && url.Contains("file-appdata-99"))
				{
					if (req.Content is StreamContent)
					{
						usedStreamContentForUpload = true;
					}

					return new HttpResponseMessage(HttpStatusCode.OK);
				}

				// 5. Read payload (GET)
				if (req.Method == HttpMethod.Get && url.Contains("file-appdata-99?alt=media"))
				{
					var payload = new StoragePayload
					{
						AppVersion = "2.0.0",
						DecisionLogs = [new DecisionLogEntity { Id = "stream-log-1", ItemName = "Stream Espresso" }]
					};
					byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, StoragePayload.JsonOptions);
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new ByteArrayContent(bytes)
					};
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			}
		};

		using var client = new HttpClient(mockHandler);
		var provider = new GoogleDriveStorageProvider(client, fakeStorage, _ => Task.FromResult<string?>("test-token"), config: config);

		var payloadToWrite = new StoragePayload
		{
			AppVersion = "2.0.0",
			DecisionLogs = [new DecisionLogEntity { Id = "stream-log-1", ItemName = "Stream Espresso" }]
		};

		// Write payload
		bool writeSuccess = await provider.WritePayloadAsync(payloadToWrite);
		Assert.True(writeSuccess);
		Assert.True(queriedAppDataFolder, "FindFileIdAsync must query with spaces=appDataFolder");
		Assert.True(createdWithAppDataFolderParent, "CreateFileMetadataAsync must create file with parents: ['appDataFolder']");
		Assert.True(usedStreamContentForUpload, "WritePayloadAsync must upload using StreamContent (0 LOH string allocation)");

		// Read payload
		var readPayload = await provider.ReadPayloadAsync();
		Assert.NotNull(readPayload);
		Assert.Single(readPayload.DecisionLogs);
		Assert.Equal("stream-log-1", readPayload.DecisionLogs[0].Id);
	}

	private sealed class MockHttpHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } =
			_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Handler(request);
	}

	#endregion
}
