using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

public class TinResampler : IResample<float>, ISurface<float>
{
	private readonly Tin _tin;

	private readonly List<Tin> _tins;

	private readonly Memory<float> _pointDataF;

	private readonly Memory<double> _pointDataD;

	private readonly bool _renderZ;

	private bool IsSingleTinMode => _tin != null;

	public Extent ValidExtent
	{
		get
		{
			if (IsSingleTinMode)
			{
				return _tin.Extent;
			}
			return Extent.Union(_tins.Select((Tin tt) => tt.Extent));
		}
	}

	public float? NoDataValue => -9999f;

	public TinResampler(Tin tin, bool renderZ)
	{
		_tin = tin;
		_renderZ = renderZ;
	}

	public TinResampler(IList<Tin> tins, bool renderZ)
	{
		_tins = tins.ToList();
		_renderZ = renderZ;
	}

	public TinResampler(Tin tin, Memory<float> pointData)
	{
		_tin = tin;
		_pointDataF = pointData;
	}

	public TinResampler(Tin tin, Memory<double> pointData)
	{
		_tin = tin;
		_pointDataD = pointData;
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue.GetValueOrDefault());
		if (IsSingleTinMode)
		{
			ResampleSingleTinInternal(args, outData);
		}
		else
		{
			ResampleMultiTinInternal(args, outData);
		}
	}

	private void ResampleSingleTinInternal(ResampleArgs args, Memory<float> outData)
	{
		if (!_pointDataF.IsEmpty)
		{
			_tin.Rasterize(args.Raster, _pointDataF, outData);
		}
		else if (!_pointDataD.IsEmpty)
		{
			_tin.Rasterize(args.Raster, _pointDataD, outData);
		}
		else if (_renderZ)
		{
			_tin.RasterizeZ(args.Raster, outData);
		}
		else
		{
			_tin.RasterizeM(args.Raster, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (IsSingleTinMode)
		{
			SamplePointsSingleTinInternal(points, outData, sampleParams);
		}
		else
		{
			SamplePointsMultiTinInternal(points, outData, sampleParams);
		}
	}

	private void SamplePointsSingleTinInternal(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (!_pointDataF.IsEmpty)
		{
			_tin.SamplePoints(points, _pointDataF, outData, NoDataValue.GetValueOrDefault());
		}
		else if (!_pointDataD.IsEmpty)
		{
			_tin.SamplePoints(points, _pointDataD, outData, NoDataValue.GetValueOrDefault());
		}
		else if (_renderZ)
		{
			_tin.SamplePoints(points, _tin.GetZBufferUnsafe(), outData, NoDataValue.GetValueOrDefault());
		}
		else
		{
			_tin.SamplePoints(points, _tin.GetMBufferUnsafe(), outData, NoDataValue.GetValueOrDefault());
		}
	}

	private void ResampleMultiTinInternal(ResampleArgs args, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue.GetValueOrDefault());
		if (_renderZ)
		{
			for (int i = 0; i < _tins.Count; i++)
			{
				_tins[i].RasterizeZ(args.Raster, outData);
			}
		}
		else
		{
			for (int j = 0; j < _tins.Count; j++)
			{
				_tins[j].RasterizeM(args.Raster, outData);
			}
		}
	}

	private void SamplePointsMultiTinInternal(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		Span<float> slice = outData.Span.Slice(0, points.Length);
		slice.Fill(valueOrDefault);
		Memory<float> mem;
		using (SharedMemoryPool<float>.RentNoFill(points.Length, out mem))
		{
			for (int i = 0; i < _tins.Count; i++)
			{
				Memory<double> pointData = (_renderZ ? _tins[i].GetZBufferUnsafe() : _tins[i].GetMBufferUnsafe());
				_tins[i].SamplePoints(points, pointData, mem, valueOrDefault);
				Span<float> memSpan = mem.Span;
				for (int j = 0; j < memSpan.Length; j++)
				{
					if (memSpan[j] != valueOrDefault)
					{
						slice[j] = memSpan[j];
					}
				}
			}
		}
	}
}
