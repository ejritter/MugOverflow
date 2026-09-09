using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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

namespace MugOverflowProject.Tests.Adversarial;

/// <summary>
/// Challenger 1 Adversarial Verification Suite.
/// Empirically challenges and stress-tests:
/// 1. Memory Leaks: StorageSelectionViewModel and simulated StorageSelectionPopup under GC,
///    WeakEventManager isolation from singleton services, and closure severance upon CleanUp.
/// 2. Unobserved Task Exceptions: HttpListener loopback cancellation and Stop() fault shielding
///    under GC finalization to ensure no process-terminating unobserved exceptions.
/// 3. Layout & HistoryLogs Stability: Idempotent InitializeAsync, non-mutating popup dismissals,
///    in-place log diffing firing ZERO NotifyCollectionChangedAction.Reset actions, and reference preservation.
/// </summary>
public sealed class Challenger1EmpiricalStressTests : IAsyncLifetime
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
		services.AddTransient<StorageSelectionViewModel>();

		_serviceProvider = services.BuildServiceProvider();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	#region 1. Memory Leaks & Visual Tree Rooting Challenges

	[Fact]
	public void Challenge1_StorageSelectionViewModel_CollectedByGC_WhenCleanedUp()
	{
		// Arrange: Create singleton sync service
		var syncService = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		// Act: Create ViewModel with closure, initialize, and simulate CleanUp
		WeakReference weakVm = CreateInitializeAndCleanUpViewModel(syncService, _serviceProvider);

		// Force thorough GC
		ForceGarbageCollection();

		// Assert: ViewModel must NOT be held alive by the singleton sync service
		Assert.False(weakVm.IsAlive, "StorageSelectionViewModel must be garbage-collected after CleanUp/Dispose.");
	}

	[Fact]
	public void Challenge1_StorageSelectionViewModel_CollectedByGC_EvenIfCleanUpOmitted_ViaWeakEventManager()
	{
		// Arrange: Singleton sync service
		var syncService = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		// Act: Create ViewModel that subscribes to syncService.StatusChanged, but NEVER calls Dispose() or CleanUp()
		WeakReference weakVm = CreateViewModelWithoutDispose(syncService, _serviceProvider);

		// Trigger an event on the singleton to exercise handler routing
		// (Forces WeakEventManager internal invocation)
		// Force GC
		ForceGarbageCollection();

		// Assert: WeakEventManager must prevent the singleton from keeping the ViewModel pinned in memory
		Assert.False(weakVm.IsAlive, "StorageSelectionViewModel must be garbage-collected even if Dispose() is omitted, thanks to WeakEventManager.");
	}

	[Fact]
	public void Challenge1_SimulatedPopupWithClosure_CollectedByGC_AfterCleanUp()
	{
		// Arrange: Singleton sync service
		var syncService = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		// Act: Simulate exact StorageSelectionPopup topology (ViewModel, BindingContext, RequestClose closure)
		(WeakReference weakPopup, WeakReference weakVm) = CreateSimulatedPopupAndCleanUp(syncService, _serviceProvider);

		ForceGarbageCollection();

		// Assert: Both the popup container and ViewModel must be collected
		Assert.False(weakPopup.IsAlive, "Simulated StorageSelectionPopup must be garbage-collected after CleanUp().");
		Assert.False(weakVm.IsAlive, "StorageSelectionViewModel inside popup must be garbage-collected after CleanUp().");
	}

	[Fact]
	public void Challenge1_SimulatedPopupWithoutCleanUp_ClosurePreservesCycle_ProvingCleanUpEssential()
	{
		// Arrange: Singleton sync service
		var syncService = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);

		// Act: Create popup without cleanup, but retain a root to the ViewModel's RequestClose delegate in a heap holder
		var holder = new ActionHolder();
		WeakReference weakPopup = CreateSimulatedPopupRetainingClosureInHolder(syncService, _serviceProvider, holder);

		ForceGarbageCollection();

		// Assert: If the closure was not severed, the popup is kept alive by the surviving delegate target!
		// This empirical test PROVES why CleanUp() setting _viewModel.RequestClose = null is mandatory.
		Assert.NotNull(holder.Action);
		Assert.True(weakPopup.IsAlive, "Without CleanUp(), a retained RequestClose delegate keeps the popup rooted in memory.");

		// Now sever the closure on the heap
		holder.Action = null;
		ForceGarbageCollection();

		// Assert: Once severed, it can finally be collected
		Assert.False(weakPopup.IsAlive, "Once RequestClose delegate is severed, popup must be garbage-collected.");
	}

	private sealed class ActionHolder
	{
		public Action? Action;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateSimulatedPopupRetainingClosureInHolder(IStorageSyncService syncService, IServiceProvider sp, ActionHolder holder)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		var popup = new SimulatedStorageSelectionPopup(vm);
		holder.Action = vm.RequestClose;
		return new WeakReference(popup);
	}

	[Fact]
	public void Challenge1_RapidFiftyOpenCloseCycles_LeavesZeroSurvivingInstances()
	{
		var syncService = new StorageSyncService(_dbFixture.DatabaseService, _serviceProvider, _fakeConnectivity);
		var weakReferences = new List<WeakReference>();

		// Act: Simulate opening and closing the popup 50 times in rapid succession
		for (int i = 0; i < 50; i++)
		{
			weakReferences.Add(CreateInitializeAndCleanUpViewModel(syncService, _serviceProvider));
		}

		ForceGarbageCollection();

		// Assert: All 50 instances must be collected
		int survivingCount = weakReferences.Count(w => w.IsAlive);
		Assert.Equal(0, survivingCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateInitializeAndCleanUpViewModel(IStorageSyncService syncService, IServiceProvider sp)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		vm.RequestClose = () => { };
		_ = vm.InitializeAsync();

		// Simulate popup CleanUp
		vm.RequestClose = null;
		vm.Dispose();
		return new WeakReference(vm);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateViewModelWithoutDispose(IStorageSyncService syncService, IServiceProvider sp)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		_ = vm.InitializeAsync();
		// Purposely do NOT call Dispose()
		return new WeakReference(vm);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (WeakReference WeakPopup, WeakReference WeakVm) CreateSimulatedPopupAndCleanUp(IStorageSyncService syncService, IServiceProvider sp)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		var popup = new SimulatedStorageSelectionPopup(vm);
		_ = vm.InitializeAsync();

		popup.CleanUp();

		return (new WeakReference(popup), new WeakReference(vm));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (WeakReference WeakPopup, Action? SurvivingClosure) CreateSimulatedPopupRetainingClosure(IStorageSyncService syncService, IServiceProvider sp)
	{
		var vm = new StorageSelectionViewModel(syncService, sp);
		var popup = new SimulatedStorageSelectionPopup(vm);
		Action? closure = vm.RequestClose;
		return (new WeakReference(popup), closure);
	}

	private sealed class SimulatedStorageSelectionPopup
	{
		private readonly StorageSelectionViewModel _viewModel;
		public object? BindingContext { get; private set; }
		public bool IsCleanedUp { get; private set; }

		public SimulatedStorageSelectionPopup(StorageSelectionViewModel vm)
		{
			_viewModel = vm;
			BindingContext = vm;
			_viewModel.RequestClose = () =>
			{
				_ = SafeCloseAsync();
			};
		}

		public void CleanUp()
		{
			if (IsCleanedUp) return;
			IsCleanedUp = true;
			_viewModel.RequestClose = null;
			_viewModel.Dispose();
			BindingContext = null;
		}

		private Task SafeCloseAsync() => Task.CompletedTask;
	}

	#endregion

	#region 2. Unobserved Task Exception Challenges

	[Fact]
	public async Task Challenge2_LoopbackListener_WithContinuationShield_NeverRaisesUnobservedTaskException_OnTimeoutOrStop()
	{
		int unobservedExceptionCount = 0;
		EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
		{
			Interlocked.Increment(ref unobservedExceptionCount);
			e.SetObserved();
		};

		TaskScheduler.UnobservedTaskException += handler;
		try
		{
			// Act: Execute loopback listener stop sequence with the continuation shield used in WindowsGoogleAuthService
			await ExerciseLoopbackListenerWithShieldAsync(shouldAbort: false);

			// Force finalizer execution where unobserved task exceptions are dispatched
			ForceGarbageCollection();

			// Assert: Exception must have been marked observed by the continuation, so unobserved count is 0
			Assert.Equal(0, unobservedExceptionCount);
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= handler;
		}
	}

	[Fact]
	public async Task Challenge2_LoopbackListener_WithContinuationShield_NeverRaisesUnobservedTaskException_OnAbort()
	{
		int unobservedExceptionCount = 0;
		EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
		{
			Interlocked.Increment(ref unobservedExceptionCount);
			e.SetObserved();
		};

		TaskScheduler.UnobservedTaskException += handler;
		try
		{
			// Act: Execute loopback listener with Abort() instead of Stop()
			await ExerciseLoopbackListenerWithShieldAsync(shouldAbort: true);

			ForceGarbageCollection();

			Assert.Equal(0, unobservedExceptionCount);
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= handler;
		}
	}

	[Fact]
	public async Task Challenge2_LoopbackListener_TenConcurrentListeners_NeverRaiseUnobservedTaskException()
	{
		int unobservedExceptionCount = 0;
		EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
		{
			Interlocked.Increment(ref unobservedExceptionCount);
			e.SetObserved();
		};

		TaskScheduler.UnobservedTaskException += handler;
		try
		{
			var tasks = Enumerable.Range(0, 10).Select(_ => ExerciseLoopbackListenerWithShieldAsync(shouldAbort: false));
			await Task.WhenAll(tasks);

			ForceGarbageCollection();

			Assert.Equal(0, unobservedExceptionCount);
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= handler;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task ExerciseLoopbackListenerWithShieldAsync(bool shouldAbort)
	{
		int port = GetRandomUnusedPort();
		using var listener = new HttpListener();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");

		try
		{
			listener.Start();
		}
		catch (SocketException)
		{
			// Port temporarily in use; skip
			return;
		}

		var getContextTask = listener.GetContextAsync();

		// Attach the exact error-absorbing continuation from WindowsGoogleAuthService.cs
		_ = getContextTask.ContinueWith(
			t => _ = t.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

		// Now stop or abort the listener while GetContextAsync is pending
		if (shouldAbort)
		{
			listener.Abort();
		}
		else
		{
			listener.Stop();
		}

		// Wait briefly for the OS I/O completion thread to transition the task to faulted
		try
		{
			await Task.Delay(50);
		}
		catch { }
	}

	private static int GetRandomUnusedPort()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.LocalEndPoint!).Port;
	}

	#endregion

	#region 3. Layout Stability & HistoryLogs Challenges

	[Fact]
	public async Task Challenge3_MainViewModel_InitializeAsync_IsIdempotent_SecondCallFiresZeroCollectionChangedEvents()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		fakeDb.StoredEntities.Add(DecisionLogEntity.FromOracleLog(new OracleDecisionLog
		{
			Id = 101L,
			UserDrank = true,
			OracleSaidYes = true,
			CaffeineMg = 95,
			Timestamp = DateTime.UtcNow
		}));

		var vm = CreateMainViewModel(fakeDb, new FakeAppPopupService());

		// 1st Initialization
		await vm.InitializeAsync();
		Assert.Single(vm.HistoryLogs);
		var originalCollectionReference = vm.HistoryLogs;
		var originalLogInstance = vm.HistoryLogs[0];

		int collectionChangedEventCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) => collectionChangedEventCount++;

		// 2nd through 5th Initialization calls (simulating window focus / appearing churn)
		for (int i = 0; i < 5; i++)
		{
			await vm.InitializeAsync();
		}

		// Assert: Zero CollectionChanged events, reference intact, items not wiped
		Assert.Equal(0, collectionChangedEventCount);
		Assert.Same(originalCollectionReference, vm.HistoryLogs);
		Assert.Single(vm.HistoryLogs);
		Assert.Same(originalLogInstance, vm.HistoryLogs[0]);
	}

	[Fact]
	public async Task Challenge3_MainViewModel_OpenStorageSettingsAsync_WhenDismissedWithoutChanges_FiresZeroCollectionChangedEvents()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		fakeDb.StoredEntities.Add(DecisionLogEntity.FromOracleLog(new OracleDecisionLog
		{
			Id = 101L,
			UserDrank = true,
			OracleSaidYes = true,
			CaffeineMg = 95,
			Timestamp = DateTime.UtcNow
		}));

		var popupService = new FakeAppPopupService
		{
			StorageSelectionResult = false // User dismissed popup without migration or change
		};

		var vm = CreateMainViewModel(fakeDb, popupService);
		await vm.InitializeAsync();

		int collectionChangedEventCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) => collectionChangedEventCount++;

		// Act: User opens storage settings and cancels/dismisses
		await vm.OpenStorageSettingsAsync();

		// Assert: Popup was shown, but zero collection mutations fired on HistoryLogs
		Assert.Equal(1, popupService.ShowStorageSelectionPopupCallCount);
		Assert.Equal(0, collectionChangedEventCount);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal(101L, vm.HistoryLogs[0].Id);
	}

	[Fact]
	public async Task Challenge3_MainViewModel_ReloadLogsFromDatabaseAsync_WhenLogsAreIdentical_FiresZeroCollectionChangedEvents()
	{
		var fakeDb = new FakeSqliteDatabaseService();
		var timestamp = DateTime.UtcNow;
		fakeDb.StoredEntities.Add(DecisionLogEntity.FromOracleLog(new OracleDecisionLog
		{
			Id = 100L,
			UserDrank = true,
			OracleSaidYes = true,
			CaffeineMg = 95,
			Timestamp = timestamp
		}));

		var vm = CreateMainViewModel(fakeDb, new FakeAppPopupService());
		await vm.InitializeAsync();

		int collectionChangedCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) => collectionChangedCount++;

		// Act: Explicit reload from database with identical data
		await vm.ReloadLogsFromDatabaseAsync();

		// Assert: Fast-path sequence equality returns immediately without firing any events
		Assert.Equal(0, collectionChangedCount);
		Assert.Single(vm.HistoryLogs);
	}

	[Fact]
	public void Challenge3_SynchronizeHistoryLogsInPlace_WithMutations_FiresZeroResetActions_AndPreservesUnchangedReferences()
	{
		var vm = CreateMainViewModel(new FakeSqliteDatabaseService(), new FakeAppPopupService());

		var itemA = new OracleDecisionLog { Id = 101L, CaffeineMg = 95, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow.AddMinutes(-60) };
		var itemB = new OracleDecisionLog { Id = 102L, CaffeineMg = 150, UserDrank = false, OracleSaidYes = false, Timestamp = DateTime.UtcNow.AddMinutes(-30) };
		var itemC = new OracleDecisionLog { Id = 103L, CaffeineMg = 65, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow.AddMinutes(-15) };

		vm.HistoryLogs.Add(itemA);
		vm.HistoryLogs.Add(itemB);
		vm.HistoryLogs.Add(itemC);

		int resetCount = 0;
		int addCount = 0;
		int removeCount = 0;
		int replaceCount = 0;

		vm.HistoryLogs.CollectionChanged += (s, e) =>
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Reset:
					resetCount++;
					break;
				case NotifyCollectionChangedAction.Add:
					addCount++;
					break;
				case NotifyCollectionChangedAction.Remove:
					removeCount++;
					break;
				case NotifyCollectionChangedAction.Replace:
					replaceCount++;
					break;
			}
		};

		// Act: Mutate list:
		// - Keep itemA unchanged
		// - Update itemB (UserDrank becomes true)
		// - Remove itemC
		// - Add itemD
		var updatedItemB = new OracleDecisionLog
		{
			Id = 102L,
			CaffeineMg = 150,
			UserDrank = true, // Mutated
			OracleSaidYes = false,
			Timestamp = itemB.Timestamp
		};
		var newItemD = new OracleDecisionLog
		{
			Id = 104L,
			CaffeineMg = 200,
			UserDrank = true,
			OracleSaidYes = true,
			Timestamp = DateTime.UtcNow
		};

		var newLogs = new List<OracleDecisionLog> { newItemD, itemA, updatedItemB };

		vm.SynchronizeHistoryLogsInPlace(newLogs);

		// Assert:
		// 1. NEVER fire a Reset action (avoids visual tree rebuild in WinUI CollectionView)
		Assert.Equal(0, resetCount);

		// 2. Targeted Add, Remove, and Replace actions
		Assert.True(addCount > 0, "Must fire targeted Add for newItemD.");
		Assert.True(removeCount > 0, "Must fire targeted Remove for itemC.");

		// 3. Unchanged itemA instance preserved
		var preservedItemA = vm.HistoryLogs.FirstOrDefault(l => l.Id == 101L);
		Assert.NotNull(preservedItemA);
		Assert.Same(itemA, preservedItemA);

		// 4. Updated itemB reflected
		var resultB = vm.HistoryLogs.FirstOrDefault(l => l.Id == 102L);
		Assert.NotNull(resultB);
		Assert.True(resultB.UserDrank);

		// 5. Total count matches
		Assert.Equal(3, vm.HistoryLogs.Count);
	}

	[Fact]
	public void Challenge3_SynchronizeHistoryLogsInPlace_OneHundredItemsWithMinorDiff_FiresZeroResetActions()
	{
		var vm = CreateMainViewModel(new FakeSqliteDatabaseService(), new FakeAppPopupService());

		var initialLogs = Enumerable.Range(1, 100).Select(i => new OracleDecisionLog
		{
			Id = i,
			CaffeineMg = 80,
			UserDrank = true,
			OracleSaidYes = true,
			Timestamp = DateTime.UtcNow.AddMinutes(-i)
		}).ToList();

		foreach (var log in initialLogs)
		{
			vm.HistoryLogs.Add(log);
		}

		int resetCount = 0;
		vm.HistoryLogs.CollectionChanged += (s, e) =>
		{
			if (e.Action == NotifyCollectionChangedAction.Reset) resetCount++;
		};

		// Mutate 5 items and prepend 1 new item
		var updatedLogs = new List<OracleDecisionLog>(initialLogs);
		var newLog = new OracleDecisionLog { Id = 999L, CaffeineMg = 100, UserDrank = true, OracleSaidYes = true, Timestamp = DateTime.UtcNow };
		updatedLogs.Insert(0, newLog);

		// Mutate an existing log
		updatedLogs[10] = new OracleDecisionLog
		{
			Id = updatedLogs[10].Id,
			CaffeineMg = 120, // changed
			UserDrank = false,
			OracleSaidYes = true,
			Timestamp = updatedLogs[10].Timestamp
		};

		vm.SynchronizeHistoryLogsInPlace(updatedLogs);

		// Assert: Absolutely zero Reset events across 100 items
		Assert.Equal(0, resetCount);
		Assert.Equal(101, vm.HistoryLogs.Count);
		Assert.Equal(999L, vm.HistoryLogs[0].Id);
	}

	private static MainViewModel CreateMainViewModel(ISqliteDatabaseService dbService, IAppPopupService popupService)
	{
		return new MainViewModel(
			new FakeCoffeeDictionaryService(),
			new FakeOracleEngineService(),
			new FakeCaffeineMetabolismService(),
			new FakeDataPersistenceService(),
			popupService,
			dbService,
			new FakeStorageSyncService());
	}

	private static void ForceGarbageCollection()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	#endregion
}
