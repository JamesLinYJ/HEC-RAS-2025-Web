namespace Geospatial.Vectors.Topology;

public struct TopologyClipOptions
{
	public bool FullyContainedGeometry;

	public bool CopyTableMetadata;

	public static TopologyClipOptions Default()
	{
		return new TopologyClipOptions
		{
			FullyContainedGeometry = true,
			CopyTableMetadata = true
		};
	}
}
