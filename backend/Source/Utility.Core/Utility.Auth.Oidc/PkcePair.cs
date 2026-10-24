namespace Utility.Auth.Oidc;

public readonly struct PkcePair
{
	public string Verifier { get; }

	public string Challenge { get; }

	public PkcePair(string verifier, string challenge)
	{
		Verifier = verifier;
		Challenge = challenge;
	}
}
