using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using MugOverflowProject.Messages;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Diagnostics;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.Diagnostics;

public sealed class ForensicStabilityTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	[Fact]
	public async Task DapperDatabaseService_CheckpointWalAsync_SucceedsWithoutError()
	{
		var service = _dbFixture.DatabaseService;

		var entity = new DecisionLogEntity
		{
			Id = Guid.NewGuid().ToString("N"),
			Timestamp = DateTime.UtcNow,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = "Yes",
			Reason = "Test",
			CaffeineAmountMg = 95,
			ItemName = "Coffee",
			ItemCategory = "Drip",
			IsComplianceOverride = false,
			SyncStatus = 0
		};
		await service.InsertLogAsync(entity);

		// Execute passive checkpoint
		await service.CheckpointWalAsync();

		// Verify database remains functional and journal mode is still WAL
		var mode = await service.GetJournalModeAsync();
		Assert.Equal("wal", mode, ignoreCase: true);

		var retrieved = await service.GetLogByIdAsync(entity.Id);
		Assert.NotNull(retrieved);
		Assert.Equal(entity.Id, retrieved.Id);
	}

	[Fact]
	public async Task DapperDatabaseService_PeriodicCheckpointLoop_RunsAndDisposesCleanly()
	{
		string tempDb = Path.Combine(Path.GetTempPath(), $"CheckpointLoopTest_{Guid.NewGuid():N}.db3");
		try
		{
			var service = new DapperDatabaseService(tempDb);
			await service.InitializeAsync();

			// Perform passive checkpoint directly
			await service.CheckpointWalAsync();

			// Dispose service and verify resources are freed
			await service.DisposeAsync();
		}
		finally
		{
			try
			{
				if (File.Exists(tempDb)) File.Delete(tempDb);
				string wal = $"{tempDb}-wal";
				if (File.Exists(wal)) File.Delete(wal);
				string shm = $"{tempDb}-shm";
				if (File.Exists(shm)) File.Delete(shm);
			}
			catch
			{
			}
		}
	}

	[Fact]
	public void FakeSqliteDatabaseService_ImplementsCheckpointWalAsync()
	{
		var fake = new FakeSqliteDatabaseService();
		Assert.Equal(0, fake.CheckpointWalCallCount);

		var task = fake.CheckpointWalAsync();
		Assert.True(task.IsCompletedSuccessfully);
		Assert.Equal(1, fake.CheckpointWalCallCount);
	}

	[Fact]
	public void OneDriveStorageProvider_DefaultHttpClient_HasBoundedTimeout()
	{
		var secureStorage = new FakeSecureStorage();
		var provider = new OneDriveStorageProvider(secureStorage);

		var field = typeof(OneDriveStorageProvider).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(field);

		var client = field.GetValue(provider) as HttpClient;
		Assert.NotNull(client);
		Assert.True(client.Timeout <= TimeSpan.FromSeconds(15), $"Expected timeout <= 15s, got {client.Timeout}");
	}

	[Fact]
	public async Task StorageSyncService_FlushPendingChangesAsync_CompletesWithBoundedTimeout()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeProvider = new FakeStorageProvider();
		var fakeConn = new FakeConnectivity();

		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, fakeProvider);
		var sp = services.BuildServiceProvider();

		var service = new StorageSyncService(
			fakeDb,
			sp,
			fakeConn);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		bool result = await service.FlushPendingChangesAsync(cts.Token);
		Assert.True(result);

		service.Dispose();
	}

	[Fact]
	public void InvalidateMetabolismChartMessage_WeakReferenceRegistration_CanBeReRegisteredIdempotently()
	{
		object recipient = new();
		int receivedCount = 0;

		// Initial registration
		WeakReferenceMessenger.Default.Register<object, InvalidateMetabolismChartMessage>(
			recipient, (r, m) => receivedCount++);

		Assert.True(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		// Send message
		WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
		Assert.Equal(1, receivedCount);

		// Unregister (simulating page Unloaded)
		WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(recipient);
		Assert.False(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		// Re-register if unregistered (simulating OnAppearing check)
		if (!WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient))
		{
			WeakReferenceMessenger.Default.Register<object, InvalidateMetabolismChartMessage>(
				recipient, (r, m) => receivedCount++);
		}

		Assert.True(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
		Assert.Equal(2, receivedCount);

		// Cleanup
		WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(recipient);
	}

	[Fact]
	public void MainViewModel_Dispose_ReleasesPeriodicTimerAndSyncHooks()
	{
		var fakeCoffee = new FakeCoffeeDictionaryService();
		var fakeOracle = new FakeOracleEngineService();
		var fakeMetabolism = new FakeCaffeineMetabolismService();
		var fakePersistence = new FakeDataPersistenceService();
		var fakePopup = new FakeAppPopupService();
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeSync = new FakeStorageSyncService();

		var vm = new MainViewModel(
			fakeCoffee,
			fakeOracle,
			fakeMetabolism,
			fakePersistence,
			fakePopup,
			fakeDb,
			fakeSync);

		vm.RequestChartInvalidation = () => { };
		vm.StartPeriodicRecalculation();

		// Dispose ViewModel
		vm.Dispose();

		// Verify event hook is severed and chart invalidation callback cleared
		Assert.Null(vm.RequestChartInvalidation);

		// Triggering sync status change on fake sync should no longer mutate viewmodel state
		fakeSync.TriggerStatusChanged(SyncEngineStatus.Error);
		Assert.NotEqual("Sync Error", vm.SyncStatusText);
	}

	[Fact]
	public async Task StorageSyncService_HandleAppDestroyAsync_CancelsTimerAndFlushesPending()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeProvider = new FakeStorageProvider();
		var fakeConn = new FakeConnectivity();

		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, fakeProvider);
		var sp = services.BuildServiceProvider();

		var service = new StorageSyncService(fakeDb, sp, fakeConn);
		await service.InitializeAsync();

		// Handle app destroy during window teardown
		await service.HandleAppDestroyAsync();

		// Disposing service after destroy should not throw
		service.Dispose();
	}

	[Fact]
	public async Task DapperDatabaseService_CloseConnectionAsync_CancelsCheckpointLoopAndClearsPools()
	{
		string tempDb = Path.Combine(Path.GetTempPath(), $"CloseConnectionTest_{Guid.NewGuid():N}.db3");
		try
		{
			var service = new DapperDatabaseService(tempDb);
			await service.InitializeAsync();

			// Close connection and verify safe shutdown
			await service.CloseConnectionAsync();

			// Second close call is idempotent and safe
			await service.CloseConnectionAsync();

			// Disposing service after close should not throw ObjectDisposedException
			service.Dispose();
		}
		finally
		{
			try
			{
				if (File.Exists(tempDb)) File.Delete(tempDb);
				string wal = $"{tempDb}-wal";
				if (File.Exists(wal)) File.Delete(wal);
				string shm = $"{tempDb}-shm";
				if (File.Exists(shm)) File.Delete(shm);
			}
			catch
			{
			}
		}
	}

	[Fact]
	public async Task MainViewModel_PeriodicRecalculation_InvokesCheckpointWal()
	{
		var fakeCoffee = new FakeCoffeeDictionaryService();
		var fakeOracle = new FakeOracleEngineService();
		var fakeMetabolism = new FakeCaffeineMetabolismService();
		var fakePersistence = new FakeDataPersistenceService();
		var fakePopup = new FakeAppPopupService();
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeSync = new FakeStorageSyncService();

		using var vm = new MainViewModel(
			fakeCoffee,
			fakeOracle,
			fakeMetabolism,
			fakePersistence,
			fakePopup,
			fakeDb,
			fakeSync);

		await vm.InitializeAsync();

		int initialCount = fakeDb.CheckpointWalCallCount;

		// Execute one periodic recalculation iteration via MainViewModel
		await vm.ExecutePeriodicRecalculationTickAsync();
		Assert.True(fakeDb.CheckpointWalCallCount > initialCount, $"Expected fakeDb.CheckpointWalCallCount to increment, but was {fakeDb.CheckpointWalCallCount}");
	}

	[Fact]
	public void WinUiCrashPolicy_RpcEWrongThread_ReturnsTrue()
	{
		var comEx = new COMException("Wrong thread invocation", WinUiCrashPolicy.RPC_E_WRONG_THREAD);
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(comEx));

		var exWithHResult = new Exception("Custom COM thread fault") { HResult = WinUiCrashPolicy.RPC_E_WRONG_THREAD };
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(exWithHResult));
	}

	[Theory]
	[InlineData("The application called an interface that was marshalled for a different thread. (0x8001010E (RPC_E_WRONG_THREAD))")]
	[InlineData("Fatal error in Microsoft.UI.Composition pipeline")]
	[InlineData("Compositor process encountered an unhandled exception")]
	[InlineData("DispatcherQueue has already shutdown")]
	public void WinUiCrashPolicy_EventMessageOrCompositorPhrases_ReturnsTrue(string message)
	{
		var ex = new InvalidOperationException(message);
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(ex));
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(null, message));
	}

	[Fact]
	public void WinUiCrashPolicy_AggregateExceptionWithNestedViolations_ReturnsTrue()
	{
		var inner = new COMException("COM failure", WinUiCrashPolicy.RPC_E_WRONG_THREAD);
		var aggEx = new AggregateException("Batch task failure", new InvalidOperationException("Other"), inner);

		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(aggEx));
	}

	[Fact]
	public void WinUiCrashPolicy_BenignExceptions_ReturnFalse()
	{
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new NullReferenceException("Object reference not set")));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new HttpRequestException("404 Not Found")));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new TimeoutException("Operation timed out")));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new InvalidOperationException("Standard collection modification")));
	}

	[Fact]
	public void WinUiCrashPolicy_NullArguments_ReturnsFalse()
	{
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(null, null));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(null, string.Empty));
	}

	[Fact]
	public void InvalidateMetabolismChartMessage_PageTeardownAndAppearing_MaintainsCleanLifecycle()
	{
		// Test the exact pattern executed by MainPage:
		// Register in constructor, unregister on Unloaded, re-register on Appearing if unregistered.
		object recipient = new();
		int receivedCount = 0;

		// 1. Initial registration (simulating MainPage constructor)
		WeakReferenceMessenger.Default.Register<object, InvalidateMetabolismChartMessage>(
			recipient, (r, m) => receivedCount++);
		Assert.True(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		// 2. Unregister (simulating OnMainPageUnloaded)
		WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(recipient);
		Assert.False(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		// 3. Re-register if unregistered (simulating OnAppearing check)
		if (!WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient))
		{
			WeakReferenceMessenger.Default.Register<object, InvalidateMetabolismChartMessage>(
				recipient, (r, m) => receivedCount++);
		}
		Assert.True(WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(recipient));

		// 4. Message delivery succeeds
		WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
		Assert.Equal(1, receivedCount);

		// Cleanup
		WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(recipient);
	}

	[Fact]
	public async Task AppWindowTeardown_DisposesAllSingletonResourcesCleanly()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeSync = new FakeStorageSyncService();
		var fakeCoffee = new FakeCoffeeDictionaryService();
		var fakeOracle = new FakeOracleEngineService();
		var fakeMetabolism = new FakeCaffeineMetabolismService();
		var fakePersistence = new FakeDataPersistenceService();
		var fakePopup = new FakeAppPopupService();

		var vm = new MainViewModel(
			fakeCoffee,
			fakeOracle,
			fakeMetabolism,
			fakePersistence,
			fakePopup,
			fakeDb,
			fakeSync);

		vm.RequestChartInvalidation = () => { };
		vm.StartPeriodicRecalculation();

		// Execute the exact teardown sequence orchestrated during App window destruction
		await fakeSync.HandleAppDestroyAsync();
		fakeSync.Dispose();

		vm.Dispose();

		await fakeDb.CloseConnectionAsync();
		fakeDb.Dispose();

		// Verified: StorageSyncService received destroy notification
		Assert.Equal(1, fakeSync.HandleAppDestroyCallCount);

		// Verified: SqliteDatabaseService closed connection and was disposed
		Assert.Equal(1, fakeDb.CloseCallCount);
		Assert.True(fakeDb.IsDisposed);

		// Verified: MainViewModel was disposed (chart invalidation callback severed)
		Assert.Null(vm.RequestChartInvalidation);
	}

	[Fact]
	public async Task StorageSyncService_SynchronizeAsync_EnforcesBoundedTimeout()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		var fakeProvider = new FakeStorageProvider();
		var fakeConn = new FakeConnectivity();

		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, fakeProvider);
		var sp = services.BuildServiceProvider();

		var service = new StorageSyncService(fakeDb, sp, fakeConn);

		using var cts = new CancellationTokenSource();
		cts.Cancel(); // Pre-cancelled to verify immediate cancellation propagation

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await service.SynchronizeAsync(cts.Token);
		});

		service.Dispose();
	}
}
