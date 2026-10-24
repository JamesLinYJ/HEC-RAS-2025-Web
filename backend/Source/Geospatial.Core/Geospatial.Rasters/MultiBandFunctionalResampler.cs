using System;
using Geospatial.Vectors;
using Utility;
using Utility.Logging;

namespace Geospatial.Rasters;

public class MultiBandFunctionalResampler<T> : IMultiBandResampler<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly Func<int, Point, T> _evaluate;

	public int ProfileCount { get; }

	public Extent ValidExtent { get; }

	public T? NoDataValue { get; }

	public static OperationResult TryBuild(int profileCount, Extent ext, string expression, out MultiBandFunctionalResampler<float> resampler)
	{
		try
		{
			ExpressionCompiler<double> expressionCompiler = new ExpressionCompiler<double>(expression, new string[3] { "x", "y", "t" });
			Func<double, double, double, double> lambda = (Func<double, double, double, double>)expressionCompiler.CompileLambda();
			resampler = new MultiBandFunctionalResampler<float>(profileCount, ext, -9999f, (int idx, Point pt) => (float)lambda(pt.X, pt.Y, (float)idx / (float)(profileCount - 1)));
			return true;
		}
		catch (Exception ex)
		{
			resampler = null;
			return OperationResult.Fail("Cannot compile expression.", ex);
		}
	}

	public MultiBandFunctionalResampler(int pfCount, Extent ext, T? noDataValue, Func<int, Point, T> evaluate)
	{
		ProfileCount = pfCount;
		ValidExtent = ext;
		NoDataValue = noDataValue;
		_evaluate = evaluate;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> result, ResampleCache cache = null)
	{
		args.EnsureBuffer(result, NoDataValue);
		RasterDefinition raster = args.Raster;
		PixelRange subRange = raster.GetPixelRange(ValidExtent, PixelRangeOptions.CenterPixelCovered);
		Parallel.SmartFor(subRange.CellCount, (int start, int end) =>
		{
			Span<T> buffer = result.Span;
			for (int i = start; i <= end; i++)
			{
				int colCountSubRangeTerm = i % subRange.ColCount;
				int colCountSubRangeTerm2 = i / subRange.ColCount + subRange.Top;
				int leftSubRangeTerm = colCountSubRangeTerm + subRange.Left;
				int colsRasterTerm = colCountSubRangeTerm2 * raster.Cols + leftSubRangeTerm;
				buffer[colsRasterTerm] = _evaluate(profileIndex, raster.CellCenter(colsRasterTerm));
			}
		});
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> result)
	{
		T ndv = NoDataValue.GetValueOrDefault();
		Parallel.SmartFor(points.Length, (int start, int end) =>
		{
			Span<Point> pointsSpan = points.Span;
			Span<T> buffer = result.Span;
			for (int i = start; i <= end; i++)
			{
				Point point = pointsSpan[i];
				if (ValidExtent.Contains(point))
				{
					buffer[i] = _evaluate(profileIndex, point);
				}
				else
				{
					buffer[i] = ndv;
				}
			}
		});
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, bandStart, bandCount, outData);
	}
}
