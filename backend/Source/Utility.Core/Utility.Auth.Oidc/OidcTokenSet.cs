using System;

namespace Utility.Auth.Oidc;

public sealed class OidcTokenSet
{
	public string AccessToken { get; init; }

	public string RefreshToken { get; init; }

	public DateTime AccessExpiresUtc { get; init; }

	public DateTime RefreshExpiresUtc { get; init; }

	public bool IsAccessValid(TimeSpan? skew = null)
	{
		if (!string.IsNullOrEmpty(AccessToken))
		{
			return DateTime.UtcNow + (skew ?? TimeSpan.FromSeconds(30L)) < AccessExpiresUtc;
		}
		return false;
	}

	public bool CanRefresh()
	{
		if (!string.IsNullOrEmpty(RefreshToken))
		{
			return DateTime.UtcNow < RefreshExpiresUtc;
		}
		return false;
	}
}
