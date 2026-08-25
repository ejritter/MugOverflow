using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public interface ICoffeeDictionaryService
{
	IReadOnlyList<CoffeeType> GetAll();
	CoffeeType? GetById(string id);
	CoffeeType GetDefault();
}
