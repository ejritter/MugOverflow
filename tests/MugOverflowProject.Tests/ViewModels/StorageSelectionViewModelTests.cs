using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.ViewModels;

public sealed class StorageSelectionViewModelTests : IDisposable
{
	private readonly FakeStorageSyncService _syncService;
	private readonly FakeStorageProvider _localStorage;
	private readonly FakeStorageProvider _googleDrive;
	private readonly FakeStorageProvider _oneDrive;
	private readonly FakeConnectivity _connectivity;
	private readonly ServiceProvider _serviceProvider;

	public StorageSelectionViewModelTests()
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
		services.AddSingleton<ISecureStorageService, FakeSecureStorage>();
		services.AddSingleton<GoogleDriveConfig>();
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

	[Fact]
	public void Constructor_InitializesDefaultStateCorrectly()
	{
		using var vm = CreateViewModel();

		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
		Assert.Equal(StorageProviderType.LocalStorage, vm.PendingTargetProviderType);
		Assert.True(vm.IsLocalStorageSelected);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Equal(MigrationOption.TwoWayMergeBoth, vm.SelectedMigrationOption);
		Assert.Equal("Idle", vm.SyncStatusDisplay);
		Assert.Equal("Never", vm.LastSyncDisplay);
		Assert.False(vm.IsBusy);
		Assert.True(vm.IsNotBusy);
		Assert.False(vm.HasStatusMessage);
		Assert.Equal("Local Storage", vm.SelectedProviderDisplayName);
		Assert.Equal(StorageSelectionViewModel.LocalStorageDisclaimer, vm.DisclaimerText);
		Assert.Equal("Local storage data is isolated to this device and will not sync across other devices.", StorageSelectionViewModel.LocalStorageDisclaimer);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenSameAsActive_ShowsAlreadyActiveMessage()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.LocalStorage);

		Assert.Contains("already the active storage provider", vm.StatusMessage);
		Assert.False(vm.ShowMigrationOptions);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenSelectingLocalStorage_ShowsIsolationDisclaimerAndMigrationOptions()
	{
		_syncService.CurrentProvider = _googleDrive;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.LocalStorage);

		Assert.Equal(StorageProviderType.LocalStorage, vm.PendingTargetProviderType);
		Assert.True(vm.IsLocalStorageSelected);
		Assert.True(vm.ShowMigrationOptions);
		Assert.Equal(StorageSelectionViewModel.LocalStorageDisclaimer, vm.StatusMessage);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenOffline_FailsWithOfflineMessage()
	{
		_connectivity.NetworkAccess = NetworkAccess.None;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Contains("Cannot connect to Google Drive while offline", vm.StatusMessage);
		Assert.False(vm.ShowMigrationOptions);
		Assert.False(vm.IsBusy);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenCloudRequiresAuth_PromptsAuthenticationAndSucceeds()
	{
		_googleDrive.IsAuthenticated = false;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.Equal(StorageProviderType.GoogleDrive, vm.PendingTargetProviderType);
		Assert.True(vm.ShowMigrationOptions);
		Assert.False(vm.IsLocalStorageSelected);
		Assert.Contains("Ready to switch to Google Drive", vm.StatusMessage);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenCloudAuthFails_SetsErrorMessage()
	{
		_googleDrive.IsAuthenticated = false;
		_googleDrive.ShouldFailAuthentication = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Authentication failed", vm.StatusMessage);
	}

	[Fact]
	public async Task SelectMigrationOption_UpdatesSelectedOption()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		vm.SelectMigrationOption(MigrationOption.UploadLocalToCloud);
		Assert.Equal(MigrationOption.UploadLocalToCloud, vm.SelectedMigrationOption);

		vm.SelectMigrationOption(MigrationOption.DownloadCloudToLocal);
		Assert.Equal(MigrationOption.DownloadCloudToLocal, vm.SelectedMigrationOption);

		vm.SelectMigrationOption(MigrationOption.TwoWayMergeBoth);
		Assert.Equal(MigrationOption.TwoWayMergeBoth, vm.SelectedMigrationOption);
	}

	[Fact]
	public async Task ConfirmMigrationCommand_WhenSuccessful_SwitchesProviderAndHidesMigration()
	{
		_googleDrive.IsAuthenticated = true;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		vm.SelectMigrationOption(MigrationOption.TwoWayMergeBoth);

		await vm.ConfirmMigrationCommand.ExecuteAsync(null);

		Assert.Equal(1, _syncService.SwitchProviderCallCount);
		Assert.Equal(StorageProviderType.GoogleDrive, _syncService.LastTargetType);
		Assert.Equal(MigrationOption.TwoWayMergeBoth, _syncService.LastMigrationOption);
		Assert.Equal(StorageProviderType.GoogleDrive, vm.SelectedProviderType);
		Assert.False(vm.ShowMigrationOptions);
		Assert.Contains("Successfully switched storage provider", vm.StatusMessage);
	}

	[Fact]
	public async Task ConfirmMigrationCommand_WhenSwitchFails_SetsErrorMessage()
	{
		_googleDrive.IsAuthenticated = true;
		_syncService.ShouldFailSwitch = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);
		await vm.ConfirmMigrationCommand.ExecuteAsync(null);

		Assert.Contains("Migration failed", vm.StatusMessage);
		Assert.Equal(StorageProviderType.LocalStorage, vm.SelectedProviderType);
	}

