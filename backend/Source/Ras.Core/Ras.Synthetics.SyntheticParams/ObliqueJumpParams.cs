using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class ObliqueJumpParams : InOutPlanarParams
{
	public double WallKinkX = 10.0;

	public double WallAngleDeg = 8.95;

	public double InflowDepth = 1.0;

	public double InflowVelocity = 8.57;

	public bool SeedVelocity = true;

	private const double G = 9.81;

	private const double WallKinkClearance = 1.0;

	private const double NMinFloor = 0.005;

	private bool _precomputed;

	private readonly object _precomputeLock = new object();

	private double _beta;

	private double _h2;

	private Vector _v1;

	private Vector _v2;

	private Mesh _mesh;

	public override UnitSystem Units => UnitSystem.SI;

	public double Froude1 => InflowVelocity / Math.Sqrt(9.81 * InflowDepth);

	public double UnitDischarge => InflowVelocity * InflowDepth;

	public double DomainWidth => (double)CellsTall * CellSize;

	private double Theta => WallAngleDeg * Math.PI / 180.0;

	public double ShockAngleDeg
	{
		get
		{
			EnsurePrecomputed();
			return _beta * 180.0 / Math.PI;
		}
	}

	public double DownstreamDepth
	{
		get
		{
			EnsurePrecomputed();
			return _h2;
		}
	}

	public new static ObliqueJumpParams Default()
	{
		ObliqueJumpParams obliqueJumpParams = new ObliqueJumpParams();
		obliqueJumpParams.CellsWide = 80;
		obliqueJumpParams.CellsTall = 60;
		obliqueJumpParams.CellSize = 0.5;
		obliqueJumpParams.Slope = 0.0;
		obliqueJumpParams.NValue = 0f;
		obliqueJumpParams.RampSeconds = 0.0;
		obliqueJumpParams.SolveDt = 0.02;
		obliqueJumpParams.SolveDuration = 40.0;
		obliqueJumpParams.InitBCs();
		return obliqueJumpParams;
	}

	public void InitBCs()
	{
		UpstreamFlow = UnitDischarge * DomainWidth;
		UpstreamEGSlope = 2.5E-05 * UnitDischarge * UnitDischarge / Math.Pow(InflowDepth, 3.3333333333333335);
		DownstreamStage = InflowDepth;
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		double maxX = SystemExtent.MaxX;
		double maxY = SystemExtent.MaxY;
		double thetaTerm = Math.Tan(Theta);
		double y = thetaTerm * (maxX - WallKinkX);
		Polygon perimeter = new Polygon(new List<Point>
		{
			new Point(0.0, 0.0),
			new Point(WallKinkX, 0.0),
			new Point(maxX, y),
			new Point(maxX, maxY),
			new Point(0.0, maxY)
		});
		List<Point> points = new List<Point>();
		for (int i = 0; i < CellsTall; i++)
		{
			double yComponent = ((double)i + 0.5) * CellSize;
			for (int j = 0; j < CellsWide; j++)
			{
				double xComponent = ((double)j + 0.5) * CellSize;
				double xComponentWallKinkXTerm = ((xComponent > WallKinkX) ? (thetaTerm * (xComponent - WallKinkX)) : 0.0);
				if (yComponent > xComponentWallKinkXTerm)
				{
					points.Add(new Point(xComponent, yComponent));
				}
			}
		}
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!MeshFactory.TryCreateMesh(perimeter, points, null, out _mesh, out var error, meshGenerationParams))
		{
			throw new Exception($"ObliqueJump mesh generation failed: {error}");
		}
		return _mesh;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) =>
		{
			EnsurePrecomputed();
			return (float)(InState2(pt) ? _h2 : InflowDepth);
		};
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		base.UpdateInitialState(state, mesh, props);
		if (!SeedVelocity)
		{
			return;
		}
		Func<Point, double, Vector> func = AnalyticalVelocityFunction();
		for (int i = 0; i < state.FaceCount; i++)
		{
			if (mesh.Faces[i].cellB >= 0 && !(state.FaceArea[i] <= 0.0))
			{
				Vector vector = func(mesh.FaceMidPoint(i), 0.0);
				Vector other = -1.0 * mesh.FaceSegment(i).PerpendicularUnitVector();
				state.FaceVelocity[i] = vector.Dot(other);
				state.FaceFlow[i] = state.FaceArea[i] * state.FaceVelocity[i];
			}
		}
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		EnsurePrecomputed();
		Extent extent = CreateMesh().Extent;
		Polyline polyline = Polyline.FromSegment(extent.LeftWall.Scale(0.01, 0.99));
		double betaMaxXTerm = Math.Tan(_beta) * (extent.MaxX - WallKinkX);
		double thetaMaxXTerm = Math.Tan(Theta) * (extent.MaxX - WallKinkX);
		double maxYExtentTerm = (extent.MaxY - betaMaxXTerm) / extent.Height;
		double tB = (extent.MaxY - (thetaMaxXTerm + 1.0)) / extent.Height;
		Polyline pl = Polyline.FromSegment(extent.RightWall.Scale(0.01, maxYExtentTerm));
		Polyline pl2 = Polyline.FromSegment(extent.RightWall.Scale(maxYExtentTerm, tB));
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		BoundaryConditionLine upstreamBC = GetUpstreamBC(bc, polyline);
		if (upstreamBC != null)
		{
			boundaryConditionLines.Add(upstreamBC);
		}
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(bc, polyline, InflowDepth);
		boundaryConditionLine.Name = "InletStage";
		boundaryConditionLines.Add(boundaryConditionLine);
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantStage(bc, pl, InflowDepth);
		boundaryConditionLine2.Name = "OutletState1";
		BoundaryConditionLine boundaryConditionLine3 = BoundaryConditionLine.ConstantStage(bc, pl2, _h2);
		boundaryConditionLine3.Name = "OutletState2";
		boundaryConditionLines.Add(boundaryConditionLine2);
		boundaryConditionLines.Add(boundaryConditionLine3);
		return boundaryConditionLines;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			EnsurePrecomputed();
			return InDryWedge(pt) ? (-9999f) : ((float)(InState2(pt) ? _h2 : InflowDepth));
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) =>
		{
			EnsurePrecomputed();
			if (InDryWedge(pt))
			{
				return new Vector(0.0, 0.0);
			}
			return (!InState2(pt)) ? _v1 : _v2;
		};
	}

	private bool InDryWedge(Point pt)
	{
		if (pt.X >= WallKinkX)
		{
			return pt.Y < Math.Tan(Theta) * (pt.X - WallKinkX);
		}
		return false;
	}

	private bool InState2(Point pt)
	{
		if (pt.X < WallKinkX)
		{
			return false;
		}
		double thetaPtTerm = Math.Tan(Theta) * (pt.X - WallKinkX);
		double betaPtTerm = Math.Tan(_beta) * (pt.X - WallKinkX);
		if (pt.Y >= thetaPtTerm)
		{
			return pt.Y <= betaPtTerm;
		}
		return false;
	}

	private void EnsurePrecomputed()
	{
		if (_precomputed)
		{
			return;
		}
		lock (_precomputeLock)
		{
			if (!_precomputed)
			{
				double froude = Froude1;
				double beta = SolveShockAngle(froude, Theta);
				double froudeBetaTerm = froude * Math.Sin(beta);
				double computed = 0.5 * (Math.Sqrt(1.0 + 8.0 * froudeBetaTerm * froudeBetaTerm) - 1.0);
				double inflowVelocityBetaTerm = InflowVelocity * Math.Cos(beta) / Math.Cos(beta - Theta);
				_beta = beta;
				_h2 = InflowDepth * computed;
				_v1 = new Vector(InflowVelocity, 0.0);
				_v2 = new Vector(inflowVelocityBetaTerm * Math.Cos(Theta), inflowVelocityBetaTerm * Math.Sin(Theta));
				_precomputed = true;
			}
		}
	}

	private static double SolveShockAngle(double fr1, double theta)
	{
		double fr1Term = Math.Asin(1.0 / fr1);
		double computed = fr1Term;
		double thetaOfBeta = 0.0;
		for (int i = 0; i <= 2000; i++)
		{
			double piTerm = fr1Term + (Math.PI / 2.0 - fr1Term) * (double)i / 2000.0;
			double thetaOfBeta2 = ThetaOfBeta(fr1, piTerm);
			if (thetaOfBeta2 > thetaOfBeta)
			{
				thetaOfBeta = thetaOfBeta2;
				computed = piTerm;
			}
		}
		if (theta >= thetaOfBeta)
		{
			throw new InvalidOperationException($"Deflection {theta * 180.0 / Math.PI:F2}° exceeds the detachment maximum {thetaOfBeta * 180.0 / Math.PI:F2}° for Fr1 = {fr1:F3}; no attached oblique jump exists.");
		}
		double computed2 = fr1Term;
		double computed3 = computed;
		for (int j = 0; j < 100; j++)
		{
			double computed4 = 0.5 * (computed2 + computed3);
			if (ThetaOfBeta(fr1, computed4) - theta > 0.0)
			{
				computed3 = computed4;
			}
			else
			{
				computed2 = computed4;
			}
		}
		return 0.5 * (computed2 + computed3);
	}

	private static double ThetaOfBeta(double fr1, double beta)
	{
		double fr1BetaTerm = fr1 * Math.Sin(beta);
		double computed = 0.5 * (Math.Sqrt(1.0 + 8.0 * fr1BetaTerm * fr1BetaTerm) - 1.0);
		double betaTerm = Math.Tan(beta);
		return Math.Atan(betaTerm * (computed - 1.0) / (computed + betaTerm * betaTerm));
	}
}
