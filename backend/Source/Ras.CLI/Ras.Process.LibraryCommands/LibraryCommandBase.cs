using System;
using System.Net;
using Amazon.S3;
using CommandLine;
using Ras.ModelLibrary;

namespace Ras.Process.LibraryCommands;

public abstract class LibraryCommandBase
{
	public const string UserEnvVar = "RAS_S3_USER";

	public const string PasswordEnvVar = "RAS_S3_PASSWORD";

	[Option('u', "user", Required = false, HelpText = "Access key for the model-library S3 bucket. Falls back to the RAS_S3_USER environment variable.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Secret key for the model-library S3 bucket. Falls back to the RAS_S3_PASSWORD environment variable.")]
	public string Password { get; set; }

	protected bool TryCreateClient(out ModelLibraryClient client)
	{
		client = null;
		string environmentVariable = (string.IsNullOrEmpty(User) ? Environment.GetEnvironmentVariable("RAS_S3_USER") : User);
		string environmentVariable2 = (string.IsNullOrEmpty(Password) ? Environment.GetEnvironmentVariable("RAS_S3_PASSWORD") : Password);
		if (string.IsNullOrEmpty(environmentVariable) || string.IsNullOrEmpty(environmentVariable2))
		{
			Console.WriteLine("ERROR: Model-library credentials are required. Pass --user/--password or set RAS_S3_USER/RAS_S3_PASSWORD.");
			return false;
		}
		client = new ModelLibraryClient(environmentVariable, environmentVariable2);
		return true;
	}

	protected static bool IsAuthFailure(AmazonS3Exception ex)
	{
		if (ex.StatusCode != HttpStatusCode.Forbidden)
		{
			return ex.StatusCode == HttpStatusCode.Unauthorized;
		}
		return true;
	}

	protected static bool ReportAuthFailure(AmazonS3Exception ex)
	{
		Console.WriteLine($"ERROR: S3 rejected the request ({(int)ex.StatusCode} {ex.StatusCode}).");
		Console.WriteLine("       Your model-library credentials may be missing or unauthorized for this bucket.");
		return false;
	}
}
