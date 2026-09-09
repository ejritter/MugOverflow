using Microsoft.UI.Xaml;
using System.Diagnostics;

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
			File.AppendAllText(
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
				$"[{DateTime.UtcNow:O}] WinUI UnhandledException: {e.Message}\n{e.Exception?.ToString()}\n\n");
			e.Handled = true;
		};

		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			Debug.WriteLine($"[CRITICAL] AppDomain UnhandledException: {ex?.Message}");
			File.AppendAllText(
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
				$"[{DateTime.UtcNow:O}] AppDomain UnhandledException: {ex?.Message}\n{ex?.ToString()}\n\n");
		};

		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			Debug.WriteLine($"[CRITICAL] UnobservedTaskException: {e.Exception?.Message}");
			File.AppendAllText(
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mugoverflow_crash.log"),
				$"[{DateTime.UtcNow:O}] UnobservedTaskException: {e.Exception?.Message}\n{e.Exception?.ToString()}\n\n");
			e.SetObserved();
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
