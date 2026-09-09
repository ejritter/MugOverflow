namespace MugOverflowProject.Models;

/// <summary>
/// Domain model for decision logs used in ViewModels and business logic.
/// </summary>
public sealed class DecisionLogItem
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public string Decision { get; set; } = string.Empty;
	public string Reason { get; set; } = string.Empty;
	public double CaffeineAmountMg { get; set; }
	public string ItemName { get; set; } = string.Empty;
	public string ItemCategory { get; set; } = string.Empty;
	public bool IsComplianceOverride { get; set; }

	public DecisionLogEntity ToEntity(SyncStatus syncStatus = SyncStatus.Pending) =>
		DecisionLogEntity.FromItem(this, syncStatus);
}
