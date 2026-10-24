using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class SuddenExpansionParams : InOutPlanarParams
{
	private const double LowFlow = 0.01817;

	private const double HighFlow = 0.03854;

	public const double DomainLength = 18.0;

	public const double NarrowWidth = 0.6;

	public const double ExpandedWidth = 1.2;

	public const double ExpansionX = 7.7;

	public const double Dy = 0.025;

	public const double Dx = 0.05;

	public const double TopWallY = 1.2;

	public const double NarrowBottomY = 0.6;

	public const double WallThickness = 0.001;

	public const double OpenTopY = 1.199;

	public const double NarrowOpenBottomY = 0.601;

	public const double WideOpenBottomY = 0.001;

	private const float WallHeight = 2f;

	private Mesh _mesh;

	public static SuddenExpansionParams Create(bool isLowFlow)
	{
		return new SuddenExpansionParams
		{
			CellSize = 0.025,
			CellSizeX = 0.05,
			CellsTall = (int)Math.Round(47.99999999999999),
			CellsWide = (int)Math.Round(360.0),
			Slope = 0.0,
			NValue = 0.015f,
			UpstreamFlow = (isLowFlow ? 0.01817 : 0.03854),
			DownstreamStage = 0.11,
			RampSeconds = 100.0,
			SolveDt = 0.05,
			SolveDuration = 600.0,
			UpstreamEGSlope = 0.01,
			Turbulence = new TurbulenceParams
			{
				BaseEddyViscosity = 0.0,
				LongMixCoeff = 0.3,
				TransMixCoeff = 0.1,
				HorizMixCoeff = 0.05
			}
		};
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		sc.dtMinMicros = 1000L;
		if (sc.timeStepping == SolverControl.TimeStepping.Implicit)
		{
			sc.Turbulence.LongMixCoeff = 0.0;
			sc.Turbulence.TransMixCoeff = 0.0;
		}
	}

	public override float TerrainElevation(Point pt)
	{
		double ptTerm = ((pt.X < 7.7) ? 0.601 : 0.001);
		if (pt.Y < ptTerm || pt.Y > 1.199)
		{
			return 2f;
		}
		return SimplePlanarTerrain(pt);
	}

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		Polygon perimeter = new Polygon(new List<Point>
		{
			new Point(0.0, 0.6),
			new Point(7.7, 0.6),
			new Point(7.7, 0.0),
			new Point(18.0, 0.0),
			new Point(18.0, 1.2),
			new Point(0.0, 1.2)
		});
		List<Point> points = new List<Point>();
		for (int i = 0; i < CellsTall; i++)
		{
			double yComponent = ((double)i + 0.5) * 0.025;
			for (int j = 0; j < CellsWide; j++)
			{
				double xComponent = ((double)j + 0.5) * 0.05;
				double xComponentTerm = ((xComponent < 7.7) ? 0.6 : 0.0);
				if (yComponent > xComponentTerm && yComponent < 1.2)
				{
					points.Add(new Point(xComponent, yComponent));
				}
			}
		}
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!MeshFactory.TryCreateMesh(perimeter, points, null, out _mesh, out var error, meshGenerationParams))
		{
			throw new Exception($"SuddenExpansion mesh generation failed: {error}");
		}
		return _mesh;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		double computed = 0.005;
		double computed2 = 0.001;
		Polyline pline = new Polyline(new Point[2]
		{
			new Point(extent.MinX - computed, 0.6 + computed2),
			new Point(extent.MinX - computed, 1.2 - computed2)
		});
		Polyline pline2 = new Polyline(new Point[2]
		{
			new Point(extent.MaxX + computed, extent.MinY + computed2),
			new Point(extent.MaxX + computed, 1.2 - computed2)
		});
		BoundaryConditionLine upstreamBC = GetUpstreamBC(bc, pline);
		BoundaryConditionLine downstreamBC = GetDownstreamBC(bc, pline2);
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		if (upstreamBC != null)
		{
			boundaryConditionLines.Add(upstreamBC);
		}
		if (downstreamBC != null)
		{
			boundaryConditionLines.Add(downstreamBC);
		}
		return boundaryConditionLines;
	}

	public override IReadOnlyList<MeasuredVelocitySample> GetMeasuredVelocitySamples()
	{
		double[] coefficients = new double[42]
		{
			7.7, 7.7, 7.7, 7.7, 7.7, 8.7, 8.7, 8.7, 8.7, 8.7,
			8.7, 8.7, 8.7, 9.7, 9.7, 9.7, 9.7, 9.7, 9.7, 9.7,
			9.7, 10.7, 10.7, 10.7, 10.7, 10.7, 10.7, 10.7, 10.7, 11.7,
			11.7, 11.7, 11.7, 11.7, 11.7, 11.7, 12.7, 12.7, 12.7, 12.7,
			12.7, 12.7
		};
		double[] coefficientsArray = new double[42]
		{
			0.7, 0.8, 0.9, 1.0, 1.1, 0.4, 0.5, 0.6, 0.7, 0.8,
			0.9, 1.0, 1.1, 0.1, 0.3, 0.4, 0.5, 0.6, 0.7, 0.9,
			1.1, 0.1, 0.2, 0.3, 0.4, 0.6, 0.7, 0.9, 1.1, 0.1,
			0.3, 0.4, 0.6, 0.8, 0.9, 1.1, 0.1, 0.3, 0.5, 0.7,
			0.9, 1.1
		};
		double[] doubleValues = new double[42]
		{
			0.2942, 0.3081, 0.3104, 0.3056, 0.2882, 0.0486, 0.0839, 0.2006, 0.2918, 0.3019,
			0.3149, 0.3091, 0.2588, -0.0666, 0.0597, 0.0663, 0.1492, 0.2288, 0.278, 0.3043,
			0.2734, -0.048, -0.0746, 0.0488, 0.0969, 0.2333, 0.2651, 0.2767, 0.2495, -0.0587,
			0.0704, 0.1305, 0.2305, 0.2477, 0.2555, 0.2217, 0.0525, 0.1027, 0.1885, 0.2232,
			0.2141, 0.1733
		};
		List<MeasuredVelocitySample> measuredVelocitySamples = new List<MeasuredVelocitySample>(doubleValues.Length);
		for (int i = 0; i < doubleValues.Length; i++)
		{
			Point location = new Point(coefficients[i], coefficientsArray[i]);
			Vector velocity = new Vector(doubleValues[i], 0.0);
			measuredVelocitySamples.Add(new MeasuredVelocitySample(location, -1.0, velocity));
		}
		return measuredVelocitySamples;
	}
}
