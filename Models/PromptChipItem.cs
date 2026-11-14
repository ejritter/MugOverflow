namespace MugOverflowProject.Models;

public sealed class PromptChipItem
{
	public string Id { get; init; } = string.Empty;
	public string DisplayText { get; init; } = string.Empty;
	public string PromptTemplate { get; init; } = string.Empty;
	public bool IsBuzzCheck { get; init; }
}
