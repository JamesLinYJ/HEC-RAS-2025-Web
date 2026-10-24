using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;
using Utility;
using Utility.Units;

namespace Ras.Synthetics;

public class QuarterAnnulusParams : BaseParams
{
	public double InnerRadius = 60960.0;

	public double OuterRadius = 152400.0;

	public double StartAngle;

	public double RotateAmt = Math.PI / 2.0;

	public double CellSize = 7620.0;

	public int PointsPerSide = 300;

	public double TidalPeriodSeconds = 44712.0;

	public double TidalAmplitude = 0.3048;

	public double InnerDepth = 10.02;

	public double OuterDepth = 25.05;

	public double DurationSeconds = 432000.0;

	public double RampSeconds;

	public double DtBc = 300.0;

	public readonly double NBathy = 1.0;

	private Topology _topo;

	private Mesh _mesh;

	private double _A;

	private double _B;

	private double _beta;

	private double _H0;

	private bool _constantsReady;

	public override UnitSystem Units => UnitSystem.SI;

	public override Extent SystemExtent => new Extent(0.0, OuterRadius, 0.0, OuterRadius);

	public double Omega => Math.PI * 2.0 / TidalPeriodSeconds;

	public static QuarterAnnulusParams Create()
	{
		return new QuarterAnnulusParams
		{
			SolveDt = 300.0,
			SolveDuration = 432000.0
		};
	}

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		_topo = AnnularTopology(StartAngle, RotateAmt, InnerRadius, OuterRadius, CellSize, PointsPerSide);
		_mesh = BuildMeshFromTopology(_topo);
		return _mesh;
	}

	protected virtual Mesh BuildMeshFromTopology(Topology topo)
	{
		topo.Complete();
		topo.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		topology = AnnularTopology(StartAngle, RotateAmt, InnerRadius, OuterRadius, CellSize, PointsPerSide);
		return true;
	}

	public override float TerrainElevation(Point pt)
	{
		double computed = 500.0;
		if (!new Extent(0.0 - computed, OuterRadius + computed, 0.0 - computed, OuterRadius + computed).Contains(pt))
		{
			return -9999f;
		}
		double distanceTo = pt.DistanceTo(new Point(0.0, 0.0));
		if (distanceTo < InnerRadius - computed || distanceTo > OuterRadius + computed)
		{
			return -9999f;
		}
		return (float)((0.0 - InnerDepth) * Math.Pow(distanceTo / InnerRadius, NBathy));
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.advectionMethod = SolverControl.AdvectionMethod.None;
		sc.cellVelocityMethod = SolverControl.CellVelocityMethod.WLS;
		sc.faceElevationMethod = FaceElevationMethod.Frozen;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		double rotateAmtOverPointsPerSide = RotateAmt / (double)PointsPerSide;
		List<Point> points = new List<Point>();
		double startAngle = StartAngle;
		double outerRadiusOffset = OuterRadius + 500.0;
		for (int i = 0; i < PointsPerSide; i++)
		{
			points.Add(new Point(outerRadiusOffset * Math.Cos(startAngle), outerRadiusOffset * Math.Sin(startAngle)));
			startAngle += rotateAmtOverPointsPerSide;
		}
		Polyline pl = new Polyline(points);
		int count = 1 + (int)(DurationSeconds / DtBc);
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int j = 0; j < count; j++)
		{
			double dtBcTerm = (doubleValues[j] = DtBc * (double)j);
			double rampSecondsTerm = ((RampSeconds > 0.0) ? Math.Min(1.0, dtBcTerm / RampSeconds) : 1.0);
			double tidalAmplitudePiTerm = TidalAmplitude * Math.Cos(Math.PI * 2.0 * dtBcTerm / TidalPeriodSeconds);
			doubleValuesArray[j] = rampSecondsTerm * tidalAmplitudePiTerm;
		}
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl)
		{
			Name = "Ocean"
		};
		boundaryConditionLine.Payloads.Add(new TimeSeriesPayload("Stage", IrregularRelativeTimeSeriesDefinition.FromSeconds(doubleValues), doubleValuesArray));
		int unity = 1;
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>(unity);
		CollectionsMarshal.SetCount(boundaryConditionLines, unity);
		Span<BoundaryConditionLine> boundaryConditionLinesSpan = CollectionsMarshal.AsSpan(boundaryConditionLines);
		int index = 0;
		boundaryConditionLinesSpan[index] = boundaryConditionLine;
		return boundaryConditionLines;
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		double midAngle = StartAngle + 0.5 * RotateAmt;
		return new _003C_003Ez__ReadOnlyArray<NamedResultPoint>(new NamedResultPoint[3]
		{
			new NamedResultPoint(RadialPoint(InnerRadius), "Station 0"),
			new NamedResultPoint(RadialPoint(0.5 * (InnerRadius + OuterRadius)), "Station 1"),
			new NamedResultPoint(RadialPoint(InnerRadius + 0.75 * (OuterRadius - InnerRadius)), "Station 2")
		});
		Point RadialPoint(double r)
		{
			return new Point(r * Math.Cos(midAngle), r * Math.Sin(midAngle));
		}
	}

	public static Topology AnnularTopology(double StartAngle, double RotateAmt, double InnerRad, double OuterRad, double CellSize, int PointsPerSide)
	{
		Topology topology = new Topology();
		double rotateAmtOverPointsPerSide = RotateAmt / (double)PointsPerSide;
		List<Point> points = new List<Point>();
		List<Point> pointsList = new List<Point>();
		double startAngle = StartAngle;
		for (int i = 0; i < PointsPerSide; i++)
		{
			points.Add(new Point(OuterRad * Math.Cos(startAngle), OuterRad * Math.Sin(startAngle)));
			pointsList.Add(new Point(InnerRad * Math.Cos(startAngle), InnerRad * Math.Sin(startAngle)));
			startAngle += rotateAmtOverPointsPerSide;
		}
		topology.AddNode(points.First());
		topology.AddNode(points.Last());
		topology.AddNode(pointsList.Last());
		topology.AddNode(pointsList.First());
		topology.AddArc(0, 1, points.GetRange(1, points.Count - 2));
		topology.AddArc(3, 2, pointsList.GetRange(1, pointsList.Count - 2));
		topology.AddArc(0, 3, null);
		topology.AddArc(1, 2, null);
		topology.AddRegionControlPoint(0.5 * (points[PointsPerSide / 2] + pointsList[PointsPerSide / 2]));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.TrianglePaver, SizeFunction.Constant, (float)CellSize, 0.1f);
		topology.SetControlPointRecipeString(0, "Default();");
		topology.SetArcData(0, LinearSizeFunction.Constant, (float)CellSize, -1, 1f);
		topology.SetArcData(1, LinearSizeFunction.Constant, (float)CellSize, -1, 1f);
		topology.SetArcData(2, LinearSizeFunction.Constant, (float)CellSize, -1, 1f);
		topology.SetArcData(3, LinearSizeFunction.Constant, (float)CellSize, -1, 1f);
		return topology;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => (float)StageFunc(pt, t);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return VelocityFunc;
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double t) => (float)VelocityFunc(pt, t).Length;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double t) => (float)VelocityFunc(pt, t).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point pt, double t) => (float)VelocityFunc(pt, t).Y;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)StageFunc(pt, 0.0);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point pt) => (float)VelocityFunc(pt, 0.0).X;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point pt) => (float)VelocityFunc(pt, 0.0).Y;
	}

	public override Func<Point, float> InitialFaceStageFunction()
	{
		return (Point pt) => 0f;
	}

	public void SetConstants()
	{
		if (!_constantsReady)
		{
			_H0 = InnerDepth / Math.Pow(InnerRadius, NBathy);
			double sqrtInnerRadius = Math.Sqrt(InnerRadius);
			double sqrtOuterRadius = Math.Sqrt(OuterRadius);
			double tidalAmplitude = TidalAmplitude;
			double gravSI = SolverControl.GravSI;
			double d = Omega * Omega / (gravSI * _H0);
			_beta = Math.Sqrt(d);
			double j1BesselTerm = Bessel.J1(2.0 * _beta * sqrtOuterRadius) * Bessel.Y2(2.0 * _beta * sqrtInnerRadius) - Bessel.J2(2.0 * _beta * sqrtInnerRadius) * Bessel.Y1(2.0 * _beta * sqrtOuterRadius);
			_A = tidalAmplitude * sqrtOuterRadius * Bessel.Y2(2.0 * _beta * sqrtInnerRadius) / j1BesselTerm;
			_B = (0.0 - tidalAmplitude) * sqrtOuterRadius * Bessel.J2(2.0 * _beta * sqrtInnerRadius) / j1BesselTerm;
			_constantsReady = true;
		}
	}

	public double StageFunc(Point pt, double t)
	{
		double r = pt.DistanceTo(new Point(0.0, 0.0));
		return StageFunc(r, t);
	}

	public double StageFunc(double r, double t)
	{
		SetConstants();
		return StageFuncSpatial(r) * Math.Cos(Omega * t);
	}

	public double StageFuncSpatial(double r)
	{
		SetConstants();
		double computed = Math.Sqrt(r);
		return 1.0 / computed * (_A * Bessel.J1(2.0 * _beta * computed) + _B * Bessel.Y1(2.0 * _beta * computed));
	}

	public Vector VelocityFunc(Point pt, double t)
	{
		double distanceTo = pt.DistanceTo(new Point(0.0, 0.0));
		return VelocityFunc(distanceTo, t) / distanceTo * (pt - new Point(0.0, 0.0));
	}

	public double VelocityFunc(double r, double t)
	{
		SetConstants();
		double computed = Math.Sqrt(r);
		return (0.0 - 1.0 / r * ((0.0 - _A) * Bessel.J2(2.0 * _beta * computed) - _B * Bessel.Y2(2.0 * _beta * computed))) * (Omega / (_beta * _H0)) * Math.Sin(Omega * t);
	}
}
