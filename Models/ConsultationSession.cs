using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MugOverflowProject.Models;

public sealed partial class ConsultationSession : ObservableObject
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	[ObservableProperty]
	public partial DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

	[ObservableProperty]
	public partial string Title { get; set; } = "Coffee Consultation";

	[ObservableProperty]
	public partial string Subtitle { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LastVerdictBadge { get; set; } = "✨ YES";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(VerdictBadgeBgColor))]
	[NotifyPropertyChangedFor(nameof(VerdictBadgeTextColor))]
	[NotifyPropertyChangedFor(nameof(VerdictBadgeBorderColor))]
	public partial bool LastOracleSaidYes { get; set; } = true;

	[ObservableProperty]
	[JsonIgnore]
	[NotifyPropertyChangedFor(nameof(SelectionStrokeThickness))]
	public partial bool IsSelected { get; set; }

	[JsonIgnore]
	public double SelectionStrokeThickness => IsSelected ? 2.0 : 1.0;

	[JsonIgnore]
	public string VerdictBadgeBgColor => LastOracleSaidYes ? "#E8F5E9" : "#FFEBEE";

	[JsonIgnore]
	public string VerdictBadgeTextColor => LastOracleSaidYes ? "#2E7D32" : "#C62828";

	[JsonIgnore]
	public string VerdictBadgeBorderColor => LastOracleSaidYes ? "#2E7D32" : "#C62828";

	public List<ChatMessageItem> Messages { get; set; } = [];

	public void UpdateSummaryFromMessages()
	{
		var nonWelcomeMessages = Messages
			.Where(m => !m.IsWelcomeMessage && !m.IsTypingIndicator)
			.ToList();

		var verdictMessages = nonWelcomeMessages
			.Where(m => m.IsOracleMessage)
			.ToList();

		var distinctCoffees = nonWelcomeMessages
			.Select(m => m.SelectedCoffeeName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (distinctCoffees.Count == 1)
		{
			Title = distinctCoffees[0];
		}
		else if (distinctCoffees.Count > 1)
		{
			Title = $"{distinctCoffees[^1]} (+{distinctCoffees.Count - 1} more)";
		}
		else
		{
			Title = "Oracle Consultation";
		}

		var lastVerdict = verdictMessages.LastOrDefault();
		if (lastVerdict != null)
		{
			LastOracleSaidYes = lastVerdict.OracleSaidYes;
			LastVerdictBadge = lastVerdict.OracleSaidYes ? "✨ YES" : "🛑 NO";
		}

		int queryCount = Math.Max(1, verdictMessages.Count > 0
			? verdictMessages.Count
			: nonWelcomeMessages.Count(m => m.IsUserMessage));

		string timeDisplay = nonWelcomeMessages.LastOrDefault()?.TimeString is { Length: > 0 } ts
			? ts
			: UpdatedAtUtc.ToLocalTime().ToString("hh:mm tt");

		Subtitle = $"{(queryCount == 1 ? "1 query" : $"{queryCount} queries")} • {timeDisplay}";
	}
}
