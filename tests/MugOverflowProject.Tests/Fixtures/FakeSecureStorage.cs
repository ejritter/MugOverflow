using System.Collections.Concurrent;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// Thread-safe in-memory fake implementation of ISecureStorageService for unit and integration testing.
/// Supports fault-injection toggles to simulate platform security/IO failures.
/// </summary>
public sealed class FakeSecureStorage : ISecureStorageService
{
	private readonly ConcurrentDictionary<string, string> _store = new();

	public bool ThrowOnGet { get; set; }
	public bool ThrowOnSet { get; set; }
	public bool ThrowOnRemove { get; set; }
	public bool ThrowOnRemoveAll { get; set; }

	public Task<string?> GetAsync(string key)
	{
		if (ThrowOnGet)
		{
			throw new InvalidOperationException("Simulated SecureStorage GetAsync failure.");
		}

		_store.TryGetValue(key, out var value);
		return Task.FromResult<string?>(value);
	}

	public Task SetAsync(string key, string value)
	{
		if (ThrowOnSet)
		{
			throw new InvalidOperationException("Simulated SecureStorage SetAsync failure.");
		}

		_store[key] = value;
		return Task.CompletedTask;
	}

	public bool Remove(string key)
	{
		if (ThrowOnRemove)
		{
			throw new InvalidOperationException("Simulated SecureStorage Remove failure.");
		}

		return _store.TryRemove(key, out _);
	}

	public void RemoveAll()
	{
		if (ThrowOnRemoveAll)
		{
			throw new InvalidOperationException("Simulated SecureStorage RemoveAll failure.");
		}

		_store.Clear();
	}

	public bool ContainsKey(string key) => _store.ContainsKey(key);

	public void Clear() => _store.Clear();

	public int Count => _store.Count;
}
