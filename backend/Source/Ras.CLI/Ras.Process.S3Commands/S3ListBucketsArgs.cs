using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using Utility.Communication;

namespace Ras.Process.S3Commands;

[Verb("list-buckets", false, null, HelpText = "Lists buckets visible on the S3 server. Attempts anonymous access if no credentials are supplied.")]
public class S3ListBucketsArgs
{
	public const string Verb = "list-buckets";

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Optional - list attempts anonymous access if omitted.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Optional - list attempts anonymous access if omitted.")]
	public string Password { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		bool useAnonymous = string.IsNullOrEmpty(User) && string.IsNullOrEmpty(Password);
		using S3Client client = (useAnonymous ? new S3Client(Server, forcePathStyle: true) : new S3Client(Server, User, Password, forcePathStyleQueries: true));
		try
		{
			List<string> bucketNamesAsync = await client.GetBucketNamesAsync();
			if (bucketNamesAsync.Count == 0)
			{
				Console.WriteLine("(no buckets)");
				return true;
			}
			foreach (string bucketNamesAsync2 in bucketNamesAsync)
			{
				Console.WriteLine(bucketNamesAsync2);
			}
			return true;
		}
		catch (AmazonS3Exception ex) when (IsAuthFailure(ex))
		{
			Console.WriteLine($"ERROR: S3 rejected the request ({(int)ex.StatusCode} {ex.StatusCode}).");
			if (useAnonymous)
			{
				Console.WriteLine("       This server likely requires authentication. Re-run with --user and --password.");
			}
			else
			{
				Console.WriteLine("       Your credentials may be unauthorized to list buckets on this server.");
			}
			return false;
		}
	}

	private static bool IsAuthFailure(AmazonS3Exception ex)
	{
		if (ex.StatusCode != HttpStatusCode.Forbidden)
		{
			return ex.StatusCode == HttpStatusCode.Unauthorized;
		}
		return true;
	}
}
