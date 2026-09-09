using Android.App;
using Android.Content;
using Microsoft.Identity.Client;

namespace MugOverflowProject.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter(
	new[] { Intent.ActionView },
	Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
	DataScheme = "msalYOUR_MICROSOFT_CLIENT_ID_PLACEHOLDER",
	DataHost = "auth")]
public class MsalActivity : BrowserTabActivity
{
}
