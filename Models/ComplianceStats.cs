namespace MugOverflowProject.Models;

public sealed class ComplianceStats
{
	public int YesYes { get; set; } // Oracle YES, User Drank (Complied)
	public int NoNo { get; set; }   // Oracle NO, User Skipped (Complied)
	public int YesNo { get; set; }  // Oracle YES, User Skipped (Rebelled)
	public int NoYes { get; set; }  // Oracle NO, User Drank (Rebelled)

	public int ObeyedCount => YesYes + NoNo;
	public int RebelledCount => YesNo + NoYes;
	public int TotalDecisions => ObeyedCount + RebelledCount;

	public int CompliancePercentage => TotalDecisions > 0
		? (int)Math.Round((double)ObeyedCount / TotalDecisions * 100)
		: 100;
}
