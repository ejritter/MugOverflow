namespace MugOverflowProject.Models;

public sealed class CoffeeType
{
	public required string Id { get; init; }
	public required string Name { get; init; }
	public required int Caffeine { get; init; }
	public required string Volume { get; init; }
	public required string Desc { get; init; }

	public string DisplayText => $"{Name} ({Caffeine} mg)";
	public string CaffeineDisplay => $"{Caffeine} mg";

	public override string ToString() => Name;
}
