using MugOverflowProject.Models;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class ChatMessageItemTests
{
	[Fact]
	public void UserMessage_HasCorrectPropertyFlags()
	{
		var msg = new ChatMessageItem
		{
			IsUserMessage = true,
			MessageText = "Should I drink coffee?"
		};

		Assert.True(msg.IsUserMessage);
		Assert.False(msg.IsOracleMessage);
		Assert.False(msg.IsTypingIndicator);
		Assert.False(msg.IsOracleVerdictCard);
		Assert.False(msg.IsTypingIndicatorMessage);
		Assert.Equal("Should I drink coffee?", msg.VisibleMessageText);
	}

	[Fact]
	public void OracleVerdictMessage_HasCorrectPropertyFlags()
	{
		var msg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsTypingIndicator = false,
			OracleSaidYes = true,
			MessageText = "Yes, enjoy your coffee!"
		};

		Assert.False(msg.IsUserMessage);
		Assert.True(msg.IsOracleMessage);
		Assert.False(msg.IsTypingIndicator);
		Assert.True(msg.IsOracleVerdictCard);
		Assert.False(msg.IsTypingIndicatorMessage);
		Assert.True(msg.ShowVerdictBadge);
		Assert.Equal("YES", msg.VerdictBadgeText);
		Assert.Equal("☕ BREW IT (YES)", msg.VerdictBadgeDisplay);
		Assert.Equal("Barista Verdict", msg.OracleHeaderTitle);
		Assert.Equal("Yes, enjoy your coffee!", msg.VisibleMessageText);
	}

	[Fact]
	public void OracleTypingIndicatorMessage_HasCorrectPropertyFlags()
	{
		var msg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsTypingIndicator = true
		};

		Assert.False(msg.IsUserMessage);
		Assert.True(msg.IsOracleMessage);
		Assert.True(msg.IsTypingIndicator);
		Assert.False(msg.IsOracleVerdictCard);
		Assert.True(msg.IsTypingIndicatorMessage);
	}

	[Fact]
	public void WelcomeMessage_HasCorrectPropertyFlagsAndHidesVerdictBadge()
	{
		var msg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsWelcomeMessage = true,
			OracleSaidYes = true,
			MessageText = "Hey there! Mugsy's behind the espresso bar."
		};

		Assert.False(msg.IsUserMessage);
		Assert.True(msg.IsOracleMessage);
		Assert.True(msg.IsWelcomeMessage);
		Assert.False(msg.IsTypingIndicator);
		Assert.True(msg.IsOracleVerdictCard);
		Assert.False(msg.ShowVerdictBadge);
		Assert.Equal("Mugsy • Barista Oracle", msg.OracleHeaderTitle);
	}

	[Fact]
	public void BuzzCheckAndOutcomeReactionCards_HaveExpectedHeadersAndHideVerdictBadge()
	{
		var buzzMsg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsBuzzCheckCard = true,
			MascotAvatarReaction = "Happy",
			MessageText = "Active buzz: 95 mg"
		};

		Assert.Equal("Roast Buzz Check", buzzMsg.OracleHeaderTitle);
		Assert.False(buzzMsg.ShowVerdictBadge);

		var reactionMsg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsOutcomeReactionCard = true,
			MascotAvatarReaction = "Drank",
			MessageText = "Pulled to perfection!"
		};

		Assert.Equal("Barista Log Receipt", reactionMsg.OracleHeaderTitle);
		Assert.False(reactionMsg.ShowVerdictBadge);

		var noVerdictMsg = new ChatMessageItem
		{
			IsOracleMessage = true,
			OracleSaidYes = false,
			MessageText = "Hold off for now."
		};

		Assert.Equal("NO", noVerdictMsg.VerdictBadgeText);
		Assert.Equal("🫗 HOLD UP (NO)", noVerdictMsg.VerdictBadgeDisplay);
		Assert.True(noVerdictMsg.ShowVerdictBadge);
	}

	[Fact]
	public void VisibleMessageText_SyncsWhenNotStreaming_AndClonePreservesReactionMetadata()
	{
		var msg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsOutcomeReactionCard = true,
			IsBuzzCheckCard = false,
			MascotAvatarReaction = "Drank",
			IsStreamingText = true,
			MessageText = "Full barista quip text",
			VisibleMessageText = "Full"
		};

		Assert.Equal("Full", msg.VisibleMessageText);

		// Ending streaming snaps VisibleMessageText to MessageText
		msg.IsStreamingText = false;
		Assert.Equal("Full barista quip text", msg.VisibleMessageText);

		var clone = msg.Clone();
		Assert.Equal(msg.VisibleMessageText, clone.VisibleMessageText);
		Assert.True(clone.IsOutcomeReactionCard);
		Assert.False(clone.IsBuzzCheckCard);
		Assert.Equal("Drank", clone.MascotAvatarReaction);
	}
}
