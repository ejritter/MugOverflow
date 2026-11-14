using MugOverflowProject.Models;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class MetabolismSummaryTests
{
	[Theory]
	[InlineData(0.0, "0h 0m")]
	[InlineData(2.5, "2h 30m")]
	[InlineData(5.25, "5h 15m")]
	[InlineData(1.995, "2h 0m")]
	[InlineData(0.999, "1h 0m")]
	[InlineData(10.993, "11h 0m")]
	public void ClearanceDisplay_CalculatesAndHandlesMinuteRollover(double hoursToClearance, string expectedDisplay)
	{
		var summary = new MetabolismSummary
		{
			HoursToFullClearance = hoursToClearance
		};

		Assert.Equal(expectedDisplay, summary.ClearanceDisplay);
	}

	[Theory]
	[InlineData(0.0, "Sleep ready (<20mg): NOW")]
	[InlineData(-1.5, "Sleep ready (<20mg): NOW")]
	[InlineData(3.5, "Sleep ready in: ~3h 30m")]
	[InlineData(2.995, "Sleep ready in: ~3h 0m")]
	[InlineData(0.999, "Sleep ready in: ~1h 0m")]
	[InlineData(6.25, "Sleep ready in: ~6h 15m")]
	public void SleepWindowDisplay_CalculatesAndHandlesMinuteRollover(double hoursToSleep, string expectedDisplay)
	{
		var summary = new MetabolismSummary
		{
			HoursToSleepReady = hoursToSleep
		};

		Assert.Equal(expectedDisplay, summary.SleepWindowDisplay);
	}

	[Theory]
	[InlineData(0, "0 coffees consumed today")]
	[InlineData(1, "1 coffee consumed today")]
	[InlineData(2, "2 coffees consumed today")]
	[InlineData(5, "5 coffees consumed today")]
	public void CupsCountDisplay_HandlesSingularAndPluralPhrasing(int cupsCount, string expectedDisplay)
	{
		var summary = new MetabolismSummary
		{
			CupsLoggedCount = cupsCount
		};

		Assert.Equal(expectedDisplay, summary.CupsCountDisplay);
	}

	[Fact]
	public void ActiveAndTotalCaffeineDisplay_FormatsCorrectly()
	{
		var summary = new MetabolismSummary
		{
			ActiveCaffeineMg = 87.6,
			TotalCaffeineTodayMg = 190
		};

		Assert.Equal("88", summary.ActiveCaffeineDisplay);
		Assert.Equal("190", summary.TotalCaffeineDisplay);
	}
}
