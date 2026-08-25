using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.DependencyInjection;
using MugOverflowProject.Models;
using MugOverflowProject.ViewModels;
using MugOverflowProject.Views.Popups;

namespace MugOverflowProject.Services;

public sealed class AppPopupService(IServiceProvider serviceProvider) : IAppPopupService
{
	private Page? GetActivePage()
	{
		return Application.Current?.Windows.FirstOrDefault()?.Page
			?? Shell.Current?.CurrentPage;
	}

	public async Task ShowCoffeeDictionaryAsync(string? targetCoffeeId = null)
	{
		var page = GetActivePage();
		if (page is null) return;

		var vm = serviceProvider.GetRequiredService<CoffeeDictionaryPopupViewModel>();
		vm.SetHighlightedCoffee(targetCoffeeId);
		var popup = new CoffeeDictionaryPopup(vm);
		await page.ShowPopupAsync(popup);
	}

	public async Task<OracleDecisionLog?> ShowEditLogAsync(OracleDecisionLog logToEdit)
	{
		var page = GetActivePage();
		if (page is null) return logToEdit;

		var vm = serviceProvider.GetRequiredService<EditLogPopupViewModel>();
		vm.LoadLog(logToEdit);
		var popup = new EditLogPopup(vm);
		await page.ShowPopupAsync(popup);

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

	public async Task<string?> ShowThemeSelectorAsync()
	{
		var page = GetActivePage();
		if (page is null) return null;

		var vm = serviceProvider.GetRequiredService<ThemeSelectorPopupViewModel>();
		var popup = new ThemeSelectorPopup(vm);
		await page.ShowPopupAsync(popup);
		return vm.SelectedThemeName;
	}
}
