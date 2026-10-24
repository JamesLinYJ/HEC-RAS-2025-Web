using System;
using CommunityToolkit.HighPerformance;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Transformers;

public class PolynomialTransformer : ISpatialTransformer
{
	private readonly Extent _srcExtent;

	private readonly PolynomialTransformer _inverted;

	private readonly Point[] _targetNodes;

	public bool IsReversible => true;

	public PolynomialTransformer(ISpatialTransformer sourceToTargetTransformer, Extent sourceDataExtent)
	{
		if (!sourceDataExtent.IsValid())
		{
			throw new ArgumentException("Invalid extent.");
		}
		Point[] points = new Point[9];
		sourceDataExtent.GetBorderPoints(points);
		_targetNodes = new Point[9];
		_srcExtent = sourceDataExtent;
		sourceToTargetTransformer.Transform(points.AsMemory().Slice(0, 8), _targetNodes.AsMemory().Slice(0, 8));
		_inverted = new PolynomialTransformer(sourceToTargetTransformer.GetInvertedTransformer(), sourceDataExtent.Transform(sourceToTargetTransformer), this);
	}

	private PolynomialTransformer(ISpatialTransformer sourceToTargetTransformer, Extent sourceDataExtent, PolynomialTransformer inverted)
	{
		if (!sourceDataExtent.IsValid())
		{
			throw new ArgumentException("Invalid extent.");
		}
		_targetNodes = new Point[9];
		_srcExtent = sourceDataExtent;
		Point[] points = new Point[9];
		sourceDataExtent.GetBorderPoints(points);
		sourceToTargetTransformer.Transform(points, _targetNodes);
		_inverted = inverted;
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point point = pointsSpan[i];
			double x = point.X;
			double y = point.Y;
			double minXSrcExtentTerm = (x - _srcExtent.MinX) / _srcExtent.Width * 2.0 - 1.0;
			double minYSrcExtentTerm = (y - _srcExtent.MinY) / _srcExtent.Height * 2.0 - 1.0;
			double computed = -0.25 * (1.0 - minXSrcExtentTerm) * (1.0 - minYSrcExtentTerm) * (1.0 + minXSrcExtentTerm + minYSrcExtentTerm);
			double computed2 = 0.5 * (1.0 - minXSrcExtentTerm) * (1.0 + minXSrcExtentTerm) * (1.0 - minYSrcExtentTerm);
			double computed3 = -0.25 * (1.0 + minXSrcExtentTerm) * (1.0 - minYSrcExtentTerm) * (1.0 - minXSrcExtentTerm + minYSrcExtentTerm);
			double computed4 = 0.5 * (1.0 + minXSrcExtentTerm) * (1.0 + minYSrcExtentTerm) * (1.0 - minYSrcExtentTerm);
			double computed5 = -0.25 * (1.0 + minXSrcExtentTerm) * (1.0 + minYSrcExtentTerm) * (1.0 - minXSrcExtentTerm - minYSrcExtentTerm);
			double computed6 = 0.5 * (1.0 - minXSrcExtentTerm) * (1.0 + minXSrcExtentTerm) * (1.0 + minYSrcExtentTerm);
			double computed7 = -0.25 * (1.0 - minXSrcExtentTerm) * (1.0 + minYSrcExtentTerm) * (1.0 + minXSrcExtentTerm - minYSrcExtentTerm);
			double computed8 = 0.5 * (1.0 - minXSrcExtentTerm) * (1.0 + minYSrcExtentTerm) * (1.0 - minYSrcExtentTerm);
			double x2 = computed * _targetNodes[0].X + computed2 * _targetNodes[1].X + computed3 * _targetNodes[2].X + computed4 * _targetNodes[3].X + computed5 * _targetNodes[4].X + computed6 * _targetNodes[5].X + computed7 * _targetNodes[6].X + computed8 * _targetNodes[7].X;
			double y2 = computed * _targetNodes[0].Y + computed2 * _targetNodes[1].Y + computed3 * _targetNodes[2].Y + computed4 * _targetNodes[3].Y + computed5 * _targetNodes[4].Y + computed6 * _targetNodes[5].Y + computed7 * _targetNodes[6].Y + computed8 * _targetNodes[7].Y;
			transformedSpan[i] = new Point(x2, y2);
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		_inverted.Transform(points, transformed);
	}

