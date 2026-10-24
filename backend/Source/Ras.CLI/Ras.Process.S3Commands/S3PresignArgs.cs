using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using CommandLine.Text;
using Utility.Communication;

namespace Ras.Process.S3Commands;

[Verb("presign", false, null, HelpText = "Presigns an S3 key, returning a URL usable with the requested HTTP verb (get or put).")]
public class S3PresignArgs
{
	public const string Verb = "presign";

	[Option('s', "server", Required = false, Default = "https://s3.hecdev.net", HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; }

	[Value(0, MetaName = "s3-uri", Required = true, HelpText = "Object in s3://bucket/key form, e.g. s3://my-bucket/folder/file.h5.")]
	public string Uri { get; set; }

	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	[Option("verb", Required = false, Default = "get", HelpText = "HTTP verb for the presigned URL: get or put. (post is not supported by the AWS SDK's GetPreSignedURL API.)")]
	public string HttpVerbName { get; set; }

	[Usage(ApplicationAlias = "ras s3")]
	public static IEnumerable<Example> Examples => new Example[2]
	{
		new Example("Presign a GET URL for download", new S3PresignArgs
		{
			Uri = "s3://my-bucket/folder/file.h5",
			User = "alice",
			Password = "secret"
		}),
		new Example("Presign a PUT URL for upload", new S3PresignArgs
		{
			Uri = "s3://my-bucket/uploads/new.h5",
			User = "alice",
			Password = "secret",
			HttpVerbName = "put"
		})
	};

	public async Task<bool> ExecuteAsync()
	{
		if (!S3Uri.TryParse(Uri, out var bucket, out var key))
		{
			Console.WriteLine("ERROR: URI must be an s3://bucket/key form. Got: '" + Uri + "'");
			return false;
		}
		switch (HttpVerbName?.ToLowerInvariant())
		{
		case "post":
			Console.WriteLine("POST presigned URLs are not supported by the AWS SDK's GetPreSignedURL API. Use 'get' or 'put'.");
			return false;
		default:
			Console.WriteLine("Unsupported verb '" + HttpVerbName + "'. Expected 'get' or 'put'.");
			return false;
		case "get":
		case "put":
		{
			HttpVerb verb = ((HttpVerbName.ToLowerInvariant() == "put") ? HttpVerb.PUT : HttpVerb.GET);
			S3Client client = new S3Client(Server, User, Password, forcePathStyleQueries: true);
			try
			{
				Console.WriteLine(await Task.Run(() => client.GetPresignedURL(bucket, key, verb)));
				return true;
			}
			finally
			{
				if (client != null)
				{
					((IDisposable)client).Dispose();
				}
			}
		}
		}
	}
}
