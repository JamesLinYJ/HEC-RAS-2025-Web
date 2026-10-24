using System;
using System.Threading.Tasks;
using Utility.Auth.Oidc;
using Utility.Web;

namespace Utility.Network.Cumulus;

public sealed class CumulusEnvironment : IOidcEnvironment
{
	private bool _urlsResolved;

	public static readonly CumulusEnvironment Local = new CumulusEnvironment
	{
		Name = "Local (mock)",
		ApiBaseUrl = "http://localhost",
		UseMock = true,
		MockToken = CumulusAuth.DefaultLocalToken,
		AppKey = "appkey"
	};

	public static readonly CumulusEnvironment Dev = new CumulusEnvironment
	{
		Name = "CWBI Dev",
		Realm = "cwbi",
		ClientId = "cumulus",
		RedirectUri = "http://127.0.0.1/callback",
		IdpHint = "federation-eams",
		SiteUrlFwLink = "cumulus-dev",
		AuthUrlFwLink = "cumulus-identity-test"
	};

	public static readonly CumulusEnvironment Test = new CumulusEnvironment
	{
		Name = "CWBI Test",
		Realm = "cwbi",
		ClientId = "cumulus",
		RedirectUri = "http://127.0.0.1/callback",
		IdpHint = "federation-eams",
		SiteUrlFwLink = "cumulus-test",
		AuthUrlFwLink = "cumulus-identity-test"
	};

	public static readonly CumulusEnvironment Prod = new CumulusEnvironment
	{
		Name = "CWBI Prod",
		Realm = "cwbi",
		ClientId = "cumulus",
		RedirectUri = "http://127.0.0.1/callback",
		IdpHint = "federation-eams",
		SiteUrlFwLink = "cumulus-prod",
		AuthUrlFwLink = "cumulus-identity-prod"
	};

	public static readonly CumulusEnvironment[] All = new CumulusEnvironment[4] { Local, Dev, Test, Prod };

	public string Name { get; set; }

	public string ApiBaseUrl { get; set; }

	public string AuthBaseUrl { get; set; }

	public string Realm { get; set; }

	public string ClientId { get; set; }

	public string RedirectUri { get; set; }

	public string IdpHint { get; set; }

	public bool UseMock { get; set; }

	public string MockToken { get; set; }

	public string AppKey { get; set; }

	public string SiteUrl { get; set; }

	public string SiteUrlFwLink { get; set; }

	public string AuthUrlFwLink { get; set; }

	public string AuthorizeEndpoint => AuthBaseUrl + "/realms/" + Realm + "/protocol/openid-connect/auth";

	public string TokenEndpoint => AuthBaseUrl + "/realms/" + Realm + "/protocol/openid-connect/token";

	public string WellKnownEndpoint => AuthBaseUrl + "/realms/" + Realm + "/.well-known/openid-configuration";

	public static CumulusEnvironment Active { get; set; } = Local;

	public async Task ResolveUrlsAsync()
	{
		if (_urlsResolved || UseMock)
		{
			return;
		}
		if (!string.IsNullOrEmpty(SiteUrlFwLink))
		{
			var (localValue, siteUrl) = await HECFWLink.TryGetLinkIDAsRawString(SiteUrlFwLink);
			if (!localValue)
			{
				throw new InvalidOperationException($"Failed to resolve fwlink '{SiteUrlFwLink}' for the {Name} Cumulus host.");
			}
			SiteUrl = siteUrl;
			ApiBaseUrl = siteUrl.TrimEnd('/');
		}
		if (!string.IsNullOrEmpty(AuthUrlFwLink))
		{
			var (localValue2, localValue3) = await HECFWLink.TryGetLinkIDAsRawString(AuthUrlFwLink);
			if (!localValue2)
			{
				throw new InvalidOperationException($"Failed to resolve fwlink '{AuthUrlFwLink}' for the {Name} identity host.");
			}
			AuthBaseUrl = localValue3.TrimEnd('/');
		}
		_urlsResolved = true;
	}

	public override string ToString()
	{
		return Name;
	}
}
