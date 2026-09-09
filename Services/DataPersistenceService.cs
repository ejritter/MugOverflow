using System.Text.Json;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public sealed class DataPersistenceService : IDataPersistenceService
{
	private const string LogFileName = "mugoverflow_logs.json";
	private const string ThemePrefKey = "mugoverflow_selected_theme";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly SemaphoreSlim _fileLock = new(1, 1);

	private string FilePath => Path.Combine(FileSystem.AppDataDirectory, LogFileName);

	public async Task<List<OracleDecisionLog>> LoadLogsAsync(CancellationToken cancellationToken = default)
	{
		await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!File.Exists(FilePath))
			{
				return [];
			}

			string json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
			var items = JsonSerializer.Deserialize<List<OracleDecisionLog>>(json, JsonOptions);
			return items ?? [];
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return [];
		}
		finally
		{
			_fileLock.Release();
		}
	}

	public async Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs, CancellationToken cancellationToken = default)
	{
		await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			string json = JsonSerializer.Serialize(logs, JsonOptions);
			await File.WriteAllTextAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			// Safe fall-through
		}
		finally
		{
			_fileLock.Release();
		}
	}

	public string GetSavedTheme()
	{
		return Preferences.Default.Get(ThemePrefKey, "Regal Earth");
	}

	public void SaveTheme(string themeName)
	{
		Preferences.Default.Set(ThemePrefKey, themeName);
	}
}
