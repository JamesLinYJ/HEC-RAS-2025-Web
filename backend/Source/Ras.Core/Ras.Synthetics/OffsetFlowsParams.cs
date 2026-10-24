using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;

namespace Ras.Synthetics;

public class OffsetFlowsParams : BasicRectangleParams
{
	public double UpstreamFlow = 5.0;

	public double DownstreamFlow = -5.0;

	public double InitialStage = 1.0;

	public double RampSeconds;

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.SetConstantElevation(InitialStage);
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = Polyline.FromSegment(extent.LeftWall.Scale(0.01, 0.24));
		Polyline pl2 = Polyline.FromSegment(extent.RightWall.Scale(0.01, 0.99));
		return new List<BoundaryConditionLine>
		{
			BuildFlowBCL(bc, pl, UpstreamFlow, RampSeconds, "Upstream"),
			BuildFlowBCL(bc, pl2, DownstreamFlow, RampSeconds, "Downstream")
		};
	}

	private static BoundaryConditionLine BuildFlowBCL(Ras.Layers.BoundaryCondition bc, Polyline pl, double flow, double rampSeconds, string name)
	{
		if (rampSeconds == 0.0)
		{
			return BoundaryConditionLine.ConstantFlow(bc, pl, flow);
		}
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl)
		{
			Name = name
		};
		boundaryConditionLine.Payloads.Add(new TimeSeriesPayload("Flow", IrregularRelativeTimeSeriesDefinition.FromSeconds(new double[4] { -1E+32, 0.0, rampSeconds, 1E+32 }), new double[4] { 0.0, 0.0, flow, flow }));
		return boundaryConditionLine;
	}
}
