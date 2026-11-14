using Microsoft.UI.Xaml;
using System.Diagnostics;
using MugOverflowProject.Services.Diagnostics;

namespace MugOverflowProject.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();

		this.UnhandledException += (sender, e) =>
		{
			Debug.WriteLine($"[CRITICAL] WinUI UnhandledException: {e.Message}");
			Debug.WriteLine($"[CRITICAL] StackTrace: {e.Exception?.StackTrace}");
			try
			{
				File.AppendAllText(
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
					$"[{DateTime.UtcNow:O}] WinUI UnhandledException: {e.Message}\n{e.Exception?.ToString()}\n\n");
			}
			catch
			{
			}

			// Do not mark unrecoverable COM thread exceptions (0x8001010E) or compositor dispatcher exceptions as handled
			if (!WinUiCrashPolicy.IsUnrecoverableException(e.Exception, e.Message))
			{
				e.Handled = true;
			}
		};

		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			try
			{
				var ex = e.ExceptionObject as Exception;
				Debug.WriteLine($"[CRITICAL] AppDomain UnhandledException: {ex?.Message}");
				File.AppendAllText(
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
					$"[{DateTime.UtcNow:O}] AppDomain UnhandledException: {ex?.Message}\n{ex?.ToString()}\n\n");
			}
			catch
			{
			}
		};

		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			try
			{
				Debug.WriteLine($"[CRITICAL] UnobservedTaskException: {e.Exception?.Message}");
				File.AppendAllText(
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
					$"[{DateTime.UtcNow:O}] UnobservedTaskException: {e.Exception?.Message}\n{e.Exception?.ToString()}\n\n");
			}
			catch
			{
			}
			e.SetObserved();
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
