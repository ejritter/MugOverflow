using CommunityToolkit.Mvvm.Messaging;
using MugOverflowProject.Messages;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.ViewModels;

public sealed class MetabolismViewModelTests
{
	private readonly FakeCaffeineMetabolismService _metabolismService = new();
	private readonly FakeDataPersistenceService _persistenceService = new();
	private readonly FakeAppPopupService _popupService = new();
	private readonly FakeSqliteDatabaseService _sqliteService = new();
	private readonly FakeStorageSyncService _syncService = new();

	private MetabolismViewModel CreateViewModel()
	{
		return new MetabolismViewModel(
			_metabolismService,
			_sqliteService,
			_syncService,
			_popupService,
			_persistenceService);
	}

	[Fact]
	public void Constructor_InitializesDefaultState()
	{
		using var vm = CreateViewModel();

		Assert.False(vm.HasHistoryLogs);
		Assert.Empty(vm.HistoryLogs);
		Assert.NotNull(vm.MetabolismDrawable);
		Assert.Equal("Espresso Crema Dark", vm.CurrentThemeName);
		Assert.Equal("Ready", vm.SyncStatusText);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
		Assert.Equal(0, vm.ActiveCaffeineMg);
		Assert.Equal(0, vm.TotalCaffeineTodayMg);
		Assert.Equal(0, vm.CupsLoggedCount);
		Assert.Equal(100, vm.CompliancePercentage);
	}

	[Fact]
	public async Task InitializeAsync_LoadsLogsFromSqliteAndRecalculates()
	{
		using var vm = CreateViewModel();
		_sqliteService.StoredEntities.Add(new DecisionLogEntity
		{
			Id = "202",
			Timestamp = DateTime.UtcNow,
			Decision = "YES",
			Reason = "Complied with Oracle",
			CaffeineAmountMg = 95,
			ItemName = "Drip Coffee",
			ItemCategory = "drip",
			IsComplianceOverride = false,
			SyncStatus = 1
		});

		await vm.InitializeAsync();

		Assert.True(vm.HasHistoryLogs);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal("Drip Coffee", vm.HistoryLogs[0].CoffeeName);
		Assert.Equal(95, vm.HistoryLogs[0].CaffeineMg);
		Assert.Equal(95, vm.TotalCaffeineTodayMg);
		Assert.True(vm.ActiveCaffeineMg > 0);
	}

	[Fact]
	public async Task AddLogAsync_AddsLogAtFrontAndRecalculates()
	{
		using var vm = CreateViewModel();

		var logEntry = new OracleDecisionLog
		{
			Id = 301,
			Timestamp = DateTime.UtcNow,
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "espresso",
			CoffeeName = "Espresso",
			CaffeineMg = 64
		};

		await vm.AddLogAsync(logEntry);

		Assert.True(vm.HasHistoryLogs);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal(64, vm.HistoryLogs[0].CaffeineMg);
		Assert.Equal(64, vm.TotalCaffeineTodayMg);
		Assert.True(vm.ActiveCaffeineMg > 0);
	}

