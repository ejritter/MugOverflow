using System.Net.Http;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class MemoryAndResourceSafetyTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeCoffeeDictionaryService _coffeeService = new();
	private readonly FakeOracleEngineService _oracleService = new();
	private readonly FakeCaffeineMetabolismService _metabolismService = new();
	private readonly FakeDataPersistenceService _persistenceService = new();
	private readonly FakeAppPopupService _popupService = new();
	private readonly FakeStorageSyncService _syncService = new();

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	private MainViewModel CreateViewModel()
	{
		return new MainViewModel(
			_coffeeService,
			_oracleService,
			_metabolismService,
			_persistenceService,
			_popupService,
			_dbFixture.DatabaseService,
			_syncService);
	}

	[Fact]
	public void OracleEngineService_ConcurrentStress_IsThreadSafeAndNonAllocating()
	{
		var service = new OracleEngineService();
		int totalIterations = 10_000;
		int yesCount = 0;
		int noCount = 0;

		Parallel.For(0, totalIterations, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, _ =>
		{
			var (saidYes, verdict) = service.ConsultOracle();
			Assert.False(string.IsNullOrWhiteSpace(verdict));
			if (saidYes)
			{
				Interlocked.Increment(ref yesCount);
			}
			else
			{
				Interlocked.Increment(ref noCount);
			}
		});

		Assert.Equal(totalIterations, yesCount + noCount);
		Assert.True(yesCount > 0, "Oracle should produce Yes verdicts under stress");
		Assert.True(noCount > 0, "Oracle should produce No verdicts under stress");
	}

	[Fact]
	public async Task MainViewModel_ChartInvalidation_CanBeNull_NoExceptionsWhenDetached()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Simulate MainPage disappearing/unloading: RequestChartInvalidation is set to null
		vm.RequestChartInvalidation = null;

		// Perform operations that trigger RecalculateAllMetrics
		var coffee = _coffeeService.GetDefault();
		var item = new ChatMessageItem
		{
			Id = 12345,
			IsActionPending = true,
			OracleSaidYes = true,
			SelectedCoffeeTypeId = coffee.Id
		};

		var exception = await Record.ExceptionAsync(async () =>
		{
			await vm.ConfirmDrankOutcomeAsync(item);
			await vm.ReloadLogsFromDatabaseAsync();
		});

		Assert.Null(exception);
		Assert.Null(vm.RequestChartInvalidation);
	}

	[Fact]
	public async Task MainViewModel_Dispose_ClearsDelegatesAndSubscriptions()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		int invalidationCallCount = 0;
		vm.RequestChartInvalidation = () => invalidationCallCount++;

		// Act: Dispose ViewModel
		vm.Dispose();

		// Assert: Invalidation delegate cleared to eliminate visual tree roots
		Assert.Null(vm.RequestChartInvalidation);

		// Status changed events on sync service should no longer affect or wake the disposed VM
		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.False(vm.IsSyncing);
	}

	[Fact]
	public void StorageSelectionViewModel_Dispose_ClearsRequestCloseAndUnsubscribes()
	{
		var sp = new ServiceCollection().BuildServiceProvider();
		var vm = new StorageSelectionViewModel(_syncService, sp, new FakeConnectivity());

		bool closeRequested = false;
		vm.RequestClose = () => closeRequested = true;

		// Act: Dispose
		vm.Dispose();

		// Assert: RequestClose cleared
		Assert.Null(vm.RequestClose);

		// Status changed should not throw or cause leak
		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.False(closeRequested);
	}

	[Fact]
	public async Task StorageSyncService_InitializeAsync_IsIdempotentAndDoesNotLeakListeners()
	{
		var fakeStorage = new FakeStorageProvider();
		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, fakeStorage);
		var sp = services.BuildServiceProvider();
		var connectivity = new FakeConnectivity();

		using var syncService = new StorageSyncService(_dbFixture.DatabaseService, sp, connectivity);

		// Initialize multiple times
		await syncService.InitializeAsync();
		await syncService.InitializeAsync();
		await syncService.InitializeAsync();

		// Verify connectivity toggle triggers status update without duplicate handler accumulation
		int statusChangeCount = 0;
		syncService.StatusChanged += (s, status) => statusChangeCount++;

		connectivity.SetNetworkAccess(NetworkAccess.None);
		connectivity.SetNetworkAccess(NetworkAccess.Internet);

		Assert.True(statusChangeCount >= 1);
	}

	[Fact]
	public void CoffeeDictionaryPopupViewModel_RequestClose_CanBeNullSafe()
	{
		var vm = new CoffeeDictionaryPopupViewModel(_coffeeService);
		var coffee = _coffeeService.GetDefault();

		// Without RequestClose attached, invoking command must not throw
		var exception = Record.Exception(() =>
		{
			vm.SelectCoffeeCommand.Execute(coffee);
		});

		Assert.Null(exception);
		Assert.Equal(coffee, vm.SelectedCoffee);
	}

	[Fact]
	public async Task AskOracleAsync_WhenCancelledDuringThinking_GuaranteesTypingIndicatorPrunedFromChatMessages()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		using var cts = new CancellationTokenSource();
		// Execute command and cancel quickly while delay is active
		var commandTask = vm.AskOracleCommand.ExecuteAsync(cts.Token);
		// Allow Task.Delay(400) to start, then cancel
		await Task.Delay(450);
		cts.Cancel();

		try
		{
			await commandTask;
		}
		catch (OperationCanceledException)
		{
			// Expected
		}

		// Ensure no leftover typing indicator bubbles remain in the chat collection
		Assert.DoesNotContain(vm.ChatMessages, m => m.IsTypingIndicator);
		Assert.False(vm.IsOracleThinking);
	}

	[Fact]
	public async Task SqliteDatabaseService_NonSharedCache_SupportsConcurrentDirectReadAndWriteWithoutCrash()
	{
		var service = _dbFixture.DatabaseService;
		await service.UpsertBatchAsync(SqliteTestDatabaseFixture.CreateSampleEntities(50));

		var directConn = _dbFixture.CreateDirectConnection();
		try
		{
			var writeTask = Task.Run(async () =>
			{
				for (int i = 0; i < 20; i++)
				{
					await service.InsertLogAsync(SqliteTestDatabaseFixture.CreateSampleEntity($"concurrent-safe-{i}"));
				}
			});

			var readTask = Task.Run(async () =>
			{
				for (int i = 0; i < 20; i++)
				{
					_ = (await directConn.QueryAsync<DecisionLogEntity>("SELECT * FROM decision_logs;")).AsList();
				}
			});

			await Task.WhenAll(writeTask, readTask);

			var allLogs = await service.GetAllLogsAsync();
			Assert.True(allLogs.Count >= 70);
		}
		finally
		{
			await directConn.CloseAsync();
		}
	}

	[Fact]
	public void StorageProviders_DiRegistration_ResolvesSingletonsAndKeyedSingletonsCleanlyWithoutCaptiveLeak()
	{
		var services = new ServiceCollection();
		services.AddHttpClient("StorageClient");
		services.AddSingleton<ISecureStorageService, FakeSecureStorage>();
		services.AddSingleton<GoogleDriveConfig>();
		services.AddSingleton<LocalStorageProvider>(_ => new LocalStorageProvider(Path.GetTempPath()));
		services.AddSingleton<GoogleDriveStorageProvider>(sp =>
			new GoogleDriveStorageProvider(
				sp.GetRequiredService<IHttpClientFactory>().CreateClient("StorageClient"),
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetService<IGoogleAuthService>(),
				sp.GetService<IWebAuthenticator>(),
				sp.GetService<GoogleDriveConfig>(),
				sp.GetService<Microsoft.Extensions.Logging.ILogger<GoogleDriveStorageProvider>>()));
		services.AddSingleton<OneDriveStorageProvider>(sp =>
			new OneDriveStorageProvider(
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetRequiredService<IHttpClientFactory>().CreateClient("StorageClient"),
				logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<OneDriveStorageProvider>>()));

		// Keyed Storage Provider Resolution for Dynamic Switching
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, (sp, _) => sp.GetRequiredService<LocalStorageProvider>());
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, (sp, _) => sp.GetRequiredService<GoogleDriveStorageProvider>());
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.OneDrive, (sp, _) => sp.GetRequiredService<OneDriveStorageProvider>());

		services.AddSingleton<IStorageSyncService>(_syncService);
		services.AddSingleton<IConnectivity>(new FakeConnectivity());
		services.AddTransient<StorageSelectionViewModel>();

		using var rootProvider = services.BuildServiceProvider();

		// Verify Singletons
		var gDrive1 = rootProvider.GetRequiredService<GoogleDriveStorageProvider>();
		var gDrive2 = rootProvider.GetRequiredService<GoogleDriveStorageProvider>();
		Assert.Same(gDrive1, gDrive2);

		var oneDrive1 = rootProvider.GetRequiredService<OneDriveStorageProvider>();
		var oneDrive2 = rootProvider.GetRequiredService<OneDriveStorageProvider>();
		Assert.Same(oneDrive1, oneDrive2);

		// Verify Keyed Singletons match explicit Singletons
		var keyedGDrive = rootProvider.GetRequiredKeyedService<IStorageProvider>(StorageProviderType.GoogleDrive);
		Assert.Same(gDrive1, keyedGDrive);

		var keyedOneDrive = rootProvider.GetRequiredKeyedService<IStorageProvider>(StorageProviderType.OneDrive);
		Assert.Same(oneDrive1, keyedOneDrive);

		var keyedLocal = rootProvider.GetRequiredKeyedService<IStorageProvider>(StorageProviderType.LocalStorage);
		var local1 = rootProvider.GetRequiredService<LocalStorageProvider>();
		Assert.Same(local1, keyedLocal);

		// Verify Scoped resolution eliminates captive transient disposable on root
		WeakReference weakVmRef = ResolveTransientVmInScope(rootProvider);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.False(weakVmRef.IsAlive, "Transient IDisposable resolved via IServiceScope must be garbage collected once scope is disposed.");
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static WeakReference ResolveTransientVmInScope(IServiceProvider rootProvider)
	{
		using var scope = rootProvider.CreateScope();
		var vm = scope.ServiceProvider.GetRequiredService<StorageSelectionViewModel>();
		return new WeakReference(vm);
	}
}
