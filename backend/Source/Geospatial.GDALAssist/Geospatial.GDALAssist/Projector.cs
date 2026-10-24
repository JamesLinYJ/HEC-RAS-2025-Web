using System;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace Geospatial.GDALAssist;

public static class Projector
{
	private enum ProjectionCriteria
	{
		ForwardBackwardAbsoluteErrorBounds,
		ExtentErrorBounds,
		Monotonicity
	}

	private const int StackAllocSz = 24;

	public static Point TransformPoint(Projection sourceProj, Projection destProj, Point pt)
	{
		Point reference = pt;
		Span<Point> points = new Span<Point>(ref reference);
		TransformPoints(sourceProj, destProj, points);
		return points[0];
	}

	public static Point TransformPoint(CoordinateTransformation ct, Point pt)
	{
		Point reference = pt;
		Span<Point> referenceSpan = new Span<Point>(ref reference);
		TransformPoints(ct, referenceSpan, referenceSpan);
		return referenceSpan[0];
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, Span<Point> points)
	{
		if (srcProjection == null)
		{
			throw new ArgumentNullException("srcProjection", "Cannot transform points from a null source projection.");
		}
		if (destProjection == null)
		{
			throw new ArgumentNullException("destProjection", "Cannot transform points to a null destination projection.");
		}
		using CoordinateTransformation ct = Projection.CreateTransformation(srcProjection, destProjection);
		TransformPoints(ct, points, points);
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, Span<Point> points, Span<Point> dest)
	{
		using CoordinateTransformation ct = Projection.CreateTransformation(srcProjection, destProjection);
		TransformPoints(ct, points, dest);
	}

	public unsafe static void TransformPoints(CoordinateTransformation ct, Span<Point> points, Span<Point> dest)
	{
		int length = points.Length;
		if (dest.Length < length)
		{
			throw new ArgumentException("Destination buffer too small.");
		}
		if (length <= 24)
		{
			double* lengthTerm = stackalloc double[length];
			double* lengthTerm2 = stackalloc double[length];
			for (int i = 0; i < length; i++)
			{
				lengthTerm[i] = points[i].X;
				lengthTerm2[i] = points[i].Y;
			}
			TRANSFORM(ct, length, lengthTerm, lengthTerm2);
			for (int j = 0; j < length; j++)
			{
				dest[j] = new Point(lengthTerm[j], lengthTerm2[j]);
			}
			return;
		}
		fixed (double* localValue = new double[length * 2])
		{
			double* localValue2 = localValue;
			double* lengthTerm3 = localValue + length;
			for (int k = 0; k < length; k++)
			{
				localValue2[k] = points[k].X;
				lengthTerm3[k] = points[k].Y;
			}
			TRANSFORM(ct, length, localValue2, lengthTerm3);
			for (int l = 0; l < length; l++)
			{
				dest[l] = new Point(localValue2[l], lengthTerm3[l]);
			}
		}
	}

	private unsafe static bool TRANSFORM(CoordinateTransformation ct, int count, double* x, double* y)
	{
		double[] xs = new ReadOnlySpan<double>(x, count).ToArray();
		double[] ys = new ReadOnlySpan<double>(y, count).ToArray();
		try
		{
			ct.TransformPoints(count, xs, ys, null);
			return true;
		}
		catch (ApplicationException)
		{
			return false;
		}
		finally
		{
			xs.AsSpan().CopyTo(new Span<double>(x, count));
			ys.AsSpan().CopyTo(new Span<double>(y, count));
		}
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, RasterDefinition src, Span<Point> dest)
	{
		using CoordinateTransformation ct = Projection.CreateTransformation(srcProjection, destProjection);
		TransformPoints(ct, src, dest);
	}

	public static void TransformPointsOneByOne(Projection srcProjection, Projection destProjection, RasterDefinition src, Span<Point> dest)
	{
		using CoordinateTransformation ct = Projection.CreateTransformation(srcProjection, destProjection);
		Span<Point> points = stackalloc Point[1];
		for (int i = 0; i < src.CellCount; i++)
		{
			try
			{
				points[0] = src.CellCenter(i);
				TransformPoints(ct, points, dest.Slice(i, 1));
			}
			catch
			{
				dest[i] = Point.NullPoint;
			}
		}
	}

