using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Authentication;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Storage provider for Google Drive using zero-backend OAuth 2.0 PKCE and Drive v3 REST API.
/// Manages file 'caffeine_consumption.json' directly within Google Drive's isolated application data folder ('appDataFolder').
/// Includes concurrency synchronization on token refresh and automatic stale cache eviction on HTTP 401/404.
/// Supports both native MAUI WebAuthenticator (Mobile) and loopback HttpListener (Windows Desktop).
/// </summary>
public sealed class GoogleDriveStorageProvider : IStorageProvider
{
	private readonly HttpClient _httpClient;
	private readonly ISecureStorageService _secureStorage;
	private readonly IGoogleAuthService? _googleAuthService;
	private readonly IWebAuthenticator _webAuthenticator;
	private readonly GoogleDriveConfig _config;
	private readonly ILogger<GoogleDriveStorageProvider>? _logger;
	private readonly Func<CancellationToken, Task<string?>>? _customTokenProvider;
	private readonly SemaphoreSlim _authLock = new(1, 1);

	private StoragePayload? _mockPayload; // Headless / mock mode memory store

	public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;
	public string DisplayName => "Google Drive";
	public bool RequiresAuthentication => true;

	public GoogleDriveStorageProvider(
		HttpClient httpClient,
		ISecureStorageService secureStorage,
		IGoogleAuthService? googleAuthService = null,
		IWebAuthenticator? webAuthenticator = null,
		GoogleDriveConfig? config = null,
		ILogger<GoogleDriveStorageProvider>? logger = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_googleAuthService = googleAuthService;
		_webAuthenticator = webAuthenticator ?? WebAuthenticator.Default;
		_config = config ?? new GoogleDriveConfig();
		_logger = logger;
	}

	public GoogleDriveStorageProvider(
		HttpClient httpClient,
		ISecureStorageService secureStorage,
		Func<CancellationToken, Task<string?>> customTokenProvider,
		GoogleDriveConfig? config = null,
		ILogger<GoogleDriveStorageProvider>? logger = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
		_customTokenProvider = customTokenProvider ?? throw new ArgumentNullException(nameof(customTokenProvider));
		_googleAuthService = null;
		_webAuthenticator = WebAuthenticator.Default;
		_config = config ?? new GoogleDriveConfig();
		_logger = logger;
	}

