using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors;

public class TinRestrictedInverseDistanceInterpolator : ISpatialInterpolator
{
	private Tin _tin;

	private List<Point> _points;

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

	public double InverseDistancePower { get; }

	public TinRestrictedInverseDistanceInterpolator(TinExtrapolationMethod extrapMethod = TinExtrapolationMethod.NearestSegmentLinear, double invDistancePower = 1.0)
	{
		ExtrapolationMethod = extrapMethod;
		InverseDistancePower = invDistancePower;
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
			return;
		}
		if (_points.Count == 1)
		{
			indexes = new int[1];
			weights = new double[1] { 1.0 };
			return;
		}
		if (_points.Count == 2)
		{
			indexes = new int[2] { 0, 1 };
			InverseDistanceInterpolator.ComputeWeights(queryPoint, _points, out weights, InverseDistancePower);
			return;
		}
		int triangleContainingPoint = _tin.FindTriangleContainingPoint(queryPoint);
		if (triangleContainingPoint == -1 || _tin.TriangleDefinition[triangleContainingPoint].IsDeleted)
		{
			Extrapolate(queryPoint, out indexes, out weights);
		}
		else
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triangleContainingPoint];
			indexes = new int[3] { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 };
			InverseDistanceInterpolator.ComputeWeights(queryPoint, indexes.Select((int i) => _tin.Points[i]).ToList(), out weights, InverseDistancePower);
		}
		for (int intervalIndex = 0; intervalIndex < indexes.Length; intervalIndex++)
		{
			indexes[intervalIndex] -= 8;
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
			InverseDistanceInterpolator.ComputeWeights(queryPoint, indexes.Select((int i) => _tin.Points[i]).ToArray(), out weights, InverseDistancePower);
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
			InverseDistanceInterpolator.ComputeWeights(queryPoint, indexes.Select((int i) => _tin.Points[i]).ToArray(), out weights, InverseDistancePower);
			return;
		}
		throw new NotSupportedException();
	}
}
