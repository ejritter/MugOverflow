using Foundation;
using Microsoft.Identity.Client;
using UIKit;

namespace MugOverflowProject;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		if (Microsoft.Maui.ApplicationModel.Platform.OpenUrl(app, url, options))
			return true;

		AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(url);
		return base.OpenUrl(app, url, options);
	}
}
