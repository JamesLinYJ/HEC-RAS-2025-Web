using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Web;

namespace Utility.Auth.Oidc;

public sealed class OidcClient
{
	private sealed class OidcTokenResponse
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; }

		[JsonPropertyName("refresh_token")]
		public string RefreshToken { get; set; }

		[JsonPropertyName("expires_in")]
		public int ExpiresIn { get; set; }

		[JsonPropertyName("refresh_expires_in")]
		public int RefreshExpiresIn { get; set; }

		[JsonPropertyName("token_type")]
		public string TokenType { get; set; }
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("OidcClient");

	public const string DefaultScope = "openid profile";

	public static HttpClient TokenHttpClient { get; set; } = SharedWebResourceManager.Client;

	public static TimeSpan TokenRequestTimeout { get; set; } = TimeSpan.FromSeconds(30L);

	public static PkcePair CreatePkce()
	{
		string randomUrlToken = RandomUrlToken();
		string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(randomUrlToken)));
		return new PkcePair(randomUrlToken, challenge);
	}

	public static string RandomUrlToken(int byteCount = 32)
	{
		return Base64Url(RandomNumberGenerator.GetBytes(byteCount));
	}

	public static string BuildAuthorizeUrl(IOidcEnvironment env, string challenge, string state)
	{
		Dictionary<string, string> stringByString = new Dictionary<string, string>
		{
			["client_id"] = env.ClientId,
			["response_type"] = "code",
			["scope"] = "openid profile",
			["redirect_uri"] = env.RedirectUri,
			["state"] = state,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256"
		};
		if (!string.IsNullOrEmpty(env.IdpHint))
		{
			stringByString["kc-idp-hint"] = env.IdpHint;
		}
		StringBuilder stringBuilder = new StringBuilder(env.AuthorizeEndpoint).Append('?');
		bool condition = true;
		foreach (KeyValuePair<string, string> localValue in stringByString)
		{
			if (!condition)
			{
				stringBuilder.Append('&');
			}
			stringBuilder.Append(Uri.EscapeDataString(localValue.Key)).Append('=').Append(Uri.EscapeDataString(localValue.Value));
			condition = false;
		}
		return stringBuilder.ToString();
	}

	public async Task<OidcTokenSet> ExchangeCodeAsync(IOidcEnvironment env, string code, string verifier)
	{
		Dictionary<string, string> form = new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["client_id"] = env.ClientId,
			["code"] = code,
			["redirect_uri"] = env.RedirectUri,
			["code_verifier"] = verifier
		};
		return await PostTokenAsync(env, form, "authorization_code");
	}

	public async Task<OidcTokenSet> RefreshAsync(IOidcEnvironment env, string refreshToken)
	{
		Dictionary<string, string> form = new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["client_id"] = env.ClientId,
			["refresh_token"] = refreshToken
		};
		return await PostTokenAsync(env, form, "refresh_token");
	}

	private async Task<OidcTokenSet> PostTokenAsync(IOidcEnvironment env, Dictionary<string, string> form, string what)
	{
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, env.TokenEndpoint)
		{
			Content = new FormUrlEncodedContent(form)
		};
		using CancellationTokenSource cts = new CancellationTokenSource(TokenRequestTimeout);
		HttpResponseMessage resp;
		try
		{
			resp = await TokenHttpClient.SendAsync(req, cts.Token);
		}
		catch (OperationCanceledException) when (cts.IsCancellationRequested)
		{
			string host = (Uri.TryCreate(env.TokenEndpoint, UriKind.Absolute, out Uri result) ? result.Host : env.TokenEndpoint);
			_logger.LogWarning("OIDC {What} timed out after {Secs}s contacting {Host}.", what, TokenRequestTimeout.TotalSeconds, host);
			throw new TimeoutException($"OIDC {what} timed out after {TokenRequestTimeout.TotalSeconds:0}s contacting {host}. " + "The token endpoint may be unreachable — check VPN/CAC/proxy and the environment's token URL.");
		}
		using (resp)
		{
			string asStringAsync = await resp.Content.ReadAsStringAsync();
			if (!resp.IsSuccessStatusCode)
			{
				_logger.LogWarning("OIDC {What} failed: HTTP {Status} {Body}", what, (int)resp.StatusCode, Truncate(asStringAsync, 300));
				throw new HttpRequestException($"OIDC {what} failed: HTTP {(int)resp.StatusCode}");
			}
			OidcTokenResponse oidcTokenResponse = JsonSerializer.Deserialize<OidcTokenResponse>(asStringAsync);
			if (oidcTokenResponse == null || string.IsNullOrEmpty(oidcTokenResponse.AccessToken))
			{
				throw new InvalidOperationException("OIDC " + what + " returned no access token.");
			}
			DateTime utcNow = DateTime.UtcNow;
			return new OidcTokenSet
			{
				AccessToken = oidcTokenResponse.AccessToken,
				RefreshToken = oidcTokenResponse.RefreshToken,
				AccessExpiresUtc = utcNow.AddSeconds((oidcTokenResponse.ExpiresIn > 0) ? oidcTokenResponse.ExpiresIn : 60),
				RefreshExpiresUtc = utcNow.AddSeconds((oidcTokenResponse.RefreshExpiresIn > 0) ? oidcTokenResponse.RefreshExpiresIn : 1800)
			};
		}
	}

	private static string Base64Url(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}

	private static string Truncate(string s, int max)
	{
		if (!string.IsNullOrEmpty(s))
		{
			if (s.Length > max)
			{
				return s.Substring(0, max) + "...";
			}
			return s;
		}
		return "";
	}
}
