using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface IDataPersistenceService
{
	Task<List<OracleDecisionLog>> LoadLogsAsync(CancellationToken cancellationToken = default);
	Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs, CancellationToken cancellationToken = default);
	string GetSavedTheme();
	void SaveTheme(string themeName);

	Task<List<ConsultationSession>> LoadConsultationsAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<List<ConsultationSession>>([]);
	}

	Task SaveConsultationsAsync(IEnumerable<ConsultationSession> consultations, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}
