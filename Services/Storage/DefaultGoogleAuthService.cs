using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Authentication;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Default / Fallback cross-platform Google OAuth service using WebAuthenticator and PKCE.
/// Used on iOS, MacCatalyst, or environments where platform-specific SDKs are not active.
/// </summary>
public sealed class DefaultGoogleAuthService : IGoogleAuthService
{
	private readonly HttpClient _httpClient;
	private readonly ISecureStorageService _secureStorage;
	private readonly IWebAuthenticator _webAuthenticator;
	private readonly GoogleDriveConfig _config;
	private readonly ILogger<DefaultGoogleAuthService>? _logger;
	private readonly SemaphoreSlim _authLock = new(1, 1);

	public DefaultGoogleAuthService(
		HttpClient httpClient,
		ISecureStorageService secureStorage,
		IWebAuthenticator? webAuthenticator = null,
		GoogleDriveConfig? config = null,
		ILogger<DefaultGoogleAuthService>? logger = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_webAuthenticator = webAuthenticator ?? WebAuthenticator.Default;
		_config = config ?? new GoogleDriveConfig();
		_logger = logger;
	}

	public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			string codeVerifier = GenerateCodeVerifier();
			string codeChallenge = GenerateCodeChallenge(codeVerifier);
			string state = Guid.NewGuid().ToString("N");

			var authUrlBuilder = new StringBuilder("https://accounts.google.com/o/oauth2/v2/auth?");
			authUrlBuilder.Append($"client_id={Uri.EscapeDataString(_config.ActiveClientId)}");
			authUrlBuilder.Append("&response_type=code");
			authUrlBuilder.Append($"&redirect_uri={Uri.EscapeDataString(_config.RedirectUri)}");
			authUrlBuilder.Append($"&scope={Uri.EscapeDataString(_config.Scope)}");
			authUrlBuilder.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge)}");
			authUrlBuilder.Append("&code_challenge_method=S256");
			authUrlBuilder.Append($"&state={Uri.EscapeDataString(state)}");
			authUrlBuilder.Append("&access_type=offline");
			authUrlBuilder.Append("&prompt=consent");

			var authUri = new Uri(authUrlBuilder.ToString());
			var callbackUri = new Uri(_config.RedirectUri);

			var authResult = await _webAuthenticator.AuthenticateAsync(authUri, callbackUri).ConfigureAwait(false);

			if (authResult == null || !authResult.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
			{
				_logger?.LogWarning("OAuth PKCE failed: missing authorization code in callback.");
				return null;
			}

			if (authResult.Properties.TryGetValue("state", out var returnedState) && returnedState != state)
			{
				_logger?.LogWarning("OAuth PKCE CSRF state mismatch.");
				return null;
			}

			return await ExchangeCodeForTokensAsync(code, codeVerifier, _config.RedirectUri, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Authentication failed for DefaultGoogleAuthService.");
			return null;
		}
		finally
		{
			_authLock.Release();
		}
	}

	public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var expiryStr = await _secureStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry).ConfigureAwait(false);
		var accessToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);

		if (!string.IsNullOrWhiteSpace(accessToken) &&
		    DateTimeOffset.TryParse(expiryStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiryOffset) &&
		    expiryOffset > DateTimeOffset.UtcNow.AddMinutes(2))
		{
			return accessToken;
		}

		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var recheckExpiry = await _secureStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry).ConfigureAwait(false);
			var recheckAccess = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);

			if (!string.IsNullOrWhiteSpace(recheckAccess) &&
			    DateTimeOffset.TryParse(recheckExpiry, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var recheckOffset) &&
			    recheckOffset > DateTimeOffset.UtcNow.AddMinutes(2))
			{
				return recheckAccess;
			}

			var refreshToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyRefreshToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(refreshToken))
			{
				return null;
			}

			return await RefreshAccessTokenInternalAsync(refreshToken, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_authLock.Release();
		}
	}

	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var token = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(token))
			{
				try
				{
					using var req = new HttpRequestMessage(HttpMethod.Post, $"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(token)}");
					await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					_logger?.LogWarning(ex, "Failed to revoke Google token on sign out.");
				}
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

	private async Task<string?> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>
		{
			["client_id"] = _config.ActiveClientId,
			["code"] = code,
			["code_verifier"] = codeVerifier,
			["grant_type"] = "authorization_code",
			["redirect_uri"] = redirectUri
		};

		if (!string.IsNullOrWhiteSpace(_config.ActiveClientSecret))
		{
			form["client_secret"] = _config.ActiveClientSecret;
		}

		using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
		{
			Content = new FormUrlEncodedContent(form)
		};

		using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
		if (!res.IsSuccessStatusCode)
		{
			string errorBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			_logger?.LogError("Token exchange failed. StatusCode={StatusCode}, Body={Body}", res.StatusCode, errorBody);
			return null;
		}

		var tokenResponse = await res.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
		if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
		{
			return null;
		}

		await SaveTokenResponseAsync(tokenResponse).ConfigureAwait(false);
		return tokenResponse.AccessToken;
	}

	private async Task<string?> RefreshAccessTokenInternalAsync(string refreshToken, CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>
		{
			["client_id"] = _config.ActiveClientId,
			["refresh_token"] = refreshToken,
			["grant_type"] = "refresh_token"
		};

		if (!string.IsNullOrWhiteSpace(_config.ActiveClientSecret))
		{
			form["client_secret"] = _config.ActiveClientSecret;
		}

		using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
		{
			Content = new FormUrlEncodedContent(form)
		};

		using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
		if (!res.IsSuccessStatusCode)
		{
			if (res.StatusCode == HttpStatusCode.BadRequest || res.StatusCode == HttpStatusCode.Unauthorized)
			{
				_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
				_secureStorage.Remove(GoogleDriveConfig.KeyRefreshToken);
				_secureStorage.Remove(GoogleDriveConfig.KeyTokenExpiry);
			}
			return null;
		}

		try
		{
			var tokenResponse = await res.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
			if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
			{
				return null;
			}

			await SaveTokenResponseAsync(tokenResponse, refreshToken).ConfigureAwait(false);
			return tokenResponse.AccessToken;
		}
		catch
		{
			return null;
		}
	}

	private async Task SaveTokenResponseAsync(GoogleTokenResponse tokenResponse, string? fallbackRefreshToken = null)
	{
		await _secureStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, tokenResponse.AccessToken).ConfigureAwait(false);

		string refresh = !string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) ? tokenResponse.RefreshToken : fallbackRefreshToken ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(refresh))
		{
			await _secureStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, refresh).ConfigureAwait(false);
		}

		var expiryUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600);
		await _secureStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, expiryUtc.ToString("O")).ConfigureAwait(false);
	}

	private static string GenerateCodeVerifier()
	{
		byte[] bytes = new byte[32];
		RandomNumberGenerator.Fill(bytes);
		return Base64UrlEncode(bytes);
	}

	private static string GenerateCodeChallenge(string codeVerifier)
	{
		byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
		return Base64UrlEncode(hash);
	}

	private static string Base64UrlEncode(byte[] input)
	{
		return Convert.ToBase64String(input)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}
}
