using MugOverflowProject.Models;
using MugOverflowProject.Services;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class CaffeineMetabolismServiceTests
{
	private readonly CaffeineMetabolismService _service = new();

	[Fact]
	public void CalculateActiveCaffeine_ZeroLogs_ReturnsZero()
	{
		var logs = new List<OracleDecisionLog>();
		double active = _service.CalculateActiveCaffeine(logs, DateTime.UtcNow);
		Assert.Equal(0.0, active);
	}

	[Fact]
	public void CalculateActiveCaffeine_SingleLog_AfterExactHalfLife_ReturnsHalf()
	{
		var now = DateTime.UtcNow;
		var logTime = now.AddHours(-5.0); // Exactly 1 half-life (5 hours ago)

		var logs = new List<OracleDecisionLog>
		{
			new()
			{
				Timestamp = logTime,
				UserDrank = true,
				CaffeineMg = 100
			}
		};

		double active = _service.CalculateActiveCaffeine(logs, now);
		Assert.Equal(50.0, active, precision: 2);
	}

	[Fact]
	public void CalculateActiveCaffeine_SingleLog_AfterTwoHalfLives_ReturnsQuarter()
	{
		var now = DateTime.UtcNow;
		var logTime = now.AddHours(-10.0); // 2 half-lives (10 hours ago)

		var logs = new List<OracleDecisionLog>
		{
			new()
			{
				Timestamp = logTime,
				UserDrank = true,
				CaffeineMg = 100
			}
		};

		double active = _service.CalculateActiveCaffeine(logs, now);
		Assert.Equal(25.0, active, precision: 2);
	}

	[Fact]
	public void CalculateActiveCaffeine_SkippedCoffee_DoesNotAddCaffeine()
	{
		var now = DateTime.UtcNow;
		var logs = new List<OracleDecisionLog>
		{
			new()
			{
				Timestamp = now,
				UserDrank = false,
				CaffeineMg = 0
			}
		};

		double active = _service.CalculateActiveCaffeine(logs, now);
		Assert.Equal(0.0, active);
	}

	[Fact]
	public void CalculateHoursToThreshold_CurrentBelowTarget_ReturnsZero()
	{
		double hours = _service.CalculateHoursToThreshold(15.0, 20.0);
		Assert.Equal(0.0, hours);
	}

	[Fact]
	public void CalculateHoursToThreshold_CurrentAboveTarget_ReturnsExpectedHours()
	{
		// From 100mg to 50mg (half) should be exactly 5 hours
		double hours = _service.CalculateHoursToThreshold(100.0, 50.0);
		Assert.Equal(5.0, hours, precision: 2);

		// From 100mg to 25mg (quarter) should be exactly 10 hours
		hours = _service.CalculateHoursToThreshold(100.0, 25.0);
		Assert.Equal(10.0, hours, precision: 2);
	}

	[Fact]
	public void Generate24HourDecayCurve_ReturnsPointsFromZeroTo24()
	{
		var points = _service.Generate24HourDecayCurve(100.0);
		Assert.NotEmpty(points);
		Assert.Equal(0.0, points[0].HourOffset);
		Assert.Equal(100.0, points[0].RemainingMg, precision: 2);

		var lastPoint = points[^1];
		Assert.True(lastPoint.HourOffset >= 24.0);
		// At 24h (4.8 half-lives): 100 * (0.5)^4.8 ≈ 3.59 mg
		Assert.True(lastPoint.RemainingMg < 5.0 && lastPoint.RemainingMg > 3.0);
	}

	[Fact]
	public void ComputeSummary_CalculatesAllKpiFieldsProperly()
	{
		var now = DateTime.UtcNow;
		var logs = new List<OracleDecisionLog>
		{
			new()
			{
				Timestamp = now,
				UserDrank = true,
				CaffeineMg = 95
			},
			new()
			{
				Timestamp = now.AddHours(-1),
				UserDrank = false,
				CaffeineMg = 0
			}
		};

		var compliance = new ComplianceStats
		{
			YesYes = 1,
			NoNo = 1,
			YesNo = 0,
			NoYes = 0
		};

		var summary = _service.ComputeSummary(logs, compliance);

		Assert.True(summary.ActiveCaffeineMg > 90);
		Assert.Equal(95, summary.TotalCaffeineTodayMg);
		Assert.Equal(1, summary.CupsLoggedCount);
		Assert.Equal(100, summary.CompliancePercentage);
		Assert.Equal(2, summary.ObeyedCount);
		Assert.Equal(0, summary.RebelledCount);
	}
}
