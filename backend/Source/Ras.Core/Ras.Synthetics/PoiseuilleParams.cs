using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class PoiseuilleParams : BasicRectangleParams
{
	public double DownstreamStage = 1.0;

	public bool leftSlip;

	public bool rightSlip;

	public bool VelocityIC;

	public bool UpstreamFlowProfile;

	public static PoiseuilleParams Default()
	{
		throw new NotImplementedException();
	}

	public PoiseuilleParams()
	{
		Turbulence.BaseEddyViscosity = 19.6;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)(DownstreamStage + Slope * (SystemExtent.MaxX - pt.X));
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) =>
		{
			double maxXSystemExtentTerm = SystemExtent.MaxX - pt.X;
			return (float)(DownstreamStage + Slope * maxXSystemExtentTerm);
		};
	}

	public override BoundaryConditionLine GetUpstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (UpstreamFlowProfile)
		{
			return null;
		}
		return BoundaryConditionLine.ConstantStage(bc, pline, DownstreamStage + Slope * ((double)CellsWide + 0.5) * CellSize);
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		base.UpdateInitialState(state, mesh, props);
		if (!VelocityIC)
		{
			return;
		}
		Vector[] faceNormal = mesh.MetadataCache.FaceNormal;
		for (int i = 0; i < state.FaceCount; i++)
		{
			Point point = mesh.FaceMidPoint(i);
			double maxValue = double.MaxValue;
			if (!leftSlip)
			{
				maxValue = Math.Min(maxValue, point.Y);
			}
			if (!rightSlip)
			{
				maxValue = Math.Min(maxValue, (double)CellsTall * CellSize - point.Y);
			}
			double scaledMaxValue = maxValue * 0.04;
			state.FaceVelocity[i] = faceNormal[i].DeltaX * scaledMaxValue;
		}
	}

	public override BoundaryConditionLine GetDownstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		return BoundaryConditionLine.ConstantStage(bc, pline, DownstreamStage - Slope * 0.5 * CellSize);
	}

	public override void AddCustomEngineBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		AddSideWallBCs(bcs, mesh);
		if (UpstreamFlowProfile)
		{
			AddUpstreamFlowProfileBCs(bcs, mesh);
		}
	}

	private void AddSideWallBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		if (leftSlip && rightSlip)
		{
			return;
		}
		double scaledCellSize = CellSize / 2.0;
		double cellsTallCellSizeTerm = ((double)CellsTall - 0.5) * CellSize;
		List<int> intList = new List<int>();
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			double y = mesh.FaceMidPoint(i).Y;
			if ((!leftSlip && Math.Abs(y - scaledCellSize) < 0.01 * CellSize) || (!rightSlip && Math.Abs(y - cellsTallCellSizeTerm) < 0.01 * CellSize))
			{
				intList.Add(i);
			}
		}
		if (intList.Count != 0)
		{
			bcs.FlowBoundaryConditions.Add(Ras.Engine.BoundaryCondition.ConstantFaceValue(intList.ToArray(), 0.0));
		}
	}

	private void AddUpstreamFlowProfileBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		double x = ((double)CellsTall - 1.0) / 2.0 * CellSize;
		double cellsTallCellSizeTerm = (double)CellsTall / 2.0 * CellSize;
		double sqrtHorizMixCoeff = Math.Sqrt(Turbulence.HorizMixCoeff);
		double d = Slope * SolverControl.GravSI / Math.Pow(sqrtHorizMixCoeff * CellSize, 2.0);
		Polyline pline = Polyline.FromSegment(mesh.Extent.LeftWall.Scale(0.01, 0.99));
		Polyline magnetizedPline;
		foreach (int ordinal in from pf in mesh.PerimeterFacesAlongPolyline(pline, out magnetizedPline)
			select pf.Face)
		{
			double x2 = Math.Abs(mesh.FaceMidPoint(ordinal).Y - cellsTallCellSizeTerm);
			double x2Term = 2.0 / 3.0 * Math.Sqrt(d) * (Math.Pow(x, 1.5) - Math.Pow(x2, 1.5));
			if (!(x2Term <= 0.0))
			{
				double downstreamStageCellSizeTerm = x2Term * DownstreamStage * CellSize;
				bcs.FlowBoundaryConditions.Add(Ras.Engine.BoundaryCondition.ConstantFaceValue(new int[1] { ordinal }, downstreamStageCellSizeTerm));
			}
		}
	}
}