	public unsafe static void TransformPoints(CoordinateTransformation ct, RasterDefinition src, Span<Point> dest)
	{
		int cellCount = src.CellCount;
		if (dest.Length < cellCount)
		{
			throw new ArgumentException("Destination buffer too small.");
		}
		if (cellCount <= 24)
		{
			double* cellCountTerm = stackalloc double[cellCount];
			double* cellCountTerm2 = stackalloc double[cellCount];
			FromRaster(src, cellCountTerm, cellCountTerm2);
			TRANSFORM(ct, cellCount, cellCountTerm, cellCountTerm2);
			for (int i = 0; i < cellCount; i++)
			{
				dest[i] = new Point(cellCountTerm[i], cellCountTerm2[i]);
			}
			return;
		}
		fixed (double* localValue = new double[cellCount * 2])
		{
			double* localValue2 = localValue;
			double* cellCountTerm3 = localValue + cellCount;
			FromRaster(src, localValue2, cellCountTerm3);
			TRANSFORM(ct, cellCount, localValue2, cellCountTerm3);
			for (int j = 0; j < cellCount; j++)
			{
				dest[j] = new Point(localValue2[j], cellCountTerm3[j]);
			}
		}
	}

	private unsafe static void FromRaster(RasterDefinition rdef, double* x, double* y)
	{
		int cellCount = rdef.CellCount;
		for (int i = 0; i < cellCount; i++)
		{
			Point point = rdef.CellCenter(i);
			x[i] = point.X;
			y[i] = point.Y;
		}
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, double[] x, double[] y, double[] z)
	{
		TransformPoints(srcProjection, destProjection, x.AsSpan(), y.AsSpan(), z.AsSpan());
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, Span<double> x, Span<double> y, Span<double> z)
	{
		using CoordinateTransformation ct = Projection.CreateTransformation(srcProjection, destProjection);
		TransformPoints(ct, x, y, z);
	}

	public unsafe static void TransformPoints(CoordinateTransformation ct, Span<double> x, Span<double> y, Span<double> z)
	{
		if (y.Length != x.Length || (!z.IsEmpty && z.Length != x.Length))
		{
			throw new ArgumentException("Coordinate buffers must have matching lengths.");
		}
		double[] xs = x.ToArray();
		double[] ys = y.ToArray();
		double[] zs = z.IsEmpty ? null : z.ToArray();
		try
		{
			ct.TransformPoints(x.Length, xs, ys, zs);
		}
		finally
		{
			xs.AsSpan().CopyTo(x);
			ys.AsSpan().CopyTo(y);
			if (zs != null) zs.AsSpan().CopyTo(z);
		}
	}

	public static void TransformPoints(Projection srcProjection, Projection destProjection, double[] xyPts)
	{
		if (xyPts.Length % 2 != 0)
		{
			throw new Exception("xyPts must contain an even number of points in the form [X1,Y1,X2,Y2,X3,Y3...]");
		}
		int scaledLength = xyPts.Length / 2;
		Span<double> doubleSpan = ((scaledLength > 24) ? ((Span<double>)new double[scaledLength * 3]) : stackalloc double[scaledLength * 3]);
		Span<double> doubleSpan2 = doubleSpan;
		Span<double> x = doubleSpan2.Slice(0, scaledLength);
		Span<double> y = doubleSpan2.Slice(scaledLength, scaledLength);
		Span<double> z = doubleSpan2.Slice(scaledLength * 2, scaledLength);
		for (int i = 0; i < x.Length; i++)
		{
			x[i] = xyPts[2 * i];
			y[i] = xyPts[2 * i + 1];
		}
		TransformPoints(srcProjection, destProjection, x, y, z);
		for (int j = 0; j < x.Length; j++)
		{
			xyPts[2 * j] = x[j];
			xyPts[2 * j + 1] = y[j];
		}
	}

	public static Extent GetBoundingBox(Extent c, Projection fromProj, Projection toProj, int edgeDivisor = 6)
	{
		Point[] edgeDivisorPoints = c.GetEdgeDivisorPoints(edgeDivisor);
		TransformPoints(fromProj, toProj, edgeDivisorPoints);
		return PointCollection.ComputeExtent(edgeDivisorPoints);
	}

	public static Extent GetBoundingBox_Rasterized(Extent ext, Projection fromProj, Projection toProj)
	{
		RasterDefinition src = RasterDefinition.ComputeBoundingRaster(ext, 1000);
		Point[] points = new Point[src.CellCount];
		TransformPoints(fromProj, toProj, src, points);
		return PointCollection.ComputeValidExtent(points);
	}

