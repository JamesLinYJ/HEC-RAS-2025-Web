using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics;

public class FourCell2Params : InOutPlanarParams
{
	public double InitialStage = 1.0;

	public FourCell2Params()
	{
		CellsWide = 2;
		CellsTall = 2;
		CellSize = 1.0;
		UpstreamFlow = 0.0;
		DownstreamStage = 2.0;
		NValue = 0.1f;
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.SetConstantElevation(InitialStage);
	}
}
