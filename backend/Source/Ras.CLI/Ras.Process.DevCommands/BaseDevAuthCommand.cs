using CommandLine;
using Utility.Communication;

namespace Ras.Process.DevCommands;

public class BaseDevAuthCommand : BaseDevCommand
{
	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	public override S3TransferHelper GetS3()
	{
		return new S3TransferHelper(Server, Bucket, User, Password, forcePathStyle: true);
	}
}
