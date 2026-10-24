using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Logging;

namespace Ras.Synthetics.SyntheticParams;

public class CadamBendParams : BaseParams
{
	public const double ReservoirLength = 2.39;

	public const double ReservoirWidth = 2.44;

	public const double ChannelWidth = 0.495;

	public const double BendOuterX = 6.805;

	public const double BendInnerX = 6.31;

	public const double ChannelBottomY = 0.4425;

	public const double ChannelTopY = 0.9375;

	public const double DomainTopY = 4.0;

	public const double CellSize = 0.05;

	public const double InitialDepth = 0.2;

	private Topology _topology;

	private Mesh _mesh;

	public override Extent SystemExtent => new Extent(0.0, 6.805, 0.0, 4.0);

	public static CadamBendParams Default()
	{
		return new CadamBendParams
		{
			NValue = 0.0095f,
			SolveDt = 0.005,
			SolveDuration = 15.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (!(pt.X <= 2.39) || !(pt.Y <= 2.44)) ? 0f : 0.2f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition boundaryCondition)
	{
		Polyline pl = Polyline.FromSegment(new Segment(new Point(6.31, 4.01), new Point(6.805, 4.01)));
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(boundaryCondition, pl, 0.0);
		boundaryConditionLine.Name = "Open outlet";
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
		return new _003C_003Ez__ReadOnlyArray<NamedResultPoint>(new NamedResultPoint[5]
		{
			new NamedResultPoint(new Point(2.24, 0.69), "G2"),
			new NamedResultPoint(new Point(4.24, 0.69), "G3"),
			new NamedResultPoint(new Point(5.74, 0.69), "G4"),
			new NamedResultPoint(new Point(6.56, 1.51), "G5"),
			new NamedResultPoint(new Point(6.56, 3.01), "G6")
		});
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

	private void EnsureGeometry()
	{
		if (_mesh == null)
		{
			Point[] points = new Point[10]
			{
				new Point(0.0, 0.0),
				new Point(2.39, 0.0),
				new Point(2.39, 0.4425),
				new Point(6.805, 0.4425),
				new Point(6.805, 4.0),
				new Point(6.31, 4.0),
				new Point(6.31, 0.9375),
				new Point(2.39, 0.9375),
				new Point(2.39, 2.44),
				new Point(0.0, 2.44)
			};
			Topology topology = new Topology();
			Point[] pointsArray = points;
			foreach (Point pt in pointsArray)
			{
				topology.AddNode(pt);
			}
			for (int j = 0; j < points.Length; j++)
			{
				topology.AddArc(j, (j + 1) % points.Length, null);
			}
			topology.AddRegionControlPoint(new Point(1.0, 1.0));
			topology.EnsureMeshMetadataColumns();
			topology.SetControlPointData(0, PatchType.Cartesian, SizeFunction.Constant, 0.05f, 0.1f, 0.05f, 0f, null, 0f, 1f);
			topology.SetControlPointRecipeString(0, "");
			for (int k = 0; k < points.Length; k++)
			{
				topology.SetArcData(k, LinearSizeFunction.Constant, 0.05f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
			}
			topology.Complete();
			OperationResult operationResult = topology.BuildConceptualMesh(out var _, out var finalMesh);
			if (!operationResult.Result || finalMesh == null || !finalMesh.IsValid())
			{
				throw new InvalidOperationException("Could not generate CADAM bend mesh: " + operationResult.GetConcatenatedMessages());
			}
			_topology = topology;
			_mesh = finalMesh;
		}
	}
}
