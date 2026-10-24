using System;
using System.Collections.Generic;
using Geospatial.Transformers;

namespace Geospatial.Vectors.MeshTopology;

public class QuadTransformBySizeFunction : ISpatialTransformer
{
	private Polyline fSouth;

	private Polyline fNorth;

	private Polyline fWest;

	private Polyline fEast;

	private ISizeFunction sizeWE;

	private ISizeFunction sizeSN;

	private ISpatialTransformer transform;

	public bool IsReversible => false;

	public QuadTransformBySizeFunction(Polyline fSouth, Polyline fNorth, ISizeFunction sizeWE, Polyline fWest, Polyline fEast, ISizeFunction sizeSN, ISpatialTransformer transform)
	{
		this.fSouth = fSouth;
		this.fNorth = fNorth;
		this.fWest = fWest;
		this.fEast = fEast;
		this.sizeWE = sizeWE;
		this.sizeSN = sizeSN;
		this.transform = transform;
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < points.Length; i++)
		{
			transformedSpan[i] = TransformOne(pointsSpan[i]);
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		throw new NotImplementedException();
	}

	public Point TransformOne(Point uvStar)
	{
		if (uvStar.X == 0.0 || uvStar.X == 1.0 || uvStar.Y == 0.0 || uvStar.Y == 1.0)
		{
			return transform.Transform(uvStar);
		}
		double x = MapStarCoordinate(uvStar.Y, uvStar.X, fSouth, fNorth, starIsU: true, sizeWE);
		double y = MapStarCoordinate(uvStar.X, uvStar.Y, fWest, fEast, starIsU: false, sizeSN);
		return transform.Transform(new Point(x, y));
		double MapStarCoordinate(double contour, double star, Polyline f0, Polyline f1, bool starIsU, ISizeFunction sizeFunction)
		{
			int count = (f0.Count + f1.Count) / 2;
			List<double> doubleList = new List<double>();
			List<Point> points = new List<Point>();
			double computed = 0.0;
			double query = 0.0;
			for (int i = 0; i < count; i++)
			{
				Point point = ISpatialTransformerExtensions.Transform(pt: (!starIsU) ? new Point(contour, (double)i / (double)(count - 1)) : new Point((double)i / (double)(count - 1), contour), xf: transform);
				double query2 = sizeFunction.Query(point);
				if (i > 0)
				{
					double distanceTo = point.DistanceTo(points[points.Count - 1]);
					computed += 0.5 * distanceTo * (1.0 / query + 1.0 / query2);
				}
				doubleList.Add(computed);
				points.Add(point);
				query = query2;
			}
			for (int j = 0; j < count; j++)
			{
				doubleList[j] /= computed;
			}
			for (int k = 1; k < count; k++)
			{
				if (!(star > doubleList[k]))
				{
					double interpolationFraction = (star - doubleList[k - 1]) / (doubleList[k] - doubleList[k - 1]);
					return ((double)(k - 1) + interpolationFraction) / (double)(count - 1);
				}
			}
			return double.NaN;
		}
	}
}
