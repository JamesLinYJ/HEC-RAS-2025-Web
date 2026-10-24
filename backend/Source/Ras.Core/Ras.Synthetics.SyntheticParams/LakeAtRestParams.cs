using System;
using Geospatial.Vectors;

namespace Ras.Synthetics.SyntheticParams;

public class LakeAtRestParams : BasicRectangleParams
{
	public double BedAmplitude = 1.0;

	public double StillWaterStage = 2.0;

	public static LakeAtRestParams FullyWet(bool hexMesh = false)
	{
		return Create(2.0, hexMesh);
	}

	public static LakeAtRestParams Emerged(bool hexMesh = false)
	{
		return Create(0.2, hexMesh);
	}

	private static LakeAtRestParams Create(double stage, bool hexMesh)
	{
		return new LakeAtRestParams
		{
			CellsWide = 40,
			CellsTall = 40,
			CellSize = 25.0,
			BedAmplitude = 1.0,
			StillWaterStage = stage,
			HexMesh = hexMesh,
			NValue = 0.03f,
			SolveDt = 1.0,
			SolveDuration = 600.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		double d = Math.PI * 2.0 * (pt.X - SystemExtent.MinX) / SystemExtent.Width;
		double d2 = Math.PI * 2.0 * (pt.Y - SystemExtent.MinY) / SystemExtent.Height;
		return (float)(BedAmplitude * Math.Cos(d) * Math.Cos(d2));
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point _, double _) => (float)StillWaterStage;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point _) => (float)StillWaterStage;
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point _, double _) => default;
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}
}
