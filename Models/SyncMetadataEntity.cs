namespace MugOverflowProject.Models;

/// <summary>
/// Constants for well-known keys stored in the sync_metadata table.
/// </summary>
public static class SyncMetadataKeys
{
	public const string LastSuccessfulSyncUtc = "LastSuccessfulSyncUtc";
	public const string ActiveProvider = "ActiveProvider";
	public const string LastHydrationUtc = "LastHydrationUtc";
	public const string SchemaVersion = "SchemaVersion";
	public const string DeviceId = "DeviceId";
}

/// <summary>
/// Persistence entity representing key-value metadata for sync orchestration and provider state.
/// Stored in table 'sync_metadata'.
/// </summary>
public sealed class SyncMetadataEntity
{
	public string Key { get; set; } = string.Empty;

	public string Value { get; set; } = string.Empty;

	public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

	public SyncMetadataEntity() { }

	public SyncMetadataEntity(string key, string value)
	{
		Key = key;
		Value = value;
		LastUpdatedUtc = DateTime.UtcNow;
	}

	/// <summary>
	/// Helper to create or update metadata with typed DateTime.
	/// </summary>
	public static SyncMetadataEntity FromDateTime(string key, DateTime utcDateTime) =>
		new(key, utcDateTime.ToString("o"));

	/// <summary>
	/// Helper to parse Value as DateTime. Returns null if invalid or not set.
	/// </summary>
	public DateTime? AsDateTime() =>
		DateTime.TryParse(Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
			? dt
			: null;
}
