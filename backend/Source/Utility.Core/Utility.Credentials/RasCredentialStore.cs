using System;
using System.Collections.Generic;

namespace Utility.Credentials;

public sealed class RasCredentialStore
{
	public const string DirectoryName = ".ras";

	public const string DefaultProfileName = "default";

	public static string DefaultDirectory
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public static bool IsEncryptedAtRest
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public RasCredentialStore()
	{
		throw new NotImplementedException();
	}

	public RasCredentialStore(string directory)
	{
		throw new NotImplementedException();
	}

	public bool TryLoad(string service, string profile, out RasCredential credential)
	{
		throw new NotImplementedException();
	}

	public bool TryLoad(string service, out RasCredential credential)
	{
		throw new NotImplementedException();
	}

	public void Save(string service, string profile, RasCredential credential)
	{
		throw new NotImplementedException();
	}

	public bool TryDelete(string service, string profile)
	{
		throw new NotImplementedException();
	}

	public bool Exists(string service, string profile)
	{
		throw new NotImplementedException();
	}

	public IReadOnlyList<string> ListServices()
	{
		throw new NotImplementedException();
	}

	public IReadOnlyList<string> ListProfiles(string service)
	{
		throw new NotImplementedException();
	}
}
