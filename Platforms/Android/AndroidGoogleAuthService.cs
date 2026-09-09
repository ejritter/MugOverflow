#if ANDROID
using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Common.Apis;
using Android.OS;
using Android.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Platforms.Android;

/// <summary>
/// Native Android Google OAuth implementation using Google Play Services SDK and AccountManager.
/// Eliminates browser redirects and custom URI scheme vulnerabilities.
/// </summary>
public sealed class AndroidGoogleAuthService : IGoogleAuthService
{
	public const int GoogleSignInRequestCode = 9005;
	private const string DriveScope = "https://www.googleapis.com/auth/drive.file";

	private static TaskCompletionSource<GoogleSignInAccount?>? _signInTcs;
	private readonly ISecureStorageService _secureStorage;
	private readonly ILogger<AndroidGoogleAuthService>? _logger;
	private readonly SemaphoreSlim _authLock = new(1, 1);

	public AndroidGoogleAuthService(
		ISecureStorageService secureStorage,
		ILogger<AndroidGoogleAuthService>? logger = null)
	{
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_logger = logger;
	}

	public static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
	{
		if (requestCode == GoogleSignInRequestCode)
		{
			try
			{
				if (resultCode == Result.Ok && data != null)
				{
					var task = GoogleSignIn.GetSignedInAccountFromIntent(data);
					if (task != null && task.IsSuccessful)
					{
						var account = task.Result as GoogleSignInAccount;
						_signInTcs?.TrySetResult(account);
						return;
					}
				}
			}
			catch (Exception ex)
			{
				_signInTcs?.TrySetException(ex);
				return;
			}

			_signInTcs?.TrySetResult(null);
		}
	}

	public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var activity = Platform.CurrentActivity;
			if (activity == null)
			{
				_logger?.LogError("AndroidGoogleAuthService: Current activity is null.");
				return null;
			}

			var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
				.RequestEmail()
				.RequestScopes(new Scope(DriveScope))
				.Build();

			var client = GoogleSignIn.GetClient(activity, gso);

			// Check if already signed in with matching scope
			var lastAccount = GoogleSignIn.GetLastSignedInAccount(activity);
			if (lastAccount != null && lastAccount.Account != null && lastAccount.GrantedScopes.Any(s => s.ScopeUri == DriveScope))
			{
				string? token = await FetchTokenFromAccountAsync(lastAccount).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(token))
				{
					return token;
				}
			}

			// Launch native Google Play Services sign-in dialog
			_signInTcs = new TaskCompletionSource<GoogleSignInAccount?>(TaskCreationOptions.RunContinuationsAsynchronously);

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromMinutes(2));

			activity.StartActivityForResult(client.SignInIntent, GoogleSignInRequestCode);

			var completedTask = await Task.WhenAny(_signInTcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
			if (completedTask != _signInTcs.Task)
			{
				_logger?.LogWarning("Android Google Sign-In timed out or was cancelled.");
				return null;
			}

			var account = await _signInTcs.Task.ConfigureAwait(false);
			if (account == null || account.Account == null)
			{
				_logger?.LogWarning("Android Google Sign-In returned null account.");
				return null;
			}

			return await FetchTokenFromAccountAsync(account).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Exception occurred during Android native Google authentication.");
			return null;
		}
		finally
		{
			_signInTcs = null;
			_authLock.Release();
		}
	}

	public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var cachedToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(cachedToken))
		{
			return cachedToken;
		}

		var activity = Platform.CurrentActivity;
		if (activity == null)
		{
			return null;
		}

		var lastAccount = GoogleSignIn.GetLastSignedInAccount(activity);
		if (lastAccount != null && lastAccount.Account != null)
		{
			return await FetchTokenFromAccountAsync(lastAccount).ConfigureAwait(false);
		}

		return null;
	}

	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var activity = Platform.CurrentActivity;
			if (activity != null)
			{
				var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn).Build();
				var client = GoogleSignIn.GetClient(activity, gso);
				await client.SignOutAsync().ConfigureAwait(false);
			}

			var cachedToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(cachedToken))
			{
				await Task.Run(() =>
				{
					try
					{
						var accountManager = AccountManager.Get(global::Android.App.Application.Context);
						accountManager?.InvalidateAuthToken("com.google", cachedToken);
					}
					catch (Exception ex)
					{
						_logger?.LogWarning(ex, "Failed to invalidate AccountManager token.");
					}
				}, cancellationToken).ConfigureAwait(false);
			}

			_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
			_secureStorage.Remove(GoogleDriveConfig.KeyRefreshToken);
			_secureStorage.Remove(GoogleDriveConfig.KeyTokenExpiry);
		}
		finally
		{
			_authLock.Release();
		}
	}

	private async Task<string?> FetchTokenFromAccountAsync(GoogleSignInAccount account)
	{
		try
		{
			string? token = await Task.Run(() =>
			{
				var context = global::Android.App.Application.Context;
				var accountManager = AccountManager.Get(context);
				if (accountManager == null || account.Account == null)
				{
					return null;
				}

				var future = accountManager.GetAuthToken(account.Account, $"oauth2:{DriveScope}", null, false, null, null);
				var bundle = future.Result as Bundle;
				return bundle?.GetString(AccountManager.KeyAuthtoken);
			}).ConfigureAwait(false);

			if (!string.IsNullOrWhiteSpace(token))
			{
				await _secureStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, token).ConfigureAwait(false);
				await _secureStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O")).ConfigureAwait(false);
				return token;
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Failed to obtain OAuth token from Android AccountManager.");
		}

		return null;
	}
}
#endif
