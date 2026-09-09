using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// Configurable mock sync service for testing ViewModel interactions,
/// provider switching, status event handling, and synchronization triggers.
/// </summary>
public sealed class FakeStorageSyncService : IStorageSyncService
{
	private readonly Dictionary<StorageProviderType, IStorageProvider> _registeredProviders = new();

	public SyncEngineStatus Status { get; set; } = SyncEngineStatus.Idle;
	public DateTime? LastSuccessfulSyncUtc { get; set; } = null;
	public IStorageProvider CurrentProvider { get; set; }

	public bool ShouldFailInitialize { get; set; }
	public bool ShouldFailSynchronize { get; set; }
	public bool ShouldFailFlush { get; set; }
	public bool ShouldFailSwitch { get; set; }

	public int InitializeCallCount { get; private set; }
	public int SynchronizeCallCount { get; private set; }
	public int FlushPendingChangesCallCount { get; private set; }
	public int SwitchProviderCallCount { get; private set; }
	public int HandleAppSleepCallCount { get; private set; }
	public int HandleAppDestroyCallCount { get; private set; }

	public StorageProviderType LastTargetType { get; private set; }
	public MigrationOption LastMigrationOption { get; private set; }

	public event EventHandler<SyncEngineStatus>? StatusChanged;

	public FakeStorageSyncService(IStorageProvider? initialProvider = null)
	{
		CurrentProvider = initialProvider ?? new FakeStorageProvider
		{
			ProviderType = StorageProviderType.LocalStorage,
			DisplayName = "Local Storage"
		};
	}

	public void RegisterProviderInstance(StorageProviderType type, IStorageProvider provider)
	{
		_registeredProviders[type] = provider;
	}

	public void TriggerStatusChanged(SyncEngineStatus status)
	{
		Status = status;
		StatusChanged?.Invoke(this, status);
	}

	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		InitializeCallCount++;
		if (ShouldFailInitialize)
		{
			TriggerStatusChanged(SyncEngineStatus.Error);
			return Task.FromException(new InvalidOperationException("Simulated sync initialization failure"));
		}

		TriggerStatusChanged(SyncEngineStatus.Idle);
		return Task.CompletedTask;
	}

	public Task<bool> SynchronizeAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SynchronizeCallCount++;
		if (Status == SyncEngineStatus.Offline)
		{
			return Task.FromResult(false);
		}

		if (ShouldFailSynchronize)
		{
			TriggerStatusChanged(SyncEngineStatus.Error);
			return Task.FromResult(false);
		}

		LastSuccessfulSyncUtc = DateTime.UtcNow;
		TriggerStatusChanged(SyncEngineStatus.Success);
		return Task.FromResult(true);
	}

	public Task<bool> FlushPendingChangesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		FlushPendingChangesCallCount++;
		if (Status == SyncEngineStatus.Offline)
		{
			return Task.FromResult(false);
		}

		if (ShouldFailFlush)
		{
			TriggerStatusChanged(SyncEngineStatus.Error);
			return Task.FromResult(false);
		}

		LastSuccessfulSyncUtc = DateTime.UtcNow;
		return Task.FromResult(true);
	}

	public Task<bool> SwitchProviderAsync(
		StorageProviderType targetType,
		MigrationOption migrationOption,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SwitchProviderCallCount++;
		LastTargetType = targetType;
		LastMigrationOption = migrationOption;

		if (ShouldFailSwitch)
		{
			TriggerStatusChanged(SyncEngineStatus.Error);
			return Task.FromResult(false);
		}

		if (_registeredProviders.TryGetValue(targetType, out var newProvider))
		{
			CurrentProvider = newProvider;
		}
		else
		{
			CurrentProvider = new FakeStorageProvider
			{
				ProviderType = targetType,
				DisplayName = targetType.ToString()
			};
		}

		LastSuccessfulSyncUtc = DateTime.UtcNow;
		TriggerStatusChanged(SyncEngineStatus.Success);
		return Task.FromResult(true);
	}

	public Task HandleAppSleepAsync(CancellationToken cancellationToken = default)
	{
		HandleAppSleepCallCount++;
		return Task.CompletedTask;
	}

	public Task HandleAppDestroyAsync(CancellationToken cancellationToken = default)
	{
		HandleAppDestroyCallCount++;
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public void Dispose()
	{
	}
}
