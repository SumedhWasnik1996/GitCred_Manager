using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitCredMan.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

// ────────────────────────────────────────────────────────────
//  OAuth provider configuration
// ────────────────────────────────────────────────────────────

/// <summary>
/// Per-host OAuth endpoints and client IDs.
/// Register your own OAuth Apps at:
///   GitHub  → https://github.com/settings/developers
///   GitLab  → https://gitlab.com/-/profile/applications
///   Bitbucket → https://bitbucket.org/account/settings/app-passwords/
/// </summary>
public sealed record OAuthProvider(
	string Host,
	string ClientId,
	string DeviceCodeUrl,
	string TokenUrl,
	string Scope,
	string VerificationUriFallback)
{
	/// <summary>Well-known providers. Add enterprise hosts as needed.</summary>
	public static readonly IReadOnlyList<OAuthProvider> KnownProviders =
	[
		new OAuthProvider(
			Host:                    "github.com",
            // Replace with your GitHub OAuth App client_id
            // (Settings → Developer settings → OAuth Apps → New OAuth App)
            // Set Homepage URL + Callback URL to anything (device flow doesn't redirect)
            ClientId:                "Iv23liXAYb9j6PWQu4l6",
			DeviceCodeUrl:           "https://github.com/login/device/code",
			TokenUrl:                "https://github.com/login/oauth/access_token",
			Scope:                   "repo,read:user,user:email",
			VerificationUriFallback: "https://github.com/login/device/activate"),

		new OAuthProvider(
			Host:                    "gitlab.com",
            // Replace with your GitLab OAuth App client_id
            // (User Settings → Applications → Add new application)
            // Scopes: api, read_user   Redirect URI: http://localhost (not used)
            ClientId:                "YOUR_GITLAB_OAUTH_APP_CLIENT_ID",
			DeviceCodeUrl:           "https://gitlab.com/oauth/authorize_device",
			TokenUrl:                "https://gitlab.com/oauth/token",
			Scope:                   "api read_user",
			VerificationUriFallback: "https://gitlab.com/-/profile/personal_access_tokens"),
	];

	public static OAuthProvider? For(string host) =>
		KnownProviders.FirstOrDefault(p =>
			p.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
}

// ────────────────────────────────────────────────────────────
//  Result types
// ────────────────────────────────────────────────────────────

public sealed record OAuthTokenResult(
	bool Success,
	string? AccessToken = null,
	string? RefreshToken = null,
	string? Username = null,
	string? Email = null,
	string? Error = null)
{
	public static OAuthTokenResult Fail(string error) => new(false, Error: error);
	public static OAuthTokenResult Ok(string access, string? refresh, string? username, string? email) =>
		new(true, access, refresh, username, email);
}

public sealed record DeviceCodeResponse(
	string DeviceCode,
	string UserCode,
	string VerificationUri,
	int ExpiresIn,
	int Interval);

// ────────────────────────────────────────────────────────────
//  OAuthService
// ────────────────────────────────────────────────────────────

/// <summary>
/// Implements RFC 8628 Device Authorization Grant for GitHub and GitLab.
/// No redirect server required — the user enters a code in their browser.
///
/// Typical flow:
///   1. Call <see cref="StartDeviceFlowAsync"/> → get DeviceCodeResponse
///   2. Show UserCode to user and open VerificationUri in browser
///   3. Call <see cref="PollForTokenAsync"/> — it polls until success/timeout/cancel
///   4. Store the returned AccessToken via ICredentialStore
/// </summary>
public sealed class OAuthService
{
	private static readonly HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(30),
	};

	private readonly ILogger<OAuthService> _log;

	public OAuthService(ILogger<OAuthService> log) => _log = log;

	// ── Step 1: Request device + user codes ──────────────────

	/// <summary>
	/// Posts to the provider's device-code endpoint and returns the codes
	/// the user must enter in their browser, plus polling parameters.
	/// </summary>
	public async Task<(DeviceCodeResponse? Response, string? Error)> StartDeviceFlowAsync(
		OAuthProvider provider,
		CancellationToken ct = default)
	{
		try
		{
			var req = new HttpRequestMessage(HttpMethod.Post, provider.DeviceCodeUrl);
			req.Headers.Accept.ParseAdd("application/json");
			req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["client_id"] = provider.ClientId,
				["scope"] = provider.Scope,
			});

			var resp = await _http.SendAsync(req, ct);
			var json = await resp.Content.ReadAsStringAsync(ct);
			_log.LogDebug("Device code response ({status}): {json}", (int)resp.StatusCode, json);

			if (!resp.IsSuccessStatusCode)
				return (null, $"Device code request failed ({(int)resp.StatusCode}): {json}");

			var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			// GitHub uses snake_case; GitLab matches RFC 8628 exactly
			var deviceCode = GetString(root, "device_code") ?? string.Empty;
			var userCode = GetString(root, "user_code") ?? string.Empty;
			var verificationUri = GetString(root, "verification_uri")
							   ?? GetString(root, "verification_url")  // GitHub uses _url
							   ?? provider.VerificationUriFallback;
			var expiresIn = GetInt(root, "expires_in") ?? 900;
			var interval = GetInt(root, "interval") ?? 5;

			if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode))
				return (null, $"Unexpected device code response: {json}");

			return (new DeviceCodeResponse(deviceCode, userCode, verificationUri, expiresIn, interval), null);
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "StartDeviceFlowAsync failed");
			return (null, ex.Message);
		}
	}

	// ── Step 2: Poll for token ────────────────────────────────

	/// <summary>
	/// Polls the token endpoint until the user authorises (or it times out / is cancelled).
	/// Reports progress strings to the caller for display in the UI.
	/// </summary>
	public async Task<OAuthTokenResult> PollForTokenAsync(
		OAuthProvider provider,
		DeviceCodeResponse deviceCode,
		IProgress<string>? progress = null,
		CancellationToken ct = default)
	{
		var deadline = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn);
		var intervalSec = Math.Max(deviceCode.Interval, 5);   // RFC 8628 § 3.5 min 5 s
		int attempt = 0;

		progress?.Report($"Waiting for browser authorisation…");

		while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
			attempt++;

			try
			{
				var req = new HttpRequestMessage(HttpMethod.Post, provider.TokenUrl);
				req.Headers.Accept.ParseAdd("application/json");
				req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["client_id"] = provider.ClientId,
					["device_code"] = deviceCode.DeviceCode,
					["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
				});

				var resp = await _http.SendAsync(req, ct);
				var json = await resp.Content.ReadAsStringAsync(ct);
				_log.LogDebug("Poll attempt {n} ({status}): {json}", attempt, (int)resp.StatusCode, json);

				var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				// Success
				var accessToken = GetString(root, "access_token");
				if (!string.IsNullOrEmpty(accessToken))
				{
					var refreshToken = GetString(root, "refresh_token");
					progress?.Report("Authorised! Fetching account details…");
					var (username, email) = await FetchUserInfoAsync(provider, accessToken, ct);
					return OAuthTokenResult.Ok(accessToken, refreshToken, username, email);
				}

				// RFC 8628 error codes
				var error = GetString(root, "error");
				switch (error)
				{
					case "authorization_pending":
						progress?.Report($"Waiting… (enter code at {deviceCode.VerificationUri})");
						continue;

					case "slow_down":
						intervalSec += 5;   // server asked us to back off
						continue;

					case "expired_token":
						return OAuthTokenResult.Fail("The authorisation code expired. Please try again.");

					case "access_denied":
						return OAuthTokenResult.Fail("Access was denied by the user.");

					default:
						var errDesc = GetString(root, "error_description") ?? error ?? json;
						return OAuthTokenResult.Fail($"Authorisation error: {errDesc}");
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				return OAuthTokenResult.Fail("Cancelled.");
			}
			catch (Exception ex)
			{
				_log.LogWarning(ex, "Poll attempt {n} threw", attempt);
				// Network hiccup — keep trying until deadline
			}
		}

		return OAuthTokenResult.Fail(ct.IsCancellationRequested
			? "Cancelled."
			: "Timed out waiting for browser authorisation.");
	}

	// ── Refresh token ─────────────────────────────────────────

	/// <summary>
	/// Exchange a refresh token for a new access token.
	/// Returns null if the provider doesn't support refresh tokens
	/// or if the refresh token has expired.
	/// </summary>
	public async Task<OAuthTokenResult> RefreshAccessTokenAsync(
		OAuthProvider provider,
		string refreshToken,
		CancellationToken ct = default)
	{
		try
		{
			var req = new HttpRequestMessage(HttpMethod.Post, provider.TokenUrl);
			req.Headers.Accept.ParseAdd("application/json");
			req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["client_id"] = provider.ClientId,
				["grant_type"] = "refresh_token",
				["refresh_token"] = refreshToken,
			});

			var resp = await _http.SendAsync(req, ct);
			var json = await resp.Content.ReadAsStringAsync(ct);

			var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			var accessToken = GetString(root, "access_token");
			if (string.IsNullOrEmpty(accessToken))
			{
				var errDesc = GetString(root, "error_description")
						   ?? GetString(root, "error")
						   ?? json;
				return OAuthTokenResult.Fail($"Token refresh failed: {errDesc}");
			}

			var newRefresh = GetString(root, "refresh_token") ?? refreshToken;
			return OAuthTokenResult.Ok(accessToken, newRefresh, null, null);
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "RefreshAccessTokenAsync failed");
			return OAuthTokenResult.Fail(ex.Message);
		}
	}

	// ── Fetch user info after successful auth ─────────────────

	private async Task<(string? Username, string? Email)> FetchUserInfoAsync(
		OAuthProvider provider,
		string accessToken,
		CancellationToken ct)
	{
		try
		{
			// GitHub user API
			if (provider.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
			{
				var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
				req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
				req.Headers.UserAgent.ParseAdd("GitCredMan/1.0");

				var resp = await _http.SendAsync(req, ct);
				if (resp.IsSuccessStatusCode)
				{
					var json = await resp.Content.ReadAsStringAsync(ct);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					var login = GetString(root, "login");
					var email = GetString(root, "email");

					// email can be null if user has it private — fetch separately
					if (string.IsNullOrEmpty(email))
						email = await FetchGitHubPrimaryEmailAsync(accessToken, ct);

					return (login, email);
				}
			}

			// GitLab user API
			if (provider.Host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
			{
				var req = new HttpRequestMessage(HttpMethod.Get, "https://gitlab.com/api/v4/user");
				req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

				var resp = await _http.SendAsync(req, ct);
				if (resp.IsSuccessStatusCode)
				{
					var json = await resp.Content.ReadAsStringAsync(ct);
					var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					return (GetString(root, "username"), GetString(root, "email"));
				}
			}
		}
		catch (Exception ex)
		{
			_log.LogWarning(ex, "FetchUserInfoAsync failed — user info will be empty");
		}

		return (null, null);
	}

	private async Task<string?> FetchGitHubPrimaryEmailAsync(string accessToken, CancellationToken ct)
	{
		try
		{
			var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
			req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
			req.Headers.UserAgent.ParseAdd("GitCredMan/1.0");

			var resp = await _http.SendAsync(req, ct);
			if (!resp.IsSuccessStatusCode) return null;

			var json = await resp.Content.ReadAsStringAsync(ct);
			var arr = JsonDocument.Parse(json).RootElement;
			if (arr.ValueKind != JsonValueKind.Array) return null;

			foreach (var item in arr.EnumerateArray())
			{
				bool primary = item.TryGetProperty("primary", out var pProp) && pProp.GetBoolean();
				bool verified = item.TryGetProperty("verified", out var vProp) && vProp.GetBoolean();
				if (primary && verified)
					return item.TryGetProperty("email", out var eProp) ? eProp.GetString() : null;
			}
		}
		catch { }
		return null;
	}

	// ── JSON helpers ──────────────────────────────────────────

	private static string? GetString(JsonElement root, string key) =>
		root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
			? p.GetString() : null;

	private static int? GetInt(JsonElement root, string key) =>
		root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
			? p.GetInt32() : null;
}