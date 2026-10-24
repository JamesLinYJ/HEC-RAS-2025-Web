using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility;
using Utility.Extensions;
using Utility.Units;

namespace Geospatial.Rasterizers;

public class GaugedVelocityResampler : ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private ISpatialInterpolator _spatialInterpolator;

	private readonly IList<Point> _gaugePoints;

	private readonly IList<IDiscreteVelocityTimeSeries> _velTimeSeries;

	private readonly List<DateTime> _times;

	public ISpatialInterpolator SpatialInterpolator
	{
		get
		{
			return _spatialInterpolator;
		}
		protected set
		{
			if (value == null)
			{
				throw new ArgumentNullException();
			}
			_spatialInterpolator = value;
			_spatialInterpolator.Initialize(_gaugePoints);
		}
	}

	public Extent ValidExtent { get; }

	public float? NoDataValue { get; }

	public IReadOnlyList<DateTime> Times => _times;

	public int ProfileCount => Times.Count;

	public int GaugeCount => _gaugePoints.Count;

	public bool CanInterpolateTimesteps
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public GaugedVelocityResampler(IList<Point> gaugeLocations, IList<IDiscreteVelocityTimeSeries> velocityTimeSeries, ISpatialInterpolator spatialInterpolator, float? noDataValue)
	{
		if (spatialInterpolator == null)
		{
			throw new ArgumentNullException("spatialInterpolator");
		}
		if (gaugeLocations == null)
		{
			throw new ArgumentNullException("gaugeLocations");
		}
		if (velocityTimeSeries == null)
		{
			throw new ArgumentNullException("velocityTimeSeries");
		}
		if (gaugeLocations.Count != velocityTimeSeries.Count)
		{
			throw new ArgumentException("gaugeLocations must match size with velocityTimeSeries");
		}
		_gaugePoints = gaugeLocations;
		_velTimeSeries = velocityTimeSeries;
		SpatialInterpolator = spatialInterpolator;
		NoDataValue = noDataValue;
		HashSet<DateTime> dateTimes = new HashSet<DateTime>();
		foreach (IDiscreteVelocityTimeSeries velocityTimeSery in velocityTimeSeries)
		{
			dateTimes.AddRange(velocityTimeSery.Times);
		}
		_times = dateTimes.OrderBy((DateTime dt) => dt).ToList();
	}

	public void ResampleMagDir(ResampleArgs args, int profileIndex, Memory<float> outMag, Memory<float> outDir, DirectionConvention desiredDirection)
	{
		args.EnsureBuffer(outDir, NoDataValue);
		args.EnsureBuffer(outMag, NoDataValue);
		ResampleInternal(args.Raster, profileIndex, (int px, double vx, double vy) =>
		{
			VelocityMath.ConvertToMagDir((float)vx, (float)vy, desiredDirection, out var dir, out var mag);
			outDir.Span[px] = dir;
			outMag.Span[px] = mag;
		});
	}

	public void ResampleVxVy(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		args.EnsureBuffer(outVx, NoDataValue);
		args.EnsureBuffer(outVy, NoDataValue);
		args.EnsureBuffer(outVelMag, NoDataValue);
		ResampleInternal(args.Raster, profileIndex, (int px, double vx, double vy) =>
		{
			outVx.Span[px] = (float)vx;
			outVy.Span[px] = (float)vy;
			outVelMag.Span[px] = (float)Math.Sqrt(vx * vx + vy * vy);
		});
	}

	private void ResampleInternal(RasterDefinition rdef, int profileIndex, Action<int, double, double> pixelVxVy)
	{
		float ndv = NoDataValue.GetValueOrDefault();
		DateTime time = Times[profileIndex];
		double[] cacheX = new double[GaugeCount];
		double[] cacheY = new double[GaugeCount];
		for (int i = 0; i < GaugeCount; i++)
		{
			_velTimeSeries[i].Sample(time, out var vx, out var vy);
			cacheX[i] = vx;
			cacheY[i] = vy;
		}
		Parallel.SmartFor(rdef.CellCount, (int start, int end) =>
		{
			for (int j = start; j <= end; j++)
			{
				Point queryPoint = rdef.CellCenter(j);
				_spatialInterpolator.Query(queryPoint, out var indexes, out var weights);
				double cacheX2TimesWeightSum = 0.0;
				double cacheY2TimesWeightSum = 0.0;
				for (int k = 0; k < indexes.Length; k++)
				{
					int index = indexes[k];
					double weight = weights[k];
					double cacheX2 = cacheX[index];
					double cacheY2 = cacheY[index];
					if (cacheX2 != (double)ndv)
					{
						cacheX2TimesWeightSum += cacheX2 * weight;
					}
					if (cacheY2 != (double)ndv)
					{
						cacheY2TimesWeightSum += cacheY2 * weight;
					}
				}
				pixelVxVy(j, cacheX2TimesWeightSum, cacheY2TimesWeightSum);
			}
		});
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outVx, Memory<float>[] outVy, Memory<float>[] outVelMag)
	{
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		bool[] boolValues = new bool[GaugeCount];
		double[][] doubleValues = new double[GaugeCount][];
		double[][] doubleValuesArray = new double[GaugeCount][];
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point queryPoint = pointsSpan[i];
			_spatialInterpolator.Query(queryPoint, out var indexes, out var weights);
			for (int j = 0; j < indexes.Length; j++)
			{
				int index = indexes[j];
				double weight = weights[j];
				double[] vx;
				double[] vy;
				if (!boolValues[index])
				{
					_velTimeSeries[index].Sample(_times, out vx, out vy);
					doubleValues[index] = vx;
					doubleValuesArray[index] = vy;
					boolValues[index] = true;
				}
				else
				{
					vx = doubleValues[index];
					vy = doubleValuesArray[index];
				}
				for (int k = bandStart; k < bandStart + bandCount; k++)
				{
					int bandStartTerm = k - bandStart;
					double vx2 = vx[k];
					double vy2 = vy[k];
					if (vx2 != (double)valueOrDefault && vy2 != (double)valueOrDefault)
					{
						float vx2TimesWeight = (float)(vx2 * weight);
						float vy2TimesWeight = (float)(vy2 * weight);
						outVx[bandStartTerm].Span[i] += vx2TimesWeight;
						outVy[bandStartTerm].Span[i] += vy2TimesWeight;
					}
				}
			}
		}
		if (outVelMag == null || outVelMag.Length == 0 || outVelMag[0].IsEmpty)
		{
			return;
		}
		for (int l = 0; l < bandCount; l++)
		{
			Span<float> outVxSpan = outVx[l].Span;
			Span<float> outVySpan = outVy[l].Span;
			Span<float> outVelMagSpan = outVelMag[l].Span;
			for (int m = 0; m < pointsSpan.Length; m++)
			{
				float outVx2 = outVxSpan[m];
				float outVy2 = outVySpan[m];
				outVelMagSpan[m] = (float)Math.Sqrt(outVx2 * outVx2 + outVy2 * outVy2);
			}
		}
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
		_spatialInterpolator.Query(pt, out var indexes, out var weights);
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		double[] doubleValues = new double[ProfileCount];
		for (int i = 0; i < ProfileCount; i++)
		{
			DateTime time = Times[i];
			double vxTimesWeightSum = 0.0;
			double vyTimesWeightSum = 0.0;
			for (int j = 0; j < indexes.Length; j++)
			{
				int index = indexes[j];
				double weight = weights[j];
				_velTimeSeries[index].Sample(time, out var vx, out var vy);
				if (vx != (double)valueOrDefault)
				{
					vxTimesWeightSum += vx * weight;
				}
				if (vy != (double)valueOrDefault)
				{
					vyTimesWeightSum += vy * weight;
				}
			}
			doubleValues[i] = Math.Sqrt(vxTimesWeightSum * vxTimesWeightSum + vyTimesWeightSum * vyTimesWeightSum);
		}
		return new TimeSeries(Times, doubleValues);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		ResampleInternal(args.Raster, bandIndex, (int px, double vx, double vy) =>
		{
			outData.Span[px] = (float)Math.Sqrt(vx * vx + vy * vy);
		});
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
		int length = points.Length;
		Memory<float>[] floatSpanArray = new Memory<float>[bandCount];
		Memory<float>[] floatSpanArraySpan = new Memory<float>[bandCount];
		for (int i = 0; i < bandCount; i++)
		{
			floatSpanArray[i] = new float[length];
			floatSpanArraySpan[i] = new float[length];
		}
		SamplePointsVxVy(points, startBandIndex, bandCount, floatSpanArray, floatSpanArraySpan, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		SamplePoints(points, bandIndex, 1, new Memory<float>[1] { outData });
	}
}
