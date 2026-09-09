using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;

namespace MugOverflowProject.ViewModels;

/// <summary>
/// ViewModel managing storage provider selection, OAuth authentication flows,
/// data migration strategies, and real-time synchronization orchestration.
/// </summary>
public sealed partial class StorageSelectionViewModel : ObservableObject, IDisposable
{
	/// <summary>
	/// Mandatory isolation disclaimer text displayed when Local Storage is active or selected.
	/// </summary>
	public const string LocalStorageDisclaimer = "Local storage data is isolated to this device and will not sync across other devices.";

	private readonly IStorageSyncService _storageSyncService;
	private readonly IServiceProvider _serviceProvider;
	private readonly IConnectivity _connectivity;
	private readonly ISecureStorageService? _secureStorage;
	private readonly GoogleDriveConfig? _googleDriveConfig;
	private bool _disposed;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SelectedProviderDisplayName))]
	[NotifyPropertyChangedFor(nameof(GoogleDriveActionText))]
	[NotifyPropertyChangedFor(nameof(GoogleDriveButtonText))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveConnected))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveNotConnected))]
	public partial StorageProviderType SelectedProviderType { get; set; }

	[ObservableProperty]
	public partial StorageProviderType PendingTargetProviderType { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ConfirmMigrationCommand))]
	[NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
	[NotifyCanExecuteChangedFor(nameof(AuthenticateCurrentProviderCommand))]
	[NotifyCanExecuteChangedFor(nameof(AllowGoogleDriveAccessCommand))]
	[NotifyPropertyChangedFor(nameof(IsNotBusy))]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatusMessage))]
	public partial string StatusMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsLocalStorageSelected { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GoogleDriveActionText))]
	public partial bool IsGoogleDriveSelected { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GoogleDriveActionText))]
	[NotifyPropertyChangedFor(nameof(GoogleDriveButtonText))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveConnected))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveNotConnected))]
	public partial bool IsGoogleDriveAuthenticated { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GoogleDriveActionText))]
	[NotifyPropertyChangedFor(nameof(GoogleDriveButtonText))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveConnected))]
	[NotifyPropertyChangedFor(nameof(IsGoogleDriveNotConnected))]
	public partial bool IsAuthRequired { get; set; }

	[ObservableProperty]
	public partial bool IsGoogleCredentialsVisible { get; set; }

	[ObservableProperty]
	public partial string GoogleCustomClientId { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string GoogleCustomClientSecret { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CurrentActiveClientIdDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool HasCustomCredentials { get; set; }

	[ObservableProperty]
	public partial bool ShowMigrationOptions { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsMergeBothSelected))]
	[NotifyPropertyChangedFor(nameof(IsUploadLocalSelected))]
	[NotifyPropertyChangedFor(nameof(IsDownloadCloudSelected))]
	public partial MigrationOption SelectedMigrationOption { get; set; } = MigrationOption.TwoWayMergeBoth;

	public bool IsMergeBothSelected => SelectedMigrationOption == MigrationOption.TwoWayMergeBoth;
	public bool IsUploadLocalSelected => SelectedMigrationOption == MigrationOption.UploadLocalToCloud;
	public bool IsDownloadCloudSelected => SelectedMigrationOption == MigrationOption.DownloadCloudToLocal;

	[ObservableProperty]
	public partial string LastSyncDisplay { get; set; } = "Never";

	[ObservableProperty]
	public partial string SyncStatusDisplay { get; set; } = "Idle";

	public bool IsNotBusy => !IsBusy;
	public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

	public string SelectedProviderDisplayName => SelectedProviderType switch
	{
		StorageProviderType.LocalStorage => "Local Storage",
		StorageProviderType.GoogleDrive => "Google Drive",
		StorageProviderType.OneDrive => "Microsoft OneDrive",
		_ => SelectedProviderType.ToString()
	};

	public string GoogleDriveActionText
	{
		get
		{
			if (SelectedProviderType == StorageProviderType.GoogleDrive)
			{
				if (IsAuthRequired || !IsGoogleDriveAuthenticated)
					return "Sign In";
				return "Connected";
			}
			return "Connect";
		}
	}

	public bool IsGoogleDriveConnected =>
		SelectedProviderType == StorageProviderType.GoogleDrive &&
		IsGoogleDriveAuthenticated &&
		!IsAuthRequired;

	public bool IsGoogleDriveNotConnected => !IsGoogleDriveConnected;

	public string GoogleDriveButtonText
	{
		get
		{
			if (SelectedProviderType == StorageProviderType.GoogleDrive)
			{
				if (IsAuthRequired || !IsGoogleDriveAuthenticated)
					return "Allow Google Drive Access";
				return "Connected";
			}
			return "Connect Google Drive";
		}
	}

	public string DisclaimerText => LocalStorageDisclaimer;

	public Action? RequestClose { get; set; }

	public bool HasMigratedData { get; private set; }

	/// <summary>
	/// Primary constructor for dependency injection and testing.
	/// </summary>
	public StorageSelectionViewModel(
		IStorageSyncService storageSyncService,
		IServiceProvider serviceProvider,
		IConnectivity? connectivity = null,
		ISecureStorageService? secureStorage = null,
		GoogleDriveConfig? googleDriveConfig = null)
	{
		_storageSyncService = storageSyncService ?? throw new ArgumentNullException(nameof(storageSyncService));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_connectivity = connectivity ?? Connectivity.Current;
		_secureStorage = secureStorage ?? serviceProvider.GetService<ISecureStorageService>();
		_googleDriveConfig = googleDriveConfig ?? serviceProvider.GetService<GoogleDriveConfig>() ?? new GoogleDriveConfig();

		_storageSyncService.StatusChanged += OnSyncStatusChanged;

		InitializeCurrentState();
	}

	/// <summary>
	/// Refreshes state from the underlying sync service.
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_secureStorage != null && _googleDriveConfig != null)
		{
			try
			{
				await _googleDriveConfig.LoadCustomCredentialsAsync(_secureStorage).ConfigureAwait(false);
			}
			catch
			{
				// Safe fallback for testing
			}
		}

		InitializeCurrentState();
	}

	private void InitializeCurrentState()
	{
		SelectedProviderType = _storageSyncService.CurrentProvider?.ProviderType ?? StorageProviderType.LocalStorage;
		PendingTargetProviderType = SelectedProviderType;
		IsLocalStorageSelected = SelectedProviderType == StorageProviderType.LocalStorage;
		IsGoogleDriveSelected = SelectedProviderType == StorageProviderType.GoogleDrive;
		IsAuthRequired = _storageSyncService.Status == SyncEngineStatus.AuthRequired;
		ShowMigrationOptions = false;
		SelectedMigrationOption = MigrationOption.TwoWayMergeBoth;
		UpdateSyncStatusDisplay(_storageSyncService.Status);
		UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);

		LoadGoogleCredentialsDisplay();
		_ = CheckGoogleAuthStatusAsync();
	}

	private void LoadGoogleCredentialsDisplay()
	{
		if (_googleDriveConfig != null)
		{
			GoogleCustomClientId = _googleDriveConfig.CustomClientId ?? string.Empty;
			GoogleCustomClientSecret = _googleDriveConfig.CustomClientSecret ?? string.Empty;
			HasCustomCredentials = !string.IsNullOrWhiteSpace(_googleDriveConfig.CustomClientId);
			CurrentActiveClientIdDisplay = _googleDriveConfig.ActiveClientId;
		}
	}

	private async Task CheckGoogleAuthStatusAsync()
	{
		try
		{
			var provider = ResolveProvider(StorageProviderType.GoogleDrive);
			bool auth = await provider.IsAuthenticatedAsync().ConfigureAwait(false);
			await SafeDispatchToMainThreadAsync(() =>
			{
				IsGoogleDriveAuthenticated = auth;
			}).ConfigureAwait(false);
		}
		catch
		{
			// Safe ignore in unit test / headless environments
		}
	}

	private void OnSyncStatusChanged(object? sender, SyncEngineStatus status)
	{
		_ = SafeDispatchToMainThreadAsync(() =>
		{
			UpdateSyncStatusDisplay(status);
			UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);
			IsBusy = status == SyncEngineStatus.Syncing;
			IsAuthRequired = status == SyncEngineStatus.AuthRequired;
			if (status == SyncEngineStatus.Success)
			{
				IsGoogleDriveAuthenticated = true;
			}
		});
	}

	private void UpdateSyncStatusDisplay(SyncEngineStatus status)
	{
		SyncStatusDisplay = status switch
		{
			SyncEngineStatus.Idle => "Idle",
			SyncEngineStatus.Syncing => "Syncing...",
			SyncEngineStatus.Offline => "Offline",
			SyncEngineStatus.Error => "Error",
			SyncEngineStatus.Success => "Synced",
			SyncEngineStatus.AuthRequired => "Sign-in Required",
			_ => status.ToString()
		};
	}

	private void UpdateLastSyncDisplay(DateTime? lastSyncUtc)
	{
		if (lastSyncUtc.HasValue)
		{
			LastSyncDisplay = lastSyncUtc.Value.ToLocalTime().ToString("g");
		}
		else
		{
			LastSyncDisplay = "Never";
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SelectProviderAsync(StorageProviderType targetType, CancellationToken cancellationToken = default)
	{
		if (targetType == _storageSyncService.CurrentProvider?.ProviderType)
		{
			if (targetType == StorageProviderType.LocalStorage)
			{
				await SafeDispatchToMainThreadAsync(() =>
				{
					StatusMessage = $"{ResolveProviderDisplayName(targetType)} is already the active storage provider.";
					ShowMigrationOptions = false;
					IsLocalStorageSelected = true;
				}).ConfigureAwait(false);
				return;
			}

			// If current cloud provider requires authentication and is not authenticated, authenticate now!
			var activeProvider = ResolveProvider(targetType);
			if (activeProvider.RequiresAuthentication)
			{
				bool isAuth = await activeProvider.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
				if (!isAuth || _storageSyncService.Status == SyncEngineStatus.AuthRequired)
				{
					await AuthenticateCurrentProviderAsync(cancellationToken).ConfigureAwait(false);
					return;
				}
			}

			await SafeDispatchToMainThreadAsync(() =>
			{
				StatusMessage = $"{ResolveProviderDisplayName(targetType)} is already the active storage provider.";
				ShowMigrationOptions = false;
				IsLocalStorageSelected = false;
			}).ConfigureAwait(false);
			return;
		}

		await SafeDispatchToMainThreadAsync(() =>
		{
			IsBusy = true;
			StatusMessage = $"Connecting to {ResolveProviderDisplayName(targetType)}...";
		}).ConfigureAwait(false);

		try
		{
			// 1. If Local Storage selected, emphasize isolation disclaimer
			if (targetType == StorageProviderType.LocalStorage)
			{
				await SafeDispatchToMainThreadAsync(() =>
				{
					PendingTargetProviderType = StorageProviderType.LocalStorage;
					IsLocalStorageSelected = true;
					ShowMigrationOptions = true;
					StatusMessage = LocalStorageDisclaimer;
				}).ConfigureAwait(false);
				return;
			}

			// 2. Remote Cloud Provider verification
			await SafeDispatchToMainThreadAsync(() => IsLocalStorageSelected = false).ConfigureAwait(false);
			if (_connectivity.NetworkAccess != NetworkAccess.Internet)
			{
				await SafeDispatchToMainThreadAsync(() =>
				{
					StatusMessage = $"Cannot connect to {ResolveProviderDisplayName(targetType)} while offline.";
					ShowMigrationOptions = false;
				}).ConfigureAwait(false);
				return;
			}

			var provider = ResolveProvider(targetType);
			if (provider.RequiresAuthentication)
			{
				bool isAuth = await provider.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
				if (!isAuth)
				{
					await SafeDispatchToMainThreadAsync(() => StatusMessage = $"Authenticating with {provider.DisplayName}...").ConfigureAwait(false);
					bool authResult = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
					if (!authResult)
					{
						await SafeDispatchToMainThreadAsync(() =>
						{
							StatusMessage = $"Authentication failed for {provider.DisplayName}.";
							ShowMigrationOptions = false;
						}).ConfigureAwait(false);
						return;
					}
				}
			}

			// 3. Armed for migration
			await SafeDispatchToMainThreadAsync(() =>
			{
				PendingTargetProviderType = targetType;
				SelectedMigrationOption = MigrationOption.TwoWayMergeBoth; // Default to two-way merge
				ShowMigrationOptions = true;
				StatusMessage = $"Ready to switch to {provider.DisplayName}. Select your migration strategy.";
			}).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = "Operation cancelled.").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				StatusMessage = $"Connection failed: {ex.Message}";
				ShowMigrationOptions = false;
			}).ConfigureAwait(false);
		}
		finally
		{
			await SafeDispatchToMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
		}
	}

	[RelayCommand]
	public void SelectMigrationOption(MigrationOption option)
	{
		SelectedMigrationOption = option;
		StatusMessage = option switch
		{
			MigrationOption.TwoWayMergeBoth => "Selected: Two-Way Merge Both. Click 'Confirm Migration' to proceed.",
			MigrationOption.UploadLocalToCloud => "Selected: Upload Local to Cloud. Click 'Confirm Migration' to proceed.",
			MigrationOption.DownloadCloudToLocal => "Selected: Download Cloud to Local. Click 'Confirm Migration' to proceed.",
			_ => StatusMessage
		};
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ConfirmMigrationAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy) return;

		await SafeDispatchToMainThreadAsync(() =>
		{
			IsBusy = true;
			StatusMessage = $"Migrating data to {ResolveProviderDisplayName(PendingTargetProviderType)}...";
		}).ConfigureAwait(false);

		try
		{
			bool success = await _storageSyncService.SwitchProviderAsync(
				PendingTargetProviderType,
				SelectedMigrationOption,
				cancellationToken).ConfigureAwait(false);

			await SafeDispatchToMainThreadAsync(() =>
			{
				if (success)
				{
					HasMigratedData = true;
					SelectedProviderType = _storageSyncService.CurrentProvider.ProviderType;
					IsLocalStorageSelected = SelectedProviderType == StorageProviderType.LocalStorage;
					ShowMigrationOptions = false;
					StatusMessage = $"Successfully switched storage provider to {SelectedProviderDisplayName}.";
					UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);
					UpdateSyncStatusDisplay(_storageSyncService.Status);
				}
				else
				{
					StatusMessage = $"Migration failed. Could not switch storage provider to {ResolveProviderDisplayName(PendingTargetProviderType)}.";
				}
			}).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = "Migration cancelled.").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = $"Migration failed: {ex.Message}").ConfigureAwait(false);
		}
		finally
		{
			await SafeDispatchToMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SyncNowAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy) return;

		await SafeDispatchToMainThreadAsync(() =>
		{
			IsBusy = true;
			StatusMessage = "Synchronizing data...";
		}).ConfigureAwait(false);

		try
		{
			bool success = await _storageSyncService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
			await SafeDispatchToMainThreadAsync(() =>
			{
				if (success)
				{
					HasMigratedData = true;
					StatusMessage = "Sync completed successfully.";
				}
				else
				{
					StatusMessage = _storageSyncService.Status == SyncEngineStatus.Offline
						? "Cannot sync while offline."
						: "Sync failed. Check credentials or connection.";
				}
				UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);
				UpdateSyncStatusDisplay(_storageSyncService.Status);
			}).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = "Sync cancelled.").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = $"Sync error: {ex.Message}").ConfigureAwait(false);
		}
		finally
		{
			await SafeDispatchToMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task AuthenticateCurrentProviderAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy) return;

		var currentType = _storageSyncService.CurrentProvider?.ProviderType ?? SelectedProviderType;
		if (currentType == StorageProviderType.LocalStorage)
		{
			StatusMessage = "Local Storage does not require authentication.";
			return;
		}

		if (_connectivity.NetworkAccess != NetworkAccess.Internet)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				StatusMessage = $"Cannot sign in to {ResolveProviderDisplayName(currentType)} while offline.";
			}).ConfigureAwait(false);
			return;
		}

		await SafeDispatchToMainThreadAsync(() =>
		{
			IsBusy = true;
			StatusMessage = $"Signing in to {ResolveProviderDisplayName(currentType)}...";
		}).ConfigureAwait(false);

		try
		{
			var provider = ResolveProvider(currentType);
			bool authSuccess = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
			await SafeDispatchToMainThreadAsync(() =>
			{
				if (authSuccess)
				{
					IsGoogleDriveAuthenticated = true;
					IsAuthRequired = false;
					StatusMessage = $"Successfully signed in to {provider.DisplayName}! Syncing...";
				}
				else
				{
					StatusMessage = $"Sign-in failed for {provider.DisplayName}. Check browser authorization or credentials.";
				}
			}).ConfigureAwait(false);

			if (authSuccess)
			{
				HasMigratedData = true;
				await _storageSyncService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
				await SafeDispatchToMainThreadAsync(() =>
				{
					UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);
					UpdateSyncStatusDisplay(_storageSyncService.Status);
					StatusMessage = $"Signed in and synced with {provider.DisplayName}.";
				}).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = "Sign-in cancelled.").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = $"Sign-in error: {ex.Message}").ConfigureAwait(false);
		}
		finally
		{
			await SafeDispatchToMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task AllowGoogleDriveAccessAsync(CancellationToken cancellationToken = default)
	{
		if (SelectedProviderType == StorageProviderType.GoogleDrive)
		{
			await AuthenticateCurrentProviderAsync(cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await SelectProviderAsync(StorageProviderType.GoogleDrive, cancellationToken).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task DisconnectGoogleAsync(CancellationToken cancellationToken = default)
	{
		await SignOutGoogleAsync(cancellationToken).ConfigureAwait(false);
	}

	[RelayCommand]
	public void ToggleGoogleCredentials()
	{
		IsGoogleCredentialsVisible = !IsGoogleCredentialsVisible;
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SaveAndAuthenticateGoogleAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy) return;

		await SafeDispatchToMainThreadAsync(() =>
		{
			IsBusy = true;
			StatusMessage = "Saving Google credentials...";
		}).ConfigureAwait(false);

		try
		{
			if (_secureStorage != null && _googleDriveConfig != null)
			{
				await _googleDriveConfig.SaveCustomCredentialsAsync(
					_secureStorage,
					GoogleCustomClientId,
					GoogleCustomClientSecret).ConfigureAwait(false);

				CurrentActiveClientIdDisplay = _googleDriveConfig.ActiveClientId;
				HasCustomCredentials = !string.IsNullOrWhiteSpace(_googleDriveConfig.CustomClientId);
			}

			IsGoogleCredentialsVisible = false;
			StatusMessage = "Credentials updated. Launching sign-in...";

			var provider = ResolveProvider(StorageProviderType.GoogleDrive);
			bool authSuccess = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
			await SafeDispatchToMainThreadAsync(() =>
			{
				if (authSuccess)
				{
					IsGoogleDriveAuthenticated = true;
					IsAuthRequired = false;
					StatusMessage = "Connected with new credentials! Syncing...";
				}
				else
				{
					StatusMessage = "Sign-in failed. Please check Client ID and Secret in Google Cloud Console.";
				}
			}).ConfigureAwait(false);

			if (authSuccess)
			{
				HasMigratedData = true;
				await _storageSyncService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
				await SafeDispatchToMainThreadAsync(() =>
				{
					UpdateLastSyncDisplay(_storageSyncService.LastSuccessfulSyncUtc);
					UpdateSyncStatusDisplay(_storageSyncService.Status);
					StatusMessage = "Google Drive connected and synced.";
				}).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = "Sign-in cancelled.").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await SafeDispatchToMainThreadAsync(() => StatusMessage = $"Error: {ex.Message}").ConfigureAwait(false);
		}
		finally
		{
			await SafeDispatchToMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ResetGoogleCredentialsAsync(CancellationToken cancellationToken = default)
	{
		if (_secureStorage != null && _googleDriveConfig != null)
		{
			await _googleDriveConfig.ResetToDefaultsAsync(_secureStorage).ConfigureAwait(false);
			GoogleCustomClientId = string.Empty;
			GoogleCustomClientSecret = string.Empty;
			HasCustomCredentials = false;
			CurrentActiveClientIdDisplay = _googleDriveConfig.ActiveClientId;
		}

		IsGoogleDriveAuthenticated = false;
		StatusMessage = "Credentials reset to defaults. Stored tokens cleared. Click 'Sign In' to reconnect.";
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SignOutGoogleAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var provider = ResolveProvider(StorageProviderType.GoogleDrive);
			await provider.SignOutAsync(cancellationToken).ConfigureAwait(false);
			IsGoogleDriveAuthenticated = false;
			IsAuthRequired = true;
			StatusMessage = "Signed out of Google Drive. Stored tokens cleared.";
			UpdateSyncStatusDisplay(SyncEngineStatus.AuthRequired);
		}
		catch (Exception ex)
		{
			StatusMessage = $"Sign out error: {ex.Message}";
		}
	}

	[RelayCommand]
	public void Close()
	{
		ShowMigrationOptions = false;
		StatusMessage = string.Empty;
		RequestClose?.Invoke();
	}

	private IStorageProvider ResolveProvider(StorageProviderType type)
	{
		var provider = _serviceProvider.GetKeyedService<IStorageProvider>(type);
		if (provider != null)
		{
			return provider;
		}

		return type switch
		{
			StorageProviderType.LocalStorage => _serviceProvider.GetRequiredService<LocalStorageProvider>(),
			StorageProviderType.GoogleDrive => _serviceProvider.GetRequiredService<GoogleDriveStorageProvider>(),
			StorageProviderType.OneDrive => _serviceProvider.GetRequiredService<OneDriveStorageProvider>(),
			_ => throw new InvalidOperationException($"Unsupported storage provider type: {type}")
		};
	}

	private static string ResolveProviderDisplayName(StorageProviderType type) => type switch
	{
		StorageProviderType.LocalStorage => "Local Storage",
		StorageProviderType.GoogleDrive => "Google Drive",
		StorageProviderType.OneDrive => "Microsoft OneDrive",
		_ => type.ToString()
	};

	private static async Task SafeDispatchToMainThreadAsync(Action action)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				action();
				return;
			}

			await MainThread.InvokeOnMainThreadAsync(action).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented") || ex is InvalidOperationException)
		{
			action();
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		RequestClose = null;
		_storageSyncService.StatusChanged -= OnSyncStatusChanged;
	}
}
