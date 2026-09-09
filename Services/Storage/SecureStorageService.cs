using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Production implementation of <see cref="ISecureStorageService"/> backed by <see cref="SecureStorage.Default"/>.
/// Handles keystore corruption, platform unavailabilities, and memory fallbacks safely.
/// </summary>
public sealed class SecureStorageService : ISecureStorageService
{
	private readonly ILogger<SecureStorageService>? _logger;
	private readonly Dictionary<string, string> _inMemoryFallback = new(StringComparer.Ordinal);
	private bool _useFallback;

	public SecureStorageService(ILogger<SecureStorageService>? logger = null)
	{
		_logger = logger;
	}

	public async Task<string?> GetAsync(string key)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		if (_useFallback)
		{
			lock (_inMemoryFallback)
			{
				return _inMemoryFallback.TryGetValue(key, out var val) ? val : null;
			}
		}

		try
		{
			return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "SecureStorage.GetAsync failed for key '{Key}'. Switching to in-memory fallback.", key);
			_useFallback = true;
			lock (_inMemoryFallback)
			{
				return _inMemoryFallback.TryGetValue(key, out var val) ? val : null;
			}
		}
	}

	public async Task SetAsync(string key, string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(value);

		if (_useFallback)
		{
			lock (_inMemoryFallback)
			{
				_inMemoryFallback[key] = value;
			}
			return;
		}

		try
		{
			await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "SecureStorage.SetAsync failed for key '{Key}'. Switching to in-memory fallback.", key);
			_useFallback = true;
			lock (_inMemoryFallback)
			{
				_inMemoryFallback[key] = value;
			}
		}
	}

	public bool Remove(string key)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		lock (_inMemoryFallback)
		{
			_inMemoryFallback.Remove(key);
		}

		if (_useFallback)
		{
			return true;
		}

		try
		{
			return SecureStorage.Default.Remove(key);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "SecureStorage.Remove failed for key '{Key}'.", key);
			return false;
		}
	}

	public void RemoveAll()
	{
		lock (_inMemoryFallback)
		{
			_inMemoryFallback.Clear();
		}

		if (_useFallback)
		{
			return;
		}

		try
		{
			SecureStorage.Default.RemoveAll();
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "SecureStorage.RemoveAll failed.");
		}
	}
}
