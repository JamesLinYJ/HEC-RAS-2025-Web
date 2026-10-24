using System;
using System.Numerics;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

public class ConvertTypeRasterizer<TFrom, TTo> : IRaster<TTo>, IResample<TTo>, ISurface<TTo>, IRasterDefinition where TFrom : unmanaged, INumber<TFrom> where TTo : unmanaged, INumber<TTo>
{
	private readonly IRaster<TFrom> _inputResampler;

	private readonly Action<Memory<TFrom>, Memory<TTo>> _converterAction;

	public Extent ValidExtent => _inputResampler.ValidExtent;

	public TTo? NoDataValue { get; }

	public RasterDefinition RasterDefinition => _inputResampler.RasterDefinition;

	public ConvertTypeRasterizer(IRaster<TFrom> input, Action<Memory<TFrom>, Memory<TTo>> converterAction, TTo? noDataValue)
	{
		NoDataValue = noDataValue;
		_inputResampler = input;
		_converterAction = converterAction;
	}

	public void Resample(ResampleArgs args, Memory<TTo> outData)
	{
		Memory<TFrom> mem;
		using (SharedMemoryPool<TFrom>.Rent(args.Raster.CellCount, out mem))
		{
			_inputResampler.Resample(args, mem);
			_converterAction(mem, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<TTo> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Memory<TFrom> mem;
		using (SharedMemoryPool<TFrom>.Rent(points.Length, out mem))
		{
			_inputResampler.SamplePoints(points, mem, sampleParams);
			_converterAction(mem, outData);
		}
	}

	public void GetRange(PixelRange range, Memory<TTo> outData)
	{
		Memory<TFrom> mem;
		using (SharedMemoryPool<TFrom>.Rent(range.CellCount, out mem))
		{
			_inputResampler.GetRange(range, mem);
			_converterAction(mem, outData);
		}
	}
}
