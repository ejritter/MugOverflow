using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Cloud storage provider for Microsoft OneDrive using MSAL.NET and Microsoft Graph API v1.0
/// targeting the sandboxed 'special/approot' folder (caffeine_consumption.json).
/// </summary>
public sealed class OneDriveStorageProvider : IStorageProvider
{
	public const string DefaultClientId = "YOUR_MICROSOFT_CLIENT_ID_PLACEHOLDER";
	public const string TokenCacheKey = "MugOverflow_OneDrive_MSAL_TokenCache";
	public const string FileName = "caffeine_consumption.json";
	public const string GraphAppRootEndpoint = "https://graph.microsoft.com/v1.0/me/drive/special/approot";
	public const string GraphFileContentEndpoint = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/caffeine_consumption.json:/content";

	public static readonly string[] Scopes =
	[
		"Files.ReadWrite.AppFolder",
		"User.Read",
		"offline_access"
	];

	private readonly ISecureStorageService _secureStorage;
	private readonly HttpClient _httpClient;
	private readonly ILogger<OneDriveStorageProvider>? _logger;
	private readonly IPublicClientApplication? _pca;
	private readonly Func<CancellationToken, Task<string?>>? _customTokenProvider;
	private readonly SemaphoreSlim _authLock = new(1, 1);

	/// <summary>
	/// Production constructor initializing MSAL PublicClientApplication and HTTP client.
	/// </summary>
	public OneDriveStorageProvider(
		ISecureStorageService secureStorage,
		HttpClient? httpClient = null,
		string? clientId = null,
		ILogger<OneDriveStorageProvider>? logger = null)
	{
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		_logger = logger;

		string activeClientId = !string.IsNullOrWhiteSpace(clientId) ? clientId : DefaultClientId;

		// Initialize MSAL PublicClientApplication
		try
		{
			var builder = PublicClientApplicationBuilder.Create(activeClientId)
				.WithAuthority(AzureCloudInstance.AzurePublic, "common")
				.WithRedirectUri(GetPlatformRedirectUri(activeClientId));

#if IOS
			builder = builder.WithIosKeychainSecurityGroup("com.microsoft.adalcache");
#endif

			_pca = builder.Build();

			// Bind encrypted token cache persistence
			_pca.UserTokenCache.SetBeforeAccessAsync(OnBeforeTokenCacheAccessAsync);
			_pca.UserTokenCache.SetAfterAccessAsync(OnAfterTokenCacheAccessAsync);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Failed to initialize MSAL PublicClientApplication. Cloud sync will operate in mock/degraded mode.");
			_pca = null;
		}
	}

	/// <summary>
	/// Test / Custom constructor for injecting mock token providers and HTTP handlers.
	/// </summary>
	public OneDriveStorageProvider(
		ISecureStorageService secureStorage,
		HttpClient httpClient,
		Func<CancellationToken, Task<string?>> customTokenProvider,
		ILogger<OneDriveStorageProvider>? logger = null)
	{
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_customTokenProvider = customTokenProvider ?? throw new ArgumentNullException(nameof(customTokenProvider));
		_logger = logger;
		_pca = null;
	}

	/// <inheritdoc />
	public StorageProviderType ProviderType => StorageProviderType.OneDrive;

	/// <inheritdoc />
	public string DisplayName => "Microsoft OneDrive";

	/// <inheritdoc />
	public bool RequiresAuthentication => true;

	/// <summary>
	/// Determines platform-specific redirect URI for MSAL authentication.
	/// </summary>
	public static string GetPlatformRedirectUri(string clientId)
	{
#if ANDROID || IOS || MACCATALYST
		return $"msal{clientId}://auth";
#else
		return "https://login.microsoftonline.com/common/oauth2/nativeclient";
#endif
	}

	/// <inheritdoc />
	public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
	{
		string? token = await GetAccessTokenSilentlyAsync(cancellationToken).ConfigureAwait(false);
		return !string.IsNullOrWhiteSpace(token);
	}

	/// <inheritdoc />
	public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			// 1. Check if existing silent session is valid
			string? existingToken = await GetAccessTokenSilentlyAsync(cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(existingToken))
			{
				return true;
			}

			if (_customTokenProvider != null)
			{
				string? customToken = await _customTokenProvider(cancellationToken).ConfigureAwait(false);
				return !string.IsNullOrWhiteSpace(customToken);
			}

			if (_pca == null)
			{
				_logger?.LogWarning("MSAL PublicClientApplication is not initialized.");
				return false;
			}

