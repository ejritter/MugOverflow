#if WINDOWS
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MugOverflowProject.Services.Storage;

namespace MugOverflowProject.Platforms.Windows;

/// <summary>
/// Windows Desktop Google OAuth implementation using local loopback HttpListener and PKCE.
/// </summary>
public sealed class WindowsGoogleAuthService : IGoogleAuthService
{
	private readonly HttpClient _httpClient;
	private readonly ISecureStorageService _secureStorage;
	private readonly GoogleDriveConfig _config;
	private readonly ILogger<WindowsGoogleAuthService>? _logger;
	private readonly SemaphoreSlim _authLock = new(1, 1);

	public WindowsGoogleAuthService(
		HttpClient httpClient,
		ISecureStorageService secureStorage,
		GoogleDriveConfig? config = null,
		ILogger<WindowsGoogleAuthService>? logger = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_config = config ?? new GoogleDriveConfig();
		_logger = logger;
	}

	public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _config.LoadCustomCredentialsAsync(_secureStorage).ConfigureAwait(false);

			string codeVerifier = GenerateCodeVerifier();
			string codeChallenge = GenerateCodeChallenge(codeVerifier);
			string state = Guid.NewGuid().ToString("N");

			int port = GetRandomUnusedPort();
			string redirectUri = $"http://127.0.0.1:{port}/";

			using var listener = new HttpListener();
			listener.Prefixes.Add(redirectUri);

			try
			{
				listener.Start();
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to start HttpListener for OAuth loopback redirect on {RedirectUri}", redirectUri);
				return null;
			}

			var authUrlBuilder = new StringBuilder("https://accounts.google.com/o/oauth2/v2/auth?");
			authUrlBuilder.Append($"client_id={Uri.EscapeDataString(_config.ActiveClientId)}");
			authUrlBuilder.Append("&response_type=code");
			authUrlBuilder.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri)}");
			authUrlBuilder.Append($"&scope={Uri.EscapeDataString(_config.Scope)}");
			authUrlBuilder.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge)}");
			authUrlBuilder.Append("&code_challenge_method=S256");
			authUrlBuilder.Append($"&state={Uri.EscapeDataString(state)}");
			authUrlBuilder.Append("&access_type=offline");
			authUrlBuilder.Append("&prompt=consent");

			string authUrl = authUrlBuilder.ToString();

			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = authUrl,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to launch default browser for Google OAuth.");
				listener.Stop();
				return null;
			}

			try
			{
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				cts.CancelAfter(TimeSpan.FromMinutes(2));

				var getContextTask = listener.GetContextAsync();
				var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
				var completedTask = await Task.WhenAny(getContextTask, delayTask).ConfigureAwait(false);

				if (completedTask == getContextTask)
				{
					await cts.CancelAsync().ConfigureAwait(false);
				}
				else
				{
					_logger?.LogWarning("Google Drive OAuth timed out or was cancelled by user.");
					_ = getContextTask.ContinueWith(
						t => _ = t.Exception,
						CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Default);
					listener.Stop();
					return null;
				}

				var context = await getContextTask.ConfigureAwait(false);
				var query = context.Request.QueryString;

				string? code = query["code"];
				string? returnedState = query["state"];
				string? error = query["error"];

				string responseHtml = string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(code)
					? "<html><body style='font-family: sans-serif; text-align: center; padding-top: 50px;'><h2>Authentication Successful!</h2><p>You can close this tab and return to MugOverflow.</p></body></html>"
					: $"<html><body style='font-family: sans-serif; text-align: center; padding-top: 50px; color: red;'><h2>Authentication Failed</h2><p>{error ?? "Unknown error"}</p></body></html>";

				byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
				context.Response.ContentLength64 = buffer.Length;
				context.Response.ContentType = "text/html; charset=utf-8";
				await context.Response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
				context.Response.OutputStream.Close();

				if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || returnedState != state)
				{
					_logger?.LogWarning("OAuth loopback failed. Error={Error}, StateMatch={StateMatch}", error, returnedState == state);
					return null;
				}

				return await ExchangeCodeForTokensAsync(code, codeVerifier, redirectUri, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				try
				{
					listener.Stop();
				}
				catch
				{
					// Ignore listener cleanup exceptions
				}
			}
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

	private static int GetRandomUnusedPort()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.LocalEndPoint!).Port;
	}
}
#endif
