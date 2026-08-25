using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface ICaffeineMetabolismService
{
	double CalculateActiveCaffeine(IEnumerable<OracleDecisionLog> logs, DateTime asOfUtc);
	double CalculateHoursToThreshold(double currentActiveMg, double targetMg);
	MetabolismSummary ComputeSummary(IReadOnlyList<OracleDecisionLog> logs, ComplianceStats compliance);
	IReadOnlyList<(double HourOffset, double RemainingMg)> Generate24HourDecayCurve(double initialActiveMg);
}
