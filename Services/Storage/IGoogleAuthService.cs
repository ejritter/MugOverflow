namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Platform-agnostic contract for Google OAuth authentication and token lifecycle.
/// Supports native Google Play Services on Android, loopback HttpListener on Windows Desktop,
/// and headless mock implementations in unit test suites.
/// </summary>
public interface IGoogleAuthService
{
	/// <summary>
	/// Initiates interactive or silent authentication to acquire an initial access token.
	/// </summary>
	Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves an active, unexpired OAuth 2.0 access token, performing silent refresh if necessary.
	/// </summary>
	Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Signs out the active user and revokes credentials locally and remotely.
	/// </summary>
	Task SignOutAsync(CancellationToken cancellationToken = default);
}
