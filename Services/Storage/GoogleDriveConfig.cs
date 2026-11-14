namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Configuration settings and secure storage keys for Google Drive OAuth 2.0 PKCE integration.
/// Supports multi-device and platform-specific credentials (Windows Desktop, Android/Pixel, iOS, etc.).
/// </summary>
public sealed partial class GoogleDriveConfig
{
    public GoogleDriveConfig()
    {
        ApplyLocalSecrets();
    }

    partial void ApplyLocalSecrets();
    // Common / Fallback Default Credentials (Configure with your own Google Cloud OAuth 2.0 credentials)
    public string ClientId { get; set; } = "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com";
    public string ClientSecret { get; set; } = "YOUR_GOOGLE_CLIENT_SECRET";
    public string RedirectUri { get; set; } = "mugoverflow://oauth2redirect";
    public string Scope { get; set; } = "https://www.googleapis.com/auth/drive.appdata";
    public bool IsMockMode { get; set; } = false;

    // Custom Overrides (configurable by user at runtime via the BYOS UI)
    public string? CustomClientId { get; set; }
    public string? CustomClientSecret { get; set; }

    // Platform-Specific Credentials (Windows Desktop vs Android Phone / Pixel)
    public string? WindowsClientId { get; set; } = "YOUR_GOOGLE_WINDOWS_CLIENT_ID.apps.googleusercontent.com";
    public string? WindowsClientSecret { get; set; } = "YOUR_GOOGLE_WINDOWS_CLIENT_SECRET";

    public string? AndroidClientId { get; set; } = "YOUR_GOOGLE_ANDROID_CLIENT_ID.apps.googleusercontent.com";
    public string? AndroidClientSecret { get; set; } = null;

    /// <summary>
    /// Dynamically resolves the active Client ID based on custom override or the executing platform.
    /// </summary>
    public string ActiveClientId
    {
        get
        {
            if (IsValidCredential(CustomClientId))
                return CustomClientId!;
#if ANDROID
            if (IsValidCredential(AndroidClientId))
                return AndroidClientId!;
#elif WINDOWS
            if (IsValidCredential(WindowsClientId))
                return WindowsClientId!;
#endif
            return IsValidCredential(ClientId) ? ClientId : string.Empty;
        }
    }

    /// <summary>
    /// Dynamically resolves the active Client Secret based on custom override or the executing platform.
    /// </summary>
    public string? ActiveClientSecret
    {
        get
        {
            if (IsValidCredential(CustomClientSecret))
                return CustomClientSecret;
#if ANDROID
            if (IsValidCredential(AndroidClientSecret))
                return AndroidClientSecret;
#elif WINDOWS
            if (IsValidCredential(WindowsClientSecret))
                return WindowsClientSecret;
#endif
            return IsValidCredential(ClientSecret) ? ClientSecret : null;
        }
    }

    private static bool IsValidCredential(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !value.StartsWith("YOUR_GOOGLE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads saved custom OAuth credentials from secure storage if present.
    /// </summary>
    public async Task LoadCustomCredentialsAsync(ISecureStorageService secureStorage)
    {
        ArgumentNullException.ThrowIfNull(secureStorage);
#if ANDROID
        var customId = await secureStorage.GetAsync(KeyCustomAndroidClientId).ConfigureAwait(false);
        var customSecret = await secureStorage.GetAsync(KeyCustomAndroidClientSecret).ConfigureAwait(false);
#else
		var customId = await secureStorage.GetAsync(KeyCustomWindowsClientId).ConfigureAwait(false);
		var customSecret = await secureStorage.GetAsync(KeyCustomWindowsClientSecret).ConfigureAwait(false);
#endif
        CustomClientId = string.IsNullOrWhiteSpace(customId) ? null : customId.Trim();
        CustomClientSecret = string.IsNullOrWhiteSpace(customSecret) ? null : customSecret.Trim();
    }

    /// <summary>
    /// Persists custom OAuth credentials into secure storage and invalidates active tokens.
    /// </summary>
    public async Task SaveCustomCredentialsAsync(ISecureStorageService secureStorage, string? clientId, string? clientSecret)
    {
        ArgumentNullException.ThrowIfNull(secureStorage);
        CustomClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        CustomClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();

#if ANDROID
        var idKey = KeyCustomAndroidClientId;
        var secretKey = KeyCustomAndroidClientSecret;
#else
		var idKey = KeyCustomWindowsClientId;
		var secretKey = KeyCustomWindowsClientSecret;
#endif

        if (CustomClientId != null)
            await secureStorage.SetAsync(idKey, CustomClientId).ConfigureAwait(false);
        else
            secureStorage.Remove(idKey);

        if (CustomClientSecret != null)
            await secureStorage.SetAsync(secretKey, CustomClientSecret).ConfigureAwait(false);
        else
            secureStorage.Remove(secretKey);

        // Invalidate existing tokens when credentials change
        secureStorage.Remove(KeyAccessToken);
        secureStorage.Remove(KeyRefreshToken);
        secureStorage.Remove(KeyTokenExpiry);
        secureStorage.Remove(KeyFileId);
    }

    /// <summary>
    /// Clears all custom credentials and invalidates active tokens, returning to built-in defaults.
    /// </summary>
    public async Task ResetToDefaultsAsync(ISecureStorageService secureStorage)
    {
        ArgumentNullException.ThrowIfNull(secureStorage);
        CustomClientId = null;
        CustomClientSecret = null;
#if ANDROID
        secureStorage.Remove(KeyCustomAndroidClientId);
        secureStorage.Remove(KeyCustomAndroidClientSecret);
#else
		secureStorage.Remove(KeyCustomWindowsClientId);
		secureStorage.Remove(KeyCustomWindowsClientSecret);
#endif
        secureStorage.Remove(KeyAccessToken);
        secureStorage.Remove(KeyRefreshToken);
        secureStorage.Remove(KeyTokenExpiry);
        secureStorage.Remove(KeyFileId);
        await Task.CompletedTask;
    }

    public const string FolderName = "MugOverflow-Caffeine-consumption";
    public const string FileName = "caffeine_consumption.json";

    public const string KeyAccessToken = "google_drive_access_token";
    public const string KeyRefreshToken = "google_drive_refresh_token";
    public const string KeyTokenExpiry = "google_drive_token_expiry_utc";
    public const string KeyFolderId = "google_drive_folder_id";
    public const string KeyFileId = "google_drive_file_id";

    public const string KeyCustomWindowsClientId = "google_drive_custom_windows_client_id";
    public const string KeyCustomWindowsClientSecret = "google_drive_custom_windows_client_secret";
    public const string KeyCustomAndroidClientId = "google_drive_custom_android_client_id";
    public const string KeyCustomAndroidClientSecret = "google_drive_custom_android_client_secret";
}
