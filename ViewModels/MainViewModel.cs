using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MugOverflowProject.Controls;
using MugOverflowProject.Messages;
using MugOverflowProject.Models;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Sync;

namespace MugOverflowProject.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
	public const string WelcomeGreetingText =
		"Hey there! Mugsy's behind the espresso bar. Pick your drink below and tap a prompt bubble—let's see if the beans bless your next cup!";

	private static readonly PromptChipItem[] CoffeePromptPool =
	[
		new()
		{
			Id = "morning_kickoff",
			DisplayText = "☀️ Morning kickoff",
			PromptTemplate = "Morning kickoff! Should I brew a {0} ({1} mg)?"
		},
		new()
		{
			Id = "slump_rescue",
			DisplayText = "🫠 3 PM slump rescue",
			PromptTemplate = "Hitting the 3 PM slump—should I pull a {0} ({1} mg)?"
		},
		new()
		{
			Id = "deep_focus",
			DisplayText = "💻 Deep-focus sprint",
			PromptTemplate = "Diving into deep focus! Do the beans bless a {0} ({1} mg)?"
		},
		new()
		{
			Id = "one_more_cup",
			DisplayText = "☕ One more cup?",
			PromptTemplate = "Be honest, Mugsy—room for one more {0} ({1} mg)?"
		},
		new()
		{
			Id = "post_lunch",
			DisplayText = "🥐 Post-lunch coma",
			PromptTemplate = "Post-lunch eyelids getting heavy! Brew a {0} ({1} mg)?"
		},
		new()
		{
			Id = "rainy_cafe",
			DisplayText = "🌧️ Cozy café vibe",
			PromptTemplate = "Cozy coffeehouse vibe—should I pour a warm {0} ({1} mg)?"
		},
		new()
		{
			Id = "code_review",
			DisplayText = "🔍 Code-review survival",
			PromptTemplate = "Big stack of code reviews ahead—fuel up with a {0} ({1} mg)?"
		},
		new()
		{
			Id = "midnight_deploy",
			DisplayText = "🌙 Late-shift deploy",
			PromptTemplate = "Late-shift deploy incoming! Safe to sip a {0} ({1} mg)?"
		},
		new()
		{
			Id = "barista_blessing",
			DisplayText = "✨ Barista's blessing",
			PromptTemplate = "Craving that fresh roast aroma! Bless a {0} ({1} mg) right now?"
		},
		new()
		{
			Id = "pre_standup",
			DisplayText = "🎙️ Pre-standup boost",
			PromptTemplate = "Standup starts in five—quick {0} ({1} mg) for sharp vibes?"
		}
	];

	private static readonly PromptChipItem BuzzCheckChip = new()
	{
		Id = "buzz_check",
		DisplayText = "📈 Check my buzz",
		PromptTemplate = "📈 How's my caffeine buzz & sleep timer looking?",
		IsBuzzCheck = true
	};

	private readonly ICoffeeDictionaryService _coffeeDictionaryService;
	private readonly IOracleEngineService _oracleEngineService;
	private readonly ICaffeineMetabolismService _caffeineMetabolismService;
	private readonly IDataPersistenceService _dataPersistenceService;
	private readonly IAppPopupService _appPopupService;
	private readonly ISqliteDatabaseService _sqliteDatabaseService;
	private readonly IStorageSyncService _storageSyncService;
	private readonly MetabolismViewModel _metabolismViewModel;
	private bool _isInitialized;
	private int _promptPoolOffset;

	public MainViewModel(
		ICoffeeDictionaryService coffeeDictionaryService,
		IOracleEngineService oracleEngineService,
		ICaffeineMetabolismService caffeineMetabolismService,
		IDataPersistenceService dataPersistenceService,
		IAppPopupService appPopupService,
		ISqliteDatabaseService sqliteDatabaseService,
		IStorageSyncService storageSyncService,
		MetabolismViewModel? metabolismViewModel = null)
	{
		_coffeeDictionaryService = coffeeDictionaryService;
		_oracleEngineService = oracleEngineService;
		_caffeineMetabolismService = caffeineMetabolismService;
		_dataPersistenceService = dataPersistenceService;
		_appPopupService = appPopupService;
		_sqliteDatabaseService = sqliteDatabaseService;
		_storageSyncService = storageSyncService;

		_metabolismViewModel = metabolismViewModel ?? new MetabolismViewModel(
			caffeineMetabolismService,
			sqliteDatabaseService,
			storageSyncService,
			appPopupService,
			dataPersistenceService);

		_metabolismViewModel.PropertyChanged += (s, e) =>
		{
			if (e.PropertyName != null &&
				e.PropertyName != nameof(SyncStatusText) &&
				e.PropertyName != nameof(SyncStatusColor) &&
				e.PropertyName != nameof(IsSyncing) &&
				e.PropertyName != nameof(IsOffline) &&
				e.PropertyName != nameof(CurrentThemeName))
			{
				OnPropertyChanged(e.PropertyName);

				if (e.PropertyName == nameof(ActiveCaffeineMg) || e.PropertyName == nameof(Summary))
				{
					OnPropertyChanged(nameof(MascotMoodTitle));
					OnPropertyChanged(nameof(MascotMoodSubtitle));
				}
			}
		};

		SelectedCoffeePreference = _coffeeDictionaryService.GetDefault();
		CoffeeTypes = new ObservableCollection<CoffeeType>(_coffeeDictionaryService.GetAll());
		PopulatePromptChips();
		_metabolismViewModel.NavigatingToMainPage += SwitchToChatTab;

		ConsultationHistory.CollectionChanged += (_, _) =>
		{
			OnPropertyChanged(nameof(HasConsultationHistory));
		};
	}

	[ObservableProperty]
	public partial bool IsChatTabActive { get; set; } = true;

	[ObservableProperty]
	public partial bool IsStatsTabActive { get; set; }

	[ObservableProperty]
	public partial bool IsDrawerOpen { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(AskOracleCommand))]
	[NotifyCanExecuteChangedFor(nameof(SendPromptChipCommand))]
	[NotifyPropertyChangedFor(nameof(CanAskOracle))]
	[NotifyPropertyChangedFor(nameof(MascotMoodTitle))]
	[NotifyPropertyChangedFor(nameof(MascotMoodSubtitle))]
	[NotifyPropertyChangedFor(nameof(ShowPromptChipsDock))]
	public partial bool IsOracleThinking { get; set; }

	public bool CanAskOracle => !IsOracleThinking;

	[ObservableProperty]
	public partial string MascotReactionState { get; set; } = string.Empty;

	public string MascotMoodTitle
	{
		get
		{
			if (IsOracleThinking)
			{
				return "Pulling Your Shot... ☕";
			}

			if (ActiveCaffeineMg < 15.0)
			{
				return "Running on Fumes 😴";
			}

			if (ActiveCaffeineMg <= 200.0)
			{
				return "Sweet Spot Buzz ☕";
			}

			return "Maximum Overdrive ⚡";
		}
	}

	public string MascotMoodSubtitle
	{
		get
		{
			if (IsOracleThinking)
			{
				return "Grinding fresh beans & reading the crema...";
			}

			if (ActiveCaffeineMg < 15.0)
			{
				return "Mug's running dry—tap a prompt chip below!";
			}

			if (ActiveCaffeineMg <= 200.0)
			{
				return "Smooth espresso flow—focus is dialed right in.";
			}

			return "Easy tiger! Your keyboard's about to catch fire.";
		}
	}

	public int BrewingDelayMs { get; set; } = 1150;

	public ObservableCollection<PromptChipItem> PromptChips { get; } = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPendingVerdict))]
	[NotifyPropertyChangedFor(nameof(ShowPromptChipsDock))]
	[NotifyPropertyChangedFor(nameof(PendingDrinkButtonText))]
	public partial ChatMessageItem? PendingVerdictMessage { get; set; }

	public bool HasPendingVerdict => PendingVerdictMessage != null;
	public bool ShowPromptChipsDock => PendingVerdictMessage == null && !IsOracleThinking;
	public string PendingDrinkButtonText => $"☕ Brewed & drank {PendingVerdictMessage?.SelectedCoffeeName ?? SelectedCoffeePreference?.Name ?? "Coffee"}!";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SelectedCoffeeCaffeineText))]
	[NotifyPropertyChangedFor(nameof(PendingDrinkButtonText))]
	public partial CoffeeType? SelectedCoffeePreference { get; set; }

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

	public ObservableCollection<CoffeeType> CoffeeTypes { get; }
	public ObservableCollection<CoffeeType> AvailableCoffeeTypes => CoffeeTypes;

	public string SelectedCoffeeCaffeineText => $"{SelectedCoffeePreference?.Caffeine ?? 95} mg";

	public ObservableCollection<ChatMessageItem> ChatMessages { get; } = [];
	private const int MaxVisibleChatMessages = 50;

	public ObservableCollection<ConsultationSession> ConsultationHistory { get; } = [];
	private const int MaxSavedConsultations = 30;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsViewingSavedConsultation))]
	public partial ConsultationSession? ActiveConsultation { get; set; }

	public bool HasConsultationHistory => ConsultationHistory.Count > 0;
	public bool IsViewingSavedConsultation => ActiveConsultation != null;

	// Delegated properties from MetabolismViewModel
	public MetabolismViewModel MetabolismViewModel => _metabolismViewModel;
	public ObservableCollection<OracleDecisionLog> HistoryLogs => _metabolismViewModel.HistoryLogs;
	public CaffeineMetabolismDrawable MetabolismDrawable => _metabolismViewModel.MetabolismDrawable;
	public Action? RequestChartInvalidation
	{
		get => _metabolismViewModel.RequestChartInvalidation;
		set => _metabolismViewModel.RequestChartInvalidation = value;
	}

	public double ActiveCaffeineMg => _metabolismViewModel.ActiveCaffeineMg;
	public int TotalCaffeineTodayMg => _metabolismViewModel.TotalCaffeineTodayMg;
	public int CupsLoggedCount => _metabolismViewModel.CupsLoggedCount;
	public int CompliancePercentage => _metabolismViewModel.CompliancePercentage;
	public int ObeyedCount => _metabolismViewModel.ObeyedCount;
	public int RebelledCount => _metabolismViewModel.RebelledCount;
	public string HoursToSleepReadyText => _metabolismViewModel.HoursToSleepReadyText;
	public string HoursToClearanceText => _metabolismViewModel.HoursToClearanceText;
	public string ClearanceDisplay => _metabolismViewModel.ClearanceDisplay;
	public string SleepWindowDisplay => _metabolismViewModel.SleepWindowDisplay;
	public string CupsCountDisplay => _metabolismViewModel.CupsCountDisplay;
	public string ComplianceBreakdownText => _metabolismViewModel.ComplianceBreakdownText;
	public ComplianceStats ComplianceStats => _metabolismViewModel.ComplianceStats;
	public bool HasHistoryLogs => _metabolismViewModel.HasHistoryLogs;
	public MetabolismSummary Summary => _metabolismViewModel.Summary;
	public ComplianceStats Compliance => _metabolismViewModel.Compliance;

	// Delegated ledger presentation properties for complete parity
	public ObservableCollection<OracleDecisionLog> PagedHistoryLogs => _metabolismViewModel.PagedHistoryLogs;
	public string LedgerFilter => _metabolismViewModel.LedgerFilter;
	public string LedgerSearchText
	{
		get => _metabolismViewModel.LedgerSearchText;
		set => _metabolismViewModel.LedgerSearchText = value;
	}
	public int LedgerCurrentPage => _metabolismViewModel.LedgerCurrentPage;
	public int LedgerPageSize => _metabolismViewModel.LedgerPageSize;
	public int LedgerTotalPages => _metabolismViewModel.LedgerTotalPages;
	public int LedgerTotalFilteredCount => _metabolismViewModel.LedgerTotalFilteredCount;
	public bool CanGoToPreviousLedgerPage => _metabolismViewModel.CanGoToPreviousLedgerPage;
	public bool CanGoToNextLedgerPage => _metabolismViewModel.CanGoToNextLedgerPage;
	public string LedgerPageSummaryText => _metabolismViewModel.LedgerPageSummaryText;
	public string LedgerCurrentPageDisplay => _metabolismViewModel.LedgerCurrentPageDisplay;
	public bool HasFilteredLedgerLogs => _metabolismViewModel.HasFilteredLedgerLogs;
	public bool IsLedgerFilteredEmpty => _metabolismViewModel.IsLedgerFilteredEmpty;
	public int LedgerAllCount => _metabolismViewModel.LedgerAllCount;
	public int LedgerDrankCount => _metabolismViewModel.LedgerDrankCount;
	public int LedgerSkippedCount => _metabolismViewModel.LedgerSkippedCount;
	public int LedgerCompliedCount => _metabolismViewModel.LedgerCompliedCount;
	public int LedgerRebelledCount => _metabolismViewModel.LedgerRebelledCount;
	public bool HasLedgerSearchText => _metabolismViewModel.HasLedgerSearchText;
	public bool IsFilterAllActive => _metabolismViewModel.IsFilterAllActive;
	public bool IsFilterDrankActive => _metabolismViewModel.IsFilterDrankActive;
	public bool IsFilterSkippedActive => _metabolismViewModel.IsFilterSkippedActive;
	public bool IsFilterCompliedActive => _metabolismViewModel.IsFilterCompliedActive;
	public bool IsPageSize5Active => _metabolismViewModel.IsPageSize5Active;
	public bool IsPageSize10Active => _metabolismViewModel.IsPageSize10Active;
	public bool IsPageSize20Active => _metabolismViewModel.IsPageSize20Active;
	public string LedgerSortOrder
	{
		get => _metabolismViewModel.LedgerSortOrder;
		set => _metabolismViewModel.LedgerSortOrder = value;
	}
	public bool IsSortNewestActive => _metabolismViewModel.IsSortNewestActive;
	public bool IsSortOldestActive => _metabolismViewModel.IsSortOldestActive;
	public bool IsSortCaffeineActive => _metabolismViewModel.IsSortCaffeineActive;

	public string LedgerFilterAllText => _metabolismViewModel.LedgerFilterAllText;
	public string LedgerFilterDrankText => _metabolismViewModel.LedgerFilterDrankText;
	public string LedgerFilterSkippedText => _metabolismViewModel.LedgerFilterSkippedText;
	public string LedgerFilterCompliedText => _metabolismViewModel.LedgerFilterCompliedText;
	public string LedgerFilterRebelledText => _metabolismViewModel.LedgerFilterRebelledText;

	public IRelayCommand NextLedgerPageCommand => _metabolismViewModel.NextLedgerPageCommand;
	public IRelayCommand PreviousLedgerPageCommand => _metabolismViewModel.PreviousLedgerPageCommand;
	public IRelayCommand FirstLedgerPageCommand => _metabolismViewModel.FirstLedgerPageCommand;
	public IRelayCommand LastLedgerPageCommand => _metabolismViewModel.LastLedgerPageCommand;
	public IRelayCommand<string> SetLedgerFilterCommand => _metabolismViewModel.SetLedgerFilterCommand;
	public IRelayCommand ClearLedgerSearchCommand => _metabolismViewModel.ClearLedgerSearchCommand;
	public IRelayCommand ClearFilterAndSearchCommand => _metabolismViewModel.ClearFilterAndSearchCommand;
	public IRelayCommand<int> SetLedgerPageSizeCommand => _metabolismViewModel.SetLedgerPageSizeCommand;
	public IRelayCommand<string> SetLedgerSortOrderCommand => _metabolismViewModel.SetLedgerSortOrderCommand;

	public void SetLedgerFilter(string filter) => _metabolismViewModel.SetLedgerFilter(filter);
	public void ClearLedgerSearch() => _metabolismViewModel.ClearLedgerSearch();
	public void ClearFilterAndSearch() => _metabolismViewModel.ClearFilterAndSearch();
	public void NextLedgerPage() => _metabolismViewModel.NextLedgerPage();
	public void PreviousLedgerPage() => _metabolismViewModel.PreviousLedgerPage();
	public void FirstLedgerPage() => _metabolismViewModel.FirstLedgerPage();
	public void LastLedgerPage() => _metabolismViewModel.LastLedgerPage();
	public void SetLedgerPageSize(int size) => _metabolismViewModel.SetLedgerPageSize(size);
	public void SetLedgerSortOrder(string order) => _metabolismViewModel.SetLedgerSortOrder(order);

	public void SynchronizeHistoryLogsInPlace(List<OracleDecisionLog> newLogs)
	{
		_metabolismViewModel.SynchronizeHistoryLogsInPlace(newLogs);
	}

	public void StartPeriodicRecalculation()
	{
		_metabolismViewModel.StartPeriodicRecalculation();
	}

	public Task ExecutePeriodicRecalculationTickAsync(CancellationToken cancellationToken = default)
	{
		return _metabolismViewModel.ExecutePeriodicRecalculationTickAsync(cancellationToken);
	}

	public void RecalculateAllMetrics()
	{
		_metabolismViewModel.RecalculateAllMetrics();
	}

	public Task RecalculateAllMetricsAsync()
	{
		return _metabolismViewModel.RecalculateAllMetricsAsync();
	}

	public Task ReloadLogsFromDatabaseAsync(CancellationToken cancellationToken = default)
	{
		return _metabolismViewModel.ReloadLogsFromDatabaseAsync(cancellationToken);
	}

	[RelayCommand]
	public void ToggleDrawer()
	{
		IsDrawerOpen = !IsDrawerOpen;
	}

	[RelayCommand]
	public void CloseDrawer()
	{
		IsDrawerOpen = false;
	}

	[RelayCommand]
	public void ShufflePromptChips()
	{
		_promptPoolOffset = (_promptPoolOffset + 4) % CoffeePromptPool.Length;
		PopulatePromptChips();
	}

	private void PopulatePromptChips()
	{
		const int coffeeChipCount = 4;

		if (PromptChips.Count == coffeeChipCount + 1)
		{
			for (int i = 0; i < coffeeChipCount; i++)
			{
				PromptChips[i] = CoffeePromptPool[(_promptPoolOffset + i) % CoffeePromptPool.Length];
			}

			if (!ReferenceEquals(PromptChips[coffeeChipCount], BuzzCheckChip))
			{
				PromptChips[coffeeChipCount] = BuzzCheckChip;
			}

			return;
		}

		PromptChips.Clear();
		for (int i = 0; i < coffeeChipCount; i++)
		{
			PromptChips.Add(CoffeePromptPool[(_promptPoolOffset + i) % CoffeePromptPool.Length]);
		}
		PromptChips.Add(BuzzCheckChip);
	}

	private int GetEffectiveBrewingDelayMs()
	{
		return Application.Current == null ? 0 : Math.Max(0, BrewingDelayMs);
	}

	private void AddChatMessageWithSlidingWindow(ChatMessageItem item)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				ChatMessages.Add(item);
				while (ChatMessages.Count > MaxVisibleChatMessages)
				{
					ChatMessages.RemoveAt(0);
				}
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			ChatMessages.Add(item);
			while (ChatMessages.Count > MaxVisibleChatMessages)
			{
				ChatMessages.RemoveAt(0);
			}
			return;
		}
		catch
		{
		}

		_ = SafeDispatchToMainThreadAsync(() =>
		{
			ChatMessages.Add(item);
			while (ChatMessages.Count > MaxVisibleChatMessages)
			{
				ChatMessages.RemoveAt(0);
			}
		});
	}

	private void RemoveChatMessageSafe(ChatMessageItem item)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				ChatMessages.Remove(item);
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			ChatMessages.Remove(item);
			return;
		}
		catch
		{
		}

		_ = SafeDispatchToMainThreadAsync(() =>
		{
			ChatMessages.Remove(item);
		});
	}

	private static async Task StreamMessageTextAsync(
		ChatMessageItem item,
		string fullText,
		int totalDurationMs,
		CancellationToken cancellationToken)
	{
		if (Application.Current == null || string.IsNullOrEmpty(fullText) || totalDurationMs <= 0)
		{
			item.IsStreamingText = false;
			item.MessageText = fullText;
			item.VisibleMessageText = fullText;
			return;
		}

		item.IsStreamingText = true;
		item.MessageText = fullText;
		item.VisibleMessageText = string.Empty;

		try
		{
			const int stepDelayMs = 22;
			int steps = Math.Max(8, totalDurationMs / stepDelayMs);
			int charsPerStep = Math.Max(1, (int)Math.Ceiling((double)fullText.Length / steps));

			for (int len = charsPerStep; len < fullText.Length; len += charsPerStep)
			{
				cancellationToken.ThrowIfCancellationRequested();
				item.VisibleMessageText = fullText[..len];
				await Task.Delay(stepDelayMs, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
			// Gracefully snap to full text in finally
		}
		finally
		{
			item.IsStreamingText = false;
			item.VisibleMessageText = fullText;
		}
	}

	private static async Task SafeDispatchToMainThreadAsync(Action action, TimeSpan? timeout = null)
	{
		if (action == null) return;

		try
		{
			if (MainThread.IsMainThread)
			{
				action();
				return;
			}

			var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
			await MainThread.InvokeOnMainThreadAsync(action).WaitAsync(effectiveTimeout).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			action();
		}
		catch (Exception)
		{
		}
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized) return;
		_isInitialized = true;

		if (SelectedCoffeePreference == null && CoffeeTypes.Count > 0)
		{
			SelectedCoffeePreference = CoffeeTypes[0];
		}

		// 1. Apply saved theme
		string savedTheme = _dataPersistenceService.GetSavedTheme();
		ThemeManager.ApplyTheme(savedTheme);
		CurrentThemeName = ThemeManager.CurrentThemeName;

		// 2. Initialize sync orchestrator and subscribe to status events
		_storageSyncService.StatusChanged -= OnSyncStatusChanged;
		_storageSyncService.StatusChanged += OnSyncStatusChanged;
		await _storageSyncService.InitializeAsync(cancellationToken).ConfigureAwait(false);
		UpdateSyncStatusProperties(_storageSyncService.Status);

		// 3. Initialize metabolism ViewModel
		await _metabolismViewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);

		// 4. Load saved consultation history
		var savedConsultations = await _dataPersistenceService.LoadConsultationsAsync(cancellationToken).ConfigureAwait(false);
		if (savedConsultations.Count > 0)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				ConsultationHistory.Clear();
				foreach (var session in savedConsultations)
				{
					session.IsSelected = false;
					ConsultationHistory.Add(session);
				}
			}).ConfigureAwait(false);
		}

		// 5. Initialize welcome chat message
		if (ChatMessages.Count == 0)
		{
			await SafeDispatchToMainThreadAsync(PerformResetConversation).ConfigureAwait(false);
		}
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
				SyncStatusColor = Color.FromArgb("#38BDF8");
				break;

			case SyncEngineStatus.Offline:
				IsSyncing = false;
				IsOffline = true;
				SyncStatusText = "Offline";
				SyncStatusColor = Color.FromArgb("#F59E0B");
				break;

			case SyncEngineStatus.Error:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Sync Error";
				SyncStatusColor = Color.FromArgb("#EF4444");
				break;

			case SyncEngineStatus.AuthRequired:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Sign-in Required";
				SyncStatusColor = Color.FromArgb("#F59E0B");
				break;

			case SyncEngineStatus.Success:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = "Synced";
				SyncStatusColor = Color.FromArgb("#10B981");
				break;

			case SyncEngineStatus.Idle:
			default:
				IsSyncing = false;
				IsOffline = false;
				SyncStatusText = _storageSyncService.LastSuccessfulSyncUtc.HasValue ? "Synced" : "Ready";
				SyncStatusColor = _storageSyncService.LastSuccessfulSyncUtc.HasValue ? Color.FromArgb("#10B981") : Color.FromArgb("#9CA3AF");
				break;
		}
	}

	[RelayCommand]
	private void SwitchToChatTab()
	{
		IsChatTabActive = true;
		IsStatsTabActive = false;
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SwitchToStatsTabAsync()
	{
		IsDrawerOpen = false;
		IsChatTabActive = false;
		IsStatsTabActive = true;
		WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
		RequestChartInvalidation?.Invoke();

		if (Shell.Current != null)
		{
			Func<Task> navigate = async () =>
			{
				try
				{
					await Shell.Current.GoToAsync("//MetabolismPage");
				}
				catch
				{
					try
					{
						await Shell.Current.GoToAsync("///MetabolismPage");
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[MainViewModel] Navigation to MetabolismPage failed: {ex}");
					}
				}
			};

			if (MainThread.IsMainThread)
			{
				await navigate();
			}
			else
			{
				await MainThread.InvokeOnMainThreadAsync(navigate);
			}
		}
	}

	[RelayCommand]
	private void SelectCoffeePreference(CoffeeType coffee)
	{
		SelectedCoffeePreference = coffee;
	}

	[RelayCommand(CanExecute = nameof(CanAskOracle), AllowConcurrentExecutions = false)]
	public async Task SendPromptChipAsync(PromptChipItem? chip, CancellationToken cancellationToken = default)
	{
		if (IsOracleThinking)
		{
			return;
		}

		if (chip?.IsBuzzCheck == true)
		{
			var typingItem = new ChatMessageItem
			{
				IsOracleMessage = true,
				IsTypingIndicator = true,
				IsActionPending = false,
				MascotAvatarReaction = "Brewing",
				MessageText = "Checking your bloodstream roast gauge...",
				TimeString = DateTime.Now.ToString("hh:mm tt")
			};

			IsOracleThinking = true;
			MascotReactionState = "Brewing";

			try
			{
				AddChatMessageWithSlidingWindow(new ChatMessageItem
				{
					IsUserMessage = true,
					IsOracleMessage = false,
					MessageText = "📈 How's my caffeine buzz & sleep timer looking?",
					TimeString = DateTime.Now.ToString("hh:mm tt")
				});

				AddChatMessageWithSlidingWindow(typingItem);

				int effectiveDelay = GetEffectiveBrewingDelayMs();
				int buzzDelay = effectiveDelay > 0 ? Math.Min(700, effectiveDelay) : 0;
				if (buzzDelay > 0)
				{
					await Task.Delay(buzzDelay, cancellationToken);
				}

				RemoveChatMessageSafe(typingItem);
				MascotReactionState = "Happy";

				string buzzText =
					$"Active buzz: {ActiveCaffeineMg:F0} mg in your system ({TotalCaffeineTodayMg} mg across {CupsCountDisplay} today). Barista alignment: {CompliancePercentage}%. Sleep window (<20 mg): {SleepWindowDisplay}.";

				var buzzCard = new ChatMessageItem
				{
					IsOracleMessage = true,
					IsBuzzCheckCard = true,
					IsActionPending = false,
					OracleSaidYes = true,
					MascotAvatarReaction = "Happy",
					MessageText = buzzText,
					TimeString = DateTime.Now.ToString("hh:mm tt")
				};

				AddChatMessageWithSlidingWindow(buzzCard);
				SyncActiveConsultationInPlace();

				if (Application.Current != null)
				{
					await StreamMessageTextAsync(buzzCard, buzzText, 420, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				RemoveChatMessageSafe(typingItem);
				IsOracleThinking = false;
			}

			return;
		}

		string promptTemplate = string.IsNullOrWhiteSpace(chip?.PromptTemplate)
			? "Should I brew a cup of {0} ({1} mg) right now?"
			: chip.PromptTemplate;

		await ExecuteCoffeeConsultationAsync(promptTemplate, cancellationToken);
	}

	[RelayCommand(CanExecute = nameof(CanAskOracle), AllowConcurrentExecutions = false)]
	private async Task AskOracleAsync(CancellationToken cancellationToken = default)
	{
		await ExecuteCoffeeConsultationAsync("Should I drink a cup of {0} ({1} mg)?", cancellationToken);
	}

	private async Task ExecuteCoffeeConsultationAsync(string promptTemplate, CancellationToken cancellationToken)
	{
		if (IsOracleThinking)
		{
			return;
		}

		var defaultCoffee = SelectedCoffeePreference ?? _coffeeDictionaryService.GetDefault();
		var (saidYes, verdictText) = _oracleEngineService.ConsultOracle();

		string userQueryText;
		try
		{
			userQueryText = string.Format(promptTemplate, defaultCoffee.Name, defaultCoffee.Caffeine);
		}
		catch (FormatException)
		{
			userQueryText = $"{promptTemplate} ({defaultCoffee.Name}, {defaultCoffee.Caffeine} mg)";
		}

		var typingItem = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsTypingIndicator = true,
			IsActionPending = false,
			MascotAvatarReaction = "Brewing",
			MessageText = "Pulling your shot & reading the crema...",
			TimeString = DateTime.Now.ToString("hh:mm tt")
		};

		IsOracleThinking = true;
		MascotReactionState = "Brewing";

		try
		{
			// 1. Add user query bubble
			AddChatMessageWithSlidingWindow(new ChatMessageItem
			{
				IsUserMessage = true,
				IsOracleMessage = false,
				MessageText = userQueryText,
				TimeString = DateTime.Now.ToString("hh:mm tt"),
				SelectedCoffeeTypeId = defaultCoffee.Id,
				SelectedCoffeeName = defaultCoffee.Name,
				SelectedCaffeineMg = defaultCoffee.Caffeine
			});

			// 2. Show brewing typing indicator and pause when live UI is active
			AddChatMessageWithSlidingWindow(typingItem);
			int effectiveDelay = GetEffectiveBrewingDelayMs();
			if (effectiveDelay > 0)
			{
				await Task.Delay(effectiveDelay, cancellationToken);
			}
			RemoveChatMessageSafe(typingItem);

			// 3. Add Barista verdict bubble and expose reply dock
			string reaction = saidYes ? "Yes" : "No";
			MascotReactionState = reaction;

			var verdictItem = new ChatMessageItem
			{
				IsOracleMessage = true,
				OracleSaidYes = saidYes,
				MascotAvatarReaction = reaction,
				MessageText = verdictText,
				TimeString = DateTime.Now.ToString("hh:mm tt"),
				IsActionPending = true,
				SelectedCoffeeTypeId = defaultCoffee.Id,
				SelectedCoffeeName = defaultCoffee.Name,
				SelectedCaffeineMg = defaultCoffee.Caffeine
			};

			AddChatMessageWithSlidingWindow(verdictItem);
			PendingVerdictMessage = verdictItem;
			SyncActiveConsultationInPlace();

			if (Application.Current != null)
			{
				await StreamMessageTextAsync(verdictItem, verdictText, 450, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			RemoveChatMessageSafe(typingItem);
			IsOracleThinking = false;
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ReplyDrankAsync(CancellationToken cancellationToken = default)
	{
		var target = PendingVerdictMessage
			?? ChatMessages.LastOrDefault(m => m.IsOracleVerdictCard && !m.IsWelcomeMessage && !m.IsBuzzCheckCard && !m.IsOutcomeReactionCard && m.IsActionPending);
		if (target == null)
		{
			return;
		}

		PendingVerdictMessage = null;

		var coffee = _coffeeDictionaryService.GetById(target.SelectedCoffeeTypeId ?? string.Empty)
			?? SelectedCoffeePreference
			?? _coffeeDictionaryService.GetDefault();

		AddChatMessageWithSlidingWindow(new ChatMessageItem
		{
			IsUserMessage = true,
			IsOracleMessage = false,
			MessageText = $"☕ Brewed & drank a {coffee.Name} (+{coffee.Caffeine} mg)!",
			TimeString = DateTime.Now.ToString("hh:mm tt"),
			SelectedCoffeeTypeId = coffee.Id,
			SelectedCoffeeName = coffee.Name,
			SelectedCaffeineMg = coffee.Caffeine
		});

		await ConfirmDrankOutcomeAsync(target, cancellationToken);

		MascotReactionState = "Drank";
		string reactionText = _oracleEngineService.GetOutcomeReaction(target.OracleSaidYes, true, coffee.Name, coffee.Caffeine);

		var reactionBubble = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsOutcomeReactionCard = true,
			OracleSaidYes = target.OracleSaidYes,
			IsActionPending = false,
			IsConfirmed = true,
			IsComplied = target.IsComplied,
			MascotAvatarReaction = "Drank",
			OutcomeFeedbackText = target.OutcomeFeedbackText,
			ComplianceTagText = target.ComplianceTagText,
			SelectedCoffeeTypeId = coffee.Id,
			SelectedCoffeeName = coffee.Name,
			SelectedCaffeineMg = coffee.Caffeine,
			MessageText = reactionText,
			TimeString = DateTime.Now.ToString("hh:mm tt")
		};

		AddChatMessageWithSlidingWindow(reactionBubble);
		SyncActiveConsultationInPlace();

		if (Application.Current != null)
		{
			await StreamMessageTextAsync(reactionBubble, reactionText, 400, cancellationToken);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ReplySkippedAsync(CancellationToken cancellationToken = default)
	{
		var target = PendingVerdictMessage
			?? ChatMessages.LastOrDefault(m => m.IsOracleVerdictCard && !m.IsWelcomeMessage && !m.IsBuzzCheckCard && !m.IsOutcomeReactionCard && m.IsActionPending);
		if (target == null)
		{
			return;
		}

		PendingVerdictMessage = null;

		AddChatMessageWithSlidingWindow(new ChatMessageItem
		{
			IsUserMessage = true,
			IsOracleMessage = false,
			MessageText = "🫗 Skipped this cup—holding the line for now.",
			TimeString = DateTime.Now.ToString("hh:mm tt")
		});

		await ConfirmSkipOutcomeAsync(target, cancellationToken);

		MascotReactionState = "Skipped";
		string reactionText = _oracleEngineService.GetOutcomeReaction(target.OracleSaidYes, false, null, 0);

		var reactionBubble = new ChatMessageItem
		{
			IsOracleMessage = true,
			IsOutcomeReactionCard = true,
			OracleSaidYes = target.OracleSaidYes,
			IsActionPending = false,
			IsConfirmed = true,
			IsComplied = target.IsComplied,
			MascotAvatarReaction = "Skipped",
			OutcomeFeedbackText = target.OutcomeFeedbackText,
			ComplianceTagText = target.ComplianceTagText,
			MessageText = reactionText,
			TimeString = DateTime.Now.ToString("hh:mm tt")
		};

		AddChatMessageWithSlidingWindow(reactionBubble);
		SyncActiveConsultationInPlace();

		if (Application.Current != null)
		{
			await StreamMessageTextAsync(reactionBubble, reactionText, 400, cancellationToken);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task SwapPendingDrinkAsync(CancellationToken cancellationToken = default)
	{
		if (PendingVerdictMessage != null)
		{
			await OpenCoffeeDictionaryForCardAsync(PendingVerdictMessage, cancellationToken);
			OnPropertyChanged(nameof(PendingDrinkButtonText));
		}
		else
		{
			await OpenCoffeeDictionaryAsync(cancellationToken);
		}
	}

	[RelayCommand]
	private void StartDrinkSelection(ChatMessageItem item)
	{
		item.IsActionPending = false;
		item.IsSelectingCoffee = true;
		item.IsConfirmingSkip = false;
		item.IsConfirmingDrink = false;
		SyncActiveConsultationInPlace();
	}

	[RelayCommand]
	private void SelectCoffeeForDrink(ChatMessageItem item)
	{
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = true;
		SyncActiveConsultationInPlace();
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ConfirmDrankOutcomeAsync(ChatMessageItem item, CancellationToken cancellationToken = default)
	{
		if (ReferenceEquals(PendingVerdictMessage, item))
		{
			PendingVerdictMessage = null;
		}

		var coffee = _coffeeDictionaryService.GetById(item.SelectedCoffeeTypeId ?? string.Empty)
			?? SelectedCoffeePreference
			?? _coffeeDictionaryService.GetDefault();

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

		SyncActiveConsultationInPlace();

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

		// 1. Insert into SQLite persistence as Pending sync
		var entity = DecisionLogEntity.FromOracleLog(logEntry, Models.SyncStatus.Pending);
		await _sqliteDatabaseService.InsertLogAsync(entity, cancellationToken).ConfigureAwait(false);

		// 2. Delegate to MetabolismViewModel
		await _metabolismViewModel.AddLogAsync(logEntry).ConfigureAwait(false);

		// 3. Trigger asynchronous background flush of pending changes
		SafeFlushPendingChanges(cancellationToken);
	}

	[RelayCommand]
	private void PromptSkip(ChatMessageItem item)
	{
		item.IsActionPending = false;
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsConfirmingSkip = true;
		SyncActiveConsultationInPlace();
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task ConfirmSkipOutcomeAsync(ChatMessageItem item, CancellationToken cancellationToken = default)
	{
		if (ReferenceEquals(PendingVerdictMessage, item))
		{
			PendingVerdictMessage = null;
		}

		bool complied = !item.OracleSaidYes;

		item.IsConfirmingSkip = false;
		item.IsSelectingCoffee = false;
		item.IsConfirmingDrink = false;
		item.IsActionPending = false;
		item.IsConfirmed = true;
		item.IsComplied = complied;
		item.OutcomeFeedbackText = "Skipped coffee intake.";
		item.ComplianceTagText = complied ? "Complied with Oracle" : "Rebelled against Oracle";

		SyncActiveConsultationInPlace();

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

		// 1. Insert into SQLite persistence as Pending sync
		var entity = DecisionLogEntity.FromOracleLog(logEntry, Models.SyncStatus.Pending);
		await _sqliteDatabaseService.InsertLogAsync(entity, cancellationToken).ConfigureAwait(false);

		// 2. Delegate to MetabolismViewModel
		await _metabolismViewModel.AddLogAsync(logEntry).ConfigureAwait(false);

		// 3. Trigger asynchronous background flush of pending changes
		SafeFlushPendingChanges(cancellationToken);
	}

	[RelayCommand]
	public void ResetConversation()
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				PerformResetConversation();
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			PerformResetConversation();
			return;
		}
		catch
		{
		}

		_ = SafeDispatchToMainThreadAsync(PerformResetConversation);
	}

	public void PerformResetConversation()
	{
		IsDrawerOpen = false;
		PendingVerdictMessage = null;
		MascotReactionState = string.Empty;
		ArchiveOrUpdateCurrentConsultation();
		ResetChatMessagesToWelcomeGreeting();
	}

	private bool HasActiveConversationTurns => ChatMessages.Any(m => !m.IsWelcomeMessage && !m.IsTypingIndicator);

	private void ArchiveOrUpdateCurrentConsultation()
	{
		if (!HasActiveConversationTurns)
		{
			if (ActiveConsultation != null)
			{
				ActiveConsultation.IsSelected = false;
				ActiveConsultation = null;
			}
			return;
		}

		var snapshot = ChatMessages
			.Where(m => !m.IsTypingIndicator)
			.Select(m => m.Clone())
			.ToList();

		if (ActiveConsultation != null)
		{
			ActiveConsultation.Messages = snapshot;
			ActiveConsultation.UpdatedAtUtc = DateTime.UtcNow;
			ActiveConsultation.UpdateSummaryFromMessages();
			ActiveConsultation.IsSelected = false;
			ActiveConsultation = null;
		}
		else
		{
			var newSession = new ConsultationSession
			{
				CreatedAtUtc = DateTime.UtcNow,
				UpdatedAtUtc = DateTime.UtcNow,
				Messages = snapshot,
				IsSelected = false
			};
			newSession.UpdateSummaryFromMessages();
			ConsultationHistory.Insert(0, newSession);

			while (ConsultationHistory.Count > MaxSavedConsultations)
			{
				ConsultationHistory.RemoveAt(ConsultationHistory.Count - 1);
			}
		}

		SafeSaveConsultations();
	}

	private void SyncActiveConsultationInPlace()
	{
		if (ActiveConsultation == null) return;

		var snapshot = ChatMessages
			.Where(m => !m.IsTypingIndicator)
			.Select(m => m.Clone())
			.ToList();

		ActiveConsultation.Messages = snapshot;
		ActiveConsultation.UpdatedAtUtc = DateTime.UtcNow;
		ActiveConsultation.UpdateSummaryFromMessages();
		SafeSaveConsultations();
	}

	private void ResetChatMessagesToWelcomeGreeting()
	{
		PendingVerdictMessage = null;

		if (ChatMessages.Count > 0 && ChatMessages[0].IsWelcomeMessage)
		{
			while (ChatMessages.Count > 1)
			{
				ChatMessages.RemoveAt(ChatMessages.Count - 1);
			}
			ChatMessages[0].MessageText = WelcomeGreetingText;
			ChatMessages[0].TimeString = DateTime.Now.ToString("hh:mm tt");
		}
		else
		{
			while (ChatMessages.Count > 0)
			{
				ChatMessages.RemoveAt(ChatMessages.Count - 1);
			}

			var welcomeMsg = new ChatMessageItem
			{
				IsOracleMessage = true,
				IsWelcomeMessage = true,
				IsActionPending = false,
				OracleSaidYes = true,
				MascotAvatarReaction = "Happy",
				MessageText = WelcomeGreetingText,
				TimeString = DateTime.Now.ToString("hh:mm tt")
			};
			AddChatMessageWithSlidingWindow(welcomeMsg);
		}
	}

	[RelayCommand]
	public void SelectConsultation(ConsultationSession? session)
	{
		if (session == null) return;

		try
		{
			if (MainThread.IsMainThread)
			{
				PerformSelectConsultation(session);
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			PerformSelectConsultation(session);
			return;
		}
		catch
		{
		}

		_ = SafeDispatchToMainThreadAsync(() => PerformSelectConsultation(session));
	}

	public void PerformSelectConsultation(ConsultationSession? session)
	{
		if (session == null) return;

		IsDrawerOpen = false;
		IsChatTabActive = true;
		IsStatsTabActive = false;

		if (ReferenceEquals(ActiveConsultation, session))
		{
			session.IsSelected = true;
			return;
		}

		ArchiveOrUpdateCurrentConsultation();
		PendingVerdictMessage = null;

		foreach (var item in ConsultationHistory)
		{
			item.IsSelected = ReferenceEquals(item, session);
		}

		session.IsSelected = true;
		ActiveConsultation = session;

		while (ChatMessages.Count > 0)
		{
			ChatMessages.RemoveAt(ChatMessages.Count - 1);
		}

		if (session.Messages.Count == 0 || !session.Messages[0].IsWelcomeMessage)
		{
			AddChatMessageWithSlidingWindow(new ChatMessageItem
			{
				IsOracleMessage = true,
				IsWelcomeMessage = true,
				IsActionPending = false,
				OracleSaidYes = true,
				MascotAvatarReaction = "Happy",
				MessageText = WelcomeGreetingText,
				TimeString = DateTime.Now.ToString("hh:mm tt")
			});
		}

		foreach (var msg in session.Messages)
		{
			AddChatMessageWithSlidingWindow(msg.Clone());
		}

		var lastNonWelcome = ChatMessages.LastOrDefault(m => !m.IsWelcomeMessage && !m.IsTypingIndicator);
		PendingVerdictMessage = lastNonWelcome is
		{
			IsOracleVerdictCard: true,
			IsBuzzCheckCard: false,
			IsOutcomeReactionCard: false,
			IsActionPending: true
		}
			? lastNonWelcome
			: null;
	}

	[RelayCommand]
	public void DeleteConsultation(ConsultationSession? session)
	{
		if (session == null) return;

		try
		{
			if (MainThread.IsMainThread)
			{
				PerformDeleteConsultation(session);
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			PerformDeleteConsultation(session);
			return;
		}
		catch
		{
		}

		_ = SafeDispatchToMainThreadAsync(() => PerformDeleteConsultation(session));
	}

	public void PerformDeleteConsultation(ConsultationSession? session)
	{
		if (session == null) return;

		bool wasActive = ReferenceEquals(ActiveConsultation, session);
		session.IsSelected = false;
		ConsultationHistory.Remove(session);

		if (wasActive)
		{
			ActiveConsultation = null;
			ResetChatMessagesToWelcomeGreeting();
		}

		SafeSaveConsultations();
	}

	private void SafeSaveConsultations(CancellationToken cancellationToken = default)
	{
		var snapshot = ConsultationHistory.ToList();
		var task = _dataPersistenceService.SaveConsultationsAsync(snapshot, cancellationToken);
		if (!task.IsCompletedSuccessfully)
		{
			_ = task.ContinueWith(t =>
			{
				if (t.IsFaulted)
				{
					_ = t.Exception;
				}
			}, TaskContinuationOptions.ExecuteSynchronously);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task OpenCoffeeDictionaryAsync(CancellationToken cancellationToken = default)
	{
		var chosenCoffee = await _appPopupService.ShowCoffeeDictionaryAsync(SelectedCoffeePreference?.Id).ConfigureAwait(false);
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
		var chosenCoffee = await _appPopupService.ShowCoffeeDictionaryAsync(item.SelectedCoffeeTypeId ?? SelectedCoffeePreference?.Id).ConfigureAwait(false);
		if (chosenCoffee != null)
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				item.SelectedCoffeeTypeId = chosenCoffee.Id;
				item.SelectedCoffeeName = chosenCoffee.Name;
				item.SelectedCaffeineMg = chosenCoffee.Caffeine;
				SelectedCoffeePreference = chosenCoffee;
				if (ReferenceEquals(PendingVerdictMessage, item))
				{
					OnPropertyChanged(nameof(PendingDrinkButtonText));
				}
				SyncActiveConsultationInPlace();
			}).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	private async Task OpenThemeSelectorAsync(CancellationToken cancellationToken = default)
	{
		string? chosenTheme = await _appPopupService.ShowThemeSelectorAsync().ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(chosenTheme))
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				ThemeManager.ApplyTheme(chosenTheme);
				CurrentThemeName = ThemeManager.CurrentThemeName;
				_dataPersistenceService.SaveTheme(chosenTheme);
				WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
				RequestChartInvalidation?.Invoke();
			}).ConfigureAwait(false);
		}
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task OpenStorageSettingsAsync(CancellationToken cancellationToken = default)
	{
		bool dataChanged = await _appPopupService.ShowStorageSelectionPopupAsync().ConfigureAwait(false);
		if (dataChanged)
		{
			await ReloadLogsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
		}
		await SafeDispatchToMainThreadAsync(() => UpdateSyncStatusProperties(_storageSyncService.Status)).ConfigureAwait(false);
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public Task EditLogAsync(OracleDecisionLog logToEdit, CancellationToken cancellationToken = default)
	{
		return _metabolismViewModel.EditLogAsync(logToEdit, cancellationToken);
	}

	private void SafeFlushPendingChanges(CancellationToken cancellationToken = default)
	{
		var task = _storageSyncService.FlushPendingChangesAsync(cancellationToken);
		if (!task.IsCompletedSuccessfully)
		{
			_ = task.ContinueWith(t =>
			{
				if (t.IsFaulted)
				{
					_ = t.Exception;
				}
			}, TaskContinuationOptions.ExecuteSynchronously);
		}
	}

	public void Dispose()
	{
		_storageSyncService.StatusChanged -= OnSyncStatusChanged;
		_metabolismViewModel.NavigatingToMainPage -= SwitchToChatTab;
		_metabolismViewModel.Dispose();
	}
}
