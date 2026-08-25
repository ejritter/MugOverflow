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
	[NotifyPropertyChangedFor(nameof(SelectedCoffeeCaffeineText))]
	public partial CoffeeType? SelectedCoffeePreference { get; set; } = coffeeDictionaryService.GetDefault();

	[ObservableProperty]
	public partial bool HasHistoryLogs { get; set; }

	[ObservableProperty]
	public partial MetabolismSummary Summary { get; set; } = new();

	[ObservableProperty]
	public partial ComplianceStats Compliance { get; set; } = new();

	[ObservableProperty]
	public partial string CurrentThemeName { get; set; } = ThemeManager.CurrentThemeName;

	public ObservableCollection<CoffeeType> CoffeeTypes { get; } = new(coffeeDictionaryService.GetAll());
	public ObservableCollection<CoffeeType> AvailableCoffeeTypes => CoffeeTypes;

	public string SelectedCoffeeCaffeineText => $"{SelectedCoffeePreference?.Caffeine ?? 95} mg";

	public ObservableCollection<ChatMessageItem> ChatMessages { get; } = [];
	public ObservableCollection<OracleDecisionLog> HistoryLogs { get; } = [];
	public CaffeineMetabolismDrawable MetabolismDrawable { get; } = new();

	public Action? RequestChartInvalidation { get; set; }

	// Direct properties for KPI cards to ensure crisp binding
	public double ActiveCaffeineMg => Summary.ActiveCaffeineMg;
	public int TotalCaffeineTodayMg => Summary.TotalCaffeineTodayMg;
	public int CupsLoggedCount => Summary.CupsLoggedCount;
	public int CompliancePercentage => Summary.CompliancePercentage;
	public int ObeyedCount => Summary.ObeyedCount;
	public int RebelledCount => Summary.RebelledCount;
	public string HoursToSleepReadyText => Summary.SleepWindowDisplay;
	public string HoursToClearanceText => Summary.ClearanceDisplay;
	public ComplianceStats ComplianceStats => Compliance;

	public async Task InitializeAsync()
	{
		if (SelectedCoffeePreference == null && CoffeeTypes.Count > 0)
		{
			SelectedCoffeePreference = CoffeeTypes[0];
		}

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
		if (ChatMessages.Count == 0)
		{
			ResetConversation();
		}
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
			MessageText = $"\"Should I have a {defaultCoffee.Name} ({defaultCoffee.Caffeine} mg)?\"",
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
		item.OutcomeFeedbackText = $"Logged: {coffee.Name} (+{coffee.Caffeine} mg)";
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
		item.OutcomeFeedbackText = "Skipped coffee intake.";
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
	private void ResetConversation()
	{
		ChatMessages.Clear();
		var welcomeMsg = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsWelcomeMessage = true,
			IsActionPending = false,
			OracleSaidYes = true,
			MessageText = "Greetings, seeker of caffeinated truth. Pick your brew below and ask if you should drink another cup!",
			TimeString = DateTime.Now.ToString("hh:mm tt")
		};
		ChatMessages.Add(welcomeMsg);
	}

	[RelayCommand]
	private async Task OpenCoffeeDictionaryAsync()
	{
		await appPopupService.ShowCoffeeDictionaryAsync(SelectedCoffeePreference?.Id);
	}

	[RelayCommand]
	private async Task OpenThemeSelectorAsync()
	{
		string? chosenTheme = await appPopupService.ShowThemeSelectorAsync();
		if (!string.IsNullOrWhiteSpace(chosenTheme))
		{
			ThemeManager.ApplyTheme(chosenTheme);
			CurrentThemeName = ThemeManager.CurrentThemeName;
			dataPersistenceService.SaveTheme(chosenTheme);
			RequestChartInvalidation?.Invoke();
		}
	}

	[RelayCommand]
	private async Task EditLogAsync(OracleDecisionLog logToEdit)
	{
		var result = await appPopupService.ShowEditLogAsync(logToEdit);
		if (result == null)
		{
			// User deleted the log
			HistoryLogs.Remove(logToEdit);
			HasHistoryLogs = HistoryLogs.Count > 0;
			RecalculateAllMetrics();
			await dataPersistenceService.SaveLogsAsync(HistoryLogs);
		}
		else
		{
			int index = HistoryLogs.IndexOf(logToEdit);
			if (index >= 0)
			{
				HistoryLogs[index] = result;
				RecalculateAllMetrics();
				await dataPersistenceService.SaveLogsAsync(HistoryLogs);
			}
		}
	}

	private void RecalculateAllMetrics()
	{
		// 1. Calculate compliance stats
		int yy = 0, nn = 0, yn = 0, ny = 0;
		foreach (var log in HistoryLogs)
		{
			if (log.OracleSaidYes && log.UserDrank) yy++;
			else if (!log.OracleSaidYes && !log.UserDrank) nn++;
			else if (log.OracleSaidYes && !log.UserDrank) yn++;
			else if (!log.OracleSaidYes && log.UserDrank) ny++;
		}

		Compliance = new ComplianceStats
		{
			YesYes = yy,
			NoNo = nn,
			YesNo = yn,
			NoYes = ny
		};

		// 2. Calculate metabolism summary
		Summary = caffeineMetabolismService.ComputeSummary(HistoryLogs.ToList(), Compliance);

		// 3. Update Chart Drawable
		MetabolismDrawable.InitialActiveMg = Summary.ActiveCaffeineMg;
		RequestChartInvalidation?.Invoke();

		// Notify direct property bindings
		OnPropertyChanged(nameof(ActiveCaffeineMg));
		OnPropertyChanged(nameof(TotalCaffeineTodayMg));
		OnPropertyChanged(nameof(CupsLoggedCount));
		OnPropertyChanged(nameof(CompliancePercentage));
		OnPropertyChanged(nameof(ObeyedCount));
		OnPropertyChanged(nameof(RebelledCount));
		OnPropertyChanged(nameof(HoursToSleepReadyText));
		OnPropertyChanged(nameof(HoursToClearanceText));
		OnPropertyChanged(nameof(ComplianceStats));
	}
}
