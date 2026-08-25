using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Controls;
using MugOverflowProject.Models;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

public sealed partial class MainViewModel(
	ICoffeeDictionaryService coffeeDictionaryService,
	IOracleEngineService oracleEngineService,
	ICaffeineMetabolismService caffeineMetabolismService,
	IDataPersistenceService dataPersistenceService,
	IAppPopupService appPopupService) : ObservableObject
{
	[ObservableProperty]
	public partial bool IsChatTabActive { get; set; } = true;

	[ObservableProperty]
	public partial bool IsStatsTabActive { get; set; }

	[ObservableProperty]
	public partial CoffeeType? SelectedCoffeePreference { get; set; }

	[ObservableProperty]
	public partial bool HasHistoryLogs { get; set; }

	[ObservableProperty]
	public partial MetabolismSummary Summary { get; set; } = new();

	[ObservableProperty]
	public partial ComplianceStats Compliance { get; set; } = new();

	[ObservableProperty]
	public partial string CurrentThemeName { get; set; } = ThemeManager.CurrentThemeName;

	public ObservableCollection<CoffeeType> CoffeeTypes { get; } = new(coffeeDictionaryService.GetAll());
	public ObservableCollection<ChatMessageItem> ChatMessages { get; } = [];
	public ObservableCollection<OracleDecisionLog> HistoryLogs { get; } = [];
	public CaffeineMetabolismDrawable MetabolismDrawable { get; } = new();

	public Action? RequestChartInvalidation { get; set; }

	public async Task InitializeAsync()
	{
		SelectedCoffeePreference = CoffeeTypes.Count > 0 ? CoffeeTypes[0] : null;

		// 1. Apply saved theme
		string savedTheme = dataPersistenceService.GetSavedTheme();
		ThemeManager.ApplyTheme(savedTheme);
		CurrentThemeName = ThemeManager.CurrentThemeName;

		// 2. Load stored logs
		var loadedLogs = await dataPersistenceService.LoadLogsAsync();
		HistoryLogs.Clear();
		foreach (var log in loadedLogs.OrderByDescending(l => l.Timestamp))
		{
			HistoryLogs.Add(log);
		}
		HasHistoryLogs = HistoryLogs.Count > 0;

		// 3. Recalculate metrics & curve
		RecalculateAllMetrics();

		// 4. Initialize welcome chat message
		ResetConversation();
	}

	[RelayCommand]
	private void SwitchToChatTab()
	{
		IsChatTabActive = true;
		IsStatsTabActive = false;
	}

	[RelayCommand]
	private void SwitchToStatsTab()
	{
		IsChatTabActive = false;
		IsStatsTabActive = true;
		RecalculateAllMetrics();
	}

	[RelayCommand]
	private void AskOracle()
	{
		var defaultCoffee = SelectedCoffeePreference ?? coffeeDictionaryService.GetDefault();
		var (saidYes, verdictText) = oracleEngineService.ConsultOracle();

		// 1. User query message
		var userMsg = new ChatMessageItem
		{
			IsUserMessage = true,
			MessageText = $"\"Should I have another cup of coffee right now?\"\nDefault preference: {defaultCoffee.Name}",
			TimeString = DateTime.Now.ToString("hh:mm tt")
		};
		ChatMessages.Add(userMsg);

		// 2. Oracle response bubble with interactive decision controls
		var oracleMsg = new ChatMessageItem
		{
			IsOracleMessage = true,
			OracleSaidYes = saidYes,
			MessageText = verdictText,
			TimeString = DateTime.Now.ToString("hh:mm tt"),
			IsActionPending = true,
			SelectedCoffeeTypeId = defaultCoffee.Id,
			SelectedCoffeeName = defaultCoffee.Name,
			SelectedCaffeineMg = defaultCoffee.Caffeine
		};
		ChatMessages.Add(oracleMsg);
	}

	[RelayCommand]
	private void StartDrinkSelection(ChatMessageItem item)
	{
		item.IsActionPending = false;
		item.IsSelectingCoffee = true;
		item.IsConfirmingSkip = false;
		item.IsConfirmingDrink = false;
	}

	[RelayCommand]
	private void SelectCoffeeForDrink(ChatMessageItem item)
	{
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = true;
	}

	[RelayCommand]
	private async Task ConfirmDrankOutcomeAsync(ChatMessageItem item)
	{
		var coffee = coffeeDictionaryService.GetById(item.SelectedCoffeeTypeId ?? string.Empty)
			?? SelectedCoffeePreference
			?? coffeeDictionaryService.GetDefault();

		bool complied = item.OracleSaidYes;

		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsActionPending = false;
		item.IsConfirmed = true;
		item.IsComplied = complied;
		item.SelectedCoffeeTypeId = coffee.Id;
		item.SelectedCoffeeName = coffee.Name;
		item.SelectedCaffeineMg = coffee.Caffeine;
		item.OutcomeFeedbackText = $"Logged: {coffee.Name} ({coffee.Caffeine} mg)";
		item.ComplianceTagText = complied ? "Complied with Oracle" : "Rebelled against Oracle";

		var logEntry = new OracleDecisionLog
		{
			Id = item.Id,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = item.OracleSaidYes,
			UserDrank = true,
			FollowedAdvice = complied,
			CoffeeTypeId = coffee.Id,
			CoffeeName = coffee.Name,
			CaffeineMg = coffee.Caffeine
		};

		HistoryLogs.Insert(0, logEntry);
		HasHistoryLogs = true;

		RecalculateAllMetrics();
		await dataPersistenceService.SaveLogsAsync(HistoryLogs);
	}

	[RelayCommand]
	private void PromptSkip(ChatMessageItem item)
	{
		item.IsActionPending = false;
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsConfirmingSkip = true;
	}

	[RelayCommand]
	private async Task ConfirmSkipOutcomeAsync(ChatMessageItem item)
	{
		bool complied = !item.OracleSaidYes;

		item.IsConfirmingSkip = false;
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsActionPending = false;
		item.IsConfirmed = true;
		item.IsComplied = complied;
		item.OutcomeFeedbackText = "Skipped coffee intake completely.";
		item.ComplianceTagText = complied ? "Complied with Oracle" : "Rebelled against Oracle";

		var logEntry = new OracleDecisionLog
		{
			Id = item.Id,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = item.OracleSaidYes,
			UserDrank = false,
			FollowedAdvice = complied,
			CoffeeTypeId = null,
			CoffeeName = null,
			CaffeineMg = 0
		};

		HistoryLogs.Insert(0, logEntry);
		HasHistoryLogs = true;

		RecalculateAllMetrics();
		await dataPersistenceService.SaveLogsAsync(HistoryLogs);
	}

	[RelayCommand]
	private void CancelAction(ChatMessageItem item)
	{
		item.IsActionPending = true;
		item.IsSelectingCoffee = false;
		item.IsConfirmingSkip = false;
		item.IsConfirmingDrink = false;
	}

	[RelayCommand]
	private void ResetConversation()
	{
		ChatMessages.Clear();
		ChatMessages.Add(new ChatMessageItem
		{
			IsWelcomeMessage = true,
			MessageText = "Hello! I am your MugOverflow Java Oracle Assistant. ☕\n\nI have calculated 50 nuanced ways to say YES and 50 strategic ways to say NO. Tap the button below to ask me if you should indulge in another coffee right now!",
			TimeString = "Java Oracle v1.0 • Ready"
		});
	}

	[RelayCommand]
	private async Task OpenCoffeeDictionaryAsync(string? coffeeId = null)
	{
		await appPopupService.ShowCoffeeDictionaryAsync(coffeeId);
	}

	[RelayCommand]
	private async Task OpenEditLogAsync(OracleDecisionLog log)
	{
		var result = await appPopupService.ShowEditLogAsync(log);
		if (result is null)
		{
			// Item was deleted
			HistoryLogs.Remove(log);
			HasHistoryLogs = HistoryLogs.Count > 0;

			// Sync chat bubble if present
			var chatItem = ChatMessages.FirstOrDefault(c => c.Id == log.Id);
			if (chatItem is not null)
			{
				ChatMessages.Remove(chatItem);
			}
		}
		else
		{
			// Item was modified
			int index = HistoryLogs.IndexOf(log);
			if (index >= 0)
			{
				HistoryLogs[index] = result;
			}

			// Sync chat bubble feedback
			var chatItem = ChatMessages.FirstOrDefault(c => c.Id == log.Id);
			if (chatItem is not null)
			{
				chatItem.IsComplied = result.FollowedAdvice;
				chatItem.SelectedCoffeeTypeId = result.CoffeeTypeId;
				chatItem.SelectedCoffeeName = result.CoffeeName;
				chatItem.SelectedCaffeineMg = result.CaffeineMg;
				chatItem.OutcomeFeedbackText = result.UserDrank
					? $"Logged: {result.CoffeeName} ({result.CaffeineMg} mg)"
					: "Skipped coffee intake completely.";
				chatItem.ComplianceTagText = result.FollowedAdvice
					? "Complied with Oracle"
					: "Rebelled against Oracle";
			}
		}

		RecalculateAllMetrics();
		await dataPersistenceService.SaveLogsAsync(HistoryLogs);
	}

	[RelayCommand]
	private async Task DeleteLogDirectAsync(OracleDecisionLog log)
	{
		HistoryLogs.Remove(log);
		HasHistoryLogs = HistoryLogs.Count > 0;

		var chatItem = ChatMessages.FirstOrDefault(c => c.Id == log.Id);
		if (chatItem is not null)
		{
			ChatMessages.Remove(chatItem);
		}

		RecalculateAllMetrics();
		await dataPersistenceService.SaveLogsAsync(HistoryLogs);
	}

	[RelayCommand]
	private async Task OpenThemeSelectorAsync()
	{
		string? theme = await appPopupService.ShowThemeSelectorAsync();
		if (!string.IsNullOrEmpty(theme))
		{
			CurrentThemeName = theme;
		}
	}

	public void RecalculateAllMetrics()
	{
		var newCompliance = new ComplianceStats();
		foreach (var log in HistoryLogs)
		{
			if (log.OracleSaidYes && log.UserDrank) newCompliance.YesYes++;
			else if (!log.OracleSaidYes && !log.UserDrank) newCompliance.NoNo++;
			else if (log.OracleSaidYes && !log.UserDrank) newCompliance.YesNo++;
			else if (!log.OracleSaidYes && log.UserDrank) newCompliance.NoYes++;
		}
		Compliance = newCompliance;

		Summary = caffeineMetabolismService.ComputeSummary(HistoryLogs.ToList(), Compliance);

		MetabolismDrawable.InitialActiveMg = Summary.ActiveCaffeineMg;
		RequestChartInvalidation?.Invoke();
	}
}
