namespace Utility.Communication;

public class DirectoryTransferOptions
{
	public bool ForceOverwriteExisting { get; set; }

	public string CacheDirectory { get; set; }

	public bool DryRun { get; set; }

	public static DirectoryTransferOptions Default()
	{
		return new DirectoryTransferOptions
		{
			CacheDirectory = "",
			DryRun = false,
			ForceOverwriteExisting = false
		};
	}

	public string DryRunPrefix(string msg)
	{
		if (DryRun)
		{
			return "(SKIPPING - DRYRUN) " + msg;
		}
		return msg;
	}
}
