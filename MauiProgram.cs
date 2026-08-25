using CommunityToolkit.Maui;
using FluentIcons.Maui;
using Microsoft.Maui.DevFlow.Agent;
using MugOverflowProject.Services;
using MugOverflowProject.ViewModels;
using MugOverflowProject.Views;
using MugOverflowProject.Views.Popups;

namespace MugOverflowProject;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseFluentIcons()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.AddMauiDevFlowAgent();
#endif

		// 1. Register Core Application Services
		builder.Services.AddSingleton<ICoffeeDictionaryService, CoffeeDictionaryService>();
		builder.Services.AddSingleton<IOracleEngineService, OracleEngineService>();
		builder.Services.AddSingleton<ICaffeineMetabolismService, CaffeineMetabolismService>();
		builder.Services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
		builder.Services.AddSingleton<IAppPopupService, AppPopupService>();

		// 2. Register ViewModels, Shell & Views
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<MainPage>();

		// 3. Register Popup ViewModels & Popups with CommunityToolkit.Maui
		builder.Services.AddTransientPopup<CoffeeDictionaryPopup, CoffeeDictionaryPopupViewModel>();
		builder.Services.AddTransientPopup<EditLogPopup, EditLogPopupViewModel>();
		builder.Services.AddTransientPopup<ThemeSelectorPopup, ThemeSelectorPopupViewModel>();

		return builder.Build();
	}
}
