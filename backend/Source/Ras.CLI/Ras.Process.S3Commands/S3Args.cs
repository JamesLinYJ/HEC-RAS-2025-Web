using CommandLine;

namespace Ras.Process.S3Commands;

[Verb("s3", false, null, HelpText = "Command group. Raw S3 operations against a bucket: list-buckets, presign, pull, push.")]
public class S3Args
{
	public const string Verb = "s3";

	public bool Execute()
	{
		return false;
	}
}
