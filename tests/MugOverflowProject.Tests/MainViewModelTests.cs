using MugOverflowProject.Messages;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class MainViewModelTests
{
	private readonly FakeCoffeeDictionaryService _coffeeService = new();
	private readonly FakeOracleEngineService _oracleService = new();
	private readonly FakeCaffeineMetabolismService _metabolismService = new();
	private readonly FakeDataPersistenceService _persistenceService = new();
	private readonly FakeAppPopupService _popupService = new();
	private readonly FakeSqliteDatabaseService _sqliteService = new();
	private readonly FakeStorageSyncService _syncService = new();

	private MainViewModel CreateViewModel()
	{
		return new MainViewModel(
			_coffeeService,
			_oracleService,
			_metabolismService,
			_persistenceService,
			_popupService,
			_sqliteService,
			_syncService);
	}

	[Fact]
	public void Constructor_InitializesDefaultState()
	{
		var vm = CreateViewModel();

		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);
		Assert.False(vm.IsOracleThinking);
		Assert.NotNull(vm.SelectedCoffeePreference);
		Assert.Equal("drip", vm.SelectedCoffeePreference?.Id);
		Assert.NotEmpty(vm.AvailableCoffeeTypes);
		Assert.Equal("95 mg", vm.SelectedCoffeeCaffeineText);
		Assert.False(vm.HasHistoryLogs);
		Assert.Equal("Espresso Crema Dark", vm.CurrentThemeName);
		Assert.Equal("Ready", vm.SyncStatusText);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
	}

	[Fact]
	public void SwitchTabCommands_UpdateTabVisibility()
	{
		var vm = CreateViewModel();

		vm.SwitchToStatsTabCommand.Execute(null);
		Assert.False(vm.IsChatTabActive);
		Assert.True(vm.IsStatsTabActive);

		vm.SwitchToChatTabCommand.Execute(null);
		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);
	}

	[Fact]
	public void SelectCoffeePreferenceCommand_UpdatesSelectedCoffee()
	{
		var vm = CreateViewModel();
		var espresso = vm.AvailableCoffeeTypes.First(c => c.Id == "espresso");

		vm.SelectCoffeePreferenceCommand.Execute(espresso);

		Assert.Equal("espresso", vm.SelectedCoffeePreference?.Id);
		Assert.Equal("64 mg", vm.SelectedCoffeeCaffeineText);
	}

	[Fact]
	public async Task AskOracleAsync_ExecutesAndAddsMessages()
	{
		var vm = CreateViewModel();
		_oracleService.VerdictToReturn = (true, "Yes, absolutely brew that cup!");

		Assert.Empty(vm.ChatMessages);

		await vm.AskOracleCommand.ExecuteAsync(null);

		// Should have user query bubble + oracle verdict card
		Assert.Equal(2, vm.ChatMessages.Count);
		var userMsg = vm.ChatMessages[0];
		Assert.True(userMsg.IsUserMessage);
		Assert.False(userMsg.IsOracleMessage);
		Assert.Contains("Should I drink a cup of Drip Coffee", userMsg.MessageText);

		var oracleMsg = vm.ChatMessages[1];
		Assert.True(oracleMsg.IsOracleMessage);
		Assert.True(oracleMsg.OracleSaidYes);
		Assert.Equal("Yes, absolutely brew that cup!", oracleMsg.MessageText);
		Assert.True(oracleMsg.IsActionPending);
		Assert.False(vm.IsOracleThinking);
		Assert.True(vm.AskOracleCommand.CanExecute(null));
	}

	[Fact]
	public async Task ResetConversation_WhenConversationHasHistory_KeepsSingleGreetingAtTopAndRemovesTrailingMessages()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Initially 1 welcome message
		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);

		// Add messages from consulting Oracle
		await vm.AskOracleCommand.ExecuteAsync(null);
		Assert.Equal(3, vm.ChatMessages.Count);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
		Assert.True(vm.ChatMessages[1].IsUserMessage);
		Assert.True(vm.ChatMessages[2].IsOracleVerdictCard);

		// Reset conversation
		vm.ResetConversationCommand.Execute(null);

		// Must contain strictly 1 message, which is the greeting at index 0
		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
		Assert.Contains("Mugsy's behind the espresso bar", vm.ChatMessages[0].MessageText);
	}

	[Fact]
	public async Task ResetConversation_CalledRepeatedly_DoesNotDuplicateGreeting()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.Single(vm.ChatMessages);

		// Call Reset multiple times
		vm.ResetConversationCommand.Execute(null);
		vm.ResetConversationCommand.Execute(null);
		vm.ResetConversationCommand.Execute(null);

		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
	}

	[Fact]
	public void ResetConversation_WhenEmpty_AddsSingleGreetingAtTop()
	{
		var vm = CreateViewModel();
		Assert.Empty(vm.ChatMessages);

		vm.ResetConversationCommand.Execute(null);

		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
		Assert.Contains("Mugsy's behind the espresso bar", vm.ChatMessages[0].MessageText);
	}

	[Fact]
	public async Task ConfirmDrankOutcomeAsync_InsertsPendingEntityInSqliteAndFlushes()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		var oracleItem = new ChatMessageItem
		{
			IsOracleMessage = true,
			OracleSaidYes = true,
			MessageText = "Drink up!",
			SelectedCoffeeTypeId = "espresso",
			SelectedCoffeeName = "Espresso",
			SelectedCaffeineMg = 64
		};
		vm.ChatMessages.Add(oracleItem);

		await vm.ConfirmDrankOutcomeCommand.ExecuteAsync(oracleItem);

		Assert.True(oracleItem.IsConfirmed);
		Assert.True(oracleItem.IsComplied);
		Assert.Single(vm.HistoryLogs);
		Assert.True(vm.HasHistoryLogs);
		Assert.Equal("Espresso", vm.HistoryLogs[0].CoffeeName);
		Assert.Equal(64, vm.HistoryLogs[0].CaffeineMg);

		Assert.Single(_sqliteService.StoredEntities);
		Assert.Equal(0, _sqliteService.StoredEntities[0].SyncStatus);
		Assert.Equal("Espresso", _sqliteService.StoredEntities[0].ItemName);
		Assert.Equal(64, _sqliteService.StoredEntities[0].CaffeineAmountMg);
		Assert.Equal(1, _syncService.FlushPendingChangesCallCount);

		vm.Dispose();
	}

	[Fact]
	public async Task ConfirmSkipOutcomeAsync_InsertsPendingSkipEntityInSqliteAndFlushes()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		var oracleItem = new ChatMessageItem
		{
			IsOracleMessage = true,
			OracleSaidYes = false,
			MessageText = "Skip it for now."
		};
		vm.ChatMessages.Add(oracleItem);

		await vm.ConfirmSkipOutcomeCommand.ExecuteAsync(oracleItem);

		Assert.True(oracleItem.IsConfirmed);
		Assert.True(oracleItem.IsComplied); // Oracle said no, user skipped => complied
		Assert.Single(vm.HistoryLogs);
		Assert.Equal(0, vm.HistoryLogs[0].CaffeineMg);
		Assert.False(vm.HistoryLogs[0].UserDrank);

		Assert.Single(_sqliteService.StoredEntities);
		Assert.Equal(0, _sqliteService.StoredEntities[0].SyncStatus);
		Assert.Equal(0, _sqliteService.StoredEntities[0].CaffeineAmountMg);
		Assert.Equal(1, _syncService.FlushPendingChangesCallCount);

		vm.Dispose();
	}

	[Fact]
	public async Task EditLogAsync_WhenUpdated_UpdatesSqliteAsPendingAndFlushes()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		var initialLog = new OracleDecisionLog
		{
			Id = 555,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "drip",
			CoffeeName = "Drip Coffee",
			CaffeineMg = 95
		};
		vm.HistoryLogs.Add(initialLog);
		_sqliteService.StoredEntities.Add(DecisionLogEntity.FromOracleLog(initialLog, Models.SyncStatus.Synced));

		var updatedLog = new OracleDecisionLog
		{
			Id = 555,
			Timestamp = initialLog.Timestamp,
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "espresso",
			CoffeeName = "Espresso",
			CaffeineMg = 64
		};
		_popupService.ReturnExplicitEditedLog = true;
		_popupService.EditedLogToReturn = updatedLog;

		await vm.EditLogCommand.ExecuteAsync(initialLog);

		Assert.Equal("Espresso", vm.HistoryLogs[0].CoffeeName);
		Assert.Equal(0, _sqliteService.StoredEntities[0].SyncStatus);
		Assert.Equal("Espresso", _sqliteService.StoredEntities[0].ItemName);
		Assert.Equal(1, _syncService.FlushPendingChangesCallCount);

		vm.Dispose();
	}

	[Fact]
	public async Task EditLogAsync_WhenDeleted_RemovesFromSqliteAndFlushes()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		var initialLog = new OracleDecisionLog
		{
			Id = 777,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "drip",
			CoffeeName = "Drip Coffee",
			CaffeineMg = 95
		};
		vm.HistoryLogs.Add(initialLog);
		_sqliteService.StoredEntities.Add(DecisionLogEntity.FromOracleLog(initialLog, Models.SyncStatus.Synced));

		_popupService.ReturnExplicitEditedLog = true;
		_popupService.EditedLogToReturn = null; // Signal delete

		await vm.EditLogCommand.ExecuteAsync(initialLog);

		Assert.Empty(vm.HistoryLogs);
		Assert.Empty(_sqliteService.StoredEntities);
		Assert.Equal(1, _sqliteService.DeleteCallCount);
		Assert.Equal(1, _syncService.FlushPendingChangesCallCount);

		vm.Dispose();
	}

	[Fact]
	public async Task OpenCoffeeDictionaryCommand_WhenCoffeeSelected_UpdatesSelectedCoffeePreference()
	{
		var vm = CreateViewModel();
		var newCoffee = new CoffeeType
		{
			Id = "espresso",
			Name = "Espresso",
			Caffeine = 64,
			Volume = "1 oz",
			Desc = "Standard single espresso shot"
		};
		_popupService.SelectedCoffeeToReturn = newCoffee;

		await vm.OpenCoffeeDictionaryCommand.ExecuteAsync(null);

		Assert.Equal("espresso", vm.SelectedCoffeePreference?.Id);
		Assert.Equal("Espresso", vm.SelectedCoffeePreference?.Name);
		Assert.Equal("64 mg", vm.SelectedCoffeeCaffeineText);
	}

	[Fact]
	public async Task OpenCoffeeDictionaryForCardCommand_WhenCoffeeSelected_UpdatesItemAndPreference()
	{
		var vm = CreateViewModel();
		var cardItem = new ChatMessageItem
		{
			IsOracleMessage = true,
			SelectedCoffeeTypeId = "drip",
			SelectedCoffeeName = "Drip Coffee",
			SelectedCaffeineMg = 95
		};

		var newCoffee = new CoffeeType
		{
			Id = "cold_brew",
			Name = "Cold Brew Coffee",
			Caffeine = 155,
			Volume = "12 oz",
			Desc = "Cold brew"
		};
		_popupService.SelectedCoffeeToReturn = newCoffee;

		await vm.OpenCoffeeDictionaryForCardCommand.ExecuteAsync(cardItem);

		Assert.Equal("cold_brew", cardItem.SelectedCoffeeTypeId);
		Assert.Equal("Cold Brew Coffee", cardItem.SelectedCoffeeName);
		Assert.Equal(155, cardItem.SelectedCaffeineMg);
		Assert.Equal("cold_brew", vm.SelectedCoffeePreference?.Id);
	}

	[Fact]
	public async Task OpenCoffeeDictionaryCommand_WhenDecafSelected_CorrectlySetsDecafAndLowCaffeine()
	{
		var vm = CreateViewModel();
		var decafCoffee = new CoffeeType
		{
			Id = "decaf",
			Name = "Decaf Brewed Coffee",
			Caffeine = 4,
			Volume = "8 oz (237 ml)",
			Desc = "97%+ caffeine removed"
		};
		_popupService.SelectedCoffeeToReturn = decafCoffee;

		await vm.OpenCoffeeDictionaryCommand.ExecuteAsync(null);

		Assert.Equal("decaf", vm.SelectedCoffeePreference?.Id);
		Assert.Equal("Decaf Brewed Coffee", vm.SelectedCoffeePreference?.Name);
		Assert.Equal("4 mg", vm.SelectedCoffeeCaffeineText);
		Assert.Equal(4, vm.SelectedCoffeePreference?.Caffeine);
	}

	[Fact]
	public async Task OpenStorageSettingsCommand_OpensPopupAndReloadsLogs()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.OpenStorageSettingsCommand.ExecuteAsync(null);

		Assert.Equal(1, _popupService.ShowStorageSelectionPopupCallCount);

		vm.Dispose();
	}

	[Fact]
	public async Task SyncStatusChanged_UpdatesBoundProperties()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.True(vm.IsSyncing);
		Assert.False(vm.IsOffline);
		Assert.Equal("Syncing...", vm.SyncStatusText);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Offline);
		Assert.False(vm.IsSyncing);
		Assert.True(vm.IsOffline);
		Assert.Equal("Offline", vm.SyncStatusText);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Error);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
		Assert.Equal("Sync Error", vm.SyncStatusText);

		_syncService.TriggerStatusChanged(SyncEngineStatus.Success);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
		Assert.Equal("Synced", vm.SyncStatusText);

		vm.Dispose();
	}

	[Fact]
	public async Task InitializeAsync_SetsUpPeriodicTimerAndLoadsLogsFromSqlite()
	{
		var vm = CreateViewModel();
		_sqliteService.StoredEntities.Add(new DecisionLogEntity
		{
			Id = "101",
			Timestamp = DateTime.UtcNow,
			Decision = "YES",
			Reason = "Complied with Oracle",
			CaffeineAmountMg = 4,
			ItemName = "Decaf Brewed Coffee",
			ItemCategory = "decaf",
			IsComplianceOverride = false,
			SyncStatus = 1
		});

		await vm.InitializeAsync();

		Assert.Equal(1, _syncService.InitializeCallCount);
		Assert.True(vm.HasHistoryLogs);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal("Decaf Brewed Coffee", vm.HistoryLogs[0].CoffeeName);
		Assert.Equal(4, vm.HistoryLogs[0].CaffeineMg);

		vm.Dispose();
	}

	[Fact]
	public void Dispose_CancelsPeriodicTimerGracefully()
	{
		var vm = CreateViewModel();
		vm.StartPeriodicRecalculation();

		vm.Dispose();
	}

	[Fact]
	public void SwitchToStatsTab_SetsIsStatsTabActiveTrue_AndInvokesChartInvalidation()
	{
		var vm = CreateViewModel();
		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);

		int invalidations = 0;
		vm.RequestChartInvalidation = () => invalidations++;

		vm.SwitchToStatsTabCommand.Execute(null);

		Assert.False(vm.IsChatTabActive);
		Assert.True(vm.IsStatsTabActive);
		Assert.Equal(1, invalidations);

		vm.SwitchToChatTabCommand.Execute(null);
		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);
	}

	[Fact]
	public void RecalculateAllMetrics_PublishesInvalidateMetabolismChartMessageViaMessenger()
	{
		var vm = CreateViewModel();
		bool messageReceived = false;

		WeakReferenceMessenger.Default.Register<MainViewModelTests, InvalidateMetabolismChartMessage>(this, (_, _) =>
		{
			messageReceived = true;
		});

		try
		{
			// Switch to stats tab invokes RecalculateAllMetrics and chart invalidation
			vm.SwitchToStatsTabCommand.Execute(null);

			Assert.True(messageReceived);
		}
		finally
		{
			WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(this);
		}
	}

	[Fact]
	public async Task AddLoggedCupAsync_DoesNotPersistFullHistoryViaJsonDataPersistence()
	{
		var vm = CreateViewModel();

		var item = new ChatMessageItem
		{
			Id = 12345L,
			IsActionPending = true,
			OracleSaidYes = true,
			SelectedCoffeeTypeId = "drip",
			SelectedCoffeeName = "Drip Coffee",
			SelectedCaffeineMg = 95
		};

		await vm.ConfirmDrankOutcomeCommand.ExecuteAsync(item);

		// Local SQLite is the single source of truth; redundant full-dataset JSON save is omitted
		Assert.Empty(_persistenceService.StoredLogs);
	}

	[Fact]
	public async Task MetabolismViewModel_NavigatingToMainPage_SwitchesMainViewModelToChatTab()
	{
		var vm = CreateViewModel();
		vm.SwitchToStatsTabCommand.Execute(null);

		Assert.False(vm.IsChatTabActive);
		Assert.True(vm.IsStatsTabActive);

		await vm.MetabolismViewModel.NavigateToMainPageAsync();

		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);
	}

	[Fact]
	public void MetabolismViewModel_NotifyNavigatingToMainPage_SwitchesMainViewModelToChatTab()
	{
		var vm = CreateViewModel();
		vm.SwitchToStatsTabCommand.Execute(null);

		Assert.False(vm.IsChatTabActive);
		Assert.True(vm.IsStatsTabActive);

		vm.MetabolismViewModel.NotifyNavigatingToMainPage();

		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);
	}

	[Fact]
	public void AskOracleCommand_CanExecute_ReflectsIsOracleThinking()
	{
		var vm = CreateViewModel();

		Assert.False(vm.IsOracleThinking);
		Assert.True(vm.CanAskOracle);
		Assert.True(vm.AskOracleCommand.CanExecute(null));

		vm.IsOracleThinking = true;

		Assert.False(vm.CanAskOracle);
		Assert.False(vm.AskOracleCommand.CanExecute(null));

		vm.IsOracleThinking = false;

		Assert.True(vm.CanAskOracle);
		Assert.True(vm.AskOracleCommand.CanExecute(null));
	}

	[Fact]
	public async Task SwitchToStatsTabAsync_ActivatesStatsTab()
	{
		var vm = CreateViewModel();
		Assert.True(vm.IsChatTabActive);
		Assert.False(vm.IsStatsTabActive);

		await vm.SwitchToStatsTabCommand.ExecuteAsync(null);

		Assert.False(vm.IsChatTabActive);
		Assert.True(vm.IsStatsTabActive);
	}

	[Fact]
	public async Task SwitchToStatsTabAsync_NavigatesShellCurrentCleanly()
	{
		var shell = new Microsoft.Maui.Controls.Shell();
		var tabBar = new Microsoft.Maui.Controls.TabBar();
		var mainContent = new Microsoft.Maui.Controls.ShellContent
		{
			Route = "MainPage",
			ContentTemplate = new Microsoft.Maui.Controls.DataTemplate(() => new Microsoft.Maui.Controls.ContentPage { Title = "MainPage" })
		};
		var statsContent = new Microsoft.Maui.Controls.ShellContent
		{
			Route = "MetabolismPage",
			ContentTemplate = new Microsoft.Maui.Controls.DataTemplate(() => new Microsoft.Maui.Controls.ContentPage { Title = "MetabolismPage" })
		};
		tabBar.Items.Add(mainContent);
		tabBar.Items.Add(statsContent);
		shell.Items.Add(tabBar);

		var prevShell = Microsoft.Maui.Controls.Shell.Current;
		try
		{
			var vm = CreateViewModel();
			// Navigate directly to shell tab
			await shell.GoToAsync("//MainPage");
			Assert.Equal("//MainPage", shell.CurrentState.Location.ToString());

			// Now invoke command
			await vm.SwitchToStatsTabCommand.ExecuteAsync(null);
			Assert.True(vm.IsStatsTabActive);
			Assert.False(vm.IsChatTabActive);
		}
		finally
		{
			// Safe cleanup
		}
	}

	[Fact]
	public async Task ResetConversation_WhenConversationHasMessages_PreservesSessionInConsultationHistory()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.Empty(vm.ConsultationHistory);
		Assert.False(vm.HasConsultationHistory);

		// Consult Oracle about Drip Coffee
		_oracleService.VerdictToReturn = (true, "Brew that Drip Coffee!");
		await vm.AskOracleCommand.ExecuteAsync(null);
		Assert.Equal(3, vm.ChatMessages.Count);

		// Click "+ New Consultation"
		vm.ResetConversationCommand.Execute(null);

		// Current chat resets to single welcome greeting, while previous session is preserved in ConsultationHistory
		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
		Assert.Single(vm.ConsultationHistory);
		Assert.True(vm.HasConsultationHistory);

		var savedSession = vm.ConsultationHistory[0];
		Assert.Equal("Drip Coffee", savedSession.Title);
		Assert.Contains("1 query", savedSession.Subtitle);
		Assert.Equal("✨ YES", savedSession.LastVerdictBadge);
		Assert.Equal(3, savedSession.Messages.Count);
		Assert.False(savedSession.IsSelected);
		Assert.Null(vm.ActiveConsultation);
	}

	[Fact]
	public async Task SelectConsultation_RestoresMessagesAndAllowsContinuingSessionLater()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		// 1. First consultation (Drip Coffee)
		_oracleService.VerdictToReturn = (true, "Yes to Drip Coffee!");
		await vm.AskOracleCommand.ExecuteAsync(null);
		vm.ResetConversationCommand.Execute(null);

		Assert.Single(vm.ConsultationHistory);
		var firstSession = vm.ConsultationHistory[0];

		// 2. Select the saved consultation from the left side panel to review it
		vm.SelectConsultationCommand.Execute(firstSession);

		Assert.Same(firstSession, vm.ActiveConsultation);
		Assert.True(firstSession.IsSelected);
		Assert.True(vm.IsViewingSavedConsultation);
		Assert.Equal(3, vm.ChatMessages.Count);
		Assert.Equal("Yes to Drip Coffee!", vm.ChatMessages[2].MessageText);

		// 3. Continue the consultation by asking about Espresso
		var espresso = vm.AvailableCoffeeTypes.First(c => c.Id == "espresso");
		vm.SelectCoffeePreferenceCommand.Execute(espresso);
		_oracleService.VerdictToReturn = (false, "Hold off on the Espresso.");
		await vm.AskOracleCommand.ExecuteAsync(null);

		Assert.Equal(5, vm.ChatMessages.Count);
		Assert.Equal(5, firstSession.Messages.Count);
		Assert.Contains("2 queries", firstSession.Subtitle);
		Assert.Equal("🛑 NO", firstSession.LastVerdictBadge);

		// 4. Click "+ New Consultation" again - updates existing session without duplicating it
		vm.ResetConversationCommand.Execute(null);

		Assert.Single(vm.ConsultationHistory);
		Assert.False(firstSession.IsSelected);
		Assert.Null(vm.ActiveConsultation);
		Assert.Single(vm.ChatMessages);
	}

	[Fact]
	public async Task SelectConsultation_WhenUnsavedNewConsultationHasMessages_AutoPreservesBeforeSwitching()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		// 1. Create and archive Session 1 (Drip Coffee)
		await vm.AskOracleCommand.ExecuteAsync(null);
		vm.ResetConversationCommand.Execute(null);
		Assert.Single(vm.ConsultationHistory);
		var session1 = vm.ConsultationHistory[0];

		// 2. Start a second consultation (Espresso) WITHOUT clicking "+ New Consultation"
		var espresso = vm.AvailableCoffeeTypes.First(c => c.Id == "espresso");
		vm.SelectCoffeePreferenceCommand.Execute(espresso);
		await vm.AskOracleCommand.ExecuteAsync(null);

		// 3. Directly click Session 1 in the sidebar -> Session 2 (Espresso) is auto-preserved!
		vm.SelectConsultationCommand.Execute(session1);

		Assert.Equal(2, vm.ConsultationHistory.Count);
		Assert.Equal("Espresso", vm.ConsultationHistory[0].Title);
		Assert.Same(session1, vm.ActiveConsultation);
		Assert.True(session1.IsSelected);
	}

	[Fact]
	public async Task DeleteConsultation_RemovesFromHistoryAndResetsStageIfActive()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.AskOracleCommand.ExecuteAsync(null);
		vm.ResetConversationCommand.Execute(null);
		var savedSession = vm.ConsultationHistory[0];

		// Select it so it's active on screen
		vm.SelectConsultationCommand.Execute(savedSession);
		Assert.Equal(3, vm.ChatMessages.Count);

		// Delete it
		vm.DeleteConsultationCommand.Execute(savedSession);

		Assert.Empty(vm.ConsultationHistory);
		Assert.False(vm.HasConsultationHistory);
		Assert.Null(vm.ActiveConsultation);
		Assert.Single(vm.ChatMessages);
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
	}
}

