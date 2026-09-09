using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Models;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

public sealed partial class CoffeeDictionaryPopupViewModel(ICoffeeDictionaryService coffeeDictionaryService) : ObservableObject
{
	[ObservableProperty]
	public partial string? HighlightedCoffeeId { get; set; }

	[ObservableProperty]
	public partial CoffeeType? SelectedCoffee { get; set; }

	public Action? RequestClose { get; set; }

	public ObservableCollection<CoffeeType> Items { get; } = new(coffeeDictionaryService.GetAll());

	public void SetHighlightedCoffee(string? id)
	{
		HighlightedCoffeeId = id;
		if (!string.IsNullOrEmpty(id))
		{
			SelectedCoffee = Items.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
		}
	}

	[RelayCommand]
	private void SelectCoffee(CoffeeType coffee)
	{
		SelectedCoffee = coffee;
		RequestClose?.Invoke();
	}
}
