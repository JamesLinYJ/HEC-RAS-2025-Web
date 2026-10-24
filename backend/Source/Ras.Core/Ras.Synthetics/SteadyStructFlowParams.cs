using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;

namespace Ras.Synthetics;

public abstract class SteadyStructFlowParams : BasicRectangleParams
{
	public double Q;

	public double DsStage;

	public double InitStage;

	public double RampSeconds = 600.0;

	public double Duration = 10800.0;

	public double UpstreamFlowEGSlope = 0.001;

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.SetConstantElevation(InitStage);
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		sc.computeControl.variableDt = true;
		sc.stepType = SolverControl.StepType.FWDBACK;
		sc.numCores = 1;
		sc.parallelMethod = SolverControl.ParallelMethod.THREAD_FOR;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = Polyline.FromSegment(extent.LeftWall.Scale(0.01, 0.99));
		Polyline pl2 = Polyline.FromSegment(extent.RightWall.Scale(0.01, 0.99));
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl)
		{
			Name = "Upstream"
		};
		boundaryConditionLine.Payloads.Add(new TimeSeriesPayload("Flow", IrregularRelativeTimeSeriesDefinition.FromSeconds(new double[4] { -10000000000.0, 0.0, RampSeconds, 10000000000.0 }), new double[4] { 0.0, 0.0, Q, Q }));
		boundaryConditionLines.Add(boundaryConditionLine);
		BoundaryConditionLine boundaryConditionLine2 = new BoundaryConditionLine(bc, pl2)
		{
			Name = "Downstream"
		};
		boundaryConditionLine2.Payloads.Add(new TimeSeriesPayload("Stage", IrregularRelativeTimeSeriesDefinition.FromSeconds(new double[4] { -10000000000.0, 0.0, RampSeconds, 10000000000.0 }), new double[4] { InitStage, InitStage, DsStage, DsStage }));
		boundaryConditionLines.Add(boundaryConditionLine2);
		return boundaryConditionLines;
	}

	public override void ConfigureEngineBC(Ras.Engine.BoundaryCondition engineBc, BoundaryConditionLine srcLine, string variable)
	{
		if (variable == "Flow" && srcLine?.Name == "Upstream")
		{
			engineBc.EGSlope = UpstreamFlowEGSlope;
		}
	}
}
