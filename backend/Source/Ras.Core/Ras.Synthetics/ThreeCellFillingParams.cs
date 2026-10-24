using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics;

public class ThreeCellFillingParams : BasicRectangleParams
{
	public double InitialStage = 0.1;

	public double FlowPerFace = 0.05;

	public ThreeCellFillingParams()
	{
		CellsWide = 1;
		CellsTall = 3;
		CellSize = 1.0;
		NValue = 0f;
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.SetConstantElevation(InitialStage);
	}

	public override void AddCustomEngineBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		Polyline pline = Polyline.FromSegment(mesh.Extent.LeftWall.Scale(0.01, 0.99));
		int[] intValues = (from pf in mesh.PerimeterFacesAlongPolyline(pline, out var _)
			select pf.Face).ToArray();
		foreach (int ordinal in intValues)
		{
			bcs.FlowBoundaryConditions.Add(BoundaryCondition.ConstantFaceValue(new int[1] { ordinal }, FlowPerFace));
		}
	}
}
