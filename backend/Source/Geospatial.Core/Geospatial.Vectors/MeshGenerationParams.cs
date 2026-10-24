namespace Geospatial.Vectors;

public class MeshGenerationParams
{
	public static bool DefaultSplitExternalFaces = true;

	public bool CreateVirtualCells { get; set; }

	public bool SplitExternalFaces { get; set; }

	public static MeshGenerationParams Default()
	{
		return new MeshGenerationParams
		{
			CreateVirtualCells = false,
			SplitExternalFaces = DefaultSplitExternalFaces
		};
	}
}
