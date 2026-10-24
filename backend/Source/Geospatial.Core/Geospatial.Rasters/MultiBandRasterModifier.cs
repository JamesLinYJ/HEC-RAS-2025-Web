using System;
using System.Buffers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class MultiBandRasterModifier<TIn, TOut> : IMultiBandRaster<TOut>, IMultiBandResampler<TOut>, ISurface<TOut>, IRasterDefinition where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
{
	private readonly IMultiBandRaster<TIn> _input;

	private readonly ConverterDelegates.ConvertTo<TIn, TOut> _converter;

	public int ProfileCount => _input.ProfileCount;

	public Extent ValidExtent => _input.ValidExtent;

	public TOut? NoDataValue { get; set; }

	public RasterDefinition RasterDefinition => _input.RasterDefinition;

	public MultiBandRasterModifier(IMultiBandRaster<TIn> input, ConverterDelegates.ConvertTo<TIn, TOut> converter)
	{
		_input = input;
		_converter = converter;
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<TOut> result)
	{
		Memory<TIn> mem;
		using (SharedMemoryPool<TIn>.Rent(range.CellCount, out mem))
		{
			_input.GetRange(range, bandIndex, mem);
			_converter(mem, result);
		}
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<TOut> result, ResampleCache cache = null)
	{
		Memory<TIn> mem;
		using (SharedMemoryPool<TIn>.Rent(args.Raster.CellCount, out mem))
		{
			_input.Resample(args, bandIndex, mem);
			_converter(mem, result);
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<TOut>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<TOut>[] outData)
	{
		int length = points.Length;
		using IMemoryOwner<TIn> memoryOwner = SharedMemoryPool<TIn>.Rent(length * bandCount);
		Memory<TIn>[] tInSpanArray = new Memory<TIn>[bandCount];
		for (int i = 0; i < bandCount; i++)
		{
			tInSpanArray[i] = memoryOwner.Memory.Slice(i * length, length);
		}
		_input.SamplePoints(points, bandStart, bandCount, tInSpanArray);
		for (int j = 0; j < bandCount; j++)
		{
			_converter(tInSpanArray[j], outData[j]);
		}
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<TOut> result)
	{
		using IMemoryOwner<TIn> memoryOwner = SharedMemoryPool<TIn>.Rent(points.Length);
		Memory<TIn> slice = memoryOwner.Memory.Slice(0, points.Length);
		_input.SamplePoints(points, bandIndex, slice);
		_converter(slice, result);
	}
}
