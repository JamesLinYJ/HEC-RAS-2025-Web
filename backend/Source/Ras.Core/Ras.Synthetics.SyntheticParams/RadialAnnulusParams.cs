using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics.SyntheticParams;

public class RadialAnnulusParams : BaseParams
{
	public enum BalanceMode
	{
		ShallowWater,
		DiffusiveWave
	}

	public double InnerRadius = 100.0;

	public double OuterRadius = 500.0;

	public double SectorAngle = Math.PI / 2.0;

	public double CellSize = 25.0;

	public int PointsPerSide = 240;

	public double Depth = 2.0;

	public double Discharge = 20.0;

	public double OuterStage = 10.0;

	public BalanceMode Mode;

	private Mesh _mesh;

	public override Extent SystemExtent => new Extent(0.0, OuterRadius, 0.0, OuterRadius);

	private double VelocityScale => Discharge / (SectorAngle * Depth);

	private double FrictionScale => (double)(NValue * NValue) * VelocityScale * VelocityScale / Math.Pow(Depth, 1.3333333333333333);

	public static RadialAnnulusParams Create(BalanceMode mode)
	{
		return new RadialAnnulusParams
		{
			Mode = mode,
			NValue = 0.03f,
			SolveDt = ((mode == BalanceMode.DiffusiveWave) ? 5.0 : 0.5),
			SolveDuration = 600.0
		};
	}

	public double StageAtRadius(double radius)
	{
		double outerStageFrictionScaleTerm = OuterStage + FrictionScale * (1.0 / radius - 1.0 / OuterRadius);
		if (Mode == BalanceMode.ShallowWater)
		{
			double velocityScalePolynomial = VelocityScale * VelocityScale;
			outerStageFrictionScaleTerm -= velocityScalePolynomial / (2.0 * SolverControl.GravSI) * (1.0 / (radius * radius) - 1.0 / (OuterRadius * OuterRadius));
		}
		return outerStageFrictionScaleTerm;
	}

	private static double Radius(Point pt)
	{
		return Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
	}

	public override float TerrainElevation(Point pt)
	{
		double radius = Radius(pt);
		if (radius < InnerRadius - CellSize || radius > OuterRadius + CellSize)
		{
			return -9999f;
		}
		return (float)(StageAtRadius(radius) - Depth);
	}

	public Vector Velocity(Point pt)
	{
		double radius = Radius(pt);
		if (radius <= 0.0)
		{
			return default;
		}
		double velocityScaleOverRadius = VelocityScale / radius;
		return new Vector(velocityScaleOverRadius * pt.X / radius, velocityScaleOverRadius * pt.Y / radius);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double _) => (float)StageAtRadius(Radius(pt));
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)StageAtRadius(Radius(pt));
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double _) => Velocity(pt);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double _) => (float)Velocity(pt).Length;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double _) => (float)Velocity(pt).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point pt, double _) => (float)Velocity(pt).Y;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point pt) => (float)Velocity(pt).X;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point pt) => (float)Velocity(pt).Y;
	}

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		Topology topology = CreateTopology();
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out _mesh);
		return _mesh;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		topology = CreateTopology();
		return true;
	}

	private Topology CreateTopology()
	{
		return QuarterAnnulusParams.AnnularTopology(0.0, SectorAngle, InnerRadius, OuterRadius, CellSize, PointsPerSide);
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition boundaryCondition)
	{
		double scaledCellSize = 0.2 * CellSize;
		Polyline pl = ArcPolyline(InnerRadius - scaledCellSize, reverse: false);
		Polyline pl2 = ArcPolyline(OuterRadius + scaledCellSize, reverse: true);
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(boundaryCondition, pl, Discharge);
		boundaryConditionLine.Name = "Inner inflow";
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantStage(boundaryCondition, pl2, OuterStage);
		boundaryConditionLine2.Name = "Outer stage";
		int ordinal = 2;
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>(ordinal);
		CollectionsMarshal.SetCount(boundaryConditionLines, ordinal);
		Span<BoundaryConditionLine> boundaryConditionLinesSpan = CollectionsMarshal.AsSpan(boundaryConditionLines);
		int ordinal2 = 0;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine;
		ordinal2++;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine2;
		return boundaryConditionLines;
	}

	private Polyline ArcPolyline(double radius, bool reverse)
	{
		List<Point> points = new List<Point>(PointsPerSide);
		for (int i = 0; i < PointsPerSide; i++)
		{
			double pointsPerSideTerm = (double)i / (double)(PointsPerSide - 1);
			double reverseSectorAngleTerm = (reverse ? (SectorAngle * (1.0 - pointsPerSideTerm)) : (SectorAngle * pointsPerSideTerm));
			points.Add(new Point(radius * Math.Cos(reverseSectorAngleTerm), radius * Math.Sin(reverseSectorAngleTerm)));
		}
		return new Polyline(points);
	}
}
