using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics;

public class TwoCellSloshParams : BasicRectangleParams
{
	public TwoCellSloshParams()
	{
		CellsWide = 2;
		CellsTall = 1;
		CellSize = 1.0;
		NValue = 0f;
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.CellElevation[0] = 0.75;
		state.CellElevation[1] = 1.25;
		for (int i = 0; i < mesh.CellCount; i++)
		{
			state.CellVolume[i] = props.InterpolateCellVolume(i, state.CellElevation[i]);
		}
	}
}
