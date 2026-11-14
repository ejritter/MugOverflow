using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

public sealed partial class MetabolismViewModel : ObservableObject, IDisposable
{
	private readonly ICaffeineMetabolismService caffeineMetabolismService;
	private readonly ISqliteDatabaseService sqliteDatabaseService;
	private readonly IStorageSyncService storageSyncService;
	private readonly IAppPopupService appPopupService;
	private readonly IDataPersistenceService dataPersistenceService;

	private CancellationTokenSource? _periodicTimerCts;
	private bool _isInitialized;

	public MetabolismViewModel(
		ICaffeineMetabolismService caffeineMetabolismService,
		ISqliteDatabaseService sqliteDatabaseService,
		IStorageSyncService storageSyncService,
		IAppPopupService appPopupService,
		IDataPersistenceService dataPersistenceService)
	{
		this.caffeineMetabolismService = caffeineMetabolismService;
		this.sqliteDatabaseService = sqliteDatabaseService;
		this.storageSyncService = storageSyncService;
		this.appPopupService = appPopupService;
		this.dataPersistenceService = dataPersistenceService;

		HistoryLogs.CollectionChanged += OnHistoryLogsCollectionChanged;
		RefreshPagedLogs();
	}

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

	public ObservableCollection<OracleDecisionLog> HistoryLogs { get; } = [];
	public ObservableCollection<OracleDecisionLog> PagedHistoryLogs { get; } = [];
	public CaffeineMetabolismDrawable MetabolismDrawable { get; } = new();
	public Action? RequestChartInvalidation { get; set; }

