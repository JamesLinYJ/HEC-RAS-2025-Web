namespace Geospatial.Vectors.Meshes;

public interface IMutableSimplePropertyTables : ISimpleMeshPropertyTables
{
	new float[] CellMinimumElevations { get; set; }

	new float[] FaceMinimumElevations { get; set; }
}