	[Fact]
	public async Task EditLogAsync_WhenUpdated_UpdatesSqliteAndFlushes()
	{
		using var vm = CreateViewModel();

		var initialLog = new OracleDecisionLog
		{
			Id = 401,
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
			Id = 401,
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
	}

	[Fact]
	public async Task EditLogAsync_WhenDeleted_RemovesFromSqliteAndFlushes()
	{
		using var vm = CreateViewModel();

		var initialLog = new OracleDecisionLog
		{
			Id = 501,
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
		_popupService.EditedLogToReturn = null; // Delete signal

		await vm.EditLogCommand.ExecuteAsync(initialLog);

		Assert.Empty(vm.HistoryLogs);
		Assert.False(vm.HasHistoryLogs);
		Assert.Empty(_sqliteService.StoredEntities);
		Assert.Equal(1, _sqliteService.DeleteCallCount);
		Assert.Equal(1, _syncService.FlushPendingChangesCallCount);
	}

	[Fact]
	public async Task RecalculateAllMetrics_PublishesInvalidateMetabolismChartMessage()
	{
		using var vm = CreateViewModel();
		bool received = false;

		WeakReferenceMessenger.Default.Register<MetabolismViewModelTests, InvalidateMetabolismChartMessage>(this, (_, _) =>
		{
			received = true;
		});

		try
		{
			await vm.RecalculateAllMetricsAsync();
			Assert.True(received);
		}
		finally
		{
			WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(this);
		}
	}

	[Fact]
	public void Dispose_CancelsPeriodicTimerSafely()
	{
		var vm = CreateViewModel();
		vm.StartPeriodicRecalculation();
		vm.Dispose();
	}

	[Fact]
	public void LedgerPresentation_DefaultState_IsConfiguredProperly()
	{
		using var vm = CreateViewModel();

		Assert.Equal("All", vm.LedgerFilter);
		Assert.Empty(vm.LedgerSearchText);
		Assert.False(vm.HasLedgerSearchText);
		Assert.Equal(1, vm.LedgerCurrentPage);
		Assert.Equal(5, vm.LedgerPageSize);
		Assert.Equal(1, vm.LedgerTotalPages);
		Assert.Equal(0, vm.LedgerTotalFilteredCount);
		Assert.False(vm.CanGoToPreviousLedgerPage);
		Assert.False(vm.CanGoToNextLedgerPage);
		Assert.Empty(vm.PagedHistoryLogs);
		Assert.False(vm.HasFilteredLedgerLogs);
		Assert.False(vm.IsLedgerFilteredEmpty);
		Assert.Equal(0, vm.LedgerAllCount);
		Assert.Equal(0, vm.LedgerDrankCount);
		Assert.Equal(0, vm.LedgerSkippedCount);
		Assert.Equal(0, vm.LedgerCompliedCount);
		Assert.Equal(0, vm.LedgerRebelledCount);
		Assert.True(vm.IsFilterAllActive);
		Assert.False(vm.IsFilterDrankActive);
		Assert.False(vm.IsFilterSkippedActive);
		Assert.False(vm.IsFilterCompliedActive);
		Assert.False(vm.IsFilterRebelledActive);
		Assert.Equal("1 / 1", vm.LedgerCurrentPageDisplay);
	}

	[Fact]
	public void LedgerPresentation_WhenLogsAdded_PagesAndPaginatesCorrectly()
	{
		using var vm = CreateViewModel();

		// Add 12 logs
		for (int i = 1; i <= 12; i++)
		{
			vm.HistoryLogs.Add(new OracleDecisionLog
			{
				Id = i,
				CoffeeName = $"Coffee {i}",
				CoffeeTypeId = "drip",
				CaffeineMg = 95,
				OracleSaidYes = i % 2 == 0,
				UserDrank = i <= 8,
				FollowedAdvice = i % 3 == 0,
				Timestamp = DateTime.UtcNow.AddMinutes(-i * 10)
			});
		}

		// Total items: 12, PageSize: 5 => TotalPages: 3
		Assert.Equal(12, vm.LedgerAllCount);
		Assert.Equal(8, vm.LedgerDrankCount);
		Assert.Equal(4, vm.LedgerSkippedCount);
		Assert.Equal(3, vm.LedgerTotalPages);
		Assert.Equal(1, vm.LedgerCurrentPage);
		Assert.Equal(5, vm.PagedHistoryLogs.Count);
		Assert.True(vm.HasFilteredLedgerLogs);
		Assert.False(vm.CanGoToPreviousLedgerPage);
		Assert.True(vm.CanGoToNextLedgerPage);
		Assert.Equal("Showing 1–5 of 12", vm.LedgerPageSummaryText);
		Assert.Equal("1 / 3", vm.LedgerCurrentPageDisplay);
		Assert.Equal("Coffee 1", vm.PagedHistoryLogs[0].CoffeeName);

		// Navigate to Page 2
		vm.NextLedgerPage();
		Assert.Equal(2, vm.LedgerCurrentPage);
		Assert.Equal(5, vm.PagedHistoryLogs.Count);
		Assert.True(vm.CanGoToPreviousLedgerPage);
		Assert.True(vm.CanGoToNextLedgerPage);
		Assert.Equal("Showing 6–10 of 12", vm.LedgerPageSummaryText);
		Assert.Equal("Coffee 6", vm.PagedHistoryLogs[0].CoffeeName);

		// Navigate to Page 3
		vm.NextLedgerPage();
		Assert.Equal(3, vm.LedgerCurrentPage);
		Assert.Equal(2, vm.PagedHistoryLogs.Count);
		Assert.True(vm.CanGoToPreviousLedgerPage);
		Assert.False(vm.CanGoToNextLedgerPage);
		Assert.Equal("Showing 11–12 of 12", vm.LedgerPageSummaryText);
		Assert.Equal("Coffee 11", vm.PagedHistoryLogs[0].CoffeeName);

		// Navigate back to Page 2
		vm.PreviousLedgerPage();
		Assert.Equal(2, vm.LedgerCurrentPage);

		// First Page jump
		vm.FirstLedgerPage();
		Assert.Equal(1, vm.LedgerCurrentPage);
		Assert.Equal("Coffee 1", vm.PagedHistoryLogs[0].CoffeeName);

		// Last Page jump
		vm.LastLedgerPage();
		Assert.Equal(3, vm.LedgerCurrentPage);
		Assert.Equal("Coffee 11", vm.PagedHistoryLogs[0].CoffeeName);
	}

	[Fact]
	public void LedgerPresentation_FiltersProperly_AcrossDrankSkippedCompliedRebelled()
	{
		using var vm = CreateViewModel();

		// 3 Drank + Complied
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 1, CoffeeName = "Espresso", UserDrank = true, OracleSaidYes = true, FollowedAdvice = true });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 2, CoffeeName = "Latte", UserDrank = true, OracleSaidYes = true, FollowedAdvice = true });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 3, CoffeeName = "Cold Brew", UserDrank = true, OracleSaidYes = true, FollowedAdvice = true });

		// 2 Drank + Rebelled (Oracle said NO, but user drank)
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 4, CoffeeName = "Rebel Espresso", UserDrank = true, OracleSaidYes = false, FollowedAdvice = false });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 5, CoffeeName = "Rebel Cold Brew", UserDrank = true, OracleSaidYes = false, FollowedAdvice = false });

		// 2 Skipped + Complied (Oracle said NO, user skipped)
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 6, CoffeeName = "Skipped Coffee", UserDrank = false, OracleSaidYes = false, FollowedAdvice = true });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 7, CoffeeName = "Skipped Decaf", UserDrank = false, OracleSaidYes = false, FollowedAdvice = true });

		// Total: 7 items. Drank: 5, Skipped: 2, Complied: 5, Rebelled: 2.
		Assert.Equal(7, vm.LedgerAllCount);
		Assert.Equal(5, vm.LedgerDrankCount);
		Assert.Equal(2, vm.LedgerSkippedCount);
		Assert.Equal(5, vm.LedgerCompliedCount);
		Assert.Equal(2, vm.LedgerRebelledCount);

		// Filter: Drank
		vm.SetLedgerFilter("Drank");
		Assert.True(vm.IsFilterDrankActive);
		Assert.Equal(5, vm.PagedHistoryLogs.Count);
		Assert.All(vm.PagedHistoryLogs, log => Assert.True(log.UserDrank));

		// Filter: Skipped
		vm.SetLedgerFilter("Skipped");
		Assert.True(vm.IsFilterSkippedActive);
		Assert.Equal(2, vm.PagedHistoryLogs.Count);
		Assert.All(vm.PagedHistoryLogs, log => Assert.False(log.UserDrank));

		// Filter: Rebelled
		vm.SetLedgerFilter("Rebelled");
		Assert.True(vm.IsFilterRebelledActive);
		Assert.Equal(2, vm.PagedHistoryLogs.Count);
		Assert.All(vm.PagedHistoryLogs, log => Assert.False(log.FollowedAdvice));

		// Filter: Complied
		vm.SetLedgerFilter("Complied");
		Assert.True(vm.IsFilterCompliedActive);
		Assert.Equal(5, vm.PagedHistoryLogs.Count);
		Assert.All(vm.PagedHistoryLogs, log => Assert.True(log.FollowedAdvice));

		// Reset to All
		vm.SetLedgerFilter("All");
		Assert.True(vm.IsFilterAllActive);
		Assert.Equal(5, vm.PagedHistoryLogs.Count); // Page size is 5, total is 7
	}

	[Fact]
	public void LedgerPresentation_LiveSearch_FiltersAccurately()
	{
		using var vm = CreateViewModel();

		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 1, CoffeeName = "Nitro Cold Brew", UserDrank = true, CaffeineMg = 215 });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 2, CoffeeName = "Single Espresso", UserDrank = true, CaffeineMg = 64 });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 3, CoffeeName = "Decaf Brew", UserDrank = true, CaffeineMg = 4 });

		// Search for "cold brew" (case-insensitive)
		vm.LedgerSearchText = "cold brew";
		Assert.True(vm.HasLedgerSearchText);
		Assert.Single(vm.PagedHistoryLogs);
		Assert.Equal("Nitro Cold Brew", vm.PagedHistoryLogs[0].CoffeeName);

		// Clear search
		vm.ClearLedgerSearch();
		Assert.False(vm.HasLedgerSearchText);
		Assert.Equal(3, vm.PagedHistoryLogs.Count);

		// Search with zero matches sets IsLedgerFilteredEmpty = true
		vm.LedgerSearchText = "Nonexistent Beverage";
		Assert.Empty(vm.PagedHistoryLogs);
		Assert.True(vm.IsLedgerFilteredEmpty);

		// ClearFilterAndSearch restores
		vm.ClearFilterAndSearch();
		Assert.False(vm.IsLedgerFilteredEmpty);
		Assert.Equal(3, vm.PagedHistoryLogs.Count);
	}

	[Fact]
	public void LedgerPresentation_PageSizeChange_ReconfiguresPaginationAndCounts()
	{
		using var vm = CreateViewModel();

		for (int i = 1; i <= 25; i++)
		{
			vm.HistoryLogs.Add(new OracleDecisionLog
			{
				Id = i,
				CoffeeName = $"Brew {i}",
				CaffeineMg = 50 + i,
				UserDrank = true,
				Timestamp = DateTime.UtcNow.AddMinutes(-i)
			});
		}

		// Default page size is 5 -> 5 pages
		Assert.Equal(5, vm.LedgerPageSize);
		Assert.True(vm.IsPageSize5Active);
		Assert.False(vm.IsPageSize10Active);
		Assert.False(vm.IsPageSize20Active);
		Assert.Equal(5, vm.LedgerTotalPages);
		Assert.Equal(5, vm.PagedHistoryLogs.Count);

		// Switch to 10 page size
		vm.SetLedgerPageSize(10);
		Assert.Equal(10, vm.LedgerPageSize);
		Assert.False(vm.IsPageSize5Active);
		Assert.True(vm.IsPageSize10Active);
		Assert.False(vm.IsPageSize20Active);
		Assert.Equal(3, vm.LedgerTotalPages);
		Assert.Equal(10, vm.PagedHistoryLogs.Count);

		// Switch to 20 page size
		vm.SetLedgerPageSize(20);
		Assert.Equal(20, vm.LedgerPageSize);
		Assert.True(vm.IsPageSize20Active);
		Assert.Equal(2, vm.LedgerTotalPages);
		Assert.Equal(20, vm.PagedHistoryLogs.Count);
	}

	[Fact]
	public void LedgerPresentation_SortOrder_SortsByNewestOldestAndCaffeine()
	{
		using var vm = CreateViewModel();

		var baseTime = DateTime.UtcNow;
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 1, CoffeeName = "Morning Espresso", CaffeineMg = 64, Timestamp = baseTime.AddHours(-3) });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 2, CoffeeName = "Noon Cold Brew", CaffeineMg = 200, Timestamp = baseTime.AddHours(-1) });
		vm.HistoryLogs.Add(new OracleDecisionLog { Id = 3, CoffeeName = "Decaf Late Brew", CaffeineMg = 5, Timestamp = baseTime });

		// Default sort: Newest first (Decaf Late Brew -> Noon Cold Brew -> Morning Espresso)
		Assert.Equal("Newest", vm.LedgerSortOrder);
		Assert.True(vm.IsSortNewestActive);
		Assert.Equal("Decaf Late Brew", vm.PagedHistoryLogs[0].CoffeeName);
		Assert.Equal("Noon Cold Brew", vm.PagedHistoryLogs[1].CoffeeName);
		Assert.Equal("Morning Espresso", vm.PagedHistoryLogs[2].CoffeeName);

		// Sort by Oldest (Morning Espresso -> Noon Cold Brew -> Decaf Late Brew)
		vm.SetLedgerSortOrder("Oldest");
		Assert.True(vm.IsSortOldestActive);
		Assert.Equal("Morning Espresso", vm.PagedHistoryLogs[0].CoffeeName);
		Assert.Equal("Noon Cold Brew", vm.PagedHistoryLogs[1].CoffeeName);
		Assert.Equal("Decaf Late Brew", vm.PagedHistoryLogs[2].CoffeeName);

		// Sort by Caffeine (Noon Cold Brew [200] -> Morning Espresso [64] -> Decaf Late Brew [5])
		vm.SetLedgerSortOrder("Caffeine");
		Assert.True(vm.IsSortCaffeineActive);
		Assert.Equal("Noon Cold Brew", vm.PagedHistoryLogs[0].CoffeeName);
		Assert.Equal("Morning Espresso", vm.PagedHistoryLogs[1].CoffeeName);
		Assert.Equal("Decaf Late Brew", vm.PagedHistoryLogs[2].CoffeeName);
	}

	[Fact]
	public void LedgerPresentation_SemanticBadgeColors_MatchExpectedThemeValues()
	{
		var compliedDrank = new OracleDecisionLog { OracleSaidYes = true, UserDrank = true, FollowedAdvice = true };
		var compliedSkipped = new OracleDecisionLog { OracleSaidYes = false, UserDrank = false, FollowedAdvice = true };
		var rebelledDrank = new OracleDecisionLog { OracleSaidYes = false, UserDrank = true, FollowedAdvice = false };
		var rebelledSkipped = new OracleDecisionLog { OracleSaidYes = true, UserDrank = false, FollowedAdvice = false };

		// Complied -> Green emerald palette
		Assert.Equal("#E8F5E9", compliedDrank.StatusBadgeBgColor);
		Assert.Equal("#2E7D32", compliedDrank.StatusBadgeTextColor);
		Assert.Equal("#2E7D32", compliedDrank.StatusBadgeBorderColor);

		Assert.Equal("#E8F5E9", compliedSkipped.StatusBadgeBgColor);
		Assert.Equal("#2E7D32", compliedSkipped.StatusBadgeTextColor);

		// Rebelled + Drank -> Red danger palette
		Assert.Equal("#FFEBEE", rebelledDrank.StatusBadgeBgColor);
		Assert.Equal("#C62828", rebelledDrank.StatusBadgeTextColor);
		Assert.Equal("#C62828", rebelledDrank.StatusBadgeBorderColor);

		// Rebelled + Skipped -> Amber warning palette
		Assert.Equal("#FFF3E0", rebelledSkipped.StatusBadgeBgColor);
		Assert.Equal("#D97706", rebelledSkipped.StatusBadgeTextColor);
		Assert.Equal("#D97706", rebelledSkipped.StatusBadgeBorderColor);
	}

	[Fact]
	public void MainViewModel_LedgerPresentation_DelegatesProperlyToMetabolismViewModel()
	{
		using var metVm = CreateViewModel();
		using var mainVm = new MainViewModel(
			new FakeCoffeeDictionaryService(),
			new FakeOracleEngineService(),
			_metabolismService,
			_persistenceService,
			_popupService,
			_sqliteService,
			_syncService,
			metVm);

		metVm.HistoryLogs.Add(new OracleDecisionLog { Id = 1, CoffeeName = "Test Brew", CaffeineMg = 95 });

		Assert.Single(mainVm.PagedHistoryLogs);
		Assert.Equal(1, mainVm.LedgerAllCount);
		Assert.NotNull(mainVm.NextLedgerPageCommand);
		Assert.NotNull(mainVm.PreviousLedgerPageCommand);
		Assert.NotNull(mainVm.FirstLedgerPageCommand);
		Assert.NotNull(mainVm.LastLedgerPageCommand);
		Assert.NotNull(mainVm.SetLedgerFilterCommand);
		Assert.NotNull(mainVm.ClearLedgerSearchCommand);
		Assert.NotNull(mainVm.ClearFilterAndSearchCommand);
		Assert.NotNull(mainVm.SetLedgerPageSizeCommand);
		Assert.NotNull(mainVm.SetLedgerSortOrderCommand);

		mainVm.SetLedgerPageSize(10);
		Assert.Equal(10, metVm.LedgerPageSize);

		mainVm.SetLedgerSortOrder("Caffeine");
		Assert.Equal("Caffeine", metVm.LedgerSortOrder);
	}

	[Fact]
	public async Task NavigateToMainPageAsync_TriggersEventAndCompletes()
	{
		using var vm = CreateViewModel();
		bool eventFired = false;
		vm.NavigatingToMainPage += () => eventFired = true;

		await vm.NavigateToMainPageCommand.ExecuteAsync(null);

		Assert.True(eventFired);
	}

	[Fact]
	public void SetLedgerPageSizeCommand_CanExecuteAndExecutesWithIntegerParameters()
	{
		using var vm = CreateViewModel();

		Assert.True(vm.SetLedgerPageSizeCommand.CanExecute(5));
		vm.SetLedgerPageSizeCommand.Execute(5);
		Assert.Equal(5, vm.LedgerPageSize);

		Assert.True(vm.SetLedgerPageSizeCommand.CanExecute(10));
		vm.SetLedgerPageSizeCommand.Execute(10);
		Assert.Equal(10, vm.LedgerPageSize);

		Assert.True(vm.SetLedgerPageSizeCommand.CanExecute(20));
		vm.SetLedgerPageSizeCommand.Execute(20);
		Assert.Equal(20, vm.LedgerPageSize);
	}
}