	public static Extent GetBoundingBox(Extent c, ISpatialTransformer transformer, int edgeDivisor = 6, Point[] ptsScratchSpace = null, bool isTransformerReversed = false)
	{
		Point[] points;
		if (ptsScratchSpace == null)
		{
			points = c.GetEdgeDivisorPoints(edgeDivisor);
		}
		else
		{
			if (ptsScratchSpace.Length != edgeDivisor * 4)
			{
				throw new ArgumentException("Scratch space length must equal edgeDivisor");
			}
			c.GetEdgeDivisorPoints(edgeDivisor, ptsScratchSpace);
			points = ptsScratchSpace;
		}
		if (isTransformerReversed)
		{
			transformer.ReverseTransform(points, points);
		}
		else
		{
			transformer.Transform(points, points);
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double minValue2 = double.MinValue;
		double maxValue2 = double.MaxValue;
		for (int i = 0; i < points.Length; i++)
		{
			if (points[i].X > minValue2)
			{
				minValue2 = points[i].X;
			}
			if (points[i].X < maxValue)
			{
				maxValue = points[i].X;
			}
			if (points[i].Y > minValue)
			{
				minValue = points[i].Y;
			}
			if (points[i].Y < maxValue2)
			{
				maxValue2 = points[i].Y;
			}
		}
		return new Extent(maxValue, minValue2, maxValue2, minValue);
	}

	public static Extent GetBoundingBox(Extent c, Projection fromProj, Projection toProj, double inputCellSize, out double outputCellSizeParity, int edgeDivisor = 6)
	{
		Point[] edgeDivisorPoints = c.GetEdgeDivisorPoints(edgeDivisor);
		int widthInputCellSizeTerm = (int)Math.Round(c.Width / inputCellSize);
		int heightInputCellSizeTerm = (int)Math.Round(c.Height / inputCellSize);
		TransformPoints(fromProj, toProj, edgeDivisorPoints);
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double minValue2 = double.MinValue;
		double maxValue2 = double.MaxValue;
		for (int i = 0; i < edgeDivisorPoints.Length; i++)
		{
			if (edgeDivisorPoints[i].X > minValue2)
			{
				minValue2 = edgeDivisorPoints[i].X;
			}
			if (edgeDivisorPoints[i].X < maxValue)
			{
				maxValue = edgeDivisorPoints[i].X;
			}
			if (edgeDivisorPoints[i].Y > minValue)
			{
				minValue = edgeDivisorPoints[i].Y;
			}
			if (edgeDivisorPoints[i].Y < maxValue2)
			{
				maxValue2 = edgeDivisorPoints[i].Y;
			}
		}
		double width = c.Width;
		double width2 = c.Width;
		double height = c.Height;
		double height2 = c.Height;
		double distanceToSum = 0.0;
		double distanceToSum2 = 0.0;
		double distanceToSum3 = 0.0;
		double edgeDivisorEdgeDivisorPointsTerm = 0.0;
		for (int j = 0; j < edgeDivisor; j++)
		{
			distanceToSum += edgeDivisorPoints[j].DistanceTo(edgeDivisorPoints[j + 1]);
			distanceToSum3 += edgeDivisorPoints[j + edgeDivisor].DistanceTo(edgeDivisorPoints[j + edgeDivisor + 1]);
			distanceToSum2 += edgeDivisorPoints[j + 2 * edgeDivisor].DistanceTo(edgeDivisorPoints[j + 2 * edgeDivisor + 1]);
			edgeDivisorEdgeDivisorPointsTerm = ((j + 3 * edgeDivisor == edgeDivisorPoints.Length - 1) ? (edgeDivisorEdgeDivisorPointsTerm + edgeDivisorPoints[j + 3 * edgeDivisor].DistanceTo(edgeDivisorPoints[0])) : (edgeDivisorEdgeDivisorPointsTerm + edgeDivisorPoints[j + 3 * edgeDivisor].DistanceTo(edgeDivisorPoints[j + 3 * edgeDivisor + 1])));
		}
		double distanceToSumOverWidth = distanceToSum / width;
		double distanceToSum3OverHeight2 = distanceToSum3 / height2;
		double distanceToSum2OverWidth2 = distanceToSum2 / width2;
		double heightTerm = edgeDivisorEdgeDivisorPointsTerm / height;
		double computed = Math.Max(Math.Max(distanceToSumOverWidth, distanceToSum3OverHeight2), Math.Max(distanceToSum2OverWidth2, heightTerm));
		if (computed == distanceToSumOverWidth)
		{
			outputCellSizeParity = distanceToSum / (double)widthInputCellSizeTerm;
		}
		else if (computed == distanceToSum3OverHeight2)
		{
			outputCellSizeParity = distanceToSum3 / (double)heightInputCellSizeTerm;
		}
		else if (computed == distanceToSum2OverWidth2)
		{
			outputCellSizeParity = distanceToSum2 / (double)widthInputCellSizeTerm;
		}
		else
		{
			outputCellSizeParity = edgeDivisorEdgeDivisorPointsTerm / (double)heightInputCellSizeTerm;
		}
		return new Extent(maxValue, minValue2, maxValue2, minValue);
	}

	internal static Extent GetCorners(Dataset ds)
	{
		double[] doubleValues = new double[6];
		ds.GetGeoTransform(doubleValues);
		Point coordinate = GetCoordinate(doubleValues, 0.0, 0.0);
		Point coordinate2 = GetCoordinate(doubleValues, ds.RasterXSize, ds.RasterYSize);
		return new Extent(coordinate.X, coordinate2.X, coordinate2.Y, coordinate.Y);
	}

	private static Point GetCoordinate(Dataset ds, double xPixel, double yPixel)
	{
		double[] doubleValues = new double[6];
		ds.GetGeoTransform(doubleValues);
		return GetCoordinate(doubleValues, xPixel, yPixel);
	}

	private static Point GetCoordinate(double[] adfGeoTransform, double xPixel, double yPixel)
	{
		double x = adfGeoTransform[0] + adfGeoTransform[1] * xPixel + adfGeoTransform[2] * yPixel;
		double y = adfGeoTransform[3] + adfGeoTransform[4] * xPixel + adfGeoTransform[5] * yPixel;
		return new Point(x, y);
	}

	private static Extent FindReasonableBox(Projection from, Projection to, Point seedPoint, double errorTolerance)
	{
		throw new NotImplementedException();
	}

	private static Extent FindReasonableBox_FB(Projection from, Projection to, Point seedPoint, double errorTolerance)
	{
		if (!from.TryGetRelevantUnitLength(out var baseIncrement))
		{
			baseIncrement = 1.0;
		}
		ReprojectionTransformer xf = new ReprojectionTransformer(from, to);
		try
		{
			Point pt = xf.Transform(seedPoint);
			Point other = xf.ReverseTransform(pt);
			double distanceTo = seedPoint.DistanceTo(other);
			if (double.IsNaN(distanceTo) || double.IsInfinity(distanceTo) || distanceTo > errorTolerance)
			{
				return Extent.NullExtent;
			}
			Point pt2 = seedPoint.Translate(baseIncrement, 0.0);
			Point other2 = xf.Transform(pt2);
			pt.DistanceTo(other2);
			Point pt3 = seedPoint.Translate(baseIncrement * 100000.0, 0.0);
			Point point = xf.Transform(pt3);
			Point pt4 = pt3.Translate(baseIncrement, 0.0);
			Point other3 = xf.Transform(pt4);
			point.DistanceTo(other3);
			Point point2 = BinarySearch(seedPoint, new Vector(baseIncrement, 0.0), 30, 0.01, (Point pA) => FBF(pA));
			Point point3 = BinarySearch(seedPoint, new Vector(0.0 - baseIncrement, 0.0), 30, 0.01, (Point pA) => FBF(pA));
			Point point4 = BinarySearch(seedPoint, new Vector(0.0, baseIncrement), 30, 0.01, (Point pA) => FBF(pA));
			Point point5 = BinarySearch(seedPoint, new Vector(0.0, 0.0 - baseIncrement), 30, 0.01, (Point pA) => FBF(pA));
			return new Extent(point3.X, point2.X, point5.Y, point4.Y);
		}
		finally
		{
			if (xf != null)
			{
				((IDisposable)xf).Dispose();
			}
		}
		bool FBF(Point pA)
		{
			return ForwardBackward(pA).DistanceTo(pA) < errorTolerance;
		}
		Point ForwardBackward(Point pA)
		{
			try
			{
				return xf.ReverseTransform(xf.Transform(pA));
			}
			catch
			{
				return Point.NullPoint;
			}
		}
	}

	private static (Point, Point) WalkWhile(Point seedPoint, Vector seedVector, double growthRate, int maxIters, Func<Point, bool> success)
	{
		Point point = seedPoint;
		Vector vector = seedVector;
		for (int i = 0; i < maxIters; i++)
		{
			vector *= growthRate;
			point += vector;
			if (!success(point))
			{
				return (point - vector, point);
			}
		}
		return (point, point);
	}

	private static Point BinarySearch(Point seedPoint, Vector seedVector, int maxIters, double stopIteratingDistance, Func<Point, bool> success)
	{
		Point point = seedPoint;
		Vector vector = seedVector;
		int i;
		for (i = 0; i < maxIters; i++)
		{
			vector *= 2.0;
			point += vector;
			if (!success(point))
			{
				break;
			}
		}
		if (i == maxIters)
		{
			return Point.NullPoint;
		}
		point -= vector;
		i = 0;
		double successPointTerm = 1.0;
		double unity = 1.0;
		Vector vector2 = vector;
		for (; i < maxIters; i++)
		{
			unity *= 0.5;
			vector = vector2 * unity * successPointTerm;
			point += vector;
			successPointTerm = ((!success(point)) ? (-1.0) : 1.0);
			if (vector.Length < stopIteratingDistance)
			{
				break;
			}
		}
		if (successPointTerm > 0.0)
		{
			return point;
		}
		return point - vector;
	}

	public static bool DebugHealthCheck()
	{
		EPSGProjection srcProjection = new EPSGProjection(4326);
		EPSGProjection destProjection = new EPSGProjection(900913);
		double[] xyPts = new double[2] { 10.0, 10.0 };
		TransformPoints(srcProjection, destProjection, xyPts);
		return true;
	}
}
