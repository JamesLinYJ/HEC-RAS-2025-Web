using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class CoriolisParams : BasicRectangleParams
{
	public double DownstreamStage = 100.0;

	public double UpstreamFlow = 500000.0;

	public double RampSeconds = 172800.0;

	public double Velocity = 1.0;

	public double Fc = 0.0001;

	public static CoriolisParams Default()
	{
		return new CoriolisParams
		{
			CellsTall = 5,
			CellsWide = 25,
			CellSize = 1000.0,
			NValue = 0.01f,
			Slope = 0.0,
			DownstreamStage = 100.0,
			UpstreamFlow = 500000.0,
			RampSeconds = 172800.0,
			Velocity = 1.0,
			Fc = 0.0001,
			SolveDt = 10.0,
			SolveDuration = 345600.0
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) =>
		{
			double fcTimesVelocityOverGravSI = Fc * Velocity / SolverControl.GravSI;
			return (float)(DownstreamStage - fcTimesVelocityOverGravSI * (pt.Y - (double)CellsTall * CellSize / 2.0));
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)DownstreamStage;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = Polyline.FromSegment(extent.LeftWall.Scale(0.01, 0.99));
		Polyline pl2 = Polyline.FromSegment(extent.RightWall.Scale(0.01, 0.99));
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		if (RampSeconds == 0.0)
		{
			boundaryConditionLines.Add(BoundaryConditionLine.ConstantFlow(bc, pl, UpstreamFlow));
		}
		else
		{
			double[] rampTimeSteps = BaseParams.RampTimeSteps(RampSeconds);
			double[] doubleValues = new double[rampTimeSteps.Length];
			for (int i = 0; i < rampTimeSteps.Length; i++)
			{
				doubleValues[i] = UpstreamFlow * BaseParams.RampFactor(rampTimeSteps[i], RampSeconds);
			}
			BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl)
			{
				Name = "Upstream"
			};
			boundaryConditionLine.Payloads.Add(new TimeSeriesPayload("Flow", IrregularRelativeTimeSeriesDefinition.FromSeconds(rampTimeSteps), doubleValues));
			boundaryConditionLines.Add(boundaryConditionLine);
		}
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantStage(bc, pl2, DownstreamStage);
		boundaryConditionLine2.Name = "Downstream";
		boundaryConditionLines.Add(boundaryConditionLine2);
		return boundaryConditionLines;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		sc.computeControl.variableDt = false;
		sc.stepType = SolverControl.StepType.FWDBACK;
		sc.computeControl.dtMinMicros = 10000000L;
		sc.fCoriolis = Fc;
	}
}
