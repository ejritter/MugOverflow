using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface IAppPopupService
{
	Task<CoffeeType?> ShowCoffeeDictionaryAsync(string? targetCoffeeId = null);
	Task<OracleDecisionLog?> ShowEditLogAsync(OracleDecisionLog logToEdit);
	Task<string?> ShowThemeSelectorAsync();
	Task<bool> ShowStorageSelectionPopupAsync();
}
