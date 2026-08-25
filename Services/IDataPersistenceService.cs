using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface IDataPersistenceService
{
	Task<List<OracleDecisionLog>> LoadLogsAsync();
	Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs);
	string GetSavedTheme();
	void SaveTheme(string themeName);
}
