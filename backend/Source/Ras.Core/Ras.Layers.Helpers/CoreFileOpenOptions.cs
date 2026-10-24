namespace Ras.Layers.Helpers;

public class CoreFileOpenOptions
{
	public bool LoadCachedProjectInfo { get; set; }

	public bool ComputeFileHash { get; set; }

	public static CoreFileOpenOptions Default()
	{
		return new CoreFileOpenOptions
		{
			LoadCachedProjectInfo = true,
			ComputeFileHash = true
		};
	}
}
