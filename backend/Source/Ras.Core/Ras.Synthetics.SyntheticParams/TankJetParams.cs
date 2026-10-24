using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Utility.Logging;

namespace Ras.Synthetics.SyntheticParams;

public class TankJetParams : BaseParams
{
	public const double DomainLength = 120.0;

	public const double DomainWidth = 100.0;

	public const double InletWidth = 12.0;

	public const double CellSize = 2.0;

	public const double Inflow = 50.0;

	public const double InflowRampDuration = 50.0;

	public const double Stage = 5.0;

	private Topology _topology;

	private Mesh _mesh;

	public static double InflowVelocity => 5.0 / 6.0;

	public double DomainRotationDegrees { get; init; }

	public override double SolveDt { get; set; } = 2.0;

	public override double SolveDuration { get; set; } = 1800.0;

	public override int ReportFrequency { get; set; } = 10;

	public override float NValue { get; set; }

	public override Extent SystemExtent
	{
		get
		{
			double computed = 110.0 / Math.Sqrt(2.0);
			return new Extent(60.0 - computed, 60.0 + computed, 50.0 - computed, 50.0 + computed);
		}
	}

	public static TankJetParams Aligned()
	{
		return new TankJetParams
		{
			DomainRotationDegrees = 0.0
		};
	}

	public static TankJetParams Rotated()
	{
		return new TankJetParams
		{
			DomainRotationDegrees = 45.0
		};
	}

	public override float TerrainElevation(Point point)
	{
		return 0f;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point _) => 5f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point _, double _) => 5f;
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point point, double time) => AnalyticalVelocity(point, time);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point point, double time) =>
		{
			Vector vector = AnalyticalVelocity(point, time);
			return (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
		};
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point point, double time) => (float)AnalyticalVelocity(point, time).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point point, double time) => (float)AnalyticalVelocity(point, time).Y;
	}

	public override Mesh CreateMesh()
	{
		EnsureGeometry();
		return _mesh;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		EnsureGeometry();
		topology = _topology;
		return true;
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		double computed = 44.0;
		double computed2 = computed + 12.0;
		Polyline pl = Polyline.FromSegment(new Segment(ToWorld(new Point(-0.01, computed + 0.01)), ToWorld(new Point(-0.01, computed2 - 0.01))));
		Polyline pl2 = Polyline.FromSegment(new Segment(ToWorld(new Point(120.01, 99.99)), ToWorld(new Point(120.01, 0.01))));
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl)
		{
			Name = "Inflow"
		};
		List<PayloadBase> payloads = boundaryConditionLine.Payloads;
		_003C_003Ey__InlineArray3<double> buffer = default;
		buffer[0] = 0.0;
		buffer[1] = 50.0;
		buffer[2] = SolveDuration;
		payloads.Add(new TimeSeriesPayload("Flow", IrregularRelativeTimeSeriesDefinition.FromSeconds(buffer), new double[3] { 0.0, 50.0, 50.0 }));
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantStage(bc, pl2, 5.0);
		boundaryConditionLine2.Name = "Stage";
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

	public override IReadOnlyList<NamedResultLine> GetResultLines()
	{
		double[] lookupTable = new double[7] { 1.0, 5.0, 25.0, 50.0, 75.0, 90.0, 110.0 };
		List<NamedResultLine> namedResultLines = new List<NamedResultLine>(lookupTable.Length);
		double[] lookupTableArray = lookupTable;
		foreach (double xComponent in lookupTableArray)
		{
			Polyline line = new Polyline(new Point[2]
			{
				ToWorld(new Point(xComponent, 0.0)),
				ToWorld(new Point(xComponent, 100.0))
			});
			namedResultLines.Add(new NamedResultLine(line, $"X{xComponent:0}"));
		}
		return namedResultLines;
	}

	public Point ToWorld(Point local)
	{
		if (DomainRotationDegrees == 0.0)
		{
			return local;
		}
		double domainRotationDegreesPiTerm = DomainRotationDegrees * Math.PI / 180.0;
		double localTerm = local.X - 60.0;
		double localTerm2 = local.Y - 50.0;
		double computed = Math.Cos(domainRotationDegreesPiTerm);
		double computed2 = Math.Sin(domainRotationDegreesPiTerm);
		return new Point(60.0 + localTerm * computed - localTerm2 * computed2, 50.0 + localTerm * computed2 + localTerm2 * computed);
	}

	public Point ToLocal(Point world)
	{
		if (DomainRotationDegrees == 0.0)
		{
			return world;
		}
		double domainRotationDegreesPiTerm = (0.0 - DomainRotationDegrees) * Math.PI / 180.0;
		double worldTerm = world.X - 60.0;
		double worldTerm2 = world.Y - 50.0;
		double computed = Math.Cos(domainRotationDegreesPiTerm);
		double computed2 = Math.Sin(domainRotationDegreesPiTerm);
		return new Point(60.0 + worldTerm * computed - worldTerm2 * computed2, 50.0 + worldTerm * computed2 + worldTerm2 * computed);
	}

	public Vector StreamwiseUnitVector()
	{
		double domainRotationDegreesPiTerm = DomainRotationDegrees * Math.PI / 180.0;
		return new Vector(Math.Cos(domainRotationDegreesPiTerm), Math.Sin(domainRotationDegreesPiTerm));
	}

	private Vector AnalyticalVelocity(Point point, double time)
	{
		Point point2 = ToLocal(point);
		double computed = 44.0;
		double computed2 = computed + 12.0;
		if (point2.Y < computed || point2.Y > computed2)
		{
			return new Vector(0.0, 0.0);
		}
		double timeTerm = Math.Clamp(time / 50.0, 0.0, 1.0) * 50.0 / 60.0;
		Vector vector = StreamwiseUnitVector();
		return new Vector(timeTerm * vector.X, timeTerm * vector.Y);
	}

	private Point[] DomainCorners()
	{
		return new Point[4]
		{
			ToWorld(new Point(0.0, 0.0)),
			ToWorld(new Point(120.0, 0.0)),
			ToWorld(new Point(120.0, 100.0)),
			ToWorld(new Point(0.0, 100.0))
		};
	}

	private void EnsureGeometry()
	{
		if (_mesh != null)
		{
			return;
		}
		Point[] domainCorners = DomainCorners();
		Topology topology = new Topology();
		Point[] domainCornersArray = domainCorners;
		foreach (Point pt in domainCornersArray)
		{
			topology.AddNode(pt);
		}
		topology.AddArc(0, 1, null);
		topology.AddArc(1, 2, null);
		topology.AddArc(2, 3, null);
		topology.AddArc(3, 0, null);
		topology.AddRegionControlPoint(ToWorld(new Point(60.0, 50.0)));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.Cartesian, SizeFunction.Constant, 2f, 0.1f, 2f, 0f, null, 0f, 1f);
		topology.SetControlPointRecipeString(0, "");
		for (int j = 0; j < 4; j++)
		{
			topology.SetArcData(j, LinearSizeFunction.Constant, 2f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		}
		topology.Complete();
		if (DomainRotationDegrees == 0.0)
		{
			_topology = topology;
			_mesh = MeshFactory.FromExtent(new Extent(0.0, 120.0, 0.0, 100.0), 60, 50);
			return;
		}
		OperationResult operationResult = topology.BuildConceptualMesh(out var _, out var finalMesh);
		if (!operationResult.Result || finalMesh == null || !finalMesh.IsValid())
		{
			throw new InvalidOperationException("Could not generate tank-jet mesh: " + operationResult.GetConcatenatedMessages());
		}
		_topology = topology;
		_mesh = finalMesh;
	}
}
