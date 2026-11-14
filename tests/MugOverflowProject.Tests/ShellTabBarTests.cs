using Microsoft.Maui.Controls;
using Xunit;

namespace MugOverflowProject.Tests;

public class ShellTabBarTests
{
	[Fact]
	public async Task TestShellTabBarNavigation()
	{
		var shell = new Shell();
		var tabBar = new TabBar();

		var mainContent = new ShellContent
		{
			Route = "MainPage",
			ContentTemplate = new DataTemplate(() => new ContentPage { Title = "MainPage" })
		};

		var statsContent = new ShellContent
		{
			Route = "MetabolismPage",
			ContentTemplate = new DataTemplate(() => new ContentPage { Title = "MetabolismPage" })
		};

		tabBar.Items.Add(mainContent);
		tabBar.Items.Add(statsContent);
		shell.Items.Add(tabBar);

		Assert.Equal("//MainPage", shell.CurrentState.Location.ToString());

		// Navigate to MetabolismPage via absolute route
		await shell.GoToAsync("//MetabolismPage");
		Assert.Equal("//MetabolismPage", shell.CurrentState.Location.ToString());

		// Navigate back to MainPage
		await shell.GoToAsync("//MainPage");
		Assert.Equal("//MainPage", shell.CurrentState.Location.ToString());

		// Navigate to MetabolismPage via /// route
		await shell.GoToAsync("///MetabolismPage");
		Assert.Equal("//MetabolismPage", shell.CurrentState.Location.ToString());

		// Navigate back to MainPage via /// route
		await shell.GoToAsync("///MainPage");
		Assert.Equal("//MainPage", shell.CurrentState.Location.ToString());
	}
}