	public static void GetTransformCoefficientsPixelSpace(RasterDefinition sourceRaster, RasterDefinition targetRaster, ISpatialTransformer targetToSourceTransformer, out TransformCoefficient8x8 coefCol, out TransformCoefficient8x8 coefRow)
	{
		Memory<Point> mem;
		using (SharedMemoryPool<Point>.RentNoFill(16, out mem))
		{
			Memory<Point> transformed = mem.Slice(8, 8);
			Memory<Point> points = mem.Slice(0, 8);
			Span<Point> transformedSpan = transformed.Span;
			Span<Point> pointsSpan = points.Span;
			targetRaster.Extent.FillWithBorderPoints(pointsSpan);
			targetToSourceTransformer.Transform(points, transformed);
			Span<double> doubleSpan = stackalloc double[8];
			Span<double> doubleSpan2 = stackalloc double[8];
			for (int i = 0; i < 8; i++)
			{
				Point point = transformedSpan[i];
				doubleSpan[i] = sourceRaster.FractionalCol(point.X);
				doubleSpan2[i] = sourceRaster.FractionalRow(point.Y);
			}
			Span2D<double> a = stackalloc double[64].AsSpan2D(8, 8);
			for (int j = 0; j < 8; j++)
			{
				Point point2 = pointsSpan[j];
				double fractionalCol = targetRaster.FractionalCol(point2.X);
				double fractionalRow = targetRaster.FractionalRow(point2.Y);
				a[j, 0] = 1.0;
				a[j, 1] = fractionalCol;
				a[j, 2] = fractionalRow;
				a[j, 3] = fractionalCol * fractionalRow;
				a[j, 4] = fractionalCol * fractionalCol;
				a[j, 5] = fractionalRow * fractionalRow;
				a[j, 6] = fractionalCol * fractionalCol * fractionalRow;
				a[j, 7] = fractionalRow * fractionalRow * fractionalCol;
			}
			Span2D<double> span2D = stackalloc double[16].AsSpan2D(8, 2);
			for (int k = 0; k < 8; k++)
			{
				span2D[k, 0] = doubleSpan[k];
				span2D[k, 1] = doubleSpan2[k];
			}
			MathHelpers.GaussJordan(a, span2D);
			coefCol = new TransformCoefficient8x8(span2D, 0);
			coefRow = new TransformCoefficient8x8(span2D, 1);
		}
	}

	public static void GetTransformCoefficientsRealWorldSpace(Extent targetExtent, ISpatialTransformer targetToSourceTransformer, out TransformCoefficient8x8 coefCol, out TransformCoefficient8x8 coefRow)
	{
		Point[] borderPoints = targetExtent.GetBorderPoints();
		Point[] points = new Point[borderPoints.Length];
		targetToSourceTransformer.Transform(borderPoints, points);
		double[] doubleValues = new double[8];
		double[] doubleValuesArray = new double[8];
		for (int i = 0; i <= 7; i++)
		{
			Point point = points[i];
			doubleValues[i] = point.X;
			doubleValuesArray[i] = point.Y;
		}
		double[,] doubleValues2 = new double[8, 8];
		for (int j = 0; j <= 7; j++)
		{
			Point point2 = borderPoints[j];
			double x = point2.X;
			double y = point2.Y;
			doubleValues2[j, 0] = 1.0;
			doubleValues2[j, 1] = x;
			doubleValues2[j, 2] = y;
			doubleValues2[j, 3] = x * y;
			doubleValues2[j, 4] = x * x;
			doubleValues2[j, 5] = y * y;
			doubleValues2[j, 6] = x * x * y;
			doubleValues2[j, 7] = y * y * x;
		}
		double[,] doubleValues3 = new double[8, 2];
		for (int k = 0; k <= 7; k++)
		{
			doubleValues3[k, 0] = doubleValues[k];
			doubleValues3[k, 1] = doubleValuesArray[k];
		}
		MathHelpers.GaussJordan(doubleValues2, doubleValues3);
		coefCol = new TransformCoefficient8x8(doubleValues3, 0);
		coefRow = new TransformCoefficient8x8(doubleValues3, 1);
	}

	public static void GetTransformCoefficientsNormalizedSpace(Extent sourceExt, Extent targetExt, ISpatialTransformer targetToSourceTransformer, out TransformCoefficient8x8 coefCol, out TransformCoefficient8x8 coefRow)
	{
		Point[] borderPoints = targetExt.GetBorderPoints();
		Point[] points = new Point[borderPoints.Length];
		targetToSourceTransformer.Transform(borderPoints, points);
		Span<double> doubleSpan = stackalloc double[8];
		Span<double> doubleSpan2 = stackalloc double[8];
		for (int i = 0; i < 8; i++)
		{
			Point point = points[i];
			doubleSpan[i] = (point.X - sourceExt.Left) / sourceExt.Width;
			doubleSpan2[i] = (point.Y - sourceExt.Bottom) / sourceExt.Height;
		}
		double[,] doubleValues = new double[8, 8];
		for (int j = 0; j < 8; j++)
		{
			Point point2 = borderPoints[j];
			double point2LeftTerm = (point2.X - targetExt.Left) / targetExt.Width;
			double point2BottomTerm = (point2.Y - targetExt.Bottom) / targetExt.Height;
			doubleValues[j, 0] = 1.0;
			doubleValues[j, 1] = point2LeftTerm;
			doubleValues[j, 2] = point2BottomTerm;
			doubleValues[j, 3] = point2LeftTerm * point2BottomTerm;
			doubleValues[j, 4] = point2LeftTerm * point2LeftTerm;
			doubleValues[j, 5] = point2BottomTerm * point2BottomTerm;
			doubleValues[j, 6] = point2LeftTerm * point2LeftTerm * point2BottomTerm;
			doubleValues[j, 7] = point2BottomTerm * point2BottomTerm * point2LeftTerm;
		}
		double[,] doubleValues2 = new double[8, 2];
		for (int k = 0; k < 8; k++)
		{
			doubleValues2[k, 0] = doubleSpan[k];
			doubleValues2[k, 1] = doubleSpan2[k];
		}
		MathHelpers.GaussJordan(doubleValues, doubleValues2);
		coefCol = new TransformCoefficient8x8(doubleValues2, 0);
		coefRow = new TransformCoefficient8x8(doubleValues2, 1);
	}
}
