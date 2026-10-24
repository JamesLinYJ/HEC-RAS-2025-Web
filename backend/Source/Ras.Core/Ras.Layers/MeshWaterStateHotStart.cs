using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using Ras.Engine;

namespace Ras.Layers;

public static class MeshWaterStateHotStart
{
	private const string CellElevationDataset = "Cell Elevation";

	private const string FaceVelocityDataset = "Face Velocity";

	private const string DtMicrosAttribute = "dtMicros";

	public static void SaveHotStart(this IHotStartState state, H5Writer hw, string groupPath)
	{
		hw.CreateGroup(groupPath);
		hw.WriteDataset(groupPath + "/Cell Elevation", state.CellElevation);
		hw.WriteDataset(groupPath + "/Face Velocity", state.FaceVelocity);
		hw.WriteAttribute(groupPath, "dtMicros", state.dtMicros);
	}

	public static MeshWaterState LoadHotStart(H5Reader hr, string groupPath, Mesh mesh, MeshPropertyTables tables)
	{
		hr.ReadDataset(groupPath + "/Cell Elevation", out double[] values);
		hr.ReadDataset(groupPath + "/Face Velocity", out double[] values2);
		hr.ReadAttributeScalar(groupPath, "dtMicros", out long ordinal);
		MeshWaterState meshWaterState = new MeshWaterState(mesh, tables);
		meshWaterState.SetInitialFromRestartVars(values, values2, ordinal);
		return meshWaterState;
	}
}
