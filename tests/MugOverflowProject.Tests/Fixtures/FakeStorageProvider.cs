using MugOverflowProject.Models;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// Configurable mock storage provider implementing IStorageProvider for unit testing
/// synchronization services, provider migration workflows, and failure recovery.
/// </summary>
public sealed class FakeStorageProvider : IStorageProvider
{
	public StorageProviderType ProviderType { get; set; } = StorageProviderType.LocalStorage;
	public string DisplayName { get; set; } = "Fake Storage Provider";
	public bool RequiresAuthentication { get; set; } = false;

	public bool IsAuthenticated { get; set; } = true;
	public bool ShouldFailAuthentication { get; set; } = false;
	public bool ShouldFailRead { get; set; } = false;
	public bool ShouldFailWrite { get; set; } = false;
	public bool ShouldFailConnection { get; set; } = false;
	public int SimulatedDelayMs { get; set; } = 0;

	public StoragePayload? StoredPayload { get; set; }

	// Telemetry & Invocation Tracking
	public int IsAuthenticatedCallCount { get; private set; }
	public int AuthenticateCallCount { get; private set; }
	public int SignOutCallCount { get; private set; }
	public int ReadPayloadCallCount { get; private set; }
	public int WritePayloadCallCount { get; private set; }
	public int TestConnectionCallCount { get; private set; }
	public List<StoragePayload> WrittenPayloadHistory { get; } = [];

	public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
	{
		IsAuthenticatedCallCount++;
		await ApplyDelayAsync(cancellationToken);
		if (ShouldFailAuthentication)
		{
			return false;
		}
		return IsAuthenticated;
	}

	public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		AuthenticateCallCount++;
		await ApplyDelayAsync(cancellationToken);
		if (ShouldFailAuthentication)
		{
			IsAuthenticated = false;
			return false;
		}

		IsAuthenticated = true;
		return true;
	}

	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		SignOutCallCount++;
		await ApplyDelayAsync(cancellationToken);
		IsAuthenticated = false;
	}

	public async Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default)
	{
		ReadPayloadCallCount++;
		await ApplyDelayAsync(cancellationToken);

		if (ShouldFailRead)
		{
			return null;
		}

		if (StoredPayload == null)
		{
			return null;
		}

		// Return a deep copy via JSON roundtrip to prevent accidental mutation of test state
		return StoragePayload.FromJson(StoredPayload.ToJson());
	}

	public async Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(payload);

		WritePayloadCallCount++;
		await ApplyDelayAsync(cancellationToken);

		if (ShouldFailWrite)
		{
			return false;
		}

		var clonedPayload = StoragePayload.FromJson(payload.ToJson())!;
		StoredPayload = clonedPayload;
		WrittenPayloadHistory.Add(clonedPayload);
		return true;
	}

	public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
	{
		TestConnectionCallCount++;
		await ApplyDelayAsync(cancellationToken);
		return !ShouldFailConnection && (!RequiresAuthentication || IsAuthenticated);
	}

	public void Reset()
	{
		IsAuthenticatedCallCount = 0;
		AuthenticateCallCount = 0;
		SignOutCallCount = 0;
		ReadPayloadCallCount = 0;
		WritePayloadCallCount = 0;
		TestConnectionCallCount = 0;
		WrittenPayloadHistory.Clear();
		StoredPayload = null;
		IsAuthenticated = !RequiresAuthentication;
		ShouldFailAuthentication = false;
		ShouldFailRead = false;
		ShouldFailWrite = false;
		ShouldFailConnection = false;
		SimulatedDelayMs = 0;
	}

	private async Task ApplyDelayAsync(CancellationToken cancellationToken)
	{
		if (SimulatedDelayMs > 0)
		{
			await Task.Delay(SimulatedDelayMs, cancellationToken);
		}
	}
}
