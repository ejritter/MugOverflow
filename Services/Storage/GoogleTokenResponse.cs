using System.Text.Json.Serialization;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Model for Google OAuth 2.0 token response payload.
/// </summary>
internal sealed class GoogleTokenResponse
{
	[JsonPropertyName("access_token")]
	public string AccessToken { get; set; } = string.Empty;

	[JsonPropertyName("refresh_token")]
	public string? RefreshToken { get; set; }

	[JsonPropertyName("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonPropertyName("token_type")]
	public string? TokenType { get; set; }

	[JsonPropertyName("scope")]
	public string? Scope { get; set; }
}
