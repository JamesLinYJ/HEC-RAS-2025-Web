using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

public static class VelocityExtensions
{
	private class SingleTimestepVelocityResampler<T> : IVelocityResampler<T>, IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
	{
		private readonly ITimeSeriesVelocityResampler<T> _res;

		private readonly int _bandIndex;

		public Extent ValidExtent => _res.ValidExtent;

		public T? NoDataValue => _res.NoDataValue;

		public SingleTimestepVelocityResampler(ITimeSeriesVelocityResampler<T> res, int bandIndex)
		{
			_res = res;
			_bandIndex = bandIndex;
		}

		public void Resample(ResampleArgs args, Memory<T> outData)
		{
			_res.Resample(args, _bandIndex, outData);
		}

		public void ResampleMagDir(ResampleArgs args, Memory<T> outMag, Memory<T> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward)
		{
			_res.ResampleMagDir(args, _bandIndex, outMag, outDir, dirConvention);
		}

		public void ResampleVxVy(ResampleArgs args, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag)
		{
			throw new NotImplementedException();
		}

		public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
		{
			throw new NotImplementedException();
		}

		public void SamplePointsVxVy(Memory<Point> points, Memory<T> outVx, Memory<T> outVy)
		{
			throw new NotImplementedException();
		}
	}

	private class ComponentRasterizer<T> : ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
	{
		private readonly ITimeSeriesVelocityRasterizer<T> _vr;

		private readonly ToVectorComponent _toVec;

		public bool CanInterpolateTimesteps => false;

		public IReadOnlyList<DateTime> Times => _vr.Times;

		public int ProfileCount => _vr.Times.Count;

		public Extent ValidExtent => _vr.ValidExtent;

		public T? NoDataValue => _vr.NoDataValue;

		public RasterDefinition RasterDefinition => _vr.RasterDefinition;

		public ComponentRasterizer(ITimeSeriesVelocityRasterizer<T> vr, ToVectorComponent toVec)
		{
			_vr = vr;
			_toVec = toVec;
		}

		public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
		{
			if (_toVec == ToVectorComponent.ToVx)
			{
				_vr.GetRangeVxVy(range, bandIndex, result, Memory<T>.Empty, Memory<T>.Empty);
				return;
			}
			if (_toVec == ToVectorComponent.ToVy)
			{
				_vr.GetRangeVxVy(range, bandIndex, Memory<T>.Empty, result, Memory<T>.Empty);
				return;
			}
			Memory<T> mem;
			if (_toVec == ToVectorComponent.ToMag)
			{
				using (IMemoryOwner<T> memoryOwner = SharedMemoryPool<T>.RentNoFill(range.CellCount * 2, out mem))
				{
					Memory<T> vxResult = memoryOwner.Memory.Slice(0, range.CellCount);
					Memory<T> magResult = memoryOwner.Memory.Slice(range.CellCount, range.CellCount);
					_vr.GetRangeVxVy(range, bandIndex, vxResult, result, magResult);
					return;
				}
			}
			if (_toVec == ToVectorComponent.ToDir)
			{
				throw new NotImplementedException();
			}
			throw new NotSupportedException();
		}

		public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
		{
			throw new NotImplementedException();
		}

		public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
		{
			throw new NotImplementedException();
		}

		public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
		{
			throw new NotImplementedException();
		}

		public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
		{
			throw new NotImplementedException();
		}

		public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
		{
			throw new NotImplementedException();
		}

		public TimeSeries SamplePointTimeSeries(Point pt)
		{
			throw new NotImplementedException();
		}
	}

	private enum ToVectorComponent
	{
		ToVx,
		ToVy,
		ToMag,
		ToDir
	}

