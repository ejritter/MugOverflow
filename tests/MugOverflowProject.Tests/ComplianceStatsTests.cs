using MugOverflowProject.Models;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class ComplianceStatsTests
{
	[Fact]
	public void ComplianceStats_Empty_Returns100Percent()
	{
		var stats = new ComplianceStats();
		Assert.Equal(0, stats.TotalDecisions);
		Assert.Equal(0, stats.ObeyedCount);
		Assert.Equal(0, stats.RebelledCount);
		Assert.Equal(100, stats.CompliancePercentage);
	}

	[Fact]
	public void ComplianceStats_AllObeyed_Returns100Percent()
	{
		var stats = new ComplianceStats
		{
			YesYes = 3,
			NoNo = 2,
			YesNo = 0,
			NoYes = 0
		};
		Assert.Equal(5, stats.TotalDecisions);
		Assert.Equal(5, stats.ObeyedCount);
		Assert.Equal(0, stats.RebelledCount);
		Assert.Equal(100, stats.CompliancePercentage);
	}

	[Fact]
	public void ComplianceStats_HalfObeyed_Returns50Percent()
	{
		var stats = new ComplianceStats
		{
			YesYes = 1,
			NoNo = 1,
			YesNo = 1,
			NoYes = 1
		};
		Assert.Equal(4, stats.TotalDecisions);
		Assert.Equal(2, stats.ObeyedCount);
		Assert.Equal(2, stats.RebelledCount);
		Assert.Equal(50, stats.CompliancePercentage);
	}
}