			// 2. Interactive login prompt
			var interactiveBuilder = _pca.AcquireTokenInteractive(Scopes)
				.WithPrompt(Prompt.SelectAccount);

#if ANDROID
			if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity != null)
			{
				interactiveBuilder = interactiveBuilder.WithParentActivityOrWindow(Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
			}
#endif

			var authResult = await interactiveBuilder.ExecuteAsync(cancellationToken).ConfigureAwait(false);
			return !string.IsNullOrWhiteSpace(authResult?.AccessToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (MsalClientException mex) when (mex.ErrorCode == "authentication_canceled")
		{
			_logger?.LogInformation("User canceled OneDrive authentication.");
			return false;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "OneDrive interactive authentication failed.");
			return false;
		}
		finally
		{
			_authLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_pca != null)
			{
				var accounts = (await _pca.GetAccountsAsync().ConfigureAwait(false)).ToList();
				foreach (var account in accounts)
				{
					await _pca.RemoveAsync(account).ConfigureAwait(false);
				}
			}

			_secureStorage.Remove(TokenCacheKey);
			_logger?.LogInformation("Signed out from OneDrive and purged secure token cache.");
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error occurred during OneDrive sign-out.");
		}
		finally
		{
			_authLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			string? token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(token))
			{
				_logger?.LogWarning("Cannot read OneDrive payload: unauthenticated.");
				return null;
			}

			using var request = new HttpRequestMessage(HttpMethod.Get, GraphFileContentEndpoint);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				_logger?.LogDebug("OneDrive payload file not found (404) in approot. Initial run state.");
				return null;
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				_logger?.LogWarning("OneDrive access token rejected (401 Unauthorized). Resetting authentication.");
				_secureStorage.Remove(TokenCacheKey);
				return null;
			}

			if (!response.IsSuccessStatusCode)
			{
				_logger?.LogWarning("Failed to read OneDrive payload. HTTP Status: {StatusCode}", response.StatusCode);
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(json))
			{
				_logger?.LogWarning("OneDrive payload content is empty.");
				return null;
			}

			var payload = StoragePayload.FromJson(json);
			if (payload == null)
			{
				_logger?.LogWarning("Failed to deserialize OneDrive storage payload JSON.");
			}

			return payload;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Exception while reading storage payload from OneDrive.");
			return null;
		}
	}

	/// <inheritdoc />
	public async Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(payload);

		try
		{
			string? token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(token))
			{
				_logger?.LogWarning("Cannot write OneDrive payload: unauthenticated.");
				return false;
			}

			string json = payload.ToJson();
			using var request = new HttpRequestMessage(HttpMethod.Put, GraphFileContentEndpoint);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Content = new StringContent(json, Encoding.UTF8, "application/json");

			using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				_logger?.LogWarning("OneDrive upload rejected (401 Unauthorized). Clearing token cache.");
				_secureStorage.Remove(TokenCacheKey);
				return false;
			}

			if (response.IsSuccessStatusCode)
			{
				_logger?.LogDebug("Successfully saved payload to OneDrive approot ({StatusCode}).", response.StatusCode);
				return true;
			}

			string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			_logger?.LogError("OneDrive write payload failed. Status: {StatusCode}, Error: {ErrorBody}", response.StatusCode, errorBody);
			return false;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Exception while writing storage payload to OneDrive.");
			return false;
		}
	}

	/// <inheritdoc />
	public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			string? token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			using var request = new HttpRequestMessage(HttpMethod.Get, GraphAppRootEndpoint);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
			return response.IsSuccessStatusCode;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "OneDrive connection test failed.");
			return false;
		}
	}

	#region Helper & Token Management Methods

	private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
	{
		if (_customTokenProvider != null)
		{
			return await _customTokenProvider(cancellationToken).ConfigureAwait(false);
		}

		return await GetAccessTokenSilentlyAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<string?> GetAccessTokenSilentlyAsync(CancellationToken cancellationToken)
	{
		if (_customTokenProvider != null)
		{
			return await _customTokenProvider(cancellationToken).ConfigureAwait(false);
		}

		if (_pca == null) return null;

		try
		{
			var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
			var firstAccount = accounts.FirstOrDefault();
			if (firstAccount == null) return null;

			var result = await _pca.AcquireTokenSilent(Scopes, firstAccount)
				.ExecuteAsync(cancellationToken)
				.ConfigureAwait(false);

			return result.AccessToken;
		}
		catch (MsalUiRequiredException)
		{
			_logger?.LogDebug("MSAL requires interactive authentication.");
			return null;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Failed silent token acquisition.");
			return null;
		}
	}

	private async Task OnBeforeTokenCacheAccessAsync(TokenCacheNotificationArgs args)
	{
		try
		{
			string? base64 = await _secureStorage.GetAsync(TokenCacheKey).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(base64))
			{
				byte[] bytes = Convert.FromBase64String(base64);
				args.TokenCache.DeserializeMsalV3(bytes);
			}
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Failed to read MSAL token cache from secure storage.");
		}
	}

	private async Task OnAfterTokenCacheAccessAsync(TokenCacheNotificationArgs args)
	{
		if (args.HasStateChanged)
		{
			try
			{
				byte[] bytes = args.TokenCache.SerializeMsalV3();
				string base64 = Convert.ToBase64String(bytes);
				await _secureStorage.SetAsync(TokenCacheKey, base64).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to persist MSAL token cache to secure storage.");
			}
		}
	}

	#endregion
}