	public static void SamplePointVxVy<T>(this ITimeSeriesVelocityResampler<T> resampler, Point pt, out T[] vx, out T[] vy) where T : unmanaged, IEquatable<T>
	{
		int count = resampler.Times.Count;
		vx = new T[count];
		vy = new T[count];
		T[] values = new T[count];
		Memory<T> vxSpan = vx.AsMemory();
		Memory<T> vySpan = vy.AsMemory();
		Memory<T> valuesSpan = values.AsMemory();
		Memory<T>[] bufferArray = new Memory<T>[count];
		Memory<T>[] bufferArraySpan = new Memory<T>[count];
		Memory<T>[] bufferArrayArray = new Memory<T>[count];
		for (int i = 0; i < count; i++)
		{
			bufferArray[i] = vxSpan.Slice(i, 1);
			bufferArraySpan[i] = vySpan.Slice(i, 1);
			bufferArrayArray[i] = valuesSpan.Slice(i, 1);
		}
		resampler.SamplePointsVxVy(new Point[1] { pt }, 0, count, bufferArray, bufferArraySpan, bufferArrayArray);
	}

	public static ITimeSeriesVelocityResampler<float> WithTransformer(this ITimeSeriesVelocityResampler<float> resampler, ISpatialTransformer transformer, VelocityTransformOptions opts, IValueTransformer<float> magnitudeTransformer, IValueTransformer<float> directionTransformer)
	{
		return new TransformedVelocityResampler(resampler, transformer, opts, magnitudeTransformer, directionTransformer);
	}

	public static ITimeSeriesVelocityRasterizer<float> WithTransformer_Raster(this ITimeSeriesVelocityRasterizer<float> resampler, ISpatialTransformer transformer, VelocityTransformOptions opts, IValueTransformer<float> magnitudeTransformer, IValueTransformer<float> directionTransformer)
	{
		return new TransformedVelocityRasterizer(resampler, transformer, opts, magnitudeTransformer, directionTransformer);
	}

	public static ITimeSeriesVelocityRasterizer<T> AsVelocityRaster<T>(this ITimeSeriesVelocityResampler<T> resampler, RasterDefinition raster) where T : unmanaged, IEquatable<T>
	{
		return new VelocityRasterizer<T>(resampler, raster);
	}

	public static ITimeSeriesVelocityRasterizer<T> AsClipped<T>(this ITimeSeriesVelocityRasterizer<T> baseRes, Extent ext) where T : unmanaged, IEquatable<T>
	{
		return new ClippedBandedVelocityRaster<T>(baseRes, baseRes.RasterDefinition.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered));
	}

	public static ITimeSeriesVelocityRasterizer<T> AsClipped<T>(this ITimeSeriesVelocityRasterizer<T> baseRes, PixelRange pr) where T : unmanaged, IEquatable<T>
	{
		return new ClippedBandedVelocityRaster<T>(baseRes, pr);
	}

	public static Vector2[] GetBandAsVec2(this ITimeSeriesVelocityRasterizer<float> baseRes, int bandIndex)
	{
		PixelRange pixelRange = baseRes.RasterDefinition.FullRange();
		float[] floatValues = new float[pixelRange.CellCount];
		float[] floatValuesArray = new float[pixelRange.CellCount];
		float[] floatValues2 = new float[pixelRange.CellCount];
		baseRes.GetRangeVxVy(baseRes.RasterDefinition.FullRange(), bandIndex, floatValues, floatValuesArray, floatValues2);
		Vector2[] vector2s = new Vector2[pixelRange.CellCount];
		for (int i = 0; i < pixelRange.CellCount; i++)
		{
			vector2s[i] = new Vector2(floatValues[i], floatValuesArray[i]);
		}
		return vector2s;
	}

	public static IVelocityResampler<T> AsSingleProfile<T>(this ITimeSeriesVelocityResampler<T> res, int bandIndex) where T : unmanaged, IEquatable<T>
	{
		return new SingleTimestepVelocityResampler<T>(res, bandIndex);
	}

	public static ITimeSeriesRaster<T> GetVxRasterizer<T>(this ITimeSeriesVelocityRasterizer<T> rasterizer) where T : unmanaged, IEquatable<T>
	{
		return new ComponentRasterizer<T>(rasterizer, ToVectorComponent.ToVx);
	}

	public static ITimeSeriesRaster<T> GetVyRasterizer<T>(this ITimeSeriesVelocityRasterizer<T> rasterizer) where T : unmanaged, IEquatable<T>
	{
		return new ComponentRasterizer<T>(rasterizer, ToVectorComponent.ToVy);
	}
}
