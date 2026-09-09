using System.Text.Json.Serialization;
using SQLite;

namespace MugOverflowProject.Models;

/// <summary>
/// Status flag representing local vs cloud synchronization state.
/// </summary>
public enum SyncStatus
{
	Pending = 0,
	Synced = 1,
	ConflictResolved = 2
}

/// <summary>
/// SQLite ORM entity representing a single coffee decision and consumption log.
/// Stored in table 'decision_logs' with indexes on Timestamp and SyncStatus.
/// </summary>
[Table("decision_logs")]
public sealed class DecisionLogEntity
{
	[PrimaryKey]
	[JsonPropertyName("id")]
	public string Id { get; set; } = Guid.NewGuid().ToString();

	[Indexed]
	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;

	[JsonPropertyName("lastModifiedUtc")]
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

	[JsonPropertyName("decision")]
	public string Decision { get; set; } = string.Empty;

	[JsonPropertyName("reason")]
	public string Reason { get; set; } = string.Empty;

	[JsonPropertyName("caffeineAmountMg")]
	public double CaffeineAmountMg { get; set; }

	[JsonPropertyName("itemName")]
	public string ItemName { get; set; } = string.Empty;

	[JsonPropertyName("itemCategory")]
	public string ItemCategory { get; set; } = string.Empty;

	[JsonPropertyName("isComplianceOverride")]
	public bool IsComplianceOverride { get; set; }

	[Indexed]
	[JsonPropertyName("syncStatus")]
	public int SyncStatus { get; set; } = (int)Models.SyncStatus.Pending;

	// --- Domain Conversion Helpers ---

	/// <summary>
	/// Creates a DecisionLogEntity from a domain DecisionLogItem.
	/// </summary>
	public static DecisionLogEntity FromItem(DecisionLogItem item, SyncStatus syncStatus = Models.SyncStatus.Pending)
	{
		ArgumentNullException.ThrowIfNull(item);
		return new DecisionLogEntity
		{
			Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
			Timestamp = item.Timestamp,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = item.Decision,
			Reason = item.Reason,
			CaffeineAmountMg = item.CaffeineAmountMg,
			ItemName = item.ItemName,
			ItemCategory = item.ItemCategory,
			IsComplianceOverride = item.IsComplianceOverride,
			SyncStatus = (int)syncStatus
		};
	}

	/// <summary>
	/// Converts this entity to a domain DecisionLogItem.
	/// </summary>
	public DecisionLogItem ToItem()
	{
		return new DecisionLogItem
		{
			Id = Id,
			Timestamp = Timestamp,
			Decision = Decision,
			Reason = Reason,
			CaffeineAmountMg = CaffeineAmountMg,
			ItemName = ItemName,
			ItemCategory = ItemCategory,
			IsComplianceOverride = IsComplianceOverride
		};
	}

	/// <summary>
	/// Creates a DecisionLogEntity from an existing UI/Domain OracleDecisionLog.
	/// </summary>
	public static DecisionLogEntity FromOracleLog(OracleDecisionLog log, SyncStatus syncStatus = Models.SyncStatus.Pending)
	{
		ArgumentNullException.ThrowIfNull(log);
		bool isOverride = !log.FollowedAdvice;
		string decision = log.OracleSaidYes ? "YES" : "NO";
		string reason = log.FollowedAdvice ? "Complied with Oracle" : "Rebelled against Oracle";

		return new DecisionLogEntity
		{
			Id = log.Id.ToString(),
			Timestamp = log.Timestamp,
			LastModifiedUtc = DateTime.UtcNow,
			Decision = decision,
			Reason = reason,
			CaffeineAmountMg = log.CaffeineMg,
			ItemName = log.CoffeeName ?? string.Empty,
			ItemCategory = log.CoffeeTypeId ?? string.Empty,
			IsComplianceOverride = isOverride,
			SyncStatus = (int)syncStatus
		};
	}

	/// <summary>
	/// Converts this entity into an OracleDecisionLog for consumption by existing UI and ViewModel components.
	/// </summary>
	public OracleDecisionLog ToOracleLog()
	{
		long parsedId = long.TryParse(Id, out var idVal) ? idVal : (long)Math.Abs(Id.GetHashCode());
		bool oracleSaidYes = string.Equals(Decision, "YES", StringComparison.OrdinalIgnoreCase) ||
		                     string.Equals(Decision, "True", StringComparison.OrdinalIgnoreCase);
		bool followedAdvice = !IsComplianceOverride;
		bool userDrank = CaffeineAmountMg > 0 || (oracleSaidYes && followedAdvice) || (!oracleSaidYes && IsComplianceOverride);

		return new OracleDecisionLog
		{
			Id = parsedId,
			Timestamp = Timestamp,
			OracleSaidYes = oracleSaidYes,
			UserDrank = userDrank,
			FollowedAdvice = followedAdvice,
			CoffeeTypeId = string.IsNullOrWhiteSpace(ItemCategory) ? null : ItemCategory,
			CoffeeName = string.IsNullOrWhiteSpace(ItemName) ? null : ItemName,
			CaffeineMg = (int)Math.Round(CaffeineAmountMg)
		};
	}
}
