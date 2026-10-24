using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;
using Utility.DataStructures;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

public class VelocityRaster : ITimeSeriesVelocityRasterizer<float>, ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private readonly ITimeSeriesRaster<float> _mag;

	private readonly ITimeSeriesRaster<float> _dir;

	private readonly DirectionConvention _dirConvention;

	private readonly ITimeSeriesRaster<float> _vx;

	private readonly ITimeSeriesRaster<float> _vy;

	private readonly VelocityDataType _vdt;

	private static readonly List<DateTime> _allTime = new List<DateTime>(new DateTime[2]
	{
		DateTime.MinValue,
		DateTime.MaxValue
	});

	public Extent ValidExtent => RasterDefinition.Extent;

	public float? NoDataValue { get; }

	public IReadOnlyList<DateTime> Times { get; }

	public int ProfileCount => Times.Count;

	public RasterDefinition RasterDefinition { get; }

	public bool CanInterpolateTimesteps => false;

	public static VelocityRaster FromMagnitudeDirection(ITimeSeriesRaster<float> mag, ITimeSeriesRaster<float> dir, DirectionConvention conv)
	{
		return new VelocityRaster(VelocityDataType.MagnitudeDirection, mag, dir, conv);
	}

	public static VelocityRaster FromMagnitudeDirection(ITimeSeriesRasterProvider<float> mag, ITimeSeriesRasterProvider<float> dir, DirectionConvention conv)
	{
		return new VelocityRaster(VelocityDataType.MagnitudeDirection, mag.AsTimeSeriesRasterizer(), dir.AsTimeSeriesRasterizer(), conv);
	}

	public static VelocityRaster FromVxVy(ITimeSeriesRaster<float> vx, ITimeSeriesRaster<float> vy)
	{
		return new VelocityRaster(VelocityDataType.VxVy, vx, vy, DirectionConvention.GoingToward);
	}

	public static VelocityRaster FromVxVy(ITimeSeriesRasterProvider<float> vx, ITimeSeriesRasterProvider<float> vy)
	{
		return new VelocityRaster(VelocityDataType.VxVy, vx.AsTimeSeriesRasterizer(), vy.AsTimeSeriesRasterizer(), DirectionConvention.GoingToward);
	}

	public static VelocityRaster FromConstantVxVy(float vx, float vy)
	{
		RasterDefinition raster = new RasterDefinition(Extent.EffectivelyInfinite, 1, 1);
		List<Any<float[]>> anies = new List<Any<float[]>>();
		anies.Add(new float[1] { vx });
		anies.Add(anies[0]);
		RasterTimeSeries magOrVx = new RasterTimeSeries(raster, _allTime, anies);
		List<Any<float[]>> aniesList = new List<Any<float[]>>();
		aniesList.Add(new float[1] { vy });
		aniesList.Add(aniesList[0]);
		RasterTimeSeries dirOrVy = new RasterTimeSeries(raster, _allTime, aniesList);
		return new VelocityRaster(VelocityDataType.VxVy, magOrVx, dirOrVy, DirectionConvention.GoingToward);
	}

	public static VelocityRaster FromConstantMagDir(float mag, float dir, DirectionConvention dc)
	{
		RasterDefinition raster = new RasterDefinition(Extent.EffectivelyInfinite, 1, 1);
		List<Any<float[]>> anies = new List<Any<float[]>>();
		anies.Add(new float[1] { mag });
		anies.Add(anies[0]);
		RasterTimeSeries magOrVx = new RasterTimeSeries(raster, _allTime, anies);
		List<Any<float[]>> aniesList = new List<Any<float[]>>();
		aniesList.Add(new float[1] { dir });
		aniesList.Add(aniesList[0]);
		RasterTimeSeries dirOrVy = new RasterTimeSeries(raster, _allTime, aniesList);
		return new VelocityRaster(VelocityDataType.VxVy, magOrVx, dirOrVy, dc);
	}

	private VelocityRaster(VelocityDataType vdt, ITimeSeriesRaster<float> magOrVx, ITimeSeriesRaster<float> dirOrVy, DirectionConvention dc)
	{
		_vdt = vdt;
		_dirConvention = dc;
		if (magOrVx == null || dirOrVy == null)
		{
			throw new ArgumentNullException();
		}
		if (!magOrVx.RasterDefinition.EqualRasterDefinition(dirOrVy.RasterDefinition))
		{
			throw new ArgumentException("Given Time-Series rasters must have matching RasterDefinitions.");
		}
		if (!magOrVx.Times.SequenceEqual(dirOrVy.Times))
		{
			throw new ArgumentException("Given Time-Series rasters must have matching TimeSteps.");
		}
		if (vdt == VelocityDataType.MagnitudeDirection)
		{
			_mag = magOrVx;
			_dir = dirOrVy;
		}
		else
		{
			_vx = magOrVx;
			_vy = dirOrVy;
		}
		RasterDefinition = magOrVx.RasterDefinition;
		NoDataValue = magOrVx.NoDataValue;
		Times = magOrVx.Times;
	}

	public void ResampleVxVy(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		if (_vdt == VelocityDataType.VxVy)
		{
			if (outVx.IsEmpty && outVy.IsEmpty)
			{
				outVx = new float[args.Raster.CellCount];
			}
			if (outVy.IsEmpty)
			{
				outVy = new float[args.Raster.CellCount];
			}
			args.EnsureBuffer(outVx, NoDataValue);
			args.EnsureBuffer(outVy, NoDataValue);
			_vx.Resample(args2, profileIndex, outVx);
			_vy.Resample(args2, profileIndex, outVy);
			if (!outVelMag.IsEmpty)
			{
				args.EnsureBuffer(outVelMag, NoDataValue);
				VelocityMath.ComputeMagnitude(outVx, outVy, outVelMag, NoDataValue);
			}
			return;
		}
		args.EnsureBuffer(outVx, NoDataValue);
		args.EnsureBuffer(outVy, NoDataValue);
		if (outVelMag.IsEmpty)
		{
			outVelMag = new float[args.Raster.CellCount];
			args.EnsureBuffer(outVelMag, NoDataValue);
		}
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(args.Raster.CellCount, out mem))
		{
			if (NoDataValue.HasValue)
			{
				mem.Span.Fill(NoDataValue.Value);
			}
			_mag.Resample(args2, profileIndex, outVelMag);
			_dir.Resample(args2, profileIndex, mem);
			if (!outVx.IsEmpty || !outVy.IsEmpty)
			{
				if (outVx.IsEmpty)
				{
					outVx = new float[args.Raster.CellCount];
				}
				if (outVy.IsEmpty)
				{
					outVy = new float[args.Raster.CellCount];
				}
				VelocityMath.ConvertToVxVy(outVelMag, mem, outVx, outVy, NoDataValue, _dirConvention);
			}
		}
	}

	public void ResampleMagDir(ResampleArgs args, int profileIndex, Memory<float> outMag, Memory<float> outDir, DirectionConvention desiredConvention)
	{
		args.EnsureBuffer(outDir, NoDataValue);
		args.EnsureBuffer(outMag, NoDataValue);
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		if (_vdt == VelocityDataType.VxVy)
		{
			int cellCount = args.Raster.CellCount;
			Memory<float> mem;
			using (SharedMemoryPool<float>.Rent(cellCount, out mem))
			{
				Memory<float> mem2;
				using (SharedMemoryPool<float>.Rent(cellCount, out mem2))
				{
					if (NoDataValue.HasValue)
					{
						mem.Span.Fill(NoDataValue.Value);
						mem2.Span.Fill(NoDataValue.Value);
					}
					_vx.Resample(args2, profileIndex, mem);
					_vy.Resample(args2, profileIndex, mem2);
					VelocityMath.ConvertToMagDir(mem, mem2, outMag, outDir, NoDataValue, desiredConvention);
					return;
				}
			}
		}
		_mag.Resample(args2, profileIndex, outMag);
		_dir.Resample(args2, profileIndex, outDir);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outVx, Memory<float>[] outVy, Memory<float>[] outVelMag)
	{
		if (_vdt == VelocityDataType.VxVy)
		{
			_vx.SamplePoints(points, bandStart, bandCount, outVx);
			_vy.SamplePoints(points, bandStart, bandCount, outVy);
			if (outVelMag != null)
			{
				for (int i = 0; i < outVx.Length; i++)
				{
					VelocityMath.ComputeMagnitude(outVx[i], outVy[i], outVelMag[i], NoDataValue);
				}
			}
		}
		else
		{
			_mag.SamplePoints(points, bandStart, bandCount, outVx);
			_dir.SamplePoints(points, bandStart, bandCount, outVy);
			for (int j = 0; j < outVx.Length; j++)
			{
				VelocityMath.ConvertToVxVy(outVx[j], outVy[j], outVx[j], outVy[j], NoDataValue, _dirConvention);
			}
		}
	}

	private static void AssertSizeOrEmpty<T>(Memory<T> buf, int desiredSize)
	{
		if (!buf.IsEmpty && buf.Length != desiredSize)
		{
			throw new ArgumentException("Buffer incorrectly sized.");
		}
	}

	public void GetRangeVxVy(PixelRange range, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		if (outVx.IsEmpty && outVy.IsEmpty && outVelMag.IsEmpty)
		{
			return;
		}
		int cellCount = range.CellCount;
		AssertSizeOrEmpty(outVx, cellCount);
		AssertSizeOrEmpty(outVy, cellCount);
		AssertSizeOrEmpty(outVelMag, cellCount);
		if (_vdt == VelocityDataType.VxVy)
		{
			bool notIsEmptyOrNotIsEmpty = !outVx.IsEmpty || !outVelMag.IsEmpty;
			bool notIsEmptyOrNotIsEmpty2 = !outVy.IsEmpty || !outVelMag.IsEmpty;
			bool notIsEmpty = !outVelMag.IsEmpty;
			if (notIsEmptyOrNotIsEmpty)
			{
				if (outVx.IsEmpty)
				{
					outVx = new float[cellCount];
				}
				_vx.GetRange(range, bandIndex, outVx);
			}
			if (notIsEmptyOrNotIsEmpty2)
			{
				if (outVy.IsEmpty)
				{
					outVy = new float[cellCount];
				}
				_vy.GetRange(range, bandIndex, outVy);
			}
			if (notIsEmpty)
			{
				VelocityMath.ComputeMagnitude(outVx, outVy, outVelMag, NoDataValue);
			}
			return;
		}
		if (_vdt == VelocityDataType.MagnitudeDirection)
		{
			if (!outVelMag.IsEmpty && outVx.IsEmpty && outVy.IsEmpty)
			{
				_mag.GetRange(range, bandIndex, outVelMag);
				return;
			}
			Memory<float> outVySpan = (outVx.IsEmpty ? outVy : outVx);
			if (outVelMag.IsEmpty)
			{
				outVelMag = ((outVx.IsEmpty || outVy.IsEmpty) ? ((Memory<float>)new float[cellCount]) : outVy);
			}
			_mag.GetRange(range, bandIndex, outVelMag);
			_dir.GetRange(range, bandIndex, outVySpan);
			VelocityMath.ConvertToVxVy(outVelMag, outVySpan, outVx, outVy, NoDataValue, _dirConvention);
			return;
		}
		throw new NotSupportedException();
	}

	public void GetRangeMagDir(PixelRange range, int bandIndex, Memory<float> outVelMag, Memory<float> outDir)
	{
		if (_vdt == VelocityDataType.VxVy)
		{
			_vx.GetRange(range, bandIndex, outVelMag);
			_vy.GetRange(range, bandIndex, outDir);
			VelocityMath.ConvertToMagDir(outVelMag, outDir, outVelMag, outDir, NoDataValue.GetValueOrDefault(), _dirConvention);
			return;
		}
		if (_vdt == VelocityDataType.MagnitudeDirection)
		{
			_mag.GetRange(range, bandIndex, outVelMag);
			_dir.GetRange(range, bandIndex, outDir);
			return;
		}
		throw new NotSupportedException();
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outMagnitude)
	{
		SamplePointsVxVy(points, bandIndex, 1, new Memory<float>[1] { outVx }, new Memory<float>[1] { outVy }, new Memory<float>[1] { outMagnitude });
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		int binarySearch = MathHelpers.BinarySearch(Times, time);
		if (binarySearch < 0)
		{
			throw new ArgumentException($"DateTime {time} not found in discrete timesteps. Interpolation not supported.");
		}
		Resample(args, binarySearch, outData, cache);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		int profileCount = ProfileCount;
		double[] doubleValues = new double[profileCount];
		Point[] points = new Point[1] { pt };
		float[] floatValues = new float[1];
		for (int i = 0; i < profileCount; i++)
		{
			SamplePoints(points, i, floatValues);
			doubleValues[i] = floatValues[0];
		}
		return new TimeSeries(Times, doubleValues);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		ResampleMagDir(args, bandIndex, outData, Memory<float>.Empty, _dirConvention);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		for (int i = 0; i < bandCount; i++)
		{
			Resample(args, startBandIndex + i, outData[i]);
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		if (_vdt == VelocityDataType.VxVy)
		{
			int length = points.Length;
			Memory<float>[] floatSpanArray = new Memory<float>[bandCount];
			Memory<float>[] floatSpanArraySpan = new Memory<float>[bandCount];
			for (int i = 0; i < bandCount; i++)
			{
				floatSpanArray[i] = new float[length];
				floatSpanArraySpan[i] = new float[length];
			}
			_vx.SamplePoints(points, startBandIndex, bandCount, floatSpanArray);
			_vy.SamplePoints(points, startBandIndex, bandCount, floatSpanArraySpan);
			for (int j = 0; j < bandCount; j++)
			{
				VelocityMath.ComputeMagnitude(floatSpanArray[j], floatSpanArraySpan[j], outData[j], NoDataValue);
			}
		}
		else
		{
			_mag.SamplePoints(points, startBandIndex, bandCount, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		SamplePoints(points, bandIndex, 1, new Memory<float>[1] { outData });
	}
}
