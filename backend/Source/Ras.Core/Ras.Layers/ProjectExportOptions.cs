namespace Ras.Layers;

public struct ProjectExportOptions
{
	public bool SoftLinkTerrain { get; set; }

	public static ProjectExportOptions Default()
	{
		return new ProjectExportOptions
		{
			SoftLinkTerrain = false
		};
	}
}
