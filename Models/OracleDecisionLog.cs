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
	public string OracleBadgeText => OracleSaidYes ? "YES" : "NO";

	[JsonIgnore]
	public string OutcomeTitle => UserDrank ? "Drank Coffee" : "Refrained";

	[JsonIgnore]
	public string ComplianceBadgeText => FollowedAdvice ? "Complied" : "Rebelled";

	[JsonIgnore]
	public string CaffeineDisplayText => UserDrank ? $"{CaffeineMg} mg" : "—";

	[JsonIgnore]
	public string CoffeeDisplayName => (UserDrank && !string.IsNullOrWhiteSpace(CoffeeName)) ? CoffeeName : "—";

	[JsonIgnore]
	public bool HasCoffeeDetails => UserDrank && !string.IsNullOrWhiteSpace(CoffeeTypeId);
}
