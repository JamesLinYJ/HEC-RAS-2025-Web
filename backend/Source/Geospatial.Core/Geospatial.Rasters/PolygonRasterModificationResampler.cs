using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility;

namespace Geospatial.Rasters;

public class PolygonRasterModificationResampler : IResample<float>, ISurface<float>, ISpatialVersion
{
	private readonly List<PolygonRasterMod> _mods = new List<PolygonRasterMod>();

	private CollectionVersionTracker _tracker;

	private Freezer _freezer;

	private readonly Versioned<Extent> _extVer = new Versioned<Extent>();

	private readonly ThreadLocal<PolygonRasterizer> _pgRasterizers = new ThreadLocal<PolygonRasterizer>();

	public int Count => _mods.Count;

	public IReadOnlyList<PolygonRasterMod> Modifications => _mods;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _tracker.GetVersion(_mods);
		}
	}

	public Extent ValidExtent => _extVer.Get(VersionXY, ComputeExtent);

	public float? NoDataValue { get; }

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public PolygonRasterModificationResampler()
		: this(-9999f)
	{
	}

	public PolygonRasterModificationResampler(float ndv)
	{
		NoDataValue = ndv;
	}

	public PolygonRasterModificationResampler(IEnumerable<PolygonRasterMod> mods)
		: this(-9999f)
	{
		_mods = mods.ToList();
	}

	public void Add(Polygon pg, RasterModType modType, float floatValue)
	{
		_mods.Add(new PolygonRasterMod(pg, modType, floatValue));
	}

	public void Add(PolygonRasterMod prm)
	{
		_mods.Add(prm);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		if (Count == 0)
		{
			return;
		}
		_ = args.Raster.Extent;
		_ = ValidExtent;
		RasterDefinition raster = args.Raster;
		PolygonRasterizer polygonRasterizer = _pgRasterizers.Value;
		if (polygonRasterizer == null)
		{
			polygonRasterizer = new PolygonRasterizer(raster);
			_pgRasterizers.Value = polygonRasterizer;
		}
		else
		{
			polygonRasterizer.Reset(raster);
		}
		if (!NoDataValue.HasValue || float.IsNaN(NoDataValue.Value))
		{
			throw new NotSupportedException("Needs a valid NoData Value");
		}
		float ndv = NoDataValue.Value;
		_ = raster.Cols;
		for (int i = 0; i < Count; i++)
		{
			PolygonRasterMod polygonRasterMod = _mods[i];
			float v = polygonRasterMod.Value;
			if (!polygonRasterMod.Polygon.Extent.Overlaps(raster.Extent))
			{
				continue;
			}
			if (polygonRasterMod.ModificationType == RasterModType.AddValue)
			{
				polygonRasterizer.Rasterize(polygonRasterMod.Polygon, (int row, int start, int stop) =>
				{
					int start2 = row * args.Raster.Cols + start;
					int length = stop - start + 1;
					Buffers.AddConstant(outData.Span.Slice(start2, length), v, ndv);
				});
				continue;
			}
			if (polygonRasterMod.ModificationType == RasterModType.SetValue)
			{
				polygonRasterizer.Rasterize(polygonRasterMod.Polygon, (int row, int start, int stop) =>
				{
					int start2 = row * args.Raster.Cols + start;
					int length = stop - start + 1;
					outData.Span.Slice(start2, length).Fill(v);
				});
				continue;
			}
			if (polygonRasterMod.ModificationType == RasterModType.TakeHigher)
			{
				polygonRasterizer.Rasterize(polygonRasterMod.Polygon, (int row, int start, int stop) =>
				{
					int start2 = row * args.Raster.Cols + start;
					int length = stop - start + 1;
					Buffers.TakeHigher(outData.Span.Slice(start2, length), v, ndv);
				});
				continue;
			}
			if (polygonRasterMod.ModificationType == RasterModType.TakeLower)
			{
				polygonRasterizer.Rasterize(polygonRasterMod.Polygon, (int row, int start, int stop) =>
				{
					int start2 = row * args.Raster.Cols + start;
					int length = stop - start + 1;
					Buffers.TakeLower(outData.Span.Slice(start2, length), v, ndv);
				});
				continue;
			}
			throw new NotImplementedException();
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Span<Point> pointsSpan = points.Span;
		Span<float> outDataSpan = outData.Span;
		for (int i = 0; i < points.Length; i++)
		{
			Point point = pointsSpan[i];
			for (int j = 0; j < Count; j++)
			{
				PolygonRasterMod polygonRasterMod = _mods[j];
				float outDataSpan2 = polygonRasterMod.Value;
				if (!polygonRasterMod.Polygon.Contains(point))
				{
					continue;
				}
				if (polygonRasterMod.ModificationType == RasterModType.AddValue)
				{
					outDataSpan[i] += outDataSpan2;
					continue;
				}
				if (polygonRasterMod.ModificationType == RasterModType.SetValue)
				{
					outDataSpan[i] = outDataSpan2;
					continue;
				}
				if (polygonRasterMod.ModificationType == RasterModType.TakeHigher)
				{
					if (outDataSpan2 > outDataSpan[i])
					{
						outDataSpan[i] = outDataSpan2;
					}
					continue;
				}
				if (polygonRasterMod.ModificationType == RasterModType.TakeLower)
				{
					if (outDataSpan2 < outDataSpan[i])
					{
						outDataSpan[i] = outDataSpan2;
					}
					continue;
				}
				throw new NotImplementedException();
			}
		}
	}

	private Extent ComputeExtent()
	{
		return Extent.Union(_mods.Select((PolygonRasterMod m) => m.Extent));
	}
}
