using CommunityToolkit.Maui;
using FluentIcons.Maui;
using Microsoft.Maui.Authentication;
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
			.UseMauiCommunityToolkit(options =>
			{
				options.SetPopupDefaults(new DefaultPopupSettings
				{
					BackgroundColor = Colors.Transparent,
					Margin = 0,
					Padding = 0
				});
				options.SetPopupOptionsDefaults(new DefaultPopupOptionsSettings
				{
					Shape = null,
					Shadow = null
				});
			})
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
		builder.Services.AddSingleton<DapperDatabaseService>();
		builder.Services.AddSingleton<ISqliteDatabaseService>(sp => sp.GetRequiredService<DapperDatabaseService>());
		builder.Services.AddSingleton<IDatabaseService>(sp => sp.GetRequiredService<DapperDatabaseService>());
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
		builder.Services.AddHttpClient<IGoogleAuthService, Platforms.Windows.WindowsGoogleAuthService>(client => client.Timeout = TimeSpan.FromSeconds(15));
#else
		builder.Services.AddHttpClient<IGoogleAuthService, DefaultGoogleAuthService>(client => client.Timeout = TimeSpan.FromSeconds(15));
#endif

		builder.Services.AddHttpClient("StorageClient", client => client.Timeout = TimeSpan.FromSeconds(15));
		builder.Services.AddSingleton<LocalStorageProvider>();
		builder.Services.AddSingleton<GoogleDriveStorageProvider>(sp =>
			new GoogleDriveStorageProvider(
				sp.GetRequiredService<IHttpClientFactory>().CreateClient("StorageClient"),
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetService<IGoogleAuthService>(),
				sp.GetService<IWebAuthenticator>(),
				sp.GetService<GoogleDriveConfig>(),
				sp.GetService<Microsoft.Extensions.Logging.ILogger<GoogleDriveStorageProvider>>()));
		builder.Services.AddSingleton<OneDriveStorageProvider>(sp =>
			new OneDriveStorageProvider(
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetRequiredService<IHttpClientFactory>().CreateClient("StorageClient"),
				logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<OneDriveStorageProvider>>()));

		// Keyed Storage Provider Resolution for Dynamic Switching
		builder.Services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.LocalStorage, (sp, _) => sp.GetRequiredService<LocalStorageProvider>());
		builder.Services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.GoogleDrive, (sp, _) => sp.GetRequiredService<GoogleDriveStorageProvider>());
		builder.Services.AddKeyedSingleton<IStorageProvider>(StorageProviderType.OneDrive, (sp, _) => sp.GetRequiredService<OneDriveStorageProvider>());

		// 3. Register Sync Orchestrator & Two-Way Hydration Service
		builder.Services.AddSingleton<IStorageSyncService, StorageSyncService>();

		// 4. Register ViewModels, Shell & Views
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddSingleton<MetabolismViewModel>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddTransient<MetabolismPage>();

		// 5. Register Popup ViewModels & Popups with CommunityToolkit.Maui
		builder.Services.AddTransientPopup<CoffeeDictionaryPopup, CoffeeDictionaryPopupViewModel>();
		builder.Services.AddTransientPopup<EditLogPopup, EditLogPopupViewModel>();
		builder.Services.AddTransientPopup<ThemeSelectorPopup, ThemeSelectorPopupViewModel>();
		builder.Services.AddTransientPopup<StorageSelectionPopup, StorageSelectionViewModel>();

		return builder.Build();
	}
}
