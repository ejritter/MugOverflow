namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Abstraction for platform-specific hardware-backed secure storage.
/// Encapsulates token persistence, credential cleanup, and platform exception safety.
/// </summary>
public interface ISecureStorageService
{
	/// <summary>
	/// Retrieves an encrypted string value associated with the specified key.
	/// Returns null if the key does not exist or if decryption fails due to corrupted platform keystore state.
	/// </summary>
	Task<string?> GetAsync(string key);

	/// <summary>
	/// Encrypts and stores a string value associated with the specified key.
	/// </summary>
	Task SetAsync(string key, string value);

	/// <summary>
	/// Removes the key-value pair from secure storage. Returns true if removed successfully.
	/// </summary>
	bool Remove(string key);

	/// <summary>
	/// Clears all encrypted entries associated with the application.
	/// </summary>
	void RemoveAll();
}
