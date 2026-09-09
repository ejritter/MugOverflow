using CommunityToolkit.Maui;
using FluentIcons.Maui;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.Networking;
using MugOverflowProject.Services;
using MugOverflowProject.Services.Database;
using MugOverflowProject.Services.Storage;
using MugOverflowProject.Services.Sync;
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

		// 1. Register Core Application & Persistence Services
		builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
		builder.Services.AddSingleton<ISqliteDatabaseService, SqliteDatabaseService>();
		builder.Services.AddSingleton<ICoffeeDictionaryService, CoffeeDictionaryService>();
		builder.Services.AddSingleton<IOracleEngineService, OracleEngineService>();
		builder.Services.AddSingleton<ICaffeineMetabolismService, CaffeineMetabolismService>();
		builder.Services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
		builder.Services.AddSingleton<IAppPopupService, AppPopupService>();

		// 2. Register Secure Storage & Storage Providers
		builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
		builder.Services.AddSingleton<GoogleDriveConfig>();

#if ANDROID
		builder.Services.AddSingleton<IGoogleAuthService, Platforms.Android.AndroidGoogleAuthService>();
#elif WINDOWS
		builder.Services.AddHttpClient<Platforms.Windows.WindowsGoogleAuthService>();
		builder.Services.AddSingleton<IGoogleAuthService, Platforms.Windows.WindowsGoogleAuthService>();
#else
		builder.Services.AddHttpClient<DefaultGoogleAuthService>();
		builder.Services.AddSingleton<IGoogleAuthService, DefaultGoogleAuthService>();
#endif

		builder.Services.AddSingleton<LocalStorageProvider>();
		builder.Services.AddHttpClient<GoogleDriveStorageProvider>();
		builder.Services.AddSingleton<GoogleDriveStorageProvider>();
		builder.Services.AddHttpClient<OneDriveStorageProvider>();
		builder.Services.AddSingleton<OneDriveStorageProvider>();

		// Keyed Storage Provider Resolution for Dynamic Switching
		builder.Services.AddKeyedSingleton<IStorageProvider, LocalStorageProvider>(StorageProviderType.LocalStorage);
		builder.Services.AddKeyedSingleton<IStorageProvider, GoogleDriveStorageProvider>(StorageProviderType.GoogleDrive);
		builder.Services.AddKeyedSingleton<IStorageProvider, OneDriveStorageProvider>(StorageProviderType.OneDrive);

		// 3. Register Sync Orchestrator & Two-Way Hydration Service
		builder.Services.AddSingleton<IStorageSyncService, StorageSyncService>();

		// 4. Register ViewModels, Shell & Views
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<StorageSelectionViewModel>();

		// 5. Register Popup ViewModels & Popups with CommunityToolkit.Maui
		builder.Services.AddTransientPopup<CoffeeDictionaryPopup, CoffeeDictionaryPopupViewModel>();
		builder.Services.AddTransientPopup<EditLogPopup, EditLogPopupViewModel>();
		builder.Services.AddTransientPopup<ThemeSelectorPopup, ThemeSelectorPopupViewModel>();
		builder.Services.AddTransientPopup<StorageSelectionPopup, StorageSelectionViewModel>();

		return builder.Build();
	}
}
