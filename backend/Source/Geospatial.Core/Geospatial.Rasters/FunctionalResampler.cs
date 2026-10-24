using System;
using Geospatial.Vectors;
using Utility;
using Utility.Logging;

namespace Geospatial.Rasters;

public class FunctionalResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly Func<Point, T> _evaluate;

	public Extent ValidExtent { get; }

	public T? NoDataValue { get; }

	public static OperationResult TryBuild(Extent ext, string expression, out FunctionalResampler<float> resampler)
	{
		try
		{
			ExpressionCompiler<double> expressionCompiler = new ExpressionCompiler<double>(expression, new string[2] { "x", "y" });
			Func<double, double, double> lambda = (Func<double, double, double>)expressionCompiler.CompileLambda();
			resampler = new FunctionalResampler<float>(ext, -9999f, (Point pt) => (float)lambda(pt.X, pt.Y));
			return true;
		}
		catch (Exception ex)
		{
			resampler = null;
			return OperationResult.Fail("Cannot compile expression.", ex);
		}
	}

	public FunctionalResampler(Extent ext, T? noDataValue, Func<Point, T> evaluate)
	{
		ValidExtent = ext;
		NoDataValue = noDataValue;
		_evaluate = evaluate;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		RasterDefinition raster = args.Raster;
		PixelRange subRange = raster.GetPixelRange(ValidExtent, PixelRangeOptions.CenterPixelCovered);
		Parallel.SmartFor(subRange.CellCount, (int start, int end) =>
		{
			Span<T> outDataSpan = outData.Span;
			for (int i = start; i <= end; i++)
			{
				int colCountSubRangeTerm = i % subRange.ColCount;
				int colCountSubRangeTerm2 = i / subRange.ColCount + subRange.Top;
				int leftSubRangeTerm = colCountSubRangeTerm + subRange.Left;
				int colsRasterTerm = colCountSubRangeTerm2 * raster.Cols + leftSubRangeTerm;
				outDataSpan[colsRasterTerm] = _evaluate(raster.CellCenter(colsRasterTerm));
			}
		});
	}

	public void SamplePoints(Memory<Point> points, Memory<T> retn, SamplingParams sampleParams = default(SamplingParams))
	{
		if (sampleParams.IsNull())
		{
			sampleParams = SamplingParams.Default;
		}
		T ndv = NoDataValue.GetValueOrDefault();
		Parallel.SmartFor(points.Length, (int start, int end) =>
		{
			Span<Point> pointsSpan = points.Span;
			Span<T> retnSpan = retn.Span;
			for (int i = start; i <= end; i++)
			{
				Point point = pointsSpan[i];
				if (ValidExtent.Contains(point))
				{
					retnSpan[i] = _evaluate(point);
				}
				else if (sampleParams.InitializeBufferToNoData)
				{
					retnSpan[i] = ndv;
				}
			}
		});
	}
}
