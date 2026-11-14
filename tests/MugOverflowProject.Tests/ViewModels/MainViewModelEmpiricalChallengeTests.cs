using System.Collections.Concurrent;
using MugOverflowProject.Models;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.Tests.Fixtures;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.ViewModels;

public sealed class MainViewModelEmpiricalChallengeTests : IAsyncLifetime
{
	private readonly SqliteTestDatabaseFixture _dbFixture = new();
	private readonly FakeCoffeeDictionaryService _coffeeService = new();
	private readonly FakeOracleEngineService _oracleService = new();
	private readonly FakeCaffeineMetabolismService _metabolismService = new();
	private readonly FakeDataPersistenceService _persistenceService = new();
	private readonly FakeAppPopupService _popupService = new();
	private readonly FakeStorageSyncService _syncService = new();

	public async Task InitializeAsync()
	{
		await _dbFixture.InitializeAsync();
	}

	public async Task DisposeAsync()
	{
		await _dbFixture.DisposeAsync();
	}

	private MainViewModel CreateViewModel(ISqliteDatabaseService? dbService = null, IStorageSyncService? syncService = null)
	{
		return new MainViewModel(
			_coffeeService,
			_oracleService,
			_metabolismService,
			_persistenceService,
			_popupService,
			dbService ?? _dbFixture.DatabaseService,
			syncService ?? _syncService);
	}

	#region 1. Lifecycle & SQLite Hydration Stress Tests

	[Fact]
	public async Task Startup_HydratesFromRealSqlite_PopulatesHistoryLogsAndMetricsAccurately()
	{
		// Arrange: Seed 50 distinct real entities into SQLite
		var entities = SqliteTestDatabaseFixture.CreateSampleEntities(50, (int)SyncStatus.Synced);
		await _dbFixture.DatabaseService.UpsertBatchAsync(entities);

		using var vm = CreateViewModel();

		// Act: Initialize ViewModel
		await vm.InitializeAsync();

		// Assert
		Assert.Equal(50, vm.HistoryLogs.Count);
		Assert.True(vm.HasHistoryLogs);
		int expectedTodayCups = entities.Count(e => e.Timestamp.ToLocalTime().Date == DateTime.Today);
		Assert.Equal(expectedTodayCups, vm.CupsLoggedCount);
		Assert.True(vm.TotalCaffeineTodayMg >= 0);
		Assert.True(vm.ActiveCaffeineMg >= 0);
		Assert.Equal(50, vm.ObeyedCount + vm.RebelledCount);

		// Verify chronological descending sort order
		for (int i = 0; i < vm.HistoryLogs.Count - 1; i++)
		{
			Assert.True(vm.HistoryLogs[i].Timestamp >= vm.HistoryLogs[i + 1].Timestamp,
				$"Log at index {i} ({vm.HistoryLogs[i].Timestamp}) should be >= log at {i + 1} ({vm.HistoryLogs[i + 1].Timestamp})");
		}
	}

