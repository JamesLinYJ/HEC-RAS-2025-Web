using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Rasters.Modifiers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Ras.Layers.Terrains;

public static class LeveeResampler
{
	private class LeveeModResampler : IResample<float>, ISurface<float>
	{
		private TinResampler _tinResampler;

		private RasterModType _modType;

		public Extent ValidExtent => _tinResampler.ValidExtent;

		public float? NoDataValue => _tinResampler.NoDataValue;

		public LeveeModResampler(TinResampler tinResampler, RasterModType modType)
		{
			_tinResampler = tinResampler;
			_modType = modType;
		}

		public void Resample(ResampleArgs args, Memory<float> outData)
		{
			Span<float> outDataSpan = outData.Span;
			using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(outData.Length);
			Memory<float> outData2 = memoryOwner.Memory.Slice(0, outDataSpan.Length);
			Span<float> outData2Span = outData2.Span;
			if (NoDataValue.HasValue)
			{
				outData2Span.Fill(NoDataValue.Value);
			}
			_tinResampler.Resample(args, outData2);
			CopySpan(outDataSpan, outData2Span, NoDataValue, _modType);
		}

		public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
		{
			Span<float> outDataSpan = outData.Span;
			using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(outData.Length);
			Memory<float> outData2 = memoryOwner.Memory.Slice(0, outDataSpan.Length);
			Span<float> outData2Span = outData2.Span;
			if (NoDataValue.HasValue)
			{
				outData2Span.Fill(NoDataValue.Value);
			}
			_tinResampler.SamplePoints(points, outData2, sampleParams);
			CopySpan(outDataSpan, outData2Span, NoDataValue, _modType);
		}

		private static void CopySpan(Span<float> to, ReadOnlySpan<float> from, double? noDataValue, RasterModType modType)
		{
			switch (modType)
			{
			case RasterModType.SetValue:
			{
				for (int l = 0; l < to.Length; l++)
				{
					if ((double)from[l] != noDataValue)
					{
						to[l] = from[l];
					}
				}
				break;
			}
			case RasterModType.AddValue:
			{
				for (int j = 0; j < to.Length; j++)
				{
					if ((double)to[j] != noDataValue && (double)from[j] != noDataValue)
					{
						to[j] += from[j];
					}
				}
				break;
			}
			case RasterModType.TakeLower:
			{
				for (int k = 0; k < to.Length; k++)
				{
					if ((double)to[k] != noDataValue && (double)from[k] != noDataValue && from[k] < to[k])
					{
						to[k] = from[k];
					}
				}
				break;
			}
			case RasterModType.TakeHigher:
			{
				for (int i = 0; i < to.Length; i++)
				{
					if ((double)to[i] == noDataValue || ((double)from[i] != noDataValue && from[i] > to[i]))
					{
						to[i] = from[i];
					}
				}
				break;
			}
			}
		}
	}

	private const double OffsetArcTolerance = 0.95;

	private static double RenormalizeStation(double station, double stationMax, double lineMax)
	{
		return station / stationMax * lineMax;
	}

	private static void InsertStations(Polyline line, StationElevationProfile profile)
	{
		foreach (double station in profile.Stations)
		{
			if (!(station <= 0.0) && !(station > line.Length))
			{
				PointOnPolyline pointOnPolyline = line.PointAtDistance(station);
				line.Insert(pointOnPolyline.SegmentIndex + 1, pointOnPolyline.Point);
			}
		}
		double[] cumulativePointStations = line.CumulativePointStations();
		line.TrackZ();
		for (int i = 0; i < cumulativePointStations.Length; i++)
		{
			line.Z[i] = profile.Query(cumulativePointStations[i]);
		}
	}

	public static IResample<float> Create(Polyline line, StationElevationProfile profile, double topWidth, double leftGrade, double rightGrade, double leftWidth, double rightWidth, LeveeMode mode, RasterModType modType)
	{
		if (!TryBuildTin(line, profile, topWidth, leftGrade, rightGrade, leftWidth, rightWidth, mode, out var tin))
		{
			return null;
		}
		return new LeveeModResampler(new TinResampler(tin, renderZ: true), modType);
	}

