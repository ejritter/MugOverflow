using System.Text.Json;
using System.Text.Json.Serialization;

namespace MugOverflowProject.Models;

/// <summary>
/// Top-level JSON payload schema for 'MugOverflow-Caffeine-consumption/caffeine_consumption.json'
/// exported and synchronized across Local, Google Drive, and OneDrive storage providers.
/// </summary>
public sealed class StoragePayload
{
	[JsonPropertyName("appVersion")]
	public string AppVersion { get; set; } = "1.0.0";

	[JsonPropertyName("schemaVersion")]
	public string SchemaVersion { get; set; } = "1.0.0";

	[JsonPropertyName("exportedAtUtc")]
	public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

	[JsonPropertyName("decisionLogs")]
	public List<DecisionLogEntity> DecisionLogs { get; set; } = [];

	/// <summary>
	/// Configured JSON serializer options for consistent serialization across all storage providers.
	/// </summary>
	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Serializes payload to formatted JSON string.
	/// </summary>
	public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

	/// <summary>
	/// Deserializes JSON string to a StoragePayload instance. Returns null if invalid.
	/// </summary>
	public static StoragePayload? FromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try
		{
			return JsonSerializer.Deserialize<StoragePayload>(json, JsonOptions);
		}
		catch
		{
			return null;
		}
	}
}
