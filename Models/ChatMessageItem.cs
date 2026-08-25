using CommunityToolkit.Mvvm.ComponentModel;

namespace MugOverflowProject.Models;

public sealed partial class ChatMessageItem : ObservableObject
{
	public long Id { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	public bool IsUserMessage { get; init; }
	public bool IsOracleMessage { get; init; }
	public bool IsWelcomeMessage { get; init; }

	[ObservableProperty]
	public partial string MessageText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TimeString { get; set; } = DateTime.Now.ToString("hh:mm tt");

	public bool OracleSaidYes { get; init; }
	public string VerdictBadgeText => OracleSaidYes ? "YES" : "NO";
	public string VerdictBadgeDisplay => OracleSaidYes ? "✨ YES" : "🛑 NO";
	public string VerdictBadgeBgColor => OracleSaidYes ? "#E8F5E9" : "#FFEBEE";
	public string VerdictBadgeTextColor => OracleSaidYes ? "#2E7D32" : "#C62828";
	public string VerdictBadgeBorderColor => OracleSaidYes ? "#2E7D32" : "#C62828";

	[ObservableProperty]
	public partial bool IsActionPending { get; set; } = true;

	[ObservableProperty]
	public partial bool IsSelectingCoffee { get; set; }

	[ObservableProperty]
	public partial bool IsConfirmingSkip { get; set; }

	[ObservableProperty]
	public partial bool IsConfirmingDrink { get; set; }

	[ObservableProperty]
	public partial bool IsConfirmed { get; set; }

	[ObservableProperty]
	public partial string? SelectedCoffeeTypeId { get; set; }

	[ObservableProperty]
	public partial string? SelectedCoffeeName { get; set; }

	[ObservableProperty]
	public partial int SelectedCaffeineMg { get; set; }

	[ObservableProperty]
	public partial string OutcomeFeedbackText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ComplianceTagText { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ComplianceEmoji))]
	[NotifyPropertyChangedFor(nameof(ComplianceBadgeBgColor))]
	[NotifyPropertyChangedFor(nameof(ComplianceBadgeTextColor))]
	[NotifyPropertyChangedFor(nameof(ComplianceBadgeBorderColor))]
	public partial bool IsComplied { get; set; }

	public string ComplianceEmoji => IsComplied ? "😇" : "😈";
	public string ComplianceBadgeBgColor => IsComplied ? "#E8F5E9" : "#FFF3E0";
	public string ComplianceBadgeTextColor => IsComplied ? "#2E7D32" : "#D97706";
	public string ComplianceBadgeBorderColor => IsComplied ? "#2E7D32" : "#D97706";
}
