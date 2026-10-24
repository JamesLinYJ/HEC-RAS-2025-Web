using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters.Modifiers;

internal class TranslateRasterModifier<TIn, TOut> : IRaster<TOut>, IResample<TOut>, ISurface<TOut>, IRasterDefinition where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
{
	public delegate void TranslateDelegate(Span<TIn> input, Span<TOut> output, TIn? inputNDV, TOut? outputNDV);

	private readonly IRaster<TIn> _input;

	private readonly TranslateDelegate _translator;

	public RasterDefinition RasterDefinition => _input.RasterDefinition;

	public Extent ValidExtent => _input.ValidExtent;

	public TOut? NoDataValue { get; set; }

	public TranslateRasterModifier(IRaster<TIn> input, TranslateDelegate translator)
	{
		_input = input;
		_translator = translator;
	}

	private void Translate(Span<TIn> input, Span<TOut> output)
	{
		_translator(input, output, _input.NoDataValue, NoDataValue);
	}

	public virtual void Resample(ResampleArgs args, Memory<TOut> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		TIn[] tIns = new TIn[args.Raster.CellCount];
		_input.Resample(args, tIns);
		Translate(tIns, outData.Span);
	}

	public void SamplePoints(Memory<Point> points, Memory<TOut> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		TIn[] tIns = new TIn[points.Length];
		_input.SamplePoints(points, tIns);
		TOut[] tOuts = new TOut[tIns.Length];
		Translate(tIns, tOuts);
	}

	public TOut Translate(TIn input2)
	{
		Span<TIn> input = stackalloc TIn[1];
		input[0] = input2;
		Span<TOut> output = stackalloc TOut[1];
		Translate(input, output);
		return output[0];
	}

	public void GetRange(PixelRange range, Memory<TOut> data)
	{
		TIn[] tIns = new TIn[data.Length];
		_input.GetRange(range, tIns);
		Translate(tIns, data.Span);
	}
}
