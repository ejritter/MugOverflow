using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.DependencyInjection;
using MugOverflowProject.Models;
using MugOverflowProject.ViewModels;
using MugOverflowProject.Views.Popups;

namespace MugOverflowProject.Services;

public sealed class AppPopupService(IServiceProvider serviceProvider) : IAppPopupService
{
	private static readonly PopupOptions BorderlessPopupOptions = new()
	{
		Shape = null,
		Shadow = null
	};

	private Page? GetActivePage()
	{
		return Shell.Current?.CurrentPage
			?? Application.Current?.Windows.FirstOrDefault()?.Page;
	}

	public async Task<CoffeeType?> ShowCoffeeDictionaryAsync(string? targetCoffeeId = null)
	{
		var page = GetActivePage();
		if (page is null) return null;

		var vm = serviceProvider.GetRequiredService<CoffeeDictionaryPopupViewModel>();
		vm.SetHighlightedCoffee(targetCoffeeId);
		var popup = new CoffeeDictionaryPopup(vm);
		try
		{
			await page.ShowPopupAsync(popup, BorderlessPopupOptions);
			return vm.SelectedCoffee;
		}
		finally
		{
			vm.RequestClose = null;
			popup.BindingContext = null;
		}
	}

	public async Task<OracleDecisionLog?> ShowEditLogAsync(OracleDecisionLog logToEdit)
	{
		var page = GetActivePage();
		if (page is null) return logToEdit;

		var vm = serviceProvider.GetRequiredService<EditLogPopupViewModel>();
		vm.LoadLog(logToEdit);
		var popup = new EditLogPopup(vm);
		try
		{
			await page.ShowPopupAsync(popup, BorderlessPopupOptions);

			if (vm.WasDeleted)
			{
				return null; // Signals deletion
			}

			if (vm.WasSaved)
			{
				bool followedAdvice = (logToEdit.OracleSaidYes && vm.UserDrank) || (!logToEdit.OracleSaidYes && !vm.UserDrank);
				return new OracleDecisionLog
				{
					Id = logToEdit.Id,
					ServerId = logToEdit.ServerId,
					Timestamp = logToEdit.Timestamp,
					OracleSaidYes = logToEdit.OracleSaidYes,
					UserDrank = vm.UserDrank,
					FollowedAdvice = followedAdvice,
					CoffeeTypeId = vm.UserDrank ? vm.SelectedCoffeeType?.Id : null,
					CoffeeName = vm.UserDrank ? vm.SelectedCoffeeType?.Name : null,
					CaffeineMg = vm.UserDrank ? (vm.SelectedCoffeeType?.Caffeine ?? 0) : 0
				};
			}

			return logToEdit;
		}
		finally
		{
			popup.BindingContext = null;
		}
	}

	public async Task<string?> ShowThemeSelectorAsync()
	{
		var page = GetActivePage();
		if (page is null) return null;

		var vm = serviceProvider.GetRequiredService<ThemeSelectorPopupViewModel>();
		var popup = new ThemeSelectorPopup(vm);
		try
		{
			await page.ShowPopupAsync(popup, BorderlessPopupOptions);
			return vm.SelectedThemeName;
		}
		finally
		{
			popup.BindingContext = null;
		}
	}

	public async Task<bool> ShowStorageSelectionPopupAsync()
	{
		var page = GetActivePage();
		if (page is null) return false;

		using var scope = serviceProvider.CreateScope();
		var vm = scope.ServiceProvider.GetRequiredService<StorageSelectionViewModel>();
		await vm.InitializeAsync().ConfigureAwait(false);
		var popup = new StorageSelectionPopup(vm);
		try
		{
			await page.ShowPopupAsync(popup, BorderlessPopupOptions);
			return vm.HasMigratedData;
		}
		finally
		{
			popup.CleanUp();
		}
	}
}
