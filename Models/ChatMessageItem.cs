using CommunityToolkit.Mvvm.ComponentModel;

namespace MugOverflowProject.Models;

public sealed partial class ChatMessageItem : ObservableObject
{
	private static long _lastIssuedId;

	private static long NextUniqueId()
	{
		while (true)
		{
			long previous = Interlocked.Read(ref _lastIssuedId);
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			long candidate = nowMs > previous ? nowMs : previous + 1;
			if (Interlocked.CompareExchange(ref _lastIssuedId, candidate, previous) == previous)
			{
				return candidate;
			}
		}
	}

	public long Id { get; init; } = NextUniqueId();
	public bool IsUserMessage { get; init; }
	public bool IsOracleMessage { get; init; }
	public bool IsWelcomeMessage { get; init; }
	public bool IsTypingIndicator { get; init; }
	public bool IsOutcomeReactionCard { get; init; }
	public bool IsBuzzCheckCard { get; init; }
	public string MascotAvatarReaction { get; init; } = string.Empty;

	public bool IsOracleVerdictCard => IsOracleMessage && !IsTypingIndicator;
	public bool IsTypingIndicatorMessage => IsOracleMessage && IsTypingIndicator;
	public bool ShowVerdictBadge => IsOracleVerdictCard && !IsWelcomeMessage && !IsBuzzCheckCard && !IsOutcomeReactionCard;
	public string OracleHeaderTitle => IsWelcomeMessage
		? "Mugsy • Barista Oracle"
		: IsBuzzCheckCard
			? "Roast Buzz Check"
			: IsOutcomeReactionCard
				? "Barista Log Receipt"
				: "Barista Verdict";

	[ObservableProperty]
	public partial bool IsStreamingText { get; set; }

	[ObservableProperty]
	public partial string MessageText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string VisibleMessageText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TimeString { get; set; } = DateTime.Now.ToString("hh:mm tt");

	public bool OracleSaidYes { get; init; }
	public string VerdictBadgeText => OracleSaidYes ? "YES" : "NO";
	public string VerdictBadgeDisplay => OracleSaidYes ? "☕ BREW IT (YES)" : "🫗 HOLD UP (NO)";
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

	partial void OnMessageTextChanged(string value)
	{
		if (!IsStreamingText)
		{
			VisibleMessageText = value ?? string.Empty;
		}
	}

	partial void OnIsStreamingTextChanged(bool value)
	{
		if (!value)
		{
			VisibleMessageText = MessageText;
		}
	}

	public ChatMessageItem Clone()
	{
		return new ChatMessageItem
		{
			Id = Id,
			IsUserMessage = IsUserMessage,
			IsOracleMessage = IsOracleMessage,
			IsWelcomeMessage = IsWelcomeMessage,
			IsTypingIndicator = IsTypingIndicator,
			IsOutcomeReactionCard = IsOutcomeReactionCard,
			IsBuzzCheckCard = IsBuzzCheckCard,
			MascotAvatarReaction = MascotAvatarReaction,
			MessageText = MessageText,
			VisibleMessageText = VisibleMessageText,
			TimeString = TimeString,
			OracleSaidYes = OracleSaidYes,
			IsActionPending = IsActionPending,
			IsSelectingCoffee = IsSelectingCoffee,
			IsConfirmingSkip = IsConfirmingSkip,
			IsConfirmingDrink = IsConfirmingDrink,
			IsConfirmed = IsConfirmed,
			SelectedCoffeeTypeId = SelectedCoffeeTypeId,
			SelectedCoffeeName = SelectedCoffeeName,
			SelectedCaffeineMg = SelectedCaffeineMg,
			OutcomeFeedbackText = OutcomeFeedbackText,
			ComplianceTagText = ComplianceTagText,
			IsComplied = IsComplied
		};
	}
}
