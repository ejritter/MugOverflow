using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Identifies the storage provider backing store type.
/// </summary>
public enum StorageProviderType
{
	/// <summary>
	/// Local on-device storage in FileSystem.AppDataDirectory/MugOverflow-Caffeine-consumption.
	/// </summary>
	LocalStorage = 0,

	/// <summary>
	/// Google Drive app folder accessed via zero-backend OAuth 2.0 PKCE.
	/// </summary>
	GoogleDrive = 1,

	/// <summary>
	/// Microsoft OneDrive app folder accessed via MSAL.NET and Microsoft Graph API.
	/// </summary>
	OneDrive = 2
}

/// <summary>
/// Contract for cloud and local storage providers capable of persisting and synchronizing
/// caffeine consumption decision logs across devices.
/// </summary>
public interface IStorageProvider
{
	/// <summary>
	/// Gets the distinct provider type identifier.
	/// </summary>
	StorageProviderType ProviderType { get; }

	/// <summary>
	/// Gets the user-friendly display name of the provider (e.g. "Local Storage", "Google Drive", "OneDrive").
	/// </summary>
	string DisplayName { get; }

	/// <summary>
	/// Gets a value indicating whether this provider requires user authentication before operations.
	/// </summary>
	bool RequiresAuthentication { get; }

	/// <summary>
	/// Checks if the provider is currently authenticated and authorized to perform read/write operations.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>True if authenticated and ready; otherwise false.</returns>
	Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Prompts the user or performs the authentication flow to authorize storage access.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>True if authentication succeeded; otherwise false.</returns>
	Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Revokes or clears stored authentication tokens for this provider.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task SignOutAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads and deserializes the caffeine consumption payload from the backing store.
	/// Returns null if the payload file does not exist or cannot be deserialized.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The deserialized StoragePayload instance, or null if absent/corrupted.</returns>
	Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Serializes and persists the caffeine consumption payload to the backing store.
	/// </summary>
	/// <param name="payload">The storage payload to persist.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>True if writing succeeded; otherwise false.</returns>
	Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default);

	/// <summary>
	/// Tests connectivity and write/read access to the provider's backing store.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>True if the storage target is accessible; otherwise false.</returns>
	Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
