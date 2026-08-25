using System.Text.Json.Serialization;

namespace MugOverflowProject.Models;

public sealed class OracleDecisionLog
{
	public long Id { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public bool OracleSaidYes { get; set; }
	public bool UserDrank { get; set; }
	public bool FollowedAdvice { get; set; }
	public string? CoffeeTypeId { get; set; }
	public string? CoffeeName { get; set; }
	public int CaffeineMg { get; set; }

	[JsonIgnore]
	public string TimeString => Timestamp.ToLocalTime().ToString("hh:mm tt");

	[JsonIgnore]
	public string FormattedTime => TimeString;

	[JsonIgnore]
	public string OracleBadgeText => OracleSaidYes ? "YES" : "NO";

	[JsonIgnore]
	public string OutcomeTitle => UserDrank ? (string.IsNullOrWhiteSpace(CoffeeName) ? "Drank Coffee" : CoffeeName) : "Skipped Coffee";

	[JsonIgnore]
	public string FormattedCoffeeName => OutcomeTitle;

	[JsonIgnore]
	public string ComplianceBadgeText => FollowedAdvice ? "Complied" : "Rebelled";

	[JsonIgnore]
	public string StatusBadgeText => $"{OracleBadgeText} • {ComplianceBadgeText}";

	[JsonIgnore]
	public string CaffeineDisplayText => UserDrank ? $"{CaffeineMg} mg" : "0 mg";

	[JsonIgnore]
	public string CoffeeDisplayName => OutcomeTitle;

	[JsonIgnore]
	public bool HasCoffeeDetails => UserDrank && !string.IsNullOrWhiteSpace(CoffeeTypeId);

	[JsonIgnore]
	public string OutcomeEmoji => UserDrank ? "☕" : "🚫";

	[JsonIgnore]
	public string ComplianceEmoji => FollowedAdvice ? "😇" : "😈";
}
