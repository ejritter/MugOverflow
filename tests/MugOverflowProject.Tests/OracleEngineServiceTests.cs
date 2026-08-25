using MugOverflowProject.Services;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class OracleEngineServiceTests
{
	private readonly OracleEngineService _service = new();

	[Fact]
	public void ConsultOracle_ReturnsValidVerdictWithNonEmptyText()
	{
		for (int i = 0; i < 50; i++)
		{
			var (saidYes, verdictText) = _service.ConsultOracle();
			Assert.False(string.IsNullOrWhiteSpace(verdictText));
			if (saidYes)
			{
				Assert.True(verdictText.Length > 5);
			}
			else
			{
				Assert.True(verdictText.Length > 5);
			}
		}
	}
}
