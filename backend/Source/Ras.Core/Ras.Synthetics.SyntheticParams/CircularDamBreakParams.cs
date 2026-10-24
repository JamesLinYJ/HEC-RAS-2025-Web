using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Ras.Synthetics.SyntheticParams;

public class CircularDamBreakParams : BasicRectangleParams
{
	public double CenterX = 20.0;

	public double CenterY = 20.0;

	public double InitialRadius = 2.5;

	public double InnerDepth = 2.5;

	public double OuterDepth = 1.0;

	public bool TriangleMesh;

	private Mesh _triangleMesh;

	public static CircularDamBreakParams Create(bool triangleMesh = false, double rotationDegrees = 0.0)
	{
		return new CircularDamBreakParams
		{
			CellsWide = 80,
			CellsTall = 80,
			CellSize = 0.5,
			TriangleMesh = triangleMesh,
			RotateDegrees = rotationDegrees,
			NValue = 0f,
			SolveDt = 0.01,
			SolveDuration = 2.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public double InitialDepth(Point pt)
	{
		double ptCenterXTerm = pt.X - CenterX;
		double ptCenterYTerm = pt.Y - CenterY;
		if (!(ptCenterXTerm * ptCenterXTerm + ptCenterYTerm * ptCenterYTerm <= InitialRadius * InitialRadius))
		{
			return OuterDepth;
		}
		return InnerDepth;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)InitialDepth(pt);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override Mesh CreateMesh()
	{
		if (!TriangleMesh)
		{
			return base.CreateMesh();
		}
		if (_triangleMesh != null)
		{
			return _triangleMesh;
		}
		OperationResult operationResult = CreateTriangleTopology().BuildConceptualMesh(out var _, out _triangleMesh);
		if (!operationResult.Result || _triangleMesh == null)
		{
			throw new InvalidOperationException("Circular dam-break triangular mesh generation failed: " + operationResult.GetConcatenatedMessages(";"));
		}
		return _triangleMesh;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		if (!TriangleMesh)
		{
			topology = null;
			return false;
		}
		topology = CreateTriangleTopology();
		return true;
	}

	private Topology CreateTriangleTopology()
	{
		Topology topology = Topology.FromExtent(SystemExtent);
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.TrianglePaver, SizeFunction.Constant, (float)CellSize);
		topology.SetControlPointRecipeString(0, "# None");
		topology.SetControlPointTINResolution(0, 1f);
		for (int i = 0; i < 4; i++)
		{
			topology.SetArcData(i, LinearSizeFunction.Unset, float.NaN, 8, 1f);
		}
		return topology;
	}
}
