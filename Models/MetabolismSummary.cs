namespace MugOverflowProject.Models;

public sealed class MetabolismSummary
{
	public double ActiveCaffeineMg { get; set; }
	public int TotalCaffeineTodayMg { get; set; }
	public int CupsLoggedCount { get; set; }
	public double HoursToFullClearance { get; set; }
	public double HoursToSleepReady { get; set; }
	public int CompliancePercentage { get; set; } = 100;
	public int ObeyedCount { get; set; }
	public int RebelledCount { get; set; }

	public string ActiveCaffeineDisplay => $"{Math.Round(ActiveCaffeineMg)}";
	public string TotalCaffeineDisplay => $"{TotalCaffeineTodayMg}";
	public string CupsCountDisplay => $"{CupsLoggedCount} {(CupsLoggedCount == 1 ? "coffee" : "coffees")} consumed today";

	public string ClearanceDisplay
	{
		get
		{
			int hours = (int)Math.Floor(HoursToFullClearance);
			int minutes = (int)Math.Round((HoursToFullClearance - hours) * 60);
			if (minutes >= 60)
			{
				hours++;
				minutes = 0;
			}
			return $"{hours}h {minutes}m";
		}
	}

	public string SleepWindowDisplay
	{
		get
		{
			if (HoursToSleepReady <= 0)
			{
				return "Sleep ready (<20mg): NOW";
			}
			int hours = (int)Math.Floor(HoursToSleepReady);
			int minutes = (int)Math.Round((HoursToSleepReady - hours) * 60);
			if (minutes >= 60)
			{
				hours++;
				minutes = 0;
			}
			return $"Sleep ready in: ~{hours}h {minutes}m";
		}
	}

	public string ComplianceRateDisplay => $"{CompliancePercentage}%";
	public string ComplianceBreakdownDisplay => $"{ObeyedCount} Obeyed • {RebelledCount} Rebelled";
}
