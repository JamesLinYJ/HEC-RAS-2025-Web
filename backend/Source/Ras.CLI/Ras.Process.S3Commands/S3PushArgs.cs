using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Utility.Communication;
using Utility.Progress;

namespace Ras.Process.S3Commands;

[Verb("push", false, null, HelpText = "Pushes a local file to S3 under the given key. Requires credentials.")]
public class S3PushArgs
{
	public const string Verb = "push";

	[Option('s', "server", Required = false, Default = "https://s3.hecdev.net", HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; }

	[Value(0, MetaName = "source", Required = true, HelpText = "Local file path to upload.")]
	public string Source { get; set; }

	[Value(1, MetaName = "destination", Required = true, HelpText = "Destination object in s3://bucket/key form, e.g. s3://my-bucket/reports/today.json.")]
	public string Destination { get; set; }

	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	[Usage(ApplicationAlias = "ras s3")]
	public static IEnumerable<Example> Examples => new Example[2]
	{
		new Example("Push a local file to a bucket", new S3PushArgs
		{
			Source = "./report.json",
			Destination = "s3://my-bucket/reports/today.json",
			User = "alice",
			Password = "secret"
		}),
		new Example("Push against a custom S3 server", new S3PushArgs
		{
			Source = "./data.h5",
			Destination = "s3://my-bucket/data/data.h5",
			User = "alice",
			Password = "secret",
			Server = "https://s3.example.com"
		})
	};

	public async Task<bool> ExecuteAsync()
	{
		if (!File.Exists(Source))
		{
			Console.WriteLine("ERROR: Local file '" + Source + "' does not exist.");
			return false;
		}
		if (!S3Uri.TryParse(Destination, out var bucket, out var key))
		{
			Console.WriteLine("ERROR: Destination must be an s3://bucket/key URI. Got: '" + Destination + "'");
			return false;
		}
		using S3Client client = new S3Client(Server, User, Password, forcePathStyleQueries: true);
		ProgressReporter reporter = ProgressReporter.ConsoleWrite();
		await client.PushSingleFileAsync(bucket, key, Source, "", reporter);
		return true;
	}
}
