using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using CommandLine.Text;
using Utility.Communication;
using Utility.Progress;

namespace Ras.Process.S3Commands;

[Verb("pull", false, null, HelpText = "Pulls a single object from S3 to a local file. Attempts anonymous access if no credentials are supplied.")]
public class S3PullArgs
{
	public const string Verb = "pull";

	[Option('s', "server", Required = false, Default = "https://s3.hecdev.net", HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; }

	[Value(0, MetaName = "source", Required = true, HelpText = "Source object in s3://bucket/key form, e.g. s3://my-bucket/folder/file.h5.")]
	public string Source { get; set; }

	[Value(1, MetaName = "destination", Required = true, HelpText = "Local file path to write the downloaded object to.")]
	public string Destination { get; set; }

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Optional - pull attempts anonymous access if omitted.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Optional - pull attempts anonymous access if omitted.")]
	public string Password { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, overwrite the local file if it already exists.")]
	public bool ForceOverwriteExisting { get; set; }

	[Usage(ApplicationAlias = "ras s3")]
	public static IEnumerable<Example> Examples => new Example[2]
	{
		new Example("Pull a public object", new S3PullArgs
		{
			Source = "s3://ras-public-examples/Muncie/manifest.json",
			Destination = "./manifest.json"
		}),
		new Example("Pull with credentials, overwriting any existing local file", new S3PullArgs
		{
			Source = "s3://private-bucket/data.h5",
			Destination = "./data.h5",
			User = "alice",
			Password = "secret",
			ForceOverwriteExisting = true
		})
	};

	public async Task<bool> ExecuteAsync()
	{
		if (!S3Uri.TryParse(Source, out var bucket, out var key))
		{
			Console.WriteLine("ERROR: Source must be an s3://bucket/key URI. Got: '" + Source + "'");
			return false;
		}
		if (File.Exists(Destination) && !ForceOverwriteExisting)
		{
			Console.WriteLine("ERROR: '" + Destination + "' already exists. Re-run with --force to overwrite.");
			return false;
		}
		bool useAnonymous = string.IsNullOrEmpty(User) && string.IsNullOrEmpty(Password);
		using S3Client client = (useAnonymous ? new S3Client(Server, forcePathStyle: true) : new S3Client(Server, User, Password, forcePathStyleQueries: true));
		ProgressReporter reporter = ProgressReporter.ConsoleWrite();
		try
		{
			await client.PullSingleFileAsync(bucket, key, Destination, reporter);
			return true;
		}
		catch (AmazonS3Exception ex) when (IsAuthFailure(ex))
		{
			Console.WriteLine($"ERROR: S3 rejected the request ({(int)ex.StatusCode} {ex.StatusCode}).");
			if (useAnonymous)
			{
				Console.WriteLine("       This bucket likely requires authentication. Re-run with --user and --password.");
			}
			else
			{
				Console.WriteLine("       Your credentials may be unauthorized for this bucket or key.");
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