	[Fact]
	public async Task Startup_WhenSqliteIsEmpty_InitializesCleanZeroMetrics()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.Empty(vm.HistoryLogs);
		Assert.False(vm.HasHistoryLogs);
		Assert.Equal(0, vm.CupsLoggedCount);
		Assert.Equal(0, vm.TotalCaffeineTodayMg);
		Assert.Equal(0.0, vm.ActiveCaffeineMg);
		Assert.Equal(0, vm.ObeyedCount);
		Assert.Equal(0, vm.RebelledCount);
		Assert.Equal(100, vm.CompliancePercentage);
		Assert.Single(vm.ChatMessages); // Initial welcome message
		Assert.True(vm.ChatMessages[0].IsWelcomeMessage);
	}

	[Fact]
	public async Task Startup_LargeDataset_HydratesThousandsOfRecordsWithoutDegradation()
	{
		// Arrange: Seed 1,000 records
		var entities = SqliteTestDatabaseFixture.CreateSampleEntities(1000, (int)SyncStatus.Synced);
		await _dbFixture.DatabaseService.UpsertBatchAsync(entities);

		using var vm = CreateViewModel();

		// Act
		var sw = System.Diagnostics.Stopwatch.StartNew();
		await vm.InitializeAsync();
		sw.Stop();

		// Assert
		Assert.Equal(1000, vm.HistoryLogs.Count);
		Assert.True(vm.HasHistoryLogs);
		int expectedTodayCups = entities.Count(e => e.Timestamp.ToLocalTime().Date == DateTime.Today);
		Assert.Equal(expectedTodayCups, vm.CupsLoggedCount);
		Assert.True(sw.ElapsedMilliseconds < 5000, $"Initialization took too long: {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Startup_MultipleInitializations_IsIdempotentAndDoesNotDuplicateSubscriptions()
	{
		using var vm = CreateViewModel();

		await vm.InitializeAsync();
		await vm.InitializeAsync();
		await vm.InitializeAsync();

		Assert.Equal(1, _syncService.InitializeCallCount);

		// StatusChanged event should only fire once per trigger
		int propertyChangeCount = 0;
		vm.PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(vm.SyncStatusText))
			{
				propertyChangeCount++;
			}
		};

		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.Equal(1, propertyChangeCount);
		Assert.Equal("Syncing...", vm.SyncStatusText);
	}

	#endregion

	#region 2. Sync Status Telemetry Stress Tests

	[Theory]
	[InlineData(SyncEngineStatus.Syncing, "Syncing...", "#38BDF8", true, false)]
	[InlineData(SyncEngineStatus.Offline, "Offline", "#F59E0B", false, true)]
	[InlineData(SyncEngineStatus.Error, "Sync Error", "#EF4444", false, false)]
	[InlineData(SyncEngineStatus.Success, "Synced", "#10B981", false, false)]
	public async Task Telemetry_StatusMatrixTransitions_MatchExactDesignTokens(
		SyncEngineStatus inputStatus,
		string expectedText,
		string expectedHexColor,
		bool expectedIsSyncing,
		bool expectedIsOffline)
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		_syncService.TriggerStatusChanged(inputStatus);

		Assert.Equal(expectedText, vm.SyncStatusText);
		Assert.Equal(Color.FromArgb(expectedHexColor), vm.SyncStatusColor);
		Assert.Equal(expectedIsSyncing, vm.IsSyncing);
		Assert.Equal(expectedIsOffline, vm.IsOffline);
	}

	[Fact]
	public async Task Telemetry_IdleStatus_WithAndWithoutSyncTimestamp()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Without timestamp -> "Ready", Grey #9CA3AF
		_syncService.LastSuccessfulSyncUtc = null;
		_syncService.TriggerStatusChanged(SyncEngineStatus.Idle);
		Assert.Equal("Ready", vm.SyncStatusText);
		Assert.Equal(Color.FromArgb("#9CA3AF"), vm.SyncStatusColor);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);

		// With timestamp -> "Synced", Green #10B981
		_syncService.LastSuccessfulSyncUtc = DateTime.UtcNow;
		_syncService.TriggerStatusChanged(SyncEngineStatus.Idle);
		Assert.Equal("Synced", vm.SyncStatusText);
		Assert.Equal(Color.FromArgb("#10B981"), vm.SyncStatusColor);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
	}

	[Fact]
	public async Task Telemetry_HighFrequencyStateOscillation_MaintainsStateConsistency()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		var statuses = new[]
		{
			SyncEngineStatus.Syncing,
			SyncEngineStatus.Success,
			SyncEngineStatus.Idle,
			SyncEngineStatus.Offline,
			SyncEngineStatus.Error
		};

		var exceptions = new ConcurrentBag<Exception>();

		// Parallel firing from multiple threads
		Parallel.For(0, 200, i =>
		{
			try
			{
				var status = statuses[i % statuses.Length];
				_syncService.TriggerStatusChanged(status);
			}
			catch (Exception ex)
			{
				exceptions.Add(ex);
			}
		});

		Assert.Empty(exceptions);

		// Set deterministic final status
		_syncService.TriggerStatusChanged(SyncEngineStatus.Success);
		Assert.Equal("Synced", vm.SyncStatusText);
		Assert.False(vm.IsSyncing);
		Assert.False(vm.IsOffline);
	}

	[Fact]
	public async Task Telemetry_AfterDispose_DoesNotReceiveOrProcessEvents()
	{
		var vm = CreateViewModel();
		await vm.InitializeAsync();

		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);
		Assert.True(vm.IsSyncing);

		// Dispose ViewModel
		vm.Dispose();

		// Fire status change
		_syncService.TriggerStatusChanged(SyncEngineStatus.Offline);

		// Property should remain as was prior to disposal because handler was unsubscribed
		Assert.True(vm.IsSyncing);
		Assert.False(vm.IsOffline);
		Assert.Equal("Syncing...", vm.SyncStatusText);
	}

	#endregion

	#region 3. Concurrent Intake Mutations While Syncing

	[Fact]
	public async Task ConcurrentMutations_WhileSyncIsActive_PersistsAllEntitiesWithPendingStatus()
	{
		// Arrange: Real SQLite database
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Simulate background sync in flight
		_syncService.TriggerStatusChanged(SyncEngineStatus.Syncing);

		// Act: Execute 20 rapid drink intake confirmations
		for (int i = 0; i < 20; i++)
		{
			var item = new ChatMessageItem
			{
				Id = i + 1,
				IsOracleMessage = true,
				OracleSaidYes = i % 2 == 0,
				SelectedCoffeeTypeId = i % 2 == 0 ? "espresso" : "drip",
				SelectedCoffeeName = i % 2 == 0 ? "Espresso" : "Drip Coffee",
				SelectedCaffeineMg = i % 2 == 0 ? 64 : 95
			};

			await vm.ConfirmDrankOutcomeCommand.ExecuteAsync(item);
		}

		// Assert: In-memory ViewModel state
		Assert.Equal(20, vm.HistoryLogs.Count);
		Assert.Equal(20, vm.CupsLoggedCount);
		Assert.Equal(20, _syncService.FlushPendingChangesCallCount);

		// Assert: Direct SQLite persistence verification
		var persisted = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(20, persisted.Count);

		// All 20 newly logged records MUST have SyncStatus = 0 (Pending)
		Assert.All(persisted, entity => Assert.Equal((int)SyncStatus.Pending, entity.SyncStatus));

		var pending = await _dbFixture.DatabaseService.GetPendingSyncLogsAsync();
		Assert.Equal(20, pending.Count);
	}

	[Fact]
	public async Task ConcurrentMutations_MixedDrankSkipAndEdit_ThreadSafetyAndConsistency()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// 1. Log 10 drinks
		for (int i = 0; i < 10; i++)
		{
			var item = new ChatMessageItem
			{
				Id = 100 + i,
				IsOracleMessage = true,
				OracleSaidYes = true,
				SelectedCoffeeTypeId = "drip",
				SelectedCoffeeName = "Drip Coffee",
				SelectedCaffeineMg = 95
			};
			await vm.ConfirmDrankOutcomeCommand.ExecuteAsync(item);
		}

		// 2. Log 5 skips
		for (int i = 0; i < 5; i++)
		{
			var item = new ChatMessageItem
			{
				Id = 200 + i,
				IsOracleMessage = true,
				OracleSaidYes = false,
				MessageText = "Skip coffee"
			};
			await vm.ConfirmSkipOutcomeCommand.ExecuteAsync(item);
		}

		Assert.Equal(15, vm.HistoryLogs.Count);

		// 3. Edit one log (update)
		var targetLog = vm.HistoryLogs.First(l => l.Id == 100);
		var updatedLog = new OracleDecisionLog
		{
			Id = targetLog.Id,
			Timestamp = targetLog.Timestamp,
			OracleSaidYes = true,
			UserDrank = true,
			FollowedAdvice = true,
			CoffeeTypeId = "espresso",
			CoffeeName = "Espresso",
			CaffeineMg = 64
		};
		_popupService.ReturnExplicitEditedLog = true;
		_popupService.EditedLogToReturn = updatedLog;

		await vm.EditLogCommand.ExecuteAsync(targetLog);

		// 4. Edit another log (delete)
		var deleteTarget = vm.HistoryLogs.First(l => l.Id == 101);
		_popupService.EditedLogToReturn = null; // Delete signal

		await vm.EditLogCommand.ExecuteAsync(deleteTarget);

		// Assertions
		Assert.Equal(14, vm.HistoryLogs.Count);
		var dbLogs = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Equal(14, dbLogs.Count);

		// Verify updated entry in DB
		var updatedDbEntity = await _dbFixture.DatabaseService.GetLogByIdAsync("100");
		Assert.NotNull(updatedDbEntity);
		Assert.Equal("Espresso", updatedDbEntity.ItemName);
		Assert.Equal(64.0, updatedDbEntity.CaffeineAmountMg);
		Assert.Equal((int)SyncStatus.Pending, updatedDbEntity.SyncStatus);

		// Verify deleted entry in DB
		var deletedDbEntity = await _dbFixture.DatabaseService.GetLogByIdAsync("101");
		Assert.Null(deletedDbEntity);
	}

	#endregion

	#region 4. Storage Selection & Migration Integration

	[Fact]
	public async Task OpenStorageSettingsCommand_ReloadsLogsAndRefreshesTelemetry()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		// Add record externally into SQLite (simulating cloud download during popup migration)
		var externalEntity = SqliteTestDatabaseFixture.CreateSampleEntity(
			id: "external-999",
			itemName: "Cold Brew Coffee",
			itemCategory: "cold_brew",
			caffeineAmountMg: 155.0,
			syncStatus: (int)SyncStatus.Synced);
		await _dbFixture.DatabaseService.UpsertLogAsync(externalEntity);

		_syncService.LastSuccessfulSyncUtc = DateTime.UtcNow;
		_syncService.Status = SyncEngineStatus.Success;
		_popupService.StorageSelectionResult = true;

		// Act: User opens storage settings popup and closes it
		await vm.OpenStorageSettingsCommand.ExecuteAsync(null);

		// Assert: ViewModel should have reloaded logs and reflected external entity
		Assert.Equal(1, _popupService.ShowStorageSelectionPopupCallCount);
		Assert.Single(vm.HistoryLogs);
		Assert.Equal("Cold Brew Coffee", vm.HistoryLogs[0].CoffeeName);
		Assert.Equal(155, vm.HistoryLogs[0].CaffeineMg);
		Assert.Equal("Synced", vm.SyncStatusText);
		Assert.Equal(Color.FromArgb("#10B981"), vm.SyncStatusColor);
	}

	#endregion

	#region 5. Conversational Tap-Chip & Back-and-Forth Barista Chat Tests

	[Fact]
	public async Task SendPromptChipCommand_CoffeeConsultation_AddsFormattedUserBubbleAndPendingVerdict()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		_oracleService.VerdictToReturn = (true, "YES! Pull that golden espresso shot!");
		var coffeeChip = vm.PromptChips.First(c => !c.IsBuzzCheck);

		Assert.True(vm.ShowPromptChipsDock);
		Assert.False(vm.HasPendingVerdict);

		await vm.SendPromptChipCommand.ExecuteAsync(coffeeChip);

		// Welcome + User query + Barista verdict (zero leftover typing indicators)
		Assert.DoesNotContain(vm.ChatMessages, m => m.IsTypingIndicator);
		Assert.Equal(3, vm.ChatMessages.Count);

		var userBubble = vm.ChatMessages[1];
		Assert.True(userBubble.IsUserMessage);
		Assert.Contains("Drip Coffee", userBubble.VisibleMessageText);
		Assert.Contains("95 mg", userBubble.VisibleMessageText);

		var verdictBubble = vm.ChatMessages[2];
		Assert.True(verdictBubble.IsOracleVerdictCard);
		Assert.True(verdictBubble.IsActionPending);
		Assert.Equal("Yes", verdictBubble.MascotAvatarReaction);
		Assert.Equal("YES! Pull that golden espresso shot!", verdictBubble.VisibleMessageText);

		Assert.Same(verdictBubble, vm.PendingVerdictMessage);
		Assert.True(vm.HasPendingVerdict);
		Assert.False(vm.ShowPromptChipsDock);
		Assert.Equal("Yes", vm.MascotReactionState);
		Assert.Equal("☕ Brewed & drank Drip Coffee!", vm.PendingDrinkButtonText);
	}

	[Fact]
	public async Task SendPromptChipCommand_BuzzCheck_AddsRoastBuzzCheckCardWithoutPendingVerdict()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		var buzzChip = vm.PromptChips.Single(c => c.IsBuzzCheck);
		await vm.SendPromptChipCommand.ExecuteAsync(buzzChip);

		Assert.DoesNotContain(vm.ChatMessages, m => m.IsTypingIndicator);
		Assert.Equal(3, vm.ChatMessages.Count);

		var userBubble = vm.ChatMessages[1];
		Assert.True(userBubble.IsUserMessage);
		Assert.Equal("📈 How's my caffeine buzz & sleep timer looking?", userBubble.VisibleMessageText);

		var buzzBubble = vm.ChatMessages[2];
		Assert.True(buzzBubble.IsOracleMessage);
		Assert.True(buzzBubble.IsBuzzCheckCard);
		Assert.False(buzzBubble.IsActionPending);
		Assert.False(buzzBubble.ShowVerdictBadge);
		Assert.Equal("Roast Buzz Check", buzzBubble.OracleHeaderTitle);
		Assert.Equal("Happy", buzzBubble.MascotAvatarReaction);
		Assert.Contains("Active buzz:", buzzBubble.VisibleMessageText);
		Assert.Contains("Sleep window (<20 mg):", buzzBubble.VisibleMessageText);

		Assert.False(vm.HasPendingVerdict);
		Assert.True(vm.ShowPromptChipsDock);
	}

	[Fact]
	public async Task ReplyDrankCommand_AppendsUserReplyAndBotReactionBubbles_AndPersistsToSqlite()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		_oracleService.VerdictToReturn = (false, "Hold up on another cup!");
		await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
		Assert.True(vm.HasPendingVerdict);

		await vm.ReplyDrankCommand.ExecuteAsync(null);

		// Welcome + User ask + Verdict + User reply + Barista reaction
		Assert.Equal(5, vm.ChatMessages.Count);
		Assert.False(vm.HasPendingVerdict);
		Assert.True(vm.ShowPromptChipsDock);
		Assert.Equal("Drank", vm.MascotReactionState);

		var userReply = vm.ChatMessages[3];
		Assert.True(userReply.IsUserMessage);
		Assert.Equal("☕ Brewed & drank a Drip Coffee (+95 mg)!", userReply.VisibleMessageText);

		var botReaction = vm.ChatMessages[4];
		Assert.True(botReaction.IsOracleMessage);
		Assert.True(botReaction.IsOutcomeReactionCard);
		Assert.True(botReaction.IsConfirmed);
		Assert.False(botReaction.IsComplied); // Oracle said No, user drank -> rebelled
		Assert.Equal("Drank", botReaction.MascotAvatarReaction);
		Assert.Equal("Barista Log Receipt", botReaction.OracleHeaderTitle);
		Assert.False(string.IsNullOrWhiteSpace(botReaction.VisibleMessageText));
		Assert.Contains("Drip Coffee", botReaction.VisibleMessageText);

		// Verify SQLite persistence
		var persisted = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(persisted);
		Assert.Equal("Drip Coffee", persisted[0].ItemName);
		Assert.Equal(95.0, persisted[0].CaffeineAmountMg);
		Assert.Equal((int)SyncStatus.Pending, persisted[0].SyncStatus);
		Assert.Equal(95, vm.TotalCaffeineTodayMg);
	}

	[Fact]
	public async Task ReplySkippedCommand_AppendsUserSkipAndBotReactionBubbles_AndPersistsToSqlite()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		_oracleService.VerdictToReturn = (false, "Hold up on another cup!");
		await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
		Assert.True(vm.HasPendingVerdict);

		await vm.ReplySkippedCommand.ExecuteAsync(null);

		Assert.Equal(5, vm.ChatMessages.Count);
		Assert.False(vm.HasPendingVerdict);
		Assert.True(vm.ShowPromptChipsDock);
		Assert.Equal("Skipped", vm.MascotReactionState);

		var userReply = vm.ChatMessages[3];
		Assert.True(userReply.IsUserMessage);
		Assert.Equal("🫗 Skipped this cup—holding the line for now.", userReply.VisibleMessageText);

		var botReaction = vm.ChatMessages[4];
		Assert.True(botReaction.IsOracleMessage);
		Assert.True(botReaction.IsOutcomeReactionCard);
		Assert.True(botReaction.IsConfirmed);
		Assert.True(botReaction.IsComplied); // Oracle said No, user skipped -> complied
		Assert.Equal("Skipped", botReaction.MascotAvatarReaction);
		Assert.False(string.IsNullOrWhiteSpace(botReaction.VisibleMessageText));

		var persisted = await _dbFixture.DatabaseService.GetAllLogsAsync();
		Assert.Single(persisted);
		Assert.Equal(0.0, persisted[0].CaffeineAmountMg);
		Assert.Equal((int)SyncStatus.Pending, persisted[0].SyncStatus);
	}

	[Fact]
	public async Task SwapPendingDrinkCommand_UpdatesPendingVerdictCoffeeAndButtonText()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
		Assert.Equal("☕ Brewed & drank Drip Coffee!", vm.PendingDrinkButtonText);

		_popupService.SelectedCoffeeToReturn = vm.AvailableCoffeeTypes.First(c => c.Id == "espresso");
		await vm.SwapPendingDrinkCommand.ExecuteAsync(null);

		Assert.Equal("Espresso", vm.PendingVerdictMessage?.SelectedCoffeeName);
		Assert.Equal("☕ Brewed & drank Espresso!", vm.PendingDrinkButtonText);
	}

	[Fact]
	public void ShufflePromptChipsCommand_RotatesFourCoffeePromptsAndPreservesBuzzChipAtEnd()
	{
		using var vm = CreateViewModel();

		Assert.Equal(5, vm.PromptChips.Count);
		Assert.True(vm.PromptChips[4].IsBuzzCheck);
		Assert.Equal("📈 Check my buzz", vm.PromptChips[4].DisplayText);

		var initialCoffeeIds = vm.PromptChips.Take(4).Select(c => c.Id).ToArray();

		vm.ShufflePromptChipsCommand.Execute(null);

		Assert.Equal(5, vm.PromptChips.Count);
		Assert.True(vm.PromptChips[4].IsBuzzCheck);
		Assert.Equal("📈 Check my buzz", vm.PromptChips[4].DisplayText);

		var rotatedCoffeeIds = vm.PromptChips.Take(4).Select(c => c.Id).ToArray();
		Assert.All(vm.PromptChips.Take(4), chip => Assert.False(chip.IsBuzzCheck));
		Assert.NotEqual(initialCoffeeIds, rotatedCoffeeIds);
	}

	[Fact]
	public async Task ToggleDrawerAndCloseDrawerCommands_ManageDrawerStateAndAutoCloseOnActions()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		Assert.False(vm.IsDrawerOpen);

		vm.ToggleDrawerCommand.Execute(null);
		Assert.True(vm.IsDrawerOpen);

		vm.CloseDrawerCommand.Execute(null);
		Assert.False(vm.IsDrawerOpen);

		// Auto-closes on ResetConversation
		await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
		vm.ToggleDrawerCommand.Execute(null);
		Assert.True(vm.IsDrawerOpen);
		vm.ResetConversationCommand.Execute(null);
		Assert.False(vm.IsDrawerOpen);
		Assert.False(vm.HasPendingVerdict);

		// Auto-closes on SelectConsultation and restores PendingVerdictMessage when last turn is pending
		var savedSession = vm.ConsultationHistory[0];
		vm.ToggleDrawerCommand.Execute(null);
		Assert.True(vm.IsDrawerOpen);
		vm.SelectConsultationCommand.Execute(savedSession);
		Assert.False(vm.IsDrawerOpen);
		Assert.True(vm.HasPendingVerdict);

		// Auto-closes on SwitchToStatsTab
		vm.ToggleDrawerCommand.Execute(null);
		Assert.True(vm.IsDrawerOpen);
		await vm.SwitchToStatsTabCommand.ExecuteAsync(null);
		Assert.False(vm.IsDrawerOpen);
	}

	[Fact]
	public async Task MascotMoodTitle_ReflectsThinkingSleepySweetSpotAndOverdriveStates()
	{
		using var vm = CreateViewModel();
		await vm.InitializeAsync();

		int moodTitleNotifications = 0;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(MainViewModel.MascotMoodTitle))
			{
				moodTitleNotifications++;
			}
		};

		// 0 mg -> Running on Fumes
		Assert.Equal("Running on Fumes 😴", vm.MascotMoodTitle);

		// Thinking -> Pulling Your Shot
		vm.IsOracleThinking = true;
		Assert.Equal("Pulling Your Shot... ☕", vm.MascotMoodTitle);
		vm.IsOracleThinking = false;
		Assert.Equal("Running on Fumes 😴", vm.MascotMoodTitle);

		// Log 95 mg -> Sweet Spot Buzz (15..200 mg)
		await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
		await vm.ReplyDrankCommand.ExecuteAsync(null);
		Assert.Equal("Sweet Spot Buzz ☕", vm.MascotMoodTitle);

		// Log two more 95 mg cups (~285 mg active > 200 mg) -> Maximum Overdrive
		for (int i = 0; i < 2; i++)
		{
			await vm.SendPromptChipCommand.ExecuteAsync(vm.PromptChips[0]);
			await vm.ReplyDrankCommand.ExecuteAsync(null);
		}
		Assert.True(vm.ActiveCaffeineMg > 200.0);
		Assert.Equal("Maximum Overdrive ⚡", vm.MascotMoodTitle);
		Assert.True(moodTitleNotifications >= 4);
	}

	[Fact]
	public void OracleEngineService_GetOutcomeReaction_ReturnsWittyBaristaQuipsForAllQuadrants()
	{
		var engine = new OracleEngineService();

		string alignedBrew = engine.GetOutcomeReaction(oracleSaidYes: true, userDrank: true, coffeeName: "Flat White", caffeineMg: 130);
		string rebelBrew = engine.GetOutcomeReaction(oracleSaidYes: false, userDrank: true, coffeeName: "Cold Brew", caffeineMg: 155);
		string alignedSkip = engine.GetOutcomeReaction(oracleSaidYes: false, userDrank: false, coffeeName: null, caffeineMg: 0);
		string plotTwistSkip = engine.GetOutcomeReaction(oracleSaidYes: true, userDrank: false, coffeeName: null, caffeineMg: 0);

		Assert.Contains("Flat White", alignedBrew);
		Assert.Contains("130", alignedBrew);
		Assert.Contains("Cold Brew", rebelBrew);
		Assert.Contains("155", rebelBrew);
		Assert.False(string.IsNullOrWhiteSpace(alignedSkip));
		Assert.False(string.IsNullOrWhiteSpace(plotTwistSkip));
	}

	#endregion
}
