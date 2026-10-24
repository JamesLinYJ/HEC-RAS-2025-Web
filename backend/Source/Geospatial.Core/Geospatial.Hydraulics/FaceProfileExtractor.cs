using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Hydraulics;

public static class FaceProfileExtractor
{
	public static bool TryBuildFromFace(Mesh mesh, IResample<float> elevations, IResample<float> nValues, int faceIndex, FaceProfileOptions opts, out FaceGeometry geom)
	{
		geom = null;
		Point[] densifyFace = DensifyFace(mesh, faceIndex, opts.SampledPointsPerFace, out var faceCenter);
		if (densifyFace.Length < 2)
		{
			return false;
		}
		float[] floatValues = new float[densifyFace.Length];
		elevations.SamplePoints(densifyFace, floatValues);
		List<float> floatList = new List<float>(densifyFace.Length);
		List<float> floatListList = new List<float>(densifyFace.Length);
		List<Point> points = new List<Point>(densifyFace.Length);
		double distanceToSum = 0.0;
		bool condition = true;
		for (int i = 0; i < densifyFace.Length; i++)
		{
			float computed = floatValues[i];
			if (computed == -9999f)
			{
				continue;
			}
			if (!condition)
			{
				distanceToSum += densifyFace[i].DistanceTo(densifyFace[i - 1]);
			}
			condition = false;
			if (points.Count > 0)
			{
				if (distanceToSum <= (double)floatList[floatList.Count - 1] + opts.StationEpsilon)
				{
					continue;
				}
			}
			floatList.Add((float)distanceToSum);
			floatListList.Add(computed);
			points.Add(densifyFace[i]);
		}
		if (points.Count < 2)
		{
			return false;
		}
		int count = points.Count - 1;
		float[] floatValuesArray = new float[count];
		if (opts.NSampling == NSampling.FaceCenterUniform)
		{
			float sampleOne = SampleOne(nValues, faceCenter, opts.DefaultManning);
			Array.Fill(floatValuesArray, sampleOne);
		}
		else
		{
			Point[] pointsArray = new Point[count];
			for (int j = 0; j < count; j++)
			{
				pointsArray[j] = points[j].Midpoint(points[j + 1]);
			}
			float[] floatValues2 = new float[count];
			nValues.SamplePoints(pointsArray, floatValues2);
			for (int k = 0; k < count; k++)
			{
				floatValuesArray[k] = ((floatValues2[k] == -9999f) ? opts.DefaultManning : floatValues2[k]);
			}
		}
		geom = new FaceGeometry(floatList.ToArray(), floatListList.ToArray(), floatValuesArray)
		{
			Name = $"Face{faceIndex}"
		};
		return true;
	}

	private static Point[] DensifyFace(Mesh mesh, int faceIndex, int pointsPerFace, out Point faceCenter)
	{
		if (mesh.FaceInternalPoints[faceIndex].IsEmpty)
		{
			Segment segment = mesh.FaceSegment(faceIndex);
			faceCenter = segment.PointAtFraction(0.5);
			double maxLength = segment.Length / (double)pointsPerFace;
			Point[] internalDensificationPoints = segment.GetInternalDensificationPoints(maxLength);
			List<Point> points = new List<Point>((internalDensificationPoints?.Length ?? 0) + 2) { segment.A };
			if (internalDensificationPoints != null)
			{
				points.AddRange(internalDensificationPoints);
			}
			points.Add(segment.B);
			return points.ToArray();
		}
		Polyline polyline = new Polyline();
		mesh.FacePolylineInto(faceIndex, polyline);
		faceCenter = polyline.PointAtFraction(0.5).Point;
		polyline.Densify(polyline.Length / (double)pointsPerFace);
		return polyline.GetPointBufferUnsafe().Span.ToArray();
	}

	private static float SampleOne(IResample<float> layer, Point pt, float fallback)
	{
		Point[] points = new Point[1] { pt };
		float[] floatValues = new float[1];
		layer.SamplePoints(points, floatValues);
		if (floatValues[0] != -9999f)
		{
			return floatValues[0];
		}
		return fallback;
	}
}
