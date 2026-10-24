namespace Utility.Communication;

public class ServerAuth
{
	public string ServerName { get; }

	public Authentication Authentication { get; }

	public ServerAuth(string server, Authentication auth)
	{
		ServerName = server;
		Authentication = auth;
	}
}
