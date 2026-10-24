using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors;

public class TinInterpolator : ISpatialInterpolator
{
	protected Tin _tin;

	protected List<Point> _points;

	public Tin Tin => _tin;

	public TinExtrapolationMethod ExtrapolationMethod { get; set; }

	public Extent ValidExtent
	{
		get
		{
			if (ExtrapolationMethod != TinExtrapolationMethod.None)
			{
				return Extent.Infinite;
			}
			return _tin?.Extent ?? Extent.Infinite;
		}
	}

	public TinInterpolator(TinExtrapolationMethod extrapMethod = TinExtrapolationMethod.NearestSegmentLinear)
	{
		ExtrapolationMethod = extrapMethod;
	}

	public void Initialize(IList<Point> points)
	{
		if (points == null)
		{
			throw new ArgumentNullException("points");
		}
		_points = points.ToList();
		if (_points.Count > 2)
		{
			PointCollection pointCollection = new PointCollection(points);
			DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory(new TriangulationParameters
			{
				EnforcedMBR = pointCollection.Extent.RatioAroundCenter(10.0)
			});
			delaunayTinFactory.AddMassPoints(pointCollection);
			_tin = delaunayTinFactory.ComputeTIN();
		}
	}

	public virtual void Query(Point queryPoint, out int[] indexes, out double[] weights)
	{
		if (_points.Count == 0)
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
		}
		else if (_points.Count == 1)
		{
			indexes = new int[1];
			weights = new double[1];
			weights[0] = 1.0;
		}
		else if (_points.Count == 2)
		{
			new Segment(_points[0], _points[1]).NearestPoint(queryPoint, out var t);
			if (double.IsNaN(t))
			{
				indexes = Array.Empty<int>();
				weights = Array.Empty<double>();
			}
			else if (t <= 0.0)
			{
				indexes = new int[1];
				weights = new double[1] { 1.0 };
			}
			else if (t >= 1.0)
			{
				indexes = new int[1] { 1 };
				weights = new double[1] { 1.0 };
			}
			else
			{
				indexes = new int[2];
				indexes[0] = 0;
				indexes[1] = 1;
				weights = new double[2];
				weights[0] = 1.0 - t;
				weights[1] = t;
			}
		}
		else
		{
			int triangleContainingPoint = _tin.FindTriangleContainingPoint(queryPoint);
			if (triangleContainingPoint == -1 || _tin.TriangleDefinition[triangleContainingPoint].IsDeleted)
			{
				Extrapolate(queryPoint, out indexes, out weights);
			}
			else
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triangleContainingPoint];
				_tin.Triangles[triangleContainingPoint].InterpolationWeights(queryPoint, out var lambda, out var lambda2, out var lambda3);
				indexes = new int[3] { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 };
				weights = new double[3] { lambda, lambda2, lambda3 };
			}
			for (int i = 0; i < indexes.Length; i++)
			{
				indexes[i] -= 8;
			}
		}
	}

	private void Extrapolate(Point queryPoint, out int[] indexes, out double[] weights)
	{
		_tin.NearestPerimeterEdge(queryPoint, out var edgeDef, out var fractionAlong);
		if (ExtrapolationMethod == TinExtrapolationMethod.None)
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
			return;
		}
		if (double.IsNaN(fractionAlong))
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
			return;
		}
		if (fractionAlong == 0.0)
		{
			indexes = new int[1] { edgeDef.APointIndex };
			weights = new double[1] { 1.0 };
			return;
		}
		if (fractionAlong == 1.0)
		{
			indexes = new int[1] { edgeDef.BPointIndex };
			weights = new double[1] { 1.0 };
			return;
		}
		if (ExtrapolationMethod == TinExtrapolationMethod.NearestSegmentLinear)
		{
			indexes = new int[2] { edgeDef.APointIndex, edgeDef.BPointIndex };
			weights = new double[2]
			{
				1.0 - fractionAlong,
				fractionAlong
			};
			return;
		}
		if (ExtrapolationMethod == TinExtrapolationMethod.NearestTriangleInverseDistance)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[edgeDef.ATriangleIndex];
			if (triangleDefinition.IsDeleted)
			{
				triangleDefinition = _tin.TriangleDefinition[edgeDef.BTriangleIndex];
			}
			if (triangleDefinition.IsDeleted)
			{
				indexes = Array.Empty<int>();
				weights = Array.Empty<double>();
				return;
			}
			indexes = new int[3] { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 };
			InverseDistanceInterpolator.ComputeWeights(queryPoint, new Point[3]
			{
				_tin.Points[triangleDefinition.p0],
				_tin.Points[triangleDefinition.p1],
				_tin.Points[triangleDefinition.p2]
			}, out weights);
			return;
		}
		throw new NotSupportedException();
	}
}
