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
		Assert.Equal("✨ YES", msg.VerdictBadgeDisplay);
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
}
