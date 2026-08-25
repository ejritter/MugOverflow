using MugOverflowProject.Services;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class CoffeeDictionaryServiceTests
{
	private readonly CoffeeDictionaryService _service = new();

	[Fact]
	public void GetAll_ReturnsTenStandardCoffeeTypes()
	{
		var drinks = _service.GetAll();
		Assert.Equal(10, drinks.Count);
	}

	[Fact]
	public void GetDefault_ReturnsStandardDripCoffee()
	{
		var defaultDrink = _service.GetDefault();
		Assert.Equal("black_coffee", defaultDrink.Id);
		Assert.Equal(95, defaultDrink.Caffeine);
	}

	[Theory]
	[InlineData("black_coffee", 95)]
	[InlineData("espresso_single", 63)]
	[InlineData("espresso_double", 126)]
	[InlineData("cold_brew", 155)]
	[InlineData("nitro_cold_brew", 215)]
	[InlineData("latte", 75)]
	[InlineData("cappuccino", 75)]
	[InlineData("pour_over", 110)]
	[InlineData("americano", 126)]
	[InlineData("decaf", 4)]
	public void GetById_ReturnsCorrectCaffeineValue(string id, int expectedCaffeine)
	{
		var drink = _service.GetById(id);
		Assert.NotNull(drink);
		Assert.Equal(expectedCaffeine, drink.Caffeine);
	}
}
