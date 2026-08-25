using MugOverflowProject.Models;

namespace MugOverflowProject.Services;

public sealed class CoffeeDictionaryService : ICoffeeDictionaryService
{
	private static readonly IReadOnlyList<CoffeeType> CoffeeTypes = new List<CoffeeType>
	{
		new()
		{
			Id = "black_coffee",
			Name = "Drip Black Coffee (Standard)",
			Caffeine = 95,
			Volume = "8 oz (237 ml)",
			Desc = "Standard USDA rating for brewed drip black coffee. Range: 80–120 mg."
		},
		new()
		{
			Id = "espresso_single",
			Name = "Single Shot Espresso",
			Caffeine = 63,
			Volume = "1 oz (30 ml)",
			Desc = "Concentrated espresso shot. Mayo Clinic benchmark: ~63 mg."
		},
		new()
		{
			Id = "espresso_double",
			Name = "Double Shot Espresso (Doppio)",
			Caffeine = 126,
			Volume = "2 oz (60 ml)",
			Desc = "Two concentrated espresso shots combined."
		},
		new()
		{
			Id = "cold_brew",
			Name = "Cold Brew Coffee",
			Caffeine = 155,
			Volume = "12 oz (355 ml)",
			Desc = "Steeped for 12–24 hrs. Higher caffeine extraction than drip coffee."
		},
		new()
		{
			Id = "nitro_cold_brew",
			Name = "Nitro Cold Brew",
			Caffeine = 215,
			Volume = "12 oz (355 ml)",
			Desc = "Nitrogen-infused smooth cold brew, highly dense."
		},
		new()
		{
			Id = "latte",
			Name = "Caffè Latte",
			Caffeine = 75,
			Volume = "12 oz (355 ml)",
			Desc = "Espresso base blended with steamed milk and light foam."
		},
		new()
		{
			Id = "cappuccino",
			Name = "Cappuccino",
			Caffeine = 75,
			Volume = "12 oz (355 ml)",
			Desc = "Equal parts espresso, steamed milk, and rich foam."
		},
		new()
		{
			Id = "pour_over",
			Name = "Pour Over Coffee (Light/Medium)",
			Caffeine = 110,
			Volume = "8 oz (237 ml)",
			Desc = "Manual pour-over filter extraction, slightly higher caffeine."
		},
		new()
		{
			Id = "americano",
			Name = "Caffè Americano",
			Caffeine = 126,
			Volume = "12 oz (355 ml)",
			Desc = "Double espresso diluted with hot water."
		},
		new()
		{
			Id = "decaf",
			Name = "Decaf Brewed Coffee",
			Caffeine = 4,
			Volume = "8 oz (237 ml)",
			Desc = "97%+ caffeine removed, contains trace amounts."
		}
	}.AsReadOnly();

	public IReadOnlyList<CoffeeType> GetAll()
	{
		return CoffeeTypes;
	}

	public CoffeeType? GetById(string id)
	{
		return CoffeeTypes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
	}

	public CoffeeType GetDefault()
	{
		return CoffeeTypes[0];
	}
}