internal sealed class FakeCoffeeDictionaryService : ICoffeeDictionaryService
{
	private readonly List<CoffeeType> _types =
	[
		new() { Id = "drip", Name = "Drip Coffee", Caffeine = 95, Volume = "8 oz", Desc = "Standard drip brew" },
		new() { Id = "espresso", Name = "Espresso", Caffeine = 64, Volume = "1 oz", Desc = "Standard single espresso shot" },
		new() { Id = "decaf", Name = "Decaf Brewed Coffee", Caffeine = 4, Volume = "8 oz", Desc = "97%+ caffeine removed" }
	];

	public IReadOnlyList<CoffeeType> GetAll() => _types;
	public CoffeeType GetDefault() => _types[0];
	public CoffeeType? GetById(string id) => _types.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FakeOracleEngineService : IOracleEngineService
{
	public (bool SaidYes, string VerdictText) VerdictToReturn { get; set; } = (true, "Yes, drink coffee!");

	public (bool SaidYes, string VerdictText) ConsultOracle() => VerdictToReturn;
}

internal sealed class FakeCaffeineMetabolismService : ICaffeineMetabolismService
{
	private readonly CaffeineMetabolismService _inner = new();

	public double CalculateActiveCaffeine(IEnumerable<OracleDecisionLog> logs, DateTime asOfUtc)
		=> _inner.CalculateActiveCaffeine(logs, asOfUtc);

