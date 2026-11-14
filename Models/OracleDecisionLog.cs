using System.Text.Json.Serialization;

namespace MugOverflowProject.Models;

public sealed class OracleDecisionLog : IEquatable<OracleDecisionLog>
{
	public long Id { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	public string? ServerId { get; set; }
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

	[JsonIgnore]
	public string StatusBadgeBgColor => FollowedAdvice ? "#E8F5E9" : (UserDrank ? "#FFEBEE" : "#FFF3E0");

	[JsonIgnore]
	public string StatusBadgeBorderColor => FollowedAdvice ? "#2E7D32" : (UserDrank ? "#C62828" : "#D97706");

	[JsonIgnore]
	public string StatusBadgeTextColor => FollowedAdvice ? "#2E7D32" : (UserDrank ? "#C62828" : "#D97706");

	public bool Equals(OracleDecisionLog? other)
	{
		if (other is null) return false;
		if (ReferenceEquals(this, other)) return true;
		return Id == other.Id &&
		       ServerId == other.ServerId &&
		       Timestamp == other.Timestamp &&
		       OracleSaidYes == other.OracleSaidYes &&
		       UserDrank == other.UserDrank &&
		       FollowedAdvice == other.FollowedAdvice &&
		       CoffeeTypeId == other.CoffeeTypeId &&
		       CoffeeName == other.CoffeeName &&
		       CaffeineMg == other.CaffeineMg;
	}

	public override bool Equals(object? obj) => Equals(obj as OracleDecisionLog);

	public override int GetHashCode() => HashCode.Combine(Id, Timestamp, CaffeineMg, UserDrank);
}
