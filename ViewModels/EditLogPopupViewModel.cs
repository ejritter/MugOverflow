using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Models;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

public sealed partial class EditLogPopupViewModel(ICoffeeDictionaryService coffeeDictionaryService) : ObservableObject
{
	[ObservableProperty]
	public partial long LogId { get; set; }

	[ObservableProperty]
	public partial string TimeString { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool OracleSaidYes { get; set; }

	[ObservableProperty]
	public partial bool UserDrank { get; set; }

	[ObservableProperty]
	public partial CoffeeType? SelectedCoffeeType { get; set; }

	[ObservableProperty]
	public partial bool WasDeleted { get; set; }

	[ObservableProperty]
	public partial bool WasSaved { get; set; }

	public string OracleVerdictText => OracleSaidYes ? "YES" : "NO";
	public ObservableCollection<CoffeeType> AvailableCoffeeTypes { get; } = new(coffeeDictionaryService.GetAll());

	public void LoadLog(OracleDecisionLog log)
	{
		LogId = log.Id;
		TimeString = log.TimeString;
		OracleSaidYes = log.OracleSaidYes;
		UserDrank = log.UserDrank;
		WasDeleted = false;
		WasSaved = false;

		SelectedCoffeeType = (!string.IsNullOrEmpty(log.CoffeeTypeId)
			? AvailableCoffeeTypes.FirstOrDefault(c => string.Equals(c.Id, log.CoffeeTypeId, StringComparison.OrdinalIgnoreCase))
			: null) ?? AvailableCoffeeTypes[0];
	}

	[RelayCommand]
	private void SetDrank(bool drank)
	{
		UserDrank = drank;
	}

	[RelayCommand]
	private void Save()
	{
		WasSaved = true;
	}

	[RelayCommand]
	private void Delete()
	{
		WasDeleted = true;
	}
}
