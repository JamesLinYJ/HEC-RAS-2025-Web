using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public class FlowOverTrapezoidParams : InOutPlanarParams
{
	public double BumpHeight;

	public double BumpCrestLength;

	public double BumpSlope;

	public double FrDown = 0.1;

	public double uDown = 0.5;

	public new static FlowOverTrapezoidParams Default()
	{
		FlowOverTrapezoidParams flowOverTrapezoidParams = new FlowOverTrapezoidParams();
		flowOverTrapezoidParams.CellsTall = 1;
		flowOverTrapezoidParams.CellsWide = 100;
		flowOverTrapezoidParams.CellSize = 10.0;
		flowOverTrapezoidParams.NValue = 0.02f;
		flowOverTrapezoidParams.Slope = 0.0;
		flowOverTrapezoidParams.BumpHeight = 1.2;
		flowOverTrapezoidParams.BumpCrestLength = 100.0;
		flowOverTrapezoidParams.BumpSlope = 0.02;
		flowOverTrapezoidParams.FrDown = 0.1;
		flowOverTrapezoidParams.uDown = 0.5;
		flowOverTrapezoidParams.SolveDt = 1.6;
		flowOverTrapezoidParams.SolveDuration = 7000.0;
		flowOverTrapezoidParams.InitStageFlowValues();
		return flowOverTrapezoidParams;
	}

	public void InitStageFlowValues()
	{
		double gravSI = SolverControl.GravSI;
		DownstreamStage = Math.Pow(uDown / FrDown, 2.0) / gravSI;
		UpstreamFlow = uDown * DownstreamStage * (double)CellsTall * CellSize;
	}

	public override float TerrainElevation(Point pt)
	{
		double ptCellsWideTerm = Math.Abs(pt.X - (double)CellsWide * CellSize / 2.0);
		return (float)Math.Clamp(BumpHeight + BumpSlope * (BumpCrestLength / 2.0 - ptCellsWideTerm), 0.0, BumpHeight);
	}
}
