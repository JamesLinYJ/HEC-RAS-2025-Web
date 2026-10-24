using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Vectors;

public abstract class TinFactory
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("TinFactory");

	protected Stack<int> _deletedTrianglesStack;

	protected Vector _normalizedOffset;

	protected double _nearPointTolerance;

	protected Tin _tin;

	public TriangulationParameters TriangulationParameters { get; set; }

	public Vector ActualNormalizedOffset => _normalizedOffset;

	public double ActualNearPointTolerance => _nearPointTolerance;

	public Tin Tin => _tin;

	public bool HasZ => _tin.HasZ;

	public bool HasM => _tin.HasM;

	protected TinFactory(TriangulationParameters tparams = null)
	{
		if (tparams == null)
		{
			tparams = new TriangulationParameters();
		}
		TriangulationParameters = tparams;
		_deletedTrianglesStack = new Stack<int>();
	}

	protected int AddTriangle(int p0, int p1, int p2, int at0, int at1, int at2)
	{
		TriangleDefinition triangleDefinition = new TriangleDefinition(p0, p1, p2, at0, at1, at2);
		int popDeletedTrianglesStackTerm;
		if (_deletedTrianglesStack.Count > 0)
		{
			popDeletedTrianglesStackTerm = _deletedTrianglesStack.Pop();
			_tin.TriangleDefinition[popDeletedTrianglesStackTerm] = triangleDefinition;
		}
		else
		{
			popDeletedTrianglesStackTerm = _tin.TriangleCount;
			_tin.TriangleDefinition.Add(triangleDefinition);
		}
		return popDeletedTrianglesStackTerm;
	}

	protected virtual void SetConstrainedEdge(ConstrainedEdge seg)
	{
	}

	protected void MarkDeleted(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		triangleDefinition.IsDeleted = true;
		_tin.TriangleDefinition[t] = triangleDefinition;
	}

	protected static bool IsXColinear(Point p1, Point p2, Point p3)
	{
		if (p1.X == p2.X)
		{
			return p2.X == p3.X;
		}
		return false;
	}

	protected static bool IsYColinear(Point p1, Point p2, Point p3)
	{
		if (p1.Y == p2.Y)
		{
			return p2.Y == p3.Y;
		}
		return false;
	}

	public static double ComputeNearPointTolerance(Extent extent)
	{
		return Math.Sqrt(extent.Area) * 1E-08;
	}
}
