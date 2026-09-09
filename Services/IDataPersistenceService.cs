using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface IDataPersistenceService
{
	Task<List<OracleDecisionLog>> LoadLogsAsync(CancellationToken cancellationToken = default);
	Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs, CancellationToken cancellationToken = default);
	string GetSavedTheme();
	void SaveTheme(string themeName);
}
