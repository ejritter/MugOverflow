using MugOverflowProject.Services.Sync;

namespace MugOverflowProject;

public partial class App : Application
{
	private readonly IStorageSyncService? _storageSyncService;
	private readonly AppShell? _appShell;

	public App(AppShell? appShell = null, IStorageSyncService? storageSyncService = null)
	{
		InitializeComponent();
		_appShell = appShell;
		_storageSyncService = storageSyncService;
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

	protected override async void OnSleep()
	{
		base.OnSleep();

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

	protected override async void OnResume()
	{
		base.OnResume();

		if (_storageSyncService != null && _storageSyncService.Status != SyncEngineStatus.Offline)
		{
			try
			{
				await _storageSyncService.FlushPendingChangesAsync().ConfigureAwait(false);
			}
			catch
			{
				// Suppress foreground sync exceptions
			}
		}
	}

	private async void OnWindowDestroying(object? sender, EventArgs e)
	{
		if (sender is Window window)
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
		}
	}
}