	public static bool TryBuildTin(LeveeMod levee, LeveeModProperties properties, out Tin tin)
	{
		if (levee == null || properties == null)
		{
			tin = null;
			return false;
		}
		return TryBuildTin(levee.Line, levee.Profile, properties.TopWidth, properties.LeftGrade, properties.EffectiveRightGrade, properties.LeftWidth, properties.EffectiveRightWidth, properties.Mode, out tin);
	}

	public static bool TryBuildTin(Polyline line, StationElevationProfile profile, double topWidth, double leftGrade, double rightGrade, double leftWidth, double rightWidth, LeveeMode mode, out Tin tin)
	{
		tin = null;
		if (line == null || profile == null)
		{
			return false;
		}
		if (line.PointCount < 2 || profile.Count < 2)
		{
			return false;
		}
		if (!profile.IsStationSorted())
		{
			return false;
		}
		if (profile.HasDuplicates())
		{
			return false;
		}
		line = line.Clone();
		InsertStations(line, profile);
		Vector vector = new Vector(leftGrade, 1.0);
		vector.Normalize();
		Vector vector2 = new Vector(rightGrade, 1.0);
		vector2.Normalize();
		double x = vector.X;
		double y = vector.Y;
		double x2 = vector2.X;
		double y2 = vector2.Y;
		OffsetPolylineFactory.ComputeOffset(topWidth, line, out var leftOffset, out var rightOffset, filterLine: false);
		OffsetPolylineFactory.ComputeOffset(topWidth + x * leftWidth, line, out var leftOffset2, out var rightOffset2, filterLine: false);
		OffsetPolylineFactory.ComputeOffset(topWidth + x2 * rightWidth, line, out rightOffset2, out var rightOffset3, filterLine: false);
		if (mode == LeveeMode.SimpleCut)
		{
			for (int i = 0; i < leftOffset2.PointCount; i++)
			{
				leftOffset2.Z[i] += y * leftWidth;
			}
			for (int j = 0; j < rightOffset3.PointCount; j++)
			{
				rightOffset3.Z[j] += y2 * rightWidth;
			}
		}
		else
		{
			for (int k = 0; k < leftOffset2.PointCount; k++)
			{
				leftOffset2.Z[k] -= y * leftWidth;
			}
			for (int l = 0; l < rightOffset3.PointCount; l++)
			{
				rightOffset3.Z[l] -= y2 * rightWidth;
			}
		}
		List<Point> points = new List<Point>(leftOffset2.Count + rightOffset3.Count + 4);
		List<double> doubleList = new List<double>(leftOffset2.Count + rightOffset3.Count + 4);
		for (int m = 0; m < leftOffset2.PointCount; m++)
		{
			points.Add(leftOffset2.Points[m]);
			doubleList.Add(leftOffset2.Z[m]);
		}
		points.Add(leftOffset[leftOffset.PointCount - 1]);
		doubleList.Add(leftOffset.Z[leftOffset.PointCount - 1]);
		points.Add(rightOffset[rightOffset.PointCount - 1]);
		doubleList.Add(rightOffset.Z[rightOffset.PointCount - 1]);
		for (int pointCountOffset = rightOffset3.PointCount - 1; pointCountOffset >= 0; pointCountOffset--)
		{
			points.Add(rightOffset3.Points[pointCountOffset]);
			doubleList.Add(rightOffset3.Z[pointCountOffset]);
		}
		points.Add(rightOffset[0]);
		doubleList.Add(rightOffset.Z[0]);
		points.Add(leftOffset[0]);
		doubleList.Add(leftOffset.Z[0]);
		Polygon ring = new Polygon(points, doubleList);
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
		delaunayTinFactory.AddPerimeter(ring);
		delaunayTinFactory.AddBreakline(leftOffset);
		delaunayTinFactory.AddBreakline(rightOffset);
		tin = delaunayTinFactory.ComputeTIN();
		return true;
	}
}
