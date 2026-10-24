using System;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class InOutPlanarParams : BasicRectangleParams
{
	public bool UseGPU;

	public double RampSeconds;

	public double UpstreamFlow;

	public double DownstreamStage;

	public double UpstreamEGSlope = 0.0001;

	public static InOutPlanarParams Default()
	{
		return new InOutPlanarParams
		{
			CellsTall = 3,
			CellsWide = 15,
			CellSize = 10.0,
			RampSeconds = 500.0,
			NValue = 0f,
			Slope = 0.0,
			DownstreamStage = 1.0,
			UpstreamFlow = 30.0,
			SolveDt = 1.6,
			SolveDuration = 7000.0
		};
	}

	public static InOutPlanarParams GPU()
	{
		return new InOutPlanarParams
		{
			CellsTall = 3,
			CellsWide = 15,
			CellSize = 10.0,
			RampSeconds = 500.0,
			NValue = 0f,
			Slope = 0.0,
			DownstreamStage = 1.0,
			UpstreamFlow = 30.0,
			SolveDt = 1.6,
			SolveDuration = 7000.0,
			UseGPU = true
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point p) => (float)DownstreamStage;
	}

	public override BoundaryConditionLine GetUpstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pl)
	{
		if (RampSeconds == 0.0)
		{
			BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pl, UpstreamFlow);
			boundaryConditionLine.Name = "Upstream";
			return boundaryConditionLine;
		}
		BoundaryConditionLine boundaryConditionLine2 = new BoundaryConditionLine(bc, pl)
		{
			Name = "Upstream"
		};
		if (SolveDuration <= 0.0)
		{
			throw new InvalidOperationException($"{"SolveDuration"} must be > 0 to build a ramped upstream flow BC (got {SolveDuration}).");
		}
		TimeSpan ts = TimeSpan.FromMinutes(1L);
		double totalSeconds = ts.TotalSeconds;
		int count = (int)Math.Ceiling(SolveDuration / totalSeconds);
		double rampSeconds = Math.Max(totalSeconds, Math.Ceiling(RampSeconds / totalSeconds) * totalSeconds);
		double[] doubleValues = new double[count + 1];
		for (int i = 0; i <= count; i++)
		{
			double time = (double)i * totalSeconds;
			doubleValues[i] = UpstreamFlow * BaseParams.RampFactor(time, rampSeconds);
		}
		boundaryConditionLine2.Payloads.Add(new TimeSeriesPayload("Flow", new RegularTimeSeriesDefinition(ts), doubleValues));
		return boundaryConditionLine2;
	}

	public override BoundaryConditionLine GetDownstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pl)
	{
		double computed = 0.0;
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(bc, pl, DownstreamStage - computed);
		boundaryConditionLine.Name = "Downstream";
		return boundaryConditionLine;
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		pl.SolverType = (UseGPU ? SolverType.GPU : SolverType.CPU);
		pl.UseGPUAdaptive = true;
	}

	public override void ConfigureEngineBC(Ras.Engine.BoundaryCondition engineBc, BoundaryConditionLine srcLine, string variable)
	{
		if (variable == "Flow" && srcLine?.Name == "Upstream")
		{
			engineBc.EGSlope = UpstreamEGSlope;
		}
	}
}