	// =========================================================================
	// Modern React/TypeScript Style High-Density Ledger Presentation State
	// =========================================================================
	[ObservableProperty]
	public partial string LedgerFilter { get; set; } = "All";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasLedgerSearchText))]
	public partial string LedgerSearchText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int LedgerCurrentPage { get; set; } = 1;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsPageSize5Active))]
	[NotifyPropertyChangedFor(nameof(IsPageSize10Active))]
	[NotifyPropertyChangedFor(nameof(IsPageSize20Active))]
	public partial int LedgerPageSize { get; set; } = 5;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSortNewestActive))]
	[NotifyPropertyChangedFor(nameof(IsSortOldestActive))]
	[NotifyPropertyChangedFor(nameof(IsSortCaffeineActive))]
	public partial string LedgerSortOrder { get; set; } = "Newest";

	[ObservableProperty]
	public partial int LedgerTotalPages { get; set; } = 1;

	[ObservableProperty]
	public partial int LedgerTotalFilteredCount { get; set; }

	[ObservableProperty]
	public partial bool CanGoToPreviousLedgerPage { get; set; }

	[ObservableProperty]
	public partial bool CanGoToNextLedgerPage { get; set; }

	[ObservableProperty]
	public partial string LedgerPageSummaryText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LedgerCurrentPageDisplay { get; set; } = "1 / 1";

	[ObservableProperty]
	public partial bool HasFilteredLedgerLogs { get; set; }

	[ObservableProperty]
	public partial bool IsLedgerFilteredEmpty { get; set; }

	[ObservableProperty]
	public partial int LedgerAllCount { get; set; }

	[ObservableProperty]
	public partial int LedgerDrankCount { get; set; }

	[ObservableProperty]
	public partial int LedgerSkippedCount { get; set; }

	[ObservableProperty]
	public partial int LedgerCompliedCount { get; set; }

	[ObservableProperty]
	public partial int LedgerRebelledCount { get; set; }

	public bool HasLedgerSearchText => !string.IsNullOrWhiteSpace(LedgerSearchText);
	public bool IsFilterAllActive => string.Equals(LedgerFilter, "All", StringComparison.OrdinalIgnoreCase);
	public bool IsFilterDrankActive => string.Equals(LedgerFilter, "Drank", StringComparison.OrdinalIgnoreCase);
	public bool IsFilterSkippedActive => string.Equals(LedgerFilter, "Skipped", StringComparison.OrdinalIgnoreCase);
	public bool IsFilterCompliedActive => string.Equals(LedgerFilter, "Complied", StringComparison.OrdinalIgnoreCase);
	public bool IsFilterRebelledActive => string.Equals(LedgerFilter, "Rebelled", StringComparison.OrdinalIgnoreCase);

	public bool IsPageSize5Active => LedgerPageSize == 5;
	public bool IsPageSize10Active => LedgerPageSize == 10;
	public bool IsPageSize20Active => LedgerPageSize == 20;

	public bool IsSortNewestActive => string.Equals(LedgerSortOrder, "Newest", StringComparison.OrdinalIgnoreCase);
	public bool IsSortOldestActive => string.Equals(LedgerSortOrder, "Oldest", StringComparison.OrdinalIgnoreCase);
	public bool IsSortCaffeineActive => string.Equals(LedgerSortOrder, "Caffeine", StringComparison.OrdinalIgnoreCase);

	public string LedgerFilterAllText => $"All ({LedgerAllCount})";
	public string LedgerFilterDrankText => $"☕ Drank ({LedgerDrankCount})";
	public string LedgerFilterSkippedText => $"🚫 Skipped ({LedgerSkippedCount})";
	public string LedgerFilterCompliedText => $"😇 Complied ({LedgerCompliedCount})";
	public string LedgerFilterRebelledText => $"😈 Rebelled ({LedgerRebelledCount})";

	// Direct properties for KPI cards to ensure crisp compiled bindings
	public double ActiveCaffeineMg => Summary.ActiveCaffeineMg;
	public int TotalCaffeineTodayMg => Summary.TotalCaffeineTodayMg;
	public int CupsLoggedCount => Summary.CupsLoggedCount;
	public int CompliancePercentage => Summary.CompliancePercentage;
	public int ObeyedCount => Summary.ObeyedCount;
	public int RebelledCount => Summary.RebelledCount;
	public string HoursToSleepReadyText => Summary.SleepWindowDisplay;
	public string HoursToClearanceText => Summary.ClearanceDisplay;
	public string ClearanceDisplay => Summary.ClearanceDisplay;
	public string SleepWindowDisplay => Summary.SleepWindowDisplay;
	public string CupsCountDisplay => Summary.CupsCountDisplay;
	public string ComplianceBreakdownText => Summary.ComplianceBreakdownDisplay;
	public ComplianceStats ComplianceStats => Compliance;

	private static async Task SafeDispatchToMainThreadAsync(Action action, TimeSpan? timeout = null)
	{
		if (action == null)
		{
			return;
		}

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
			// Safe fallback exclusively for non-UI unit test runners where MAUI MainThread is not implemented
			action();
		}
		catch (Exception)
		{
			// Eliminate background thread execution fallback on InvalidOperationException, timeouts, or window deactivation
		}
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized)
		{
			return;
		}
		_isInitialized = true;

		// 1. Subscribe to storage sync status changes
		storageSyncService.StatusChanged -= OnSyncStatusChanged;
		storageSyncService.StatusChanged += OnSyncStatusChanged;
		UpdateSyncStatusProperties(storageSyncService.Status);

		// 2. Load stored logs from SQLite persistence
		await ReloadLogsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);

		// 3. Start periodic background decay updates
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
		}).ConfigureAwait(false);

		await RecalculateAllMetricsAsync().ConfigureAwait(false);
	}

	public void SynchronizeHistoryLogsInPlace(List<OracleDecisionLog> newLogs)
	{
		// 1. Fast-path: Check for sequence equality by Id and relevant properties
		if (HistoryLogs.Count == newLogs.Count)
		{
			bool identical = true;
			for (int i = 0; i < newLogs.Count; i++)
			{
				if (!HistoryLogs[i].Equals(newLogs[i]))
				{
					identical = false;
					break;
				}
			}
			if (identical)
			{
				return;
			}
		}

		// 2. Remove deleted logs using HashSet
		var freshIds = newLogs.Select(x => x.Id).ToHashSet();
		for (int i = HistoryLogs.Count - 1; i >= 0; i--)
		{
			if (!freshIds.Contains(HistoryLogs[i].Id))
			{
				HistoryLogs.RemoveAt(i);
			}
		}

		// 3. Map remaining items by Id to their index in HistoryLogs
		var existingIndices = new Dictionary<long, int>(HistoryLogs.Count);
		for (int i = 0; i < HistoryLogs.Count; i++)
		{
			existingIndices[HistoryLogs[i].Id] = i;
		}

		// 4. Align items to target positions in-place
		for (int i = 0; i < newLogs.Count; i++)
		{
			var fresh = newLogs[i];
			if (existingIndices.TryGetValue(fresh.Id, out int existingIndex))
			{
				if (existingIndex == i)
				{
					if (!HistoryLogs[existingIndex].Equals(fresh))
					{
						HistoryLogs[existingIndex] = fresh;
					}
				}
				else if (existingIndex > i)
				{
					HistoryLogs.Move(existingIndex, i);
					foreach (var key in existingIndices.Keys.ToList())
					{
						int idx = existingIndices[key];
						if (idx == existingIndex)
						{
							existingIndices[key] = i;
						}
						else if (idx >= i && idx < existingIndex)
						{
							existingIndices[key] = idx + 1;
						}
					}

					if (!HistoryLogs[i].Equals(fresh))
					{
						HistoryLogs[i] = fresh;
					}
				}
				else
				{
					if (!HistoryLogs[existingIndex].Equals(fresh))
					{
						HistoryLogs[existingIndex] = fresh;
					}
				}
			}
			else
			{
				HistoryLogs.Insert(i, fresh);
				foreach (var key in existingIndices.Keys.ToList())
				{
					if (existingIndices[key] >= i)
					{
						existingIndices[key]++;
					}
				}
				existingIndices[fresh.Id] = i;
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

	public async Task ExecutePeriodicRecalculationTickAsync(CancellationToken cancellationToken = default)
	{
		await RecalculateAllMetricsAsync().ConfigureAwait(false);
		await sqliteDatabaseService.CheckpointWalAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task RunPeriodicRecalculationAsync(CancellationToken cancellationToken)
	{
		using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
		try
		{
			while (await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				if (cancellationToken.IsCancellationRequested) break;

				try
				{
					await ExecutePeriodicRecalculationTickAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception)
				{
					// Safe background tick failure isolation
				}
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// Clean exit
		}
	}

	public void RecalculateAllMetrics()
	{
		_ = RecalculateAllMetricsAsync();
	}

	public async Task RecalculateAllMetricsAsync()
	{
		List<OracleDecisionLog> logsSnapshot;
		try
		{
			if (MainThread.IsMainThread)
			{
				logsSnapshot = HistoryLogs.ToList();
			}
			else
			{
				logsSnapshot = await MainThread.InvokeOnMainThreadAsync(() => HistoryLogs.ToList()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			// Unit test fallback
			logsSnapshot = HistoryLogs.ToList();
		}
		catch (Exception)
		{
			return;
		}

		// 1. Calculate compliance stats
		int yy = 0, nn = 0, yn = 0, ny = 0;
		foreach (var log in logsSnapshot)
		{
			if (log.OracleSaidYes && log.UserDrank) yy++;
			else if (!log.OracleSaidYes && !log.UserDrank) nn++;
			else if (log.OracleSaidYes && !log.UserDrank) yn++;
			else if (!log.OracleSaidYes && log.UserDrank) ny++;
		}

		var compliance = new ComplianceStats
		{
			YesYes = yy,
			NoNo = nn,
			YesNo = yn,
			NoYes = ny
		};

		// 2. Offload heavy half-life kinetics math
		var summary = await Task.Run(() => caffeineMetabolismService.ComputeSummary(logsSnapshot, compliance)).ConfigureAwait(false);

		// 3. Update properties on UI thread
		await SafeDispatchToMainThreadAsync(() =>
		{
			Compliance = compliance;
			Summary = summary;

			MetabolismDrawable.InitialActiveMg = summary.ActiveCaffeineMg;
			WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
			RequestChartInvalidation?.Invoke();

			OnPropertyChanged(nameof(ActiveCaffeineMg));
			OnPropertyChanged(nameof(TotalCaffeineTodayMg));
			OnPropertyChanged(nameof(CupsLoggedCount));
			OnPropertyChanged(nameof(CompliancePercentage));
			OnPropertyChanged(nameof(ObeyedCount));
			OnPropertyChanged(nameof(RebelledCount));
			OnPropertyChanged(nameof(HoursToSleepReadyText));
			OnPropertyChanged(nameof(HoursToClearanceText));
			OnPropertyChanged(nameof(ClearanceDisplay));
			OnPropertyChanged(nameof(SleepWindowDisplay));
			OnPropertyChanged(nameof(CupsCountDisplay));
			OnPropertyChanged(nameof(ComplianceBreakdownText));
			OnPropertyChanged(nameof(ComplianceStats));
		}).ConfigureAwait(false);
	}

	public async Task AddLogAsync(OracleDecisionLog logEntry)
	{
		await SafeDispatchToMainThreadAsync(() =>
		{
			HistoryLogs.Insert(0, logEntry);
			HasHistoryLogs = true;
		}).ConfigureAwait(false);

		await RecalculateAllMetricsAsync().ConfigureAwait(false);
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task EditLogAsync(OracleDecisionLog logToEdit, CancellationToken cancellationToken = default)
	{
		var result = await appPopupService.ShowEditLogAsync(logToEdit).ConfigureAwait(false);
		await SafeDispatchToMainThreadAsync(() =>
		{
			if (result == null)
			{
				HistoryLogs.Remove(logToEdit);
				HasHistoryLogs = HistoryLogs.Count > 0;
			}
			else
			{
				int index = HistoryLogs.IndexOf(logToEdit);
				if (index >= 0)
				{
					HistoryLogs[index] = result;
				}
			}
		}).ConfigureAwait(false);

		await RecalculateAllMetricsAsync().ConfigureAwait(false);

		if (result == null)
		{
			await sqliteDatabaseService.DeleteLogAsync(logToEdit.Id.ToString(), cancellationToken).ConfigureAwait(false);
		}
		else
		{
			var entity = DecisionLogEntity.FromOracleLog(result, Models.SyncStatus.Pending);
			await sqliteDatabaseService.UpsertLogAsync(entity, cancellationToken).ConfigureAwait(false);
		}

		SafeFlushPendingChanges(cancellationToken);
	}

	public event Action? NavigatingToMainPage;

	public void NotifyNavigatingToMainPage()
	{
		NavigatingToMainPage?.Invoke();
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task NavigateToMainPageAsync()
	{
		NotifyNavigatingToMainPage();
		if (Shell.Current != null)
		{
			Func<Task> navigate = async () =>
			{
				try
				{
					await Shell.Current.GoToAsync("//MainPage");
				}
				catch
				{
					try
					{
						await Shell.Current.GoToAsync("///MainPage");
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[MetabolismViewModel] Navigation to MainPage failed: {ex}");
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

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task OpenStorageSettingsAsync(CancellationToken cancellationToken = default)
	{
		bool dataChanged = await appPopupService.ShowStorageSelectionPopupAsync().ConfigureAwait(false);
		if (dataChanged)
		{
			await ReloadLogsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
		}
		await SafeDispatchToMainThreadAsync(() => UpdateSyncStatusProperties(storageSyncService.Status)).ConfigureAwait(false);
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task OpenCoffeeDictionaryAsync(CancellationToken cancellationToken = default)
	{
		await appPopupService.ShowCoffeeDictionaryAsync(null).ConfigureAwait(false);
	}

	[RelayCommand(AllowConcurrentExecutions = false)]
	public async Task OpenThemeSelectorAsync(CancellationToken cancellationToken = default)
	{
		string? chosenTheme = await appPopupService.ShowThemeSelectorAsync().ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(chosenTheme))
		{
			await SafeDispatchToMainThreadAsync(() =>
			{
				ThemeManager.ApplyTheme(chosenTheme);
				CurrentThemeName = ThemeManager.CurrentThemeName;
				dataPersistenceService.SaveTheme(chosenTheme);
				WeakReferenceMessenger.Default.Send(new InvalidateMetabolismChartMessage());
				RequestChartInvalidation?.Invoke();
			}).ConfigureAwait(false);
		}
	}

	private void SafeFlushPendingChanges(CancellationToken cancellationToken = default)
	{
		var task = storageSyncService.FlushPendingChangesAsync(cancellationToken);
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

	partial void OnLedgerSearchTextChanged(string value)
	{
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
	}

	partial void OnLedgerPageSizeChanged(int value)
	{
		if (value < 1)
		{
			LedgerPageSize = 5;
			return;
		}
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
		OnPropertyChanged(nameof(IsPageSize5Active));
		OnPropertyChanged(nameof(IsPageSize10Active));
		OnPropertyChanged(nameof(IsPageSize20Active));
	}

	partial void OnLedgerSortOrderChanged(string value)
	{
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
		OnPropertyChanged(nameof(IsSortNewestActive));
		OnPropertyChanged(nameof(IsSortOldestActive));
		OnPropertyChanged(nameof(IsSortCaffeineActive));
	}

	[RelayCommand]
	public void SetLedgerPageSize(int size)
	{
		if (size < 1) size = 5;
		if (LedgerPageSize == size) return;
		LedgerPageSize = size;
	}

	[RelayCommand]
	public void SetLedgerSortOrder(string sortOrder)
	{
		if (string.IsNullOrWhiteSpace(sortOrder)) sortOrder = "Newest";
		if (string.Equals(LedgerSortOrder, sortOrder, StringComparison.OrdinalIgnoreCase)) return;
		LedgerSortOrder = sortOrder;
	}

	private void OnHistoryLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		HasHistoryLogs = HistoryLogs.Count > 0;
		RefreshPagedLogs();
	}

	[RelayCommand]
	public void SetLedgerFilter(string filter)
	{
		if (string.IsNullOrWhiteSpace(filter)) filter = "All";
		if (string.Equals(LedgerFilter, filter, StringComparison.OrdinalIgnoreCase)) return;

		LedgerFilter = filter;
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
	}

	[RelayCommand]
	public void ClearLedgerSearch()
	{
		LedgerSearchText = string.Empty;
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
	}

	[RelayCommand]
	public void ClearFilterAndSearch()
	{
		LedgerFilter = "All";
		LedgerSearchText = string.Empty;
		LedgerCurrentPage = 1;
		RefreshPagedLogs();
	}

	[RelayCommand]
	public void NextLedgerPage()
	{
		if (CanGoToNextLedgerPage)
		{
			LedgerCurrentPage++;
			RefreshPagedLogs();
		}
	}

	[RelayCommand]
	public void PreviousLedgerPage()
	{
		if (CanGoToPreviousLedgerPage)
		{
			LedgerCurrentPage--;
			RefreshPagedLogs();
		}
	}

	[RelayCommand]
	public void FirstLedgerPage()
	{
		if (LedgerCurrentPage > 1)
		{
			LedgerCurrentPage = 1;
			RefreshPagedLogs();
		}
	}

	[RelayCommand]
	public void LastLedgerPage()
	{
		if (LedgerCurrentPage < LedgerTotalPages)
		{
			LedgerCurrentPage = LedgerTotalPages;
			RefreshPagedLogs();
		}
	}

	public void RefreshPagedLogs()
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				RefreshPagedLogsCore();
				return;
			}
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
		{
			// Safe fallback for non-UI unit test runners where MAUI MainThread is not implemented
			RefreshPagedLogsCore();
			return;
		}
		catch (Exception)
		{
		}

		_ = SafeDispatchToMainThreadAsync(RefreshPagedLogsCore);
	}

	private void RefreshPagedLogsCore()
	{
		var allLogs = HistoryLogs.ToList();

		LedgerAllCount = allLogs.Count;
		LedgerDrankCount = allLogs.Count(x => x.UserDrank);
		LedgerSkippedCount = allLogs.Count(x => !x.UserDrank);
		LedgerCompliedCount = allLogs.Count(x => x.FollowedAdvice);
		LedgerRebelledCount = allLogs.Count(x => !x.FollowedAdvice);

		IEnumerable<OracleDecisionLog> filtered = allLogs;
		if (string.Equals(LedgerFilter, "Drank", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.Where(x => x.UserDrank);
		}
		else if (string.Equals(LedgerFilter, "Skipped", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.Where(x => !x.UserDrank);
		}
		else if (string.Equals(LedgerFilter, "Complied", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.Where(x => x.FollowedAdvice);
		}
		else if (string.Equals(LedgerFilter, "Rebelled", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.Where(x => !x.FollowedAdvice);
		}

		if (!string.IsNullOrWhiteSpace(LedgerSearchText))
		{
			var query = LedgerSearchText.Trim();
			filtered = filtered.Where(x =>
				(!string.IsNullOrWhiteSpace(x.CoffeeName) && x.CoffeeName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
				(!string.IsNullOrWhiteSpace(x.CoffeeTypeId) && x.CoffeeTypeId.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
				x.FormattedTime.Contains(query, StringComparison.OrdinalIgnoreCase) ||
				x.StatusBadgeText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
				x.CaffeineDisplayText.Contains(query, StringComparison.OrdinalIgnoreCase));
		}

		if (string.Equals(LedgerSortOrder, "Oldest", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.OrderBy(x => x.Timestamp);
		}
		else if (string.Equals(LedgerSortOrder, "Caffeine", StringComparison.OrdinalIgnoreCase))
		{
			filtered = filtered.OrderByDescending(x => x.CaffeineMg).ThenByDescending(x => x.Timestamp);
		}
		else
		{
			filtered = filtered.OrderByDescending(x => x.Timestamp);
		}

		var filteredList = filtered.ToList();
		LedgerTotalFilteredCount = filteredList.Count;

		int pageSize = Math.Max(1, LedgerPageSize);
		LedgerTotalPages = Math.Max(1, (int)Math.Ceiling((double)filteredList.Count / pageSize));

		if (LedgerCurrentPage > LedgerTotalPages)
		{
			LedgerCurrentPage = LedgerTotalPages;
		}
		if (LedgerCurrentPage < 1)
		{
			LedgerCurrentPage = 1;
		}

		CanGoToPreviousLedgerPage = LedgerCurrentPage > 1;
		CanGoToNextLedgerPage = LedgerCurrentPage < LedgerTotalPages;
		LedgerCurrentPageDisplay = $"{LedgerCurrentPage} / {LedgerTotalPages}";

		int skip = (LedgerCurrentPage - 1) * pageSize;
		var pageItems = filteredList.Skip(skip).Take(pageSize).ToList();

		SynchronizePagedLogsInPlace(pageItems);

		HasFilteredLedgerLogs = PagedHistoryLogs.Count > 0;
		IsLedgerFilteredEmpty = HistoryLogs.Count > 0 && PagedHistoryLogs.Count == 0;

		if (filteredList.Count == 0)
		{
			LedgerPageSummaryText = HistoryLogs.Count == 0 ? "0 entries" : "0 matching entries";
		}
		else
		{
			int start = skip + 1;
			int end = Math.Min(skip + pageSize, filteredList.Count);
			LedgerPageSummaryText = $"Showing {start}–{end} of {filteredList.Count}";
		}

		OnPropertyChanged(nameof(HasLedgerSearchText));
		OnPropertyChanged(nameof(IsFilterAllActive));
		OnPropertyChanged(nameof(IsFilterDrankActive));
		OnPropertyChanged(nameof(IsFilterSkippedActive));
		OnPropertyChanged(nameof(IsFilterCompliedActive));
		OnPropertyChanged(nameof(IsFilterRebelledActive));
		OnPropertyChanged(nameof(LedgerFilterAllText));
		OnPropertyChanged(nameof(LedgerFilterDrankText));
		OnPropertyChanged(nameof(LedgerFilterSkippedText));
		OnPropertyChanged(nameof(LedgerFilterCompliedText));
		OnPropertyChanged(nameof(LedgerFilterRebelledText));
		OnPropertyChanged(nameof(IsPageSize5Active));
		OnPropertyChanged(nameof(IsPageSize10Active));
		OnPropertyChanged(nameof(IsPageSize20Active));
		OnPropertyChanged(nameof(IsSortNewestActive));
		OnPropertyChanged(nameof(IsSortOldestActive));
		OnPropertyChanged(nameof(IsSortCaffeineActive));
	}

	private void SynchronizePagedLogsInPlace(List<OracleDecisionLog> targetItems)
	{
		if (PagedHistoryLogs.Count == targetItems.Count)
		{
			bool identical = true;
			for (int i = 0; i < targetItems.Count; i++)
			{
				if (!PagedHistoryLogs[i].Equals(targetItems[i]))
				{
					identical = false;
					break;
				}
			}
			if (identical)
			{
				return;
			}
		}

		while (PagedHistoryLogs.Count > targetItems.Count)
		{
			PagedHistoryLogs.RemoveAt(PagedHistoryLogs.Count - 1);
		}

		for (int i = 0; i < targetItems.Count; i++)
		{
			if (i < PagedHistoryLogs.Count)
			{
				if (!PagedHistoryLogs[i].Equals(targetItems[i]))
				{
					PagedHistoryLogs[i] = targetItems[i];
				}
			}
			else
			{
				PagedHistoryLogs.Add(targetItems[i]);
			}
		}
	}

	public void Dispose()
	{
		RequestChartInvalidation = null;
		HistoryLogs.CollectionChanged -= OnHistoryLogsCollectionChanged;
		storageSyncService.StatusChanged -= OnSyncStatusChanged;
		_periodicTimerCts?.Cancel();
		_periodicTimerCts?.Dispose();
		_periodicTimerCts = null;
	}
}
