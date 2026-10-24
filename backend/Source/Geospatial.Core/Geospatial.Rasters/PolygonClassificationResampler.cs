using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility;

namespace Geospatial.Rasters;

public class PolygonClassificationResampler : IResample<int>, ISurface<int>, ISpatialVersion
{
	private readonly List<PolygonClassifierMod> _mods;

	private CollectionVersionTracker _tracker;

	private readonly ThreadLocal<PolygonRasterizer> _pgRasterizers = new ThreadLocal<PolygonRasterizer>();

	private Freezer _freezer;

	private readonly UniqueClassificationMap _classifier;

	private readonly Versioned<Extent> _extVer = new Versioned<Extent>();

	public IReadOnlyList<PolygonClassifierMod> Modifications => _mods;

	public int Count => _mods.Count;

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

	public int? NoDataValue { get; }

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public PolygonClassificationResampler(UniqueClassificationMap classifier)
	{
		NoDataValue = 0;
		_classifier = classifier;
		_mods = new List<PolygonClassifierMod>();
	}

	public PolygonClassificationResampler(UniqueClassificationMap classifier, IEnumerable<PolygonClassifierMod> mods)
	{
		NoDataValue = 0;
		_classifier = classifier;
		_mods = mods.ToList();
	}

	public void Add(Polygon pg, string classifier)
	{
		_mods.Add(new PolygonClassifierMod(pg, classifier));
	}

	public void Add(PolygonClassifierMod prm)
	{
		_mods.Add(prm);
	}

	public void Resample(ResampleArgs args, Memory<int> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
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
		if (!NoDataValue.HasValue)
		{
			throw new Exception("Layer needs NoDataValue to resample correctly.");
		}
		_ = NoDataValue.Value;
		_ = raster.Cols;
		int count = Count;
		for (int i = 0; i < count; i++)
		{
			PolygonClassifierMod polygonClassifierMod = _mods[i];
			string classification = polygonClassifierMod.Classification;
			int bestID = _classifier.GetBestID(classification);
			if (polygonClassifierMod.Polygon.Extent.Overlaps(raster.Extent) && !classification.Equals("NoData"))
			{
				(int, int, Memory<int>) t = (args.Raster.Cols, bestID, outData);
				polygonRasterizer.Rasterize(polygonClassifierMod.Polygon, t, (int row, int start, int stop, (int, int, Memory<int>) od) =>
				{
					int start2 = row * od.Item1 + start;
					int length = stop - start + 1;
					od.Item3.Span.Slice(start2, length).Fill(od.Item2);
				});
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<int> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		int count = Count;
		int length = points.Length;
		Span<Point> pointsSpan = points.Span;
		Span<int> outDataSpan = outData.Span;
		for (int i = 0; i < count; i++)
		{
			PolygonClassifierMod polygonClassifierMod = Modifications[i];
			if (polygonClassifierMod.Classification.Equals("NoData"))
			{
				continue;
			}
			int bestID = _classifier.GetBestID(polygonClassifierMod.Classification);
			Polygon polygon = polygonClassifierMod.Polygon;
			for (int j = 0; j < length; j++)
			{
				if (polygon.Contains(pointsSpan[j]))
				{
					outDataSpan[j] = bestID;
				}
			}
		}
	}

	private Extent ComputeExtent()
	{
		return Extent.Union(_mods.Select((PolygonClassifierMod m) => m.Extent));
	}
}