	public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (IsMockModeActive())
		{
			var mockToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);
			return !string.IsNullOrWhiteSpace(mockToken);
		}

		var token = await GetValidAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		return !string.IsNullOrWhiteSpace(token);
	}

	public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _config.LoadCustomCredentialsAsync(_secureStorage).ConfigureAwait(false);

			if (IsMockModeActive())
			{
				_logger?.LogInformation("GoogleDriveStorageProvider: Operating in mock/fallback mode.");
				await _secureStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, "mock_google_access_token").ConfigureAwait(false);
				await _secureStorage.SetAsync(GoogleDriveConfig.KeyRefreshToken, "mock_google_refresh_token").ConfigureAwait(false);
				await _secureStorage.SetAsync(GoogleDriveConfig.KeyTokenExpiry, DateTime.UtcNow.AddHours(1).ToString("O")).ConfigureAwait(false);
				return true;
			}

			if (_customTokenProvider != null)
			{
				var customToken = await _customTokenProvider(cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(customToken))
				{
					await _secureStorage.SetAsync(GoogleDriveConfig.KeyAccessToken, customToken).ConfigureAwait(false);
					return true;
				}
				return false;
			}

			if (_googleAuthService != null)
			{
				string? token = await _googleAuthService.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
				return !string.IsNullOrWhiteSpace(token);
			}

			var fallbackAuth = new DefaultGoogleAuthService(_httpClient, _secureStorage, _webAuthenticator, _config);
			string? fallbackToken = await fallbackAuth.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
			return !string.IsNullOrWhiteSpace(fallbackToken);
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
			if (_googleAuthService != null && !IsMockModeActive() && _customTokenProvider == null)
			{
				await _googleAuthService.SignOutAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				var token = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(token) && !IsMockModeActive())
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

			_secureStorage.Remove(GoogleDriveConfig.KeyFolderId);
			_secureStorage.Remove(GoogleDriveConfig.KeyFileId);
			_mockPayload = null;
		}
		finally
		{
			_authLock.Release();
		}
	}

	public async Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default)
	{
		if (IsMockModeActive())
		{
			_logger?.LogInformation("GoogleDriveStorageProvider: Reading payload from in-memory mock store.");
			return _mockPayload;
		}

		var token = await GetValidAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(token))
		{
			_logger?.LogWarning("Cannot read payload: Google Drive is not authenticated.");
			return null;
		}

		var (searchSuccess, fileId) = await FindFileIdAsync(token, cancellationToken).ConfigureAwait(false);
		if (!searchSuccess)
		{
			_logger?.LogWarning("Cannot read payload: Failed to query Google Drive.");
			return null;
		}

		if (string.IsNullOrWhiteSpace(fileId))
		{
			_logger?.LogInformation("No existing caffeine_consumption.json found on Google Drive. Returning empty payload.");
			return new StoragePayload { DecisionLogs = [] };
		}

		using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		if (res.StatusCode == HttpStatusCode.Unauthorized)
		{
			_logger?.LogWarning("Google Drive returned 401 Unauthorized for ReadPayloadAsync. Evicting access token.");
			_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
			return null;
		}

		if (res.StatusCode == HttpStatusCode.NotFound)
		{
			_logger?.LogWarning("Google Drive returned 404 for file ID {FileId}. Evicting stale cache.", fileId);
			_secureStorage.Remove(GoogleDriveConfig.KeyFileId);
			return null;
		}

		if (!res.IsSuccessStatusCode)
		{
			_logger?.LogError("Failed to download file from Google Drive. StatusCode={StatusCode}", res.StatusCode);
			return null;
		}

		await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		if (stream == null || (stream.CanSeek && stream.Length == 0))
		{
			return new StoragePayload { DecisionLogs = [] };
		}

		try
		{
			return await JsonSerializer.DeserializeAsync<StoragePayload>(stream, StoragePayload.JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		catch (JsonException ex)
		{
			_logger?.LogError(ex, "Failed to deserialize storage payload from Google Drive.");
			return null;
		}
	}

	public async Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(payload);

		if (IsMockModeActive())
		{
			_logger?.LogInformation("GoogleDriveStorageProvider: Writing payload to in-memory mock store.");
			_mockPayload = payload;
			return true;
		}

		var token = await GetValidAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(token))
		{
			_logger?.LogWarning("Cannot write payload: Google Drive is not authenticated.");
			return false;
		}

		var (searchSuccess, fileId) = await FindFileIdAsync(token, cancellationToken).ConfigureAwait(false);
		if (!searchSuccess)
		{
			_logger?.LogWarning("Cannot write payload: Failed to query Google Drive.");
			return false;
		}

		var memoryStream = new MemoryStream();
		await JsonSerializer.SerializeAsync(memoryStream, payload, StoragePayload.JsonOptions, cancellationToken).ConfigureAwait(false);
		memoryStream.Position = 0;

		using var content = new StreamContent(memoryStream);
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

		if (string.IsNullOrWhiteSpace(fileId))
		{
			fileId = await CreateFileMetadataAsync(token, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(fileId))
			{
				_logger?.LogError("Failed to create file on Google Drive.");
				return false;
			}
		}

		using var uploadReq = new HttpRequestMessage(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media")
		{
			Content = content
		};
		uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var uploadRes = await _httpClient.SendAsync(uploadReq, cancellationToken).ConfigureAwait(false);
		if (uploadRes.StatusCode == HttpStatusCode.Unauthorized)
		{
			_logger?.LogWarning("Google Drive returned 401 Unauthorized for WritePayloadAsync. Evicting access token.");
			_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
			return false;
		}

		if (uploadRes.StatusCode == HttpStatusCode.NotFound)
		{
			_logger?.LogWarning("Uploading content failed with 404 (file ID {FileId} invalid/deleted). Evicting file ID cache and retrying.", fileId);
			_secureStorage.Remove(GoogleDriveConfig.KeyFileId);

			string? newFileId = await CreateFileMetadataAsync(token, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(newFileId))
			{
				_secureStorage.Remove(GoogleDriveConfig.KeyFolderId);
				return false;
			}

			memoryStream.Position = 0;
			using var retryContent = new StreamContent(memoryStream);
			retryContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

			using var retryUploadReq = new HttpRequestMessage(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{newFileId}?uploadType=media")
			{
				Content = retryContent
			};
			retryUploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var retryUploadRes = await _httpClient.SendAsync(retryUploadReq, cancellationToken).ConfigureAwait(false);
			if (retryUploadRes.StatusCode == HttpStatusCode.NotFound)
			{
				_secureStorage.Remove(GoogleDriveConfig.KeyFileId);
				_secureStorage.Remove(GoogleDriveConfig.KeyFolderId);
				return false;
			}
			return retryUploadRes.IsSuccessStatusCode;
		}

		if (!uploadRes.IsSuccessStatusCode)
		{
			_logger?.LogError("Failed to upload content to Google Drive. StatusCode={StatusCode}", uploadRes.StatusCode);
			return false;
		}

		return true;
	}

	public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
	{
		if (IsMockModeActive()) return true;

		var token = await GetValidAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(token)) return false;

		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user");
			req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
			if (res.StatusCode == HttpStatusCode.Unauthorized)
			{
				_logger?.LogWarning("Google Drive TestConnectionAsync returned 401. Evicting access token.");
				_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
				return false;
			}
			return res.IsSuccessStatusCode;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Google Drive TestConnectionAsync failed.");
			return false;
		}
	}

	// --- Private Helpers ---

	private bool IsMockModeActive()
	{
		return _config.IsMockMode ||
		       string.IsNullOrWhiteSpace(_config.ActiveClientId) ||
		       string.Equals(_config.ActiveClientId, "mock", StringComparison.OrdinalIgnoreCase);
	}

	private async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_customTokenProvider != null)
		{
			return await _customTokenProvider(cancellationToken).ConfigureAwait(false);
		}

		if (_googleAuthService != null && !IsMockModeActive())
		{
			return await _googleAuthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		}

		var expiryStr = await _secureStorage.GetAsync(GoogleDriveConfig.KeyTokenExpiry).ConfigureAwait(false);
		var accessToken = await _secureStorage.GetAsync(GoogleDriveConfig.KeyAccessToken).ConfigureAwait(false);

		if (!string.IsNullOrWhiteSpace(accessToken) &&
		    DateTimeOffset.TryParse(expiryStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiryOffset) &&
		    expiryOffset > DateTimeOffset.UtcNow.AddMinutes(2))
		{
			return accessToken;
		}

		// Refresh token with double-checked locking
		await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			// Recheck inside lock
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

	private async Task<string?> RefreshAccessTokenInternalAsync(string refreshToken, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

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
			_logger?.LogWarning("Silent token refresh failed. StatusCode={StatusCode}", res.StatusCode);
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

	private async Task<string?> CreateFileMetadataAsync(string token, CancellationToken cancellationToken)
	{
		var metaContent = new StringContent(
			JsonSerializer.Serialize(new { name = GoogleDriveConfig.FileName, parents = new[] { "appDataFolder" } }),
			Encoding.UTF8,
			"application/json");

		using var createReq = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files")
		{
			Content = metaContent
		};
		createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var createRes = await _httpClient.SendAsync(createReq, cancellationToken).ConfigureAwait(false);
		if (createRes.StatusCode == HttpStatusCode.NotFound)
		{
			_logger?.LogWarning("Creating file metadata failed with 404. Evicting file ID cache.");
			_secureStorage.Remove(GoogleDriveConfig.KeyFolderId);
			_secureStorage.Remove(GoogleDriveConfig.KeyFileId);
			return null;
		}

		if (!createRes.IsSuccessStatusCode)
		{
			_logger?.LogError("Failed to create file metadata on Google Drive. StatusCode={StatusCode}", createRes.StatusCode);
			return null;
		}

		var createResult = await createRes.Content.ReadFromJsonAsync<GoogleDriveItemResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
		var fileId = createResult?.Id;

		if (string.IsNullOrWhiteSpace(fileId))
		{
			_logger?.LogError("Google Drive returned empty file ID upon file creation.");
			return null;
		}

		await _secureStorage.SetAsync(GoogleDriveConfig.KeyFileId, fileId).ConfigureAwait(false);
		return fileId;
	}

	private async Task<(bool success, string? fileId)> FindFileIdAsync(string token, CancellationToken cancellationToken)
	{
		var cachedFileId = await _secureStorage.GetAsync(GoogleDriveConfig.KeyFileId).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(cachedFileId))
		{
			return (true, cachedFileId);
		}

		string query = $"name = '{GoogleDriveConfig.FileName}' and trashed = false";
		using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={Uri.EscapeDataString(query)}&fields=files(id,name)");
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
		if (res.StatusCode == HttpStatusCode.NotFound)
		{
			_logger?.LogWarning("Searching for file in appDataFolder returned 404. Invalidating cached file ID.");
			_secureStorage.Remove(GoogleDriveConfig.KeyFolderId);
			_secureStorage.Remove(GoogleDriveConfig.KeyFileId);
			return (true, null);
		}

		if (res.StatusCode == HttpStatusCode.Unauthorized)
		{
			_logger?.LogWarning("Google Drive returned 401 Unauthorized for file search. Evicting access token.");
			_secureStorage.Remove(GoogleDriveConfig.KeyAccessToken);
			return (false, null);
		}

		if (!res.IsSuccessStatusCode)
		{
			_logger?.LogError("Failed to search for file on Google Drive. StatusCode={StatusCode}", res.StatusCode);
			return (false, null);
		}

		var list = await res.Content.ReadFromJsonAsync<GoogleDriveFileListResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
		var file = list?.Files?.FirstOrDefault();
		if (file != null && !string.IsNullOrWhiteSpace(file.Id))
		{
			await _secureStorage.SetAsync(GoogleDriveConfig.KeyFileId, file.Id).ConfigureAwait(false);
			return (true, file.Id);
		}

		return (true, null);
	}

	private sealed class GoogleDriveFileListResult
	{
		[JsonPropertyName("files")]
		public List<GoogleDriveItemResult>? Files { get; set; }
	}

	private sealed class GoogleDriveItemResult
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = string.Empty;

		[JsonPropertyName("name")]
		public string Name { get; set; } = string.Empty;
	}
}