	[Fact]
	public async Task SyncNowCommand_TriggersSynchronizeAsync_AndUpdatesDisplays()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SyncNowCommand.ExecuteAsync(null);

		Assert.Equal(1, _syncService.SynchronizeCallCount);
		Assert.Equal("Synced", vm.SyncStatusDisplay);
		Assert.Contains("Sync completed successfully", vm.StatusMessage);
	}

	[Fact]
	public async Task SyncNowCommand_WhenSyncFails_SetsErrorMessage()
	{
		_syncService.ShouldFailSynchronize = true;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SyncNowCommand.ExecuteAsync(null);

		Assert.Equal(1, _syncService.SynchronizeCallCount);
		Assert.Contains("Sync failed", vm.StatusMessage);
	}

	[Fact]
	public async Task EngineStatusChangedEvent_UpdatesViewModelStatusProperties()
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
	}

	[Fact]
	public void CloseCommand_HidesMigrationAndInvokesRequestClose()
	{
		using var vm = CreateViewModel();
		vm.ShowMigrationOptions = true;
		vm.StatusMessage = "Some status";
		bool closeRequested = false;
		vm.RequestClose = () => closeRequested = true;

		vm.CloseCommand.Execute(null);

		Assert.False(vm.ShowMigrationOptions);
		Assert.Empty(vm.StatusMessage);
		Assert.True(closeRequested);
	}

	[Fact]
	public async Task SelectProviderCommand_WhenGoogleDriveIsActiveAndAuthRequired_TriggersAuthenticate()
	{
		_googleDrive.IsAuthenticated = false;
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.Equal("Sign In", vm.GoogleDriveActionText);

		// Execute SelectProvider on already active provider
		await vm.SelectProviderCommand.ExecuteAsync(StorageProviderType.GoogleDrive);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.Contains("Signed in", vm.StatusMessage);
		Assert.False(vm.IsAuthRequired);
	}

	[Fact]
	public async Task AuthenticateCurrentProviderCommand_WhenSignInProgress_AuthenticatesAndSyncs()
	{
		_googleDrive.IsAuthenticated = false;
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.AuthenticateCurrentProviderCommand.ExecuteAsync(null);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.Equal(1, _syncService.SynchronizeCallCount);
		Assert.False(vm.IsAuthRequired);
	}

	[Fact]
	public void ToggleGoogleCredentialsCommand_TogglesVisibility()
	{
		using var vm = CreateViewModel();
		Assert.False(vm.IsGoogleCredentialsVisible);

		vm.ToggleGoogleCredentialsCommand.Execute(null);
		Assert.True(vm.IsGoogleCredentialsVisible);

		vm.ToggleGoogleCredentialsCommand.Execute(null);
		Assert.False(vm.IsGoogleCredentialsVisible);
	}

	[Fact]
	public async Task SaveAndAuthenticateGoogleCommand_PersistsCustomCredentialsAndAuthenticates()
	{
		_googleDrive.IsAuthenticated = false;
		_syncService.CurrentProvider = _googleDrive;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		vm.GoogleCustomClientId = "custom-client-id-12345";
		vm.GoogleCustomClientSecret = "custom-client-secret-xyz";

		await vm.SaveAndAuthenticateGoogleCommand.ExecuteAsync(null);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.True(vm.HasCustomCredentials);
		Assert.Equal("custom-client-id-12345", vm.CurrentActiveClientIdDisplay);
	}

	[Fact]
	public async Task ResetGoogleCredentialsCommand_ClearsCustomOverrides()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		vm.GoogleCustomClientId = "custom-override";
		await vm.SaveAndAuthenticateGoogleCommand.ExecuteAsync(null);
		Assert.True(vm.HasCustomCredentials);

		await vm.ResetGoogleCredentialsCommand.ExecuteAsync(null);
		Assert.False(vm.HasCustomCredentials);
		Assert.Empty(vm.GoogleCustomClientId);
		Assert.Empty(vm.GoogleCustomClientSecret);
	}

	[Fact]
	public async Task GoogleDriveActionText_ReflectsStateAccurately()
	{
		_syncService.CurrentProvider = _localStorage;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// When not current provider
		Assert.Equal("Connect", vm.GoogleDriveActionText);

		// When current provider and auth required
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;
		await vm.InitializeAsync();
		Assert.Equal("Sign In", vm.GoogleDriveActionText);

		// When current provider and authenticated
		_syncService.Status = SyncEngineStatus.Success;
		_googleDrive.IsAuthenticated = true;
		await vm.InitializeAsync();
		Assert.Equal("Connected", vm.GoogleDriveActionText);
	}

	[Fact]
	public async Task AuthenticateCurrentProviderCommand_SetsHasMigratedDataTrue_WhenSyncSucceeds()
	{
		_googleDrive.IsAuthenticated = false;
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();
		Assert.False(vm.HasMigratedData);

		await vm.AuthenticateCurrentProviderCommand.ExecuteAsync(null);

		Assert.True(vm.HasMigratedData, "HasMigratedData must be true so the main view reloads synced logs upon popup closure.");
	}

	[Fact]
	public async Task GoogleDriveConnectionProperties_ReflectStateProperly()
	{
		_syncService.CurrentProvider = _localStorage;
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.False(vm.IsGoogleDriveConnected);
		Assert.True(vm.IsGoogleDriveNotConnected);
		Assert.Equal("Connect Google Drive", vm.GoogleDriveButtonText);

		// Switch to Google Drive, unauthenticated
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;
		await vm.InitializeAsync();
		Assert.False(vm.IsGoogleDriveConnected);
		Assert.True(vm.IsGoogleDriveNotConnected);
		Assert.Equal("Allow Google Drive Access", vm.GoogleDriveButtonText);

		// Authenticated
		_syncService.Status = SyncEngineStatus.Success;
		_googleDrive.IsAuthenticated = true;
		await vm.InitializeAsync();
		Assert.True(vm.IsGoogleDriveConnected);
		Assert.False(vm.IsGoogleDriveNotConnected);
		Assert.Equal("Connected", vm.GoogleDriveButtonText);
	}

	[Fact]
	public async Task AllowGoogleDriveAccessCommand_WhenAuthRequired_TriggersAuthenticationAndSync()
	{
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.AuthRequired;
		_googleDrive.IsAuthenticated = false;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.AllowGoogleDriveAccessCommand.ExecuteAsync(null);

		Assert.Equal(1, _googleDrive.AuthenticateCallCount);
		Assert.True(vm.IsGoogleDriveConnected);
		Assert.True(vm.HasMigratedData);
	}

	[Fact]
	public async Task DisconnectGoogleCommand_SignsOutAndResetsAuthState()
	{
		_syncService.CurrentProvider = _googleDrive;
		_syncService.Status = SyncEngineStatus.Success;
		_googleDrive.IsAuthenticated = true;

		using var vm = CreateViewModel();
		await vm.InitializeAsync();
		Assert.True(vm.IsGoogleDriveConnected);

		await vm.DisconnectGoogleCommand.ExecuteAsync(null);

		Assert.False(vm.IsGoogleDriveConnected);
		Assert.True(vm.IsGoogleDriveNotConnected);
		Assert.True(vm.IsAuthRequired);
	}
}

