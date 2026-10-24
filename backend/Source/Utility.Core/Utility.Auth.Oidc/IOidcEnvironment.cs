namespace Utility.Auth.Oidc;

public interface IOidcEnvironment
{
	string ClientId { get; }

	string AuthorizeEndpoint { get; }

	string TokenEndpoint { get; }

	string RedirectUri { get; }

	string IdpHint { get; }
}
