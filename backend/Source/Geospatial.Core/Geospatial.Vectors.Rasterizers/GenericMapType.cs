namespace Geospatial.Vectors.Rasterizers;

public class GenericMapType
{
	public string VariableName { get; }

	public MeshElementType MeshElementType { get; }

	public bool FaceValuesAsNormalVectors { get; set; }

	public bool InterpolateCellValues { get; set; }

	public bool HonorHydraulicConnectivity { get; set; }

	public bool IsDebug { get; set; }

	public GenericMapType(string varName, MeshElementType mt)
	{
		VariableName = varName;
		MeshElementType = mt;
	}
}
