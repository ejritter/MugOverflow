using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Sync;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject;

public partial class App : Application
{
	private readonly IStorageSyncService? _storageSyncService;
	private readonly AppShell? _appShell;
	private readonly MainViewModel? _mainViewModel;
	private readonly ISqliteDatabaseService? _databaseService;

	public App(
		AppShell? appShell = null,
		IStorageSyncService? storageSyncService = null,
		MainViewModel? mainViewModel = null,
		ISqliteDatabaseService? databaseService = null)
	{
		try
		{
			InitializeComponent();
		}
		catch
		{
			// Safe fallback for headless unit test runner where XAML inflator is not attached
		}
		_appShell = appShell;
		_storageSyncService = storageSyncService;
		_mainViewModel = mainViewModel;
		_databaseService = databaseService;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var shell = _appShell ?? new AppShell();
		var window = new Window(shell)
		{
			Title = "MugOverflow - Java Oracle & Caffeine Tracker"
		};

		window.Destroying += OnWindowDestroying;
		return window;
	}

	protected override void OnSleep()
	{
		base.OnSleep();
		_ = HandleAppSleepAsync();
	}

	protected override void OnResume()
	{
		base.OnResume();
		_ = HandleAppResumeAsync();
	}

	public async Task HandleAppSleepAsync()
	{
		if (_storageSyncService != null)
		{
			try
			{
				await _storageSyncService.HandleAppSleepAsync().ConfigureAwait(false);
			}
			catch
			{
				// Prevent sleep lifecycle exceptions
			}
		}
	}

	public async Task HandleAppResumeAsync()
	{
		if (_storageSyncService != null && _storageSyncService.Status != SyncEngineStatus.Offline)
		{
			try
			{
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				await _storageSyncService.FlushPendingChangesAsync(cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Bounded timeout clean cancellation
			}
			catch
			{
				// Suppress foreground sync exceptions
			}
		}
	}

	private async void OnWindowDestroying(object? sender, EventArgs e)
	{
		await HandleWindowDestroyingAsync(sender as Window).ConfigureAwait(false);
	}

	public async Task HandleWindowDestroyingAsync(Window? window = null)
	{
		if (window != null)
		{
			window.Destroying -= OnWindowDestroying;
		}

		if (_storageSyncService != null)
		{
			try
			{
				await _storageSyncService.HandleAppDestroyAsync().ConfigureAwait(false);
			}
			catch
			{
				// Prevent window teardown exceptions
			}

			try
			{
				_storageSyncService.Dispose();
			}
			catch
			{
			}
		}

		if (_mainViewModel != null)
		{
			try
			{
				_mainViewModel.Dispose();
			}
			catch
			{
			}
		}

		if (_databaseService != null)
		{
			try
			{
				await _databaseService.CloseConnectionAsync().ConfigureAwait(false);
				_databaseService.Dispose();
			}
			catch
			{
			}
		}
	}
}
