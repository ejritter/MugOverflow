using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Controls;
using MugOverflowProject.Models;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Sync;

namespace MugOverflowProject.ViewModels;

public sealed partial class MainViewModel(
	ICoffeeDictionaryService coffeeDictionaryService,
	IOracleEngineService oracleEngineService,
	ICaffeineMetabolismService caffeineMetabolismService,
	IDataPersistenceService dataPersistenceService,
	IAppPopupService appPopupService,
	ISqliteDatabaseService sqliteDatabaseService,
	IStorageSyncService storageSyncService) : ObservableObject, IDisposable
{
	private CancellationTokenSource? _periodicTimerCts;

	[ObservableProperty]
	public partial bool IsChatTabActive { get; set; } = true;

	[ObservableProperty]
	public partial bool IsStatsTabActive { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(AskOracleCommand))]
	public partial bool IsOracleThinking { get; set; }

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

	[ObservableProperty]
	public partial string SyncStatusText { get; set; } = "Ready";

	[ObservableProperty]
	public partial Color SyncStatusColor { get; set; } = Color.FromArgb("#9CA3AF");

	[ObservableProperty]
	public partial bool IsSyncing { get; set; }

	[ObservableProperty]
	public partial bool IsOffline { get; set; }

	public ObservableCollection<CoffeeType> CoffeeTypes { get; } = new(coffeeDictionaryService.GetAll());
	public ObservableCollection<CoffeeType> AvailableCoffeeTypes => CoffeeTypes;

	public string SelectedCoffeeCaffeineText => $"{SelectedCoffeePreference?.Caffeine ?? 95} mg";

	public ObservableCollection<ChatMessageItem> ChatMessages { get; } = [];
	public ObservableCollection<OracleDecisionLog> HistoryLogs { get; } = [];
	public CaffeineMetabolismDrawable MetabolismDrawable { get; } = new();

	public Action? RequestChartInvalidation { get; set; }
	private bool _isInitialized;

	// Direct properties for KPI cards to ensure crisp binding
	public double ActiveCaffeineMg => Summary.ActiveCaffeineMg;
	public int TotalCaffeineTodayMg => Summary.TotalCaffeineTodayMg;
	public int CupsLoggedCount => Summary.CupsLoggedCount;
	public int CompliancePercentage => Summary.CompliancePercentage;
	public int ObeyedCount => Summary.ObeyedCount;
	public int RebelledCount => Summary.RebelledCount;
	public string HoursToSleepReadyText => Summary.SleepWindowDisplay;
	public string HoursToClearanceText => Summary.ClearanceDisplay;
	public string ComplianceBreakdownText => Summary.ComplianceBreakdownDisplay;
	public ComplianceStats ComplianceStats => Compliance;

	private static async Task SafeDispatchToMainThreadAsync(Action action)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				action();
				return;
			}

			await MainThread.InvokeOnMainThreadAsync(action).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented") || ex is InvalidOperationException)
		{
			action();
		}
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized)
		{
			return;
		}
		_isInitialized = true;

		if (SelectedCoffeePreference == null && CoffeeTypes.Count > 0)
		{
			SelectedCoffeePreference = CoffeeTypes[0];
		}

		// 1. Apply saved theme
		string savedTheme = dataPersistenceService.GetSavedTheme();
		ThemeManager.ApplyTheme(savedTheme);
		CurrentThemeName = ThemeManager.CurrentThemeName;

		// 2. Initialize sync orchestrator and subscribe to status events
		storageSyncService.StatusChanged -= OnSyncStatusChanged;
		storageSyncService.StatusChanged += OnSyncStatusChanged;
		await storageSyncService.InitializeAsync(cancellationToken).ConfigureAwait(false);
		UpdateSyncStatusProperties(storageSyncService.Status);

		// 3. Load stored logs from SQLite persistence
		await ReloadLogsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);

		// 4. Initialize welcome chat message
		if (ChatMessages.Count == 0)
		{
			ResetConversation();
		}

		// 5. Start low-overhead periodic background kinetic decay updates (every 60s)
		StartPeriodicRecalculation();
	}

	private void OnSyncStatusChanged(object? sender, SyncEngineStatus status)
	{
		_ = SafeDispatchToMainThreadAsync(() =>
		{
			UpdateSyncStatusProperties(status);
		});
	}

	private void UpdateSyncStatusProperties(SyncEngineStatus status)
	{
		switch (status)
		{
			case SyncEngineStatus.Syncing:
				IsSyncing = true;
				IsOffline = false;
				SyncStatusText = "Syncing...";
				SyncStatusColor = Color.FromArgb("#38BDF8"); // Sky blue
				break;

			case SyncEngineStatus.Offline:
				IsSyncing = false;
				IsOffline = true;
				SyncStatusText = "Offline";
				SyncStatusColor = Color.FromArgb("#F59E0B"); // Amber warning
				break;

			case SyncEngineStatus.Error:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Sync Error";
				SyncStatusColor = Color.FromArgb("#EF4444"); // Red error
				break;

			case SyncEngineStatus.AuthRequired:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Sign-in Required";
				SyncStatusColor = Color.FromArgb("#F59E0B"); // Amber warning
				break;

			case SyncEngineStatus.Success:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Synced";
				SyncStatusColor = Color.FromArgb("#10B981"); // Emerald green
				break;

			case SyncEngineStatus.Idle:
			default:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = storageSyncService.LastSuccessfulSyncUtc.HasValue ? "Synced" : "Ready";
				SyncStatusColor = storageSyncService.LastSuccessfulSyncUtc.HasValue ? Color.FromArgb("#10B981") : Color.FromArgb("#9CA3AF");
				break;
		}
	}

	public async Task ReloadLogsFromDatabaseAsync(CancellationToken cancellationToken = default)
	{
		var entities = await sqliteDatabaseService.GetAllLogsAsync(cancellationToken).ConfigureAwait(false);
		var logs = entities.Select(e => e.ToOracleLog()).OrderByDescending(l => l.Timestamp).ToList();

		await SafeDispatchToMainThreadAsync(() =>
		{
			SynchronizeHistoryLogsInPlace(logs);
			HasHistoryLogs = HistoryLogs.Count > 0;
			RecalculateAllMetrics();
		}).ConfigureAwait(false);
	}

	public void SynchronizeHistoryLogsInPlace(List<OracleDecisionLog> newLogs)
	{
		// 1. Fast-path: Check for sequence equality by Id and relevant properties
		if (HistoryLogs.Count == newLogs.Count)
		{
			bool identical = true;
			for (int i = 0; i < newLogs.Count; i++)
			{
				if (HistoryLogs[i].Id != newLogs[i].Id ||
				    HistoryLogs[i].UserDrank != newLogs[i].UserDrank ||
				    HistoryLogs[i].CaffeineMg != newLogs[i].CaffeineMg ||
				    HistoryLogs[i].OracleSaidYes != newLogs[i].OracleSaidYes ||
				    HistoryLogs[i].FollowedAdvice != newLogs[i].FollowedAdvice ||
				    HistoryLogs[i].Timestamp != newLogs[i].Timestamp ||
				    HistoryLogs[i].CoffeeTypeId != newLogs[i].CoffeeTypeId ||
				    HistoryLogs[i].CoffeeName != newLogs[i].CoffeeName)
				{
					identical = false;
					break;
				}
			}
			if (identical)
			{
				return; // Zero CollectionChanged events, zero layout passes!
			}
		}

		// 2. In-place reconciliation by ID
		var newIdMap = newLogs.ToDictionary(l => l.Id, l => l);

		// Remove deleted logs
		for (int i = HistoryLogs.Count - 1; i >= 0; i--)
		{
			if (!newIdMap.ContainsKey(HistoryLogs[i].Id))
			{
				HistoryLogs.RemoveAt(i);
			}
		}

		// Insert or update logs
		for (int targetIndex = 0; targetIndex < newLogs.Count; targetIndex++)
		{
			var targetLog = newLogs[targetIndex];
			int existingIndex = -1;
			for (int j = targetIndex; j < HistoryLogs.Count; j++)
			{
				if (HistoryLogs[j].Id == targetLog.Id)
				{
					existingIndex = j;
					break;
				}
			}

			if (existingIndex == targetIndex)
			{
				// Check if item details changed
				if (HistoryLogs[existingIndex].UserDrank != targetLog.UserDrank ||
				    HistoryLogs[existingIndex].CaffeineMg != targetLog.CaffeineMg ||
				    HistoryLogs[existingIndex].OracleSaidYes != targetLog.OracleSaidYes ||
				    HistoryLogs[existingIndex].FollowedAdvice != targetLog.FollowedAdvice ||
				    HistoryLogs[existingIndex].Timestamp != targetLog.Timestamp ||
				    HistoryLogs[existingIndex].CoffeeTypeId != targetLog.CoffeeTypeId ||
				    HistoryLogs[existingIndex].CoffeeName != targetLog.CoffeeName)
				{
					HistoryLogs[existingIndex] = targetLog;
				}
			}
			else if (existingIndex > targetIndex)
			{
				HistoryLogs.Move(existingIndex, targetIndex);
				HistoryLogs[targetIndex] = targetLog;
			}
			else
			{
				HistoryLogs.Insert(targetIndex, targetLog);
			}
		}
	}

	public void StartPeriodicRecalculation()
	{
		_periodicTimerCts?.Cancel();
		_periodicTimerCts?.Dispose();
		_periodicTimerCts = new CancellationTokenSource();
		_ = RunPeriodicRecalculationAsync(_periodicTimerCts.Token);
	}

	private async Task RunPeriodicRecalculationAsync(CancellationToken cancellationToken)
	{
		using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
		try
		{
			while (await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				if (cancellationToken.IsCancellationRequested) break;

				await SafeDispatchToMainThreadAsync(RecalculateAllMetrics).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// Clean exit
		}
		catch
		{
			// Safe background task failure isolation
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
	private void SelectCoffeePreference(CoffeeType coffee)
	{
		SelectedCoffeePreference = coffee;
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task AskOracleAsync(CancellationToken cancellationToken = default)
	{
		if (IsOracleThinking) return;

		var defaultCoffee = SelectedCoffeePreference ?? coffeeDictionaryService.GetDefault();
		var (saidYes, verdictText) = oracleEngineService.ConsultOracle();

		IsOracleThinking = true;

		ChatMessageItem? typingMsg = null;
		try
		{
			// 1. Add user query bubble
			var userMsg = new ChatMessageItem
			{
				IsOracleMessage = false,
				MessageText = $"Should I drink a cup of {defaultCoffee.Name} ({defaultCoffee.Caffeine} mg)?",
				TimeString = DateTime.Now.ToString("hh:mm tt"),
				SelectedCoffeeTypeId = defaultCoffee.Id,
				SelectedCoffeeName = defaultCoffee.Name,
				SelectedCaffeineMg = defaultCoffee.Caffeine
			};
			ChatMessages.Add(userMsg);

			// 2. Pause before typing indicator appears
			await Task.Delay(400, cancellationToken).ConfigureAwait(true);

			// 3. Add typing indicator bubble
			typingMsg = new ChatMessageItem
			{
				IsOracleMessage = true,
				IsTypingIndicator = true,
				TimeString = DateTime.Now.ToString("hh:mm tt")
			};
			ChatMessages.Add(typingMsg);

			// 4. Simulate thinking duration with pulsing dots
			await Task.Delay(1200, cancellationToken).ConfigureAwait(true);

			// 5. Remove typing indicator and add final oracle verdict card
			ChatMessages.Remove(typingMsg);
			typingMsg = null;

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
		catch (OperationCanceledException)
		{
			// Safe cancellation
		}
		finally
		{
			if (typingMsg != null)
			{
				ChatMessages.Remove(typingMsg);
			}
			IsOracleThinking = false;
		}
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

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ConfirmDrankOutcomeAsync(ChatMessageItem item, CancellationToken cancellationToken = default)
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

		// 1. Insert into SQLite persistence as Pending sync
		var entity = DecisionLogEntity.FromOracleLog(logEntry, Models.SyncStatus.Pending);
		await sqliteDatabaseService.InsertLogAsync(entity, cancellationToken).ConfigureAwait(false);

		// 2. Trigger asynchronous background flush of pending changes
		_ = storageSyncService.FlushPendingChangesAsync(CancellationToken.None);

		// 3. Fallback compatibility save
		await dataPersistenceService.SaveLogsAsync(HistoryLogs, cancellationToken).ConfigureAwait(false);
	}

	[RelayCommand]
	private void PromptSkip(ChatMessageItem item)
	{
		item.IsActionPending = false;
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsConfirmingSkip = true;
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ConfirmSkipOutcomeAsync(ChatMessageItem item, CancellationToken cancellationToken = default)
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

		// 1. Insert into SQLite persistence as Pending sync
		var entity = DecisionLogEntity.FromOracleLog(logEntry, Models.SyncStatus.Pending);
		await sqliteDatabaseService.InsertLogAsync(entity, cancellationToken).ConfigureAwait(false);

		// 2. Trigger asynchronous background flush of pending changes
		_ = storageSyncService.FlushPendingChangesAsync(CancellationToken.None);

		// 3. Fallback compatibility save
		await dataPersistenceService.SaveLogsAsync(HistoryLogs, cancellationToken).ConfigureAwait(false);
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

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task OpenCoffeeDictionaryAsync(CancellationToken cancellationToken = default)
	{
		var chosenCoffee = await appPopupService.ShowCoffeeDictionaryAsync(SelectedCoffeePreference?.Id).ConfigureAwait(false);
		if (chosenCoffee != null)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				SelectedCoffeePreference = chosenCoffee;
			}).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task OpenCoffeeDictionaryForCardAsync(ChatMessageItem item, CancellationToken cancellationToken = default)
	{
		var chosenCoffee = await appPopupService.ShowCoffeeDictionaryAsync(item.SelectedCoffeeTypeId ?? SelectedCoffeePreference?.Id).ConfigureAwait(false);
		if (chosenCoffee != null)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				item.SelectedCoffeeTypeId = chosenCoffee.Id;
				item.SelectedCoffeeName = chosenCoffee.Name;
				item.SelectedCaffeineMg = chosenCoffee.Caffeine;
				SelectedCoffeePreference = chosenCoffee;
			}).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task OpenThemeSelectorAsync(CancellationToken cancellationToken = default)
	{
		string? chosenTheme = await appPopupService.ShowThemeSelectorAsync().ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(chosenTheme))
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				ThemeManager.ApplyTheme(chosenTheme);
				CurrentThemeName = ThemeManager.CurrentThemeName;
				dataPersistenceService.SaveTheme(chosenTheme);
				RequestChartInvalidation?.Invoke();
			}).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task OpenStorageSettingsAsync(CancellationToken cancellationToken = default)
	{
		bool dataChanged = await appPopupService.ShowStorageSelectionPopupAsync().ConfigureAwait(false);
		if (dataChanged)
		{
			await ReloadLogsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
		}
		UpdateSyncStatusProperties(storageSyncService.Status);
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task EditLogAsync(OracleDecisionLog logToEdit, CancellationToken cancellationToken = default)
	{
		var result = await appPopupService.ShowEditLogAsync(logToEdit).ConfigureAwait(false);
		await SafeDispatchToMainThreadAsync(() =>
		{
			if (result == null)
			{
				// User deleted the log
				HistoryLogs.Remove(logToEdit);
				HasHistoryLogs = HistoryLogs.Count > 0;
				RecalculateAllMetrics();
			}
			else
			{
				int index = HistoryLogs.IndexOf(logToEdit);
				if (index >= 0)
				{
					HistoryLogs[index] = result;
					RecalculateAllMetrics();
				}
			}
		}).ConfigureAwait(false);

		if (result == null)
		{
			await sqliteDatabaseService.DeleteLogAsync(logToEdit.Id.ToString(), cancellationToken).ConfigureAwait(false);
		}
		else
		{
			var entity = DecisionLogEntity.FromOracleLog(result, Models.SyncStatus.Pending);
			await sqliteDatabaseService.UpsertLogAsync(entity, cancellationToken).ConfigureAwait(false);
		}

		_ = storageSyncService.FlushPendingChangesAsync(CancellationToken.None);
		await dataPersistenceService.SaveLogsAsync(HistoryLogs, cancellationToken).ConfigureAwait(false);
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
		OnPropertyChanged(nameof(ComplianceBreakdownText));
		OnPropertyChanged(nameof(ComplianceStats));
	}

	public void Dispose()
	{
		RequestChartInvalidation = null;
		storageSyncService.StatusChanged -= OnSyncStatusChanged;
		_periodicTimerCts?.Cancel();
		_periodicTimerCts?.Dispose();
		_periodicTimerCts = null;
	}
}
