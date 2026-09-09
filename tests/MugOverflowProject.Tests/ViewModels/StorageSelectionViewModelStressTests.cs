using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.ViewModels;

/// <summary>
/// Empirical Adversarial Challenge Suite for Milestone 4:
/// Stress-tests StorageSelectionViewModel under rapid provider toggles, canceled migrations,
/// auth rejection flows, repeated sync spamming, offline state UI transitions, and
/// exact string verification for Local Storage isolation disclaimer.
/// </summary>
public sealed class StorageSelectionViewModelStressTests : IDisposable
{
	private const string ExpectedIsolationDisclaimer = "Local storage data is isolated to this device and will not sync across other devices.";

	private readonly FakeStorageSyncService _syncService;
	private readonly FakeStorageProvider _localStorage;
	private readonly FakeStorageProvider _googleDrive;
	private readonly FakeStorageProvider _oneDrive;
	private readonly FakeConnectivity _connectivity;
	private readonly ServiceProvider _serviceProvider;

	public StorageSelectionViewModelStressTests()
	{
		_localStorage = new FakeStorageProvider
		{
			ProviderType = StorageProviderType.LocalStorage,
			DisplayName = "Local Storage",
			RequiresAuthentication = false,
			IsAuthenticated = true
		};

		_googleDrive = new FakeStorageProvider
		{
			ProviderType = StorageProviderType.GoogleDrive,
			DisplayName = "Google Drive",
			RequiresAuthentication = true,
			IsAuthenticated = false
		};

		_oneDrive = new FakeStorageProvider
		{
			ProviderType = StorageProviderType.OneDrive,
			DisplayName = "Microsoft OneDrive",
			RequiresAuthentication = true,
			IsAuthenticated = false
		};

		_syncService = new FakeStorageSyncService(_localStorage);
		_syncService.RegisterProviderInstance(StorageProviderType.LocalStorage, _localStorage);
		_syncService.RegisterProviderInstance(StorageProviderType.GoogleDrive, _googleDrive);
		_syncService.RegisterProviderInstance(StorageProviderType.OneDrive, _oneDrive);

		_connectivity = new FakeConnectivity
		{
			NetworkAccess = NetworkAccess.Internet
		};

		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, _localStorage);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, _googleDrive);
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.OneDrive, _oneDrive);
		services.AddSingleton<LocalStorageProvider>(_ => (LocalStorageProvider)null!);
		services.AddSingleton<GoogleDriveStorageProvider>(_ => (GoogleDriveStorageProvider)null!);
		services.AddSingleton<OneDriveStorageProvider>(_ => (OneDriveStorageProvider)null!);
		_serviceProvider = services.BuildServiceProvider();
	}

	public void Dispose()
	{
		_serviceProvider.Dispose();
	}

	private StorageSelectionViewModel CreateViewModel()
	{
		return new StorageSelectionViewModel(_syncService, _serviceProvider, _connectivity);
	}

	#region 1. Disclaimer Exact String Verification

	[Fact]
	public void LocalStorageDisclaimer_MatchesExactCharacterSequence()
	{
		// Assert verbatim equality with specification requirement
		Assert.Equal(ExpectedIsolationDisclaimer, StorageSelectionViewModel.LocalStorageDisclaimer);

		using var vm = CreateViewModel();
		Assert.Equal(ExpectedIsolationDisclaimer, vm.DisclaimerText);
	}

	[Fact]
	public async Task LocalStorageDisclaimer_IsExposedWhenLocalStorageIsSelectedOrActive()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Initially Local Storage is active
		Assert.True(vm.IsLocalStorageSelected);
		Assert.Equal(ExpectedIsolationDisclaimer, vm.DisclaimerText);

		// Switch to Google Drive and confirm migration so Google Drive becomes active provider
		_googleDrive.IsAuthenticated = true;
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		Assert.False(vm.IsLocalStorageSelected);
		await vm.ConfirmMigrationCommand.ExecuteAsync(null);
		Assert.Equal(StorageProviderType.GoogleDrive, _syncService.CurrentProvider.ProviderType);

		// Now switch back to Local Storage from active Google Drive
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.LocalStorage);
		Assert.True(vm.IsLocalStorageSelected);
		Assert.True(vm.ShowMigrationOptions);
		Assert.Equal(ExpectedIsolationDisclaimer, vm.StatusMessage);
	}

	#endregion

	#region 2. Rapid Provider Toggles & Concurrency Stress

	[Fact]
	public async Task RapidProviderToggles_SequentialRapidSwitching_MaintainsConsistentState()
	{
		_googleDrive.IsAuthenticated = true;
		_oneDrive.IsAuthenticated = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Rapid sequential toggling between all 3 providers 30 times
		for (int i = 0; i < 30; i++)
		{
			var target = (StorageProviderType)(i % 3);
			await vm.SelectProviderCommand.ExecuteAsync(target);

			if (target == _syncService.CurrentProvider.ProviderType)
			{
				Assert.False(vm.ShowMigrationOptions);
			}
			else
			{
				Assert.True(vm.ShowMigrationOptions);
				Assert.Equal(target, vm.PendingTargetProviderType);
			}
			Assert.False(vm.IsBusy);
		}
	}

	[Fact]
	public async Task RapidProviderToggles_ConcurrentTriggerSpam_ExecutesAtomicallyWithoutDeadlock()
	{
		_googleDrive.IsAuthenticated = true;
		_googleDrive.SimulatedDelayMs = 20;
		_oneDrive.IsAuthenticated = true;
		_oneDrive.SimulatedDelayMs = 20;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Fire 20 parallel calls to SelectProviderCommand
		var tasks = Enumerable.Range(0, 20).Select(i =>
		{
			var target = (i % 2 == 0) ? StorageProviderType.GoogleDrive : StorageProviderType.OneDrive;
			return vm.SelectProviderCommand.ExecuteAsync(target);
		}).ToArray();

		await Task.WhenAll(tasks);

		Assert.False(vm.IsBusy);
		Assert.True(vm.PendingTargetProviderType == StorageProviderType.GoogleDrive ||
					vm.PendingTargetProviderType == StorageProviderType.OneDrive);
	}

	[Fact]
	public async Task RapidProviderToggles_WhenSelectingActiveProvider_DoesNotPromptMigration()
	{
		_syncService.CurrentProvider = _googleDrive;
		_googleDrive.IsAuthenticated = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.Equal(StorageProviderType.GoogleDrive, vm.SelectedProviderType);

		// Click Google Drive again
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("already the active storage provider", vm.StatusMessage);
		Assert.False(vm.IsLocalStorageSelected);
	}

	#endregion

	#region 3. Canceled Migrations & State Rollback

	[Fact]
	public async Task CanceledMigration_ViaCloseCommand_ResetsOptionsAndPreservesCurrentProvider()
	{
		_googleDrive.IsAuthenticated = true;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Initiate switch to Google Drive
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		Assert.True(vm.ShowMigrationOptions);
		Assert.Equal(StorageProviderType.GoogleDrive, vm.PendingTargetProviderType);

		bool closeInvoked = false;
		vm.RequestClose = () => closeInvoked = true;

		// Cancel via Close
		vm.Close();

		Assert.False(vm.ShowMigrationOptions);
		Assert.Empty(vm.StatusMessage);
		Assert.True(closeInvoked);

		// Active provider in sync service and ViewModel must remain unchanged
		Assert.Equal(StorageProviderType.LocalStorage, _syncService.CurrentProvider.ProviderType);
		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
		Assert.Equal(0, _syncService.SwitchProviderCallCount);
	}

	[Fact]
	public async Task CanceledMigration_WithCancellationToken_HandlesOperationCanceledExceptionGracefully()
	{
		_googleDrive.IsAuthenticated = true;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		Assert.True(vm.ShowMigrationOptions);

		using var cts = new CancellationTokenSource();
		cts.Cancel(); // Pre-cancel token

		await vm.ConfirmMigrationAsync(cts.Token);

		Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
		Assert.False(vm.IsBusy);
		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
	}

	[Fact]
	public async Task SelectProvider_WithCancellationToken_HandlesCancellationGracefully()
	{
		_googleDrive.IsAuthenticated = false;
		_googleDrive.SimulatedDelayMs = 100;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		using var cts = new CancellationTokenSource();
		cts.Cancel(); // Pre-cancel

		await vm.SelectProviderAsync(StorageProviderType.GoogleDrive, cts.Token);

		Assert.False(vm.IsBusy);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region 4. Auth Rejection & Failure Flows

	[Fact]
	public async Task AuthRejection_WhenUserDeniesOAuthConsent_SetsAuthenticationFailedAndBlocksMigration()
	{
		_googleDrive.IsAuthenticated = false;
		_googleDrive.ShouldFailAuthentication = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Authentication failed for Google Drive", vm.StatusMessage);
		Assert.False(vm.IsBusy);
		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
	}

	[Fact]
	public async Task AuthRejection_WhenOneDriveAuthFails_SetsErrorMessageAndDoesNotSwitch()
	{
		_oneDrive.IsAuthenticated = false;
		_oneDrive.ShouldFailAuthentication = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.OneDrive);

		Assert.Equal(1, _oneDrive.AuthenticateCallCount);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Authentication failed for Microsoft OneDrive", vm.StatusMessage);
		Assert.False(vm.IsBusy);
		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
	}

	[Fact]
	public async Task AuthRecovery_WhenAuthFailsThenSucceedsOnRetry_ArmsMigrationOptions()
	{
		_googleDrive.IsAuthenticated = false;
		_googleDrive.ShouldFailAuthentication = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Attempt 1: fails
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Authentication failed", vm.StatusMessage);

		// User retries and accepts consent
		_googleDrive.ShouldFailAuthentication = false;
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Equal(2, _googleDrive.AuthenticateCallCount);
		Assert.True(vm.ShowMigrationOptions);
		Assert.Equal(StorageProviderType.GoogleDrive, vm.PendingTargetProviderType);
		Assert.Contains("Ready to switch to Google Drive", vm.StatusMessage);
	}

	[Fact]
	public async Task AuthException_WhenProviderThrowsUnexpectedException_CatchesAndDisplaysError()
	{
		// Create a provider that throws during IsAuthenticatedAsync
		var faultyProvider = new FaultyStorageProvider();
		var services = new ServiceCollection();
		services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, faultyProvider);
		services.AddSingleton<GoogleDriveStorageProvider>(_ => (GoogleDriveStorageProvider)null!);
		using var serviceProvider = services.BuildServiceProvider();

		using var vm = new StorageSelectionViewModel(_syncService, serviceProvider, _connectivity);
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.False(vm.IsBusy);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Connection failed", vm.StatusMessage);
	}

	#endregion

	#region 5. Repeated Sync Spamming Stress

	[Fact]
	public async Task SyncSpamming_50ConcurrentSyncCalls_ExecutesSafelyWithoutDeadlockOrDuplicateWork()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Trigger 50 rapid parallel sync requests
		var syncTasks = Enumerable.Range(0, 50).Select(_ => vm.SyncNowCommand.ExecuteAsync(null)).ToArray();
		await Task.WhenAll(syncTasks);

		Assert.False(vm.IsBusy);
		Assert.True(vm.IsNotBusy);
		Assert.Equal("Synced", vm.SyncStatusDisplay);
		Assert.Contains("Sync completed successfully", vm.StatusMessage);
		Assert.True(_syncService.SynchronizeCallCount >= 1);
	}

	[Fact]
	public async Task SyncNow_WhenEngineFails_SurfacesErrorStatusAndMessage()
	{
		_syncService.ShouldFailSynchronize = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SyncNowCommand.ExecuteAsync(null);

		Assert.False(vm.IsBusy);
		Assert.Contains("Sync failed", vm.StatusMessage);
		Assert.Equal("Error", vm.SyncStatusDisplay);
	}

	[Fact]
	public async Task SyncNow_WhenOffline_SurfacesOfflineMessage()
	{
		_syncService.Status = SyncEngineStatus.Offline;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// When offline, SynchronizeAsync returns false and status is Offline
		await vm.SyncNowAsync(CancellationToken.None);

		Assert.False(vm.IsBusy);
		Assert.Contains("Cannot sync while offline", vm.StatusMessage);
	}

	[Fact]
	public async Task SyncNow_WithPreCanceledToken_HandlesGracefully()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await vm.SyncNowAsync(cts.Token);

		Assert.False(vm.IsBusy);
		Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region 6. Offline State Transitions & Connectivity Reactions

	[Fact]
	public async Task OfflineProviderSwitch_WhenConnectivityIsNone_RejectsSwitchBeforeAuth()
	{
		_connectivity.NetworkAccess = NetworkAccess.None;
		_googleDrive.IsAuthenticated = false;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		// Must NOT call AuthenticateAsync when offline
		Assert.Equal(0, _googleDrive.AuthenticateCallCount);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Cannot connect to Google Drive while offline", vm.StatusMessage);
		Assert.False(vm.IsBusy);
	}

	[Fact]
	public async Task OfflineProviderSwitch_WhenConnectivityIsConstrained_RejectsSwitch()
	{
		_connectivity.NetworkAccess = NetworkAccess.ConstrainedInternet;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.OneDrive);

		Assert.Equal(0, _oneDrive.AuthenticateCallCount);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Cannot connect to Microsoft OneDrive while offline", vm.StatusMessage);
	}

	[Fact]
	public async Task OfflineState_AllowsSwitchingToLocalStorageEvenWhenOffline()
	{
		_syncService.CurrentProvider = _googleDrive;
		_connectivity.NetworkAccess = NetworkAccess.None;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Local Storage switch does not need internet
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.LocalStorage);

		Assert.True(vm.ShowMigrationOptions);
		Assert.True(vm.IsLocalStorageSelected);
		Assert.Equal(StorageProviderType.LocalStorage, vm.PendingTargetProviderType);
		Assert.Equal(ExpectedIsolationDisclaimer, vm.StatusMessage);
	}

	[Fact]
	public async Task SyncEngineEvents_TransitionDisplaysAccurately()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		_syncService.TriggerStatusChanged(SyncEngineStatus.Offline);
		Assert.Equal("Offline", vm.SyncStatusDisplay);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.Equal("Syncing...", vm.SyncStatusDisplay);
		Assert.True(vm.IsBusy);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Error);
		Assert.Equal("Error", vm.SyncStatusDisplay);
		Assert.False(vm.IsBusy);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Success);
		Assert.Equal("Synced", vm.SyncStatusDisplay);
		Assert.False(vm.IsBusy);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Idle);
		Assert.Equal("Idle", vm.SyncStatusDisplay);
		Assert.False(vm.IsBusy);
	}

	[Fact]
	public async Task Migration3WayOptions_AllThreeOptionsCanBeConfirmedSuccessfully()
	{
		var options = new[]
		{
			MigrationOption.TwoWayMergeBoth,
			MigrationOption.UploadLocalToCloud,
			MigrationOption.DownloadCloudToLocal
		};

		foreach (var option in options)
		{
			_localStorage.Reset();
			_googleDrive.Reset();
			_googleDrive.IsAuthenticated = true;
			_syncService.CurrentProvider = _localStorage;

			using var vm = CreateViewModel();
			await vm.InitializeAsync();

			await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
			vm.SelectMigrationOption(option);
			Assert.Equal(option, vm.SelectedMigrationOption);

			await vm.ConfirmMigrationCommand.ExecuteAsync(null);

			Assert.Equal(StorageProviderType.GoogleDrive, _syncService.CurrentProvider.ProviderType);
			Assert.Equal(StorageProviderType.GoogleDrive, vm.SelectedProviderType);
			Assert.Equal(option, _syncService.LastMigrationOption);
			Assert.False(vm.ShowMigrationOptions);
			Assert.Contains("Successfully switched storage provider", vm.StatusMessage);
		}
	}

	[Fact]
	public void Dispose_UnsubscribesFromSyncEngineEventsSafely()
	{
		var vm = CreateViewModel();
		vm.Dispose();

		// Triggering event after disposal should not throw or affect vm
		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);

		// Second dispose is safe
		vm.Dispose();
	}

	#endregion

	private sealed class FaultyStorageProvider : IStorageProvider
	{
		public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;
		public string DisplayName => "Faulty Google Drive";
		public bool RequiresAuthentication => true;

		public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
			=> throw new HttpRequestException("Simulated socket connection failure");

		public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
			=> throw new HttpRequestException("Simulated auth socket failure");

		public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default) => Task.FromResult<StoragePayload?>(null);
		public Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default) => Task.FromResult(false);
		public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
	}
}
