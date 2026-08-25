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

	public ObservableCollection<CoffeeType> Items { get; } = new(coffeeDictionaryService.GetAll());

	public void SetHighlightedCoffee(string? id)
	{
		HighlightedCoffeeId = id;
	}
}