	public double CalculateHoursToThreshold(double currentActiveMg, double targetMg)
		=> _inner.CalculateHoursToThreshold(currentActiveMg, targetMg);

	public IReadOnlyList<(double HourOffset, double RemainingMg)> Generate24HourDecayCurve(double initialActiveMg)
		=> _inner.Generate24HourDecayCurve(initialActiveMg);

	public MetabolismSummary ComputeSummary(IReadOnlyList<OracleDecisionLog> logs, ComplianceStats compliance)
		=> _inner.ComputeSummary(logs, compliance);

	public int CalculateDailyTotalCaffeineMg(IEnumerable<OracleDecisionLog> logs)
		=> _inner.CalculateDailyTotalCaffeineMg(logs);
}

internal sealed class FakeDataPersistenceService : IDataPersistenceService
{
	public List<OracleDecisionLog> StoredLogs { get; } = [];
	public List<ConsultationSession> StoredConsultations { get; } = [];
	public string StoredTheme { get; set; } = "Espresso Crema Dark";

	public Task<List<OracleDecisionLog>> LoadLogsAsync(CancellationToken cancellationToken = default) => Task.FromResult(StoredLogs.ToList());
	public Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs, CancellationToken cancellationToken = default)
	{
		StoredLogs.Clear();
		StoredLogs.AddRange(logs);
		return Task.CompletedTask;
	}
	public Task<List<ConsultationSession>> LoadConsultationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(StoredConsultations.ToList());
	public Task SaveConsultationsAsync(IEnumerable<ConsultationSession> consultations, CancellationToken cancellationToken = default)
	{
		StoredConsultations.Clear();
		StoredConsultations.AddRange(consultations);
		return Task.CompletedTask;
	}
	public string GetSavedTheme() => StoredTheme;
	public void SaveTheme(string themeName) => StoredTheme = themeName;
}

internal sealed class FakeAppPopupService : IAppPopupService
{
	public CoffeeType? SelectedCoffeeToReturn { get; set; }
	public OracleDecisionLog? EditedLogToReturn { get; set; }
	public bool ReturnExplicitEditedLog { get; set; }
	public int ShowStorageSelectionPopupCallCount { get; private set; }

	public Task<CoffeeType?> ShowCoffeeDictionaryAsync(string? targetCoffeeId = null)
		=> Task.FromResult(SelectedCoffeeToReturn);

	public Task<OracleDecisionLog?> ShowEditLogAsync(OracleDecisionLog logToEdit)
	{
		if (ReturnExplicitEditedLog)
		{
			return Task.FromResult(EditedLogToReturn);
		}
		return Task.FromResult<OracleDecisionLog?>(EditedLogToReturn ?? logToEdit);
	}

	public Task<string?> ShowThemeSelectorAsync()
		=> Task.FromResult<string?>("Espresso Crema Dark");

	public bool StorageSelectionResult { get; set; }

	public Task<bool> ShowStorageSelectionPopupAsync()
	{
		ShowStorageSelectionPopupCallCount++;
		return Task.FromResult(StorageSelectionResult);
	}
}
