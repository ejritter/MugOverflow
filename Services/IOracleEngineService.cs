namespace MugOverflowProject.Services;

public interface IOracleEngineService
{
	(bool SaidYes, string VerdictText) ConsultOracle();

	string GetOutcomeReaction(bool oracleSaidYes, bool userDrank, string? coffeeName, int caffeineMg)
	{
		string brew = string.IsNullOrWhiteSpace(coffeeName) ? "cup" : coffeeName;
		return (oracleSaidYes, userDrank) switch
		{
			(true, true) => $"☕ Right on cue! Enjoy every velvety sip of that {brew} (+{caffeineMg} mg logged to your roast gauge).",
			(false, true) => $"😈 Rebel at the espresso bar! Mugsy said hold up, you poured that {brew} (+{caffeineMg} mg) anyway—respect the boldness.",
			(false, false) => "🫗 Elite hydro-champion move! Skipping this round keeps your sleep curve silky smooth tonight.",
			(true, false) => "✨ Plot twist at the counter! The beans gave you the green light, yet you banked that caffeine token for later."
		};
	}
}
