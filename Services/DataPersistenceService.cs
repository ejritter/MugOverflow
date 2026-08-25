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

	private string FilePath => Path.Combine(FileSystem.AppDataDirectory, LogFileName);

	public async Task<List<OracleDecisionLog>> LoadLogsAsync()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				return [];
			}

			string json = await File.ReadAllTextAsync(FilePath);
			var items = JsonSerializer.Deserialize<List<OracleDecisionLog>>(json, JsonOptions);
			return items ?? [];
		}
		catch
		{
			return [];
		}
	}

	public async Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs)
	{
		try
		{
			string json = JsonSerializer.Serialize(logs, JsonOptions);
			await File.WriteAllTextAsync(FilePath, json);
		}
		catch
		{
			// Safe fall-through
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
