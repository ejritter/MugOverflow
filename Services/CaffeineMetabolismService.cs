using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public sealed class CaffeineMetabolismService : ICaffeineMetabolismService
{
	public const double HalfLifeHours = 5.0;
	public const double FullClearanceThresholdMg = 1.0;
	public const double SleepThresholdMg = 20.0;

	public int CalculateDailyTotalCaffeineMg(IEnumerable<OracleDecisionLog> logs)
	{
		return logs.Where(l => l.UserDrank && l.Timestamp.ToLocalTime().Date == DateTime.Today).Sum(l => l.CaffeineMg);
	}

	public double CalculateActiveCaffeine(IEnumerable<OracleDecisionLog> logs, DateTime asOfUtc)
	{
		double totalActive = 0.0;
		DateTime asOfUtcNormalized = asOfUtc.Kind == DateTimeKind.Utc ? asOfUtc : asOfUtc.ToUniversalTime();

		foreach (var log in logs)
		{
			if (log.UserDrank && log.CaffeineMg > 0)
			{
				DateTime logTimestampUtc = log.Timestamp.Kind == DateTimeKind.Utc ? log.Timestamp : log.Timestamp.ToUniversalTime();
				double hoursElapsed = (asOfUtcNormalized - logTimestampUtc).TotalHours;
				if (hoursElapsed < 0 && hoursElapsed >= -0.5) hoursElapsed = 0.0;
				if (hoursElapsed >= 0 && hoursElapsed <= 48.0)
				{
					// Exponential decay formula: N(t) = N0 * (0.5)^(t / 5)
					double remaining = log.CaffeineMg * Math.Pow(0.5, hoursElapsed / HalfLifeHours);
					totalActive += remaining;
				}
			}
		}

		return Math.Max(0.0, totalActive);
	}

	public double CalculateHoursToThreshold(double currentActiveMg, double targetMg)
	{
		if (currentActiveMg <= targetMg || currentActiveMg <= 0.001)
		{
			return 0.0;
		}

		// target = current * (0.5)^(t/5) ==> t = 5 * ln(target/current) / ln(0.5)
		double hours = HalfLifeHours * (Math.Log(targetMg / currentActiveMg) / Math.Log(0.5));
		return Math.Max(0.0, hours);
	}

	public MetabolismSummary ComputeSummary(IReadOnlyList<OracleDecisionLog> logs, ComplianceStats compliance)
	{
		DateTime now = DateTime.UtcNow;
		double activeMg = CalculateActiveCaffeine(logs, now);
		int totalTodayMg = CalculateDailyTotalCaffeineMg(logs);
		int drankCount = logs.Count(l => l.UserDrank && l.Timestamp.ToLocalTime().Date == DateTime.Today);

		double hoursToClear = CalculateHoursToThreshold(activeMg, FullClearanceThresholdMg);
		double hoursToSleep = CalculateHoursToThreshold(activeMg, SleepThresholdMg);

		return new MetabolismSummary
		{
			ActiveCaffeineMg = activeMg,
			TotalCaffeineTodayMg = totalTodayMg,
			CupsLoggedCount = drankCount,
			HoursToFullClearance = hoursToClear,
			HoursToSleepReady = hoursToSleep,
			CompliancePercentage = compliance.CompliancePercentage,
			ObeyedCount = compliance.ObeyedCount,
			RebelledCount = compliance.RebelledCount
		};
	}

	public IReadOnlyList<(double HourOffset, double RemainingMg)> Generate24HourDecayCurve(double initialActiveMg)
	{
		var points = new List<(double HourOffset, double RemainingMg)>();
		double totalHours = 24.0;
		double step = 0.25; // 15-minute resolution

		for (double h = 0.0; h <= totalHours + 0.001; h += step)
		{
			double remaining = initialActiveMg * Math.Pow(0.5, h / HalfLifeHours);
			points.Add((h, remaining));
		}

		return points.AsReadOnly();
	}
}
