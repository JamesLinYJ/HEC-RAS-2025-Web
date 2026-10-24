using System;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Utility.Communication;

namespace Ras.Process.DevCommands;

public abstract class BaseDevCommand
{
	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to ras-public-releases for all commands except checkupdate, which uses ras-public-releases-staging")]
	public string Bucket { get; set; } = "ras-public-releases";

	[Option("dry-run", Required = false, HelpText = "Executes the code to prepare a release without actually uploading or modifying any files.")]
	public bool DryRun { get; set; }

	[Option("verbose", Required = false, HelpText = "Adds verbose messaging. Defaults to false.")]
	public bool Verbose { get; set; }

	public virtual S3TransferHelper GetS3()
	{
		return new S3TransferHelper(Server, Bucket, "", "", forcePathStyle: true);
	}

	public static async Task<T> PullDeserialize<T>(S3TransferHelper s3u, string key, Func<string, T> deserialize)
	{
		string fn = RasSpecialDirectories.GetTempFile();
		try
		{
			await s3u.PullSingleFileAsync(fn, key);
			return deserialize(File.ReadAllText(fn));
		}
		finally
		{
			if (File.Exists(fn))
			{
				File.Delete(fn);
			}
		}
	}

	protected string DryRunPrefix(string msg)
	{
		return DryRunPrefix(msg, DryRun);
	}

	public static string DryRunPrefix(string msg, bool isDryRun)
	{
		if (isDryRun)
		{
			return "(SKIPPING - DRYRUN) " + msg;
		}
		return msg;
	}
}
