namespace MugOverflowProject.Services;

public interface IOracleEngineService
{
	(bool SaidYes, string VerdictText) ConsultOracle();
}
