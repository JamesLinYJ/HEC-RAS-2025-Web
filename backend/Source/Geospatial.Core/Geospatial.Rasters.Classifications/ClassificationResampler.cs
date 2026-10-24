using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Progress;

namespace Geospatial.Rasters.Classifications;

public class ClassificationResampler : IClassificationResampler, IResample<int>, ISurface<int>, IBlame
{
	private struct PriorityPolygon(Polygon poly, int classifier)
	{
		public readonly Polygon Polygon = poly;

		public readonly Extent Extent = poly.Extent;

		public readonly int Classifier = classifier;

		public readonly Lazy<Tin> DevTin = new Lazy<Tin>(() => poly.DevTin, isThreadSafe: true);
	}

	private readonly IResample<int> _resampler;

	private readonly IClassification _classifier;

	private readonly List<PriorityPolygon> _classifierPolygons;

	private readonly Extent _pgonExtents;

	private readonly Lazy<SpatialIndex<int>> _pgonSpIdx;

	public IClassification Classifier => _classifier;

	private IList<PriorityPolygon> PolygonsHighestToLowest => _classifierPolygons;

	private IList<PriorityPolygon> PolygonsLowestToHighest => _classifierPolygons.ReverseIndexing();

	public IRasterSelector RasterSelector { get; set; } = new EqualDefinitionRasterSelector(10000);

	public Extent ValidExtent { get; private set; }

	public int? NoDataValue => _resampler.NoDataValue;

	public ClassificationResampler(IResample<int> raster, IClassification classifier)
	{
		_resampler = raster ?? throw new ArgumentNullException("raster");
		_classifier = classifier ?? throw new ArgumentNullException("classifier");
		_classifierPolygons = new List<PriorityPolygon>();
		_pgonExtents = Extent.Union(_classifierPolygons.Select((PriorityPolygon a) => a.Polygon));
		ValidExtent = Extent.Union(_pgonExtents, _resampler.ValidExtent);
		_pgonSpIdx = new Lazy<SpatialIndex<int>>(() => SpatialIndex.FromPolygons(_classifierPolygons.Select((PriorityPolygon a) => a.Polygon).ToList()), isThreadSafe: true);
	}

	public ClassificationResampler(IResample<int> raster, IList<Tuple<Polygon, int>> classifierPolygons, IClassification classifier)
	{
		_resampler = raster ?? throw new ArgumentNullException("raster");
		_classifier = classifier ?? throw new ArgumentNullException("classifier");
		_classifierPolygons = classifierPolygons?.Select((Tuple<Polygon, int> tup) => new PriorityPolygon(tup.Item1, tup.Item2)).ToList() ?? new List<PriorityPolygon>();
		_pgonExtents = Extent.Union(_classifierPolygons.Select((PriorityPolygon a) => a.Polygon));
		ValidExtent = Extent.Union(_pgonExtents, _resampler.ValidExtent);
		_pgonSpIdx = new Lazy<SpatialIndex<int>>(() => SpatialIndex.FromPolygons(_classifierPolygons.Select((PriorityPolygon a) => a.Polygon).ToList()), isThreadSafe: true);
	}

	public virtual HashSet<int> UniqueClassifications(Extent ext)
	{
		if (!ValidExtent.Overlaps(in ext))
		{
			return new HashSet<int>();
		}
		ThreadLocal<HashSet<int>> tlHS = new ThreadLocal<HashSet<int>>(() => new HashSet<int>(), trackAllValues: true);
		if (_resampler is IRaster<int> raster)
		{
			RasterOperations.ProcessTiled(raster, ext, clipTilesToDesiredExtent: true, (RasterDefinition tileRD, Memory<int> tileData) =>
			{
				HashSet<int> tlHSTerm = tlHS.Value;
				HashSet<int> newElements = HashHelpers.ComputeHashSet(tileData);
				tlHSTerm.AddRange(newElements);
			}, new ParallelizationPlan(ParallelizationIntent.Parallel, 17));
		}
		else
		{
			RasterDefinition appropriateResampleRaster = RasterSelector.GetAppropriateResampleRaster(ext);
			RasterOperations.ProcessTiled(_resampler.AsRaster(appropriateResampleRaster), ext, clipTilesToDesiredExtent: true, (RasterDefinition tileRD, Memory<int> tileData) =>
			{
				HashSet<int> tlHSTermSet = tlHS.Value;
				HashSet<int> newElements = HashHelpers.ComputeHashSet(tileData);
				tlHSTermSet.AddRange(newElements);
			}, new ParallelizationPlan(ParallelizationIntent.Parallel, 17));
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (HashSet<int> localValue in tlHS.Values)
		{
			intSet.AddRange(localValue);
		}
		if (!ext.Overlaps(in _pgonExtents))
		{
			return intSet;
		}
		foreach (PriorityPolygon classifierPolygon in _classifierPolygons)
		{
			if (!intSet.Contains(classifierPolygon.Classifier) && classifierPolygon.Polygon.AreaIntersects(ext))
			{
				intSet.Add(classifierPolygon.Classifier);
			}
		}
		return intSet;
	}

	public Dictionary<int, double> SampleClassifications(Extent ext)
	{
		return SampleClassifications(Polygon.FromExtent(ext));
	}

	public Dictionary<int, double> SampleClassifications(Polygon polygon)
	{
		return ClassificationRasterOperations.SampleClassifications(this, polygon, RasterSelector.GetAppropriateResampleRaster(polygon));
	}

	public List<Dictionary<int, double>> SampleClassifications(IList<Polygon> polygons)
	{
		List<Dictionary<int, double>> doubleByIntList = new List<Dictionary<int, double>>(polygons.Count);
		foreach (Polygon polygon in polygons)
		{
			doubleByIntList.Add(SampleClassifications(polygon));
		}
		return doubleByIntList;
	}

	public void Resample(ResampleArgs args, Memory<int> outData)
	{
		_resampler.Resample(Sanitize(args), outData);
		if (_classifierPolygons.Count == 0)
		{
			return;
		}
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(args.Raster);
		foreach (PriorityPolygon prio in PolygonsLowestToHighest)
		{
			polygonRasterizer.Rasterize(prio.Polygon, (int px) =>
			{
				outData.Span[px] = prio.Classifier;
			});
		}
	}

	public int SamplePoint(Point query)
	{
		foreach (PriorityPolygon polygonsHighestToLowest in PolygonsHighestToLowest)
		{
			if (polygonsHighestToLowest.DevTin.Value.ContainsPoint(query))
			{
				return polygonsHighestToLowest.Classifier;
			}
		}
		return _resampler.SamplePoint(query);
	}

	public void SamplePoints(Memory<Point> points, Memory<int> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_resampler.SamplePoints(points, outData);
		Span<Point> pointsSpan = points.Span;
		Span<int> outDataSpan = outData.Span;
		foreach (PriorityPolygon polygonsLowestToHighest in PolygonsLowestToHighest)
		{
			Extent extent = polygonsLowestToHighest.Extent;
			Lazy<Tin> devTin = polygonsLowestToHighest.DevTin;
			int t = 0;
			for (int i = 0; i < pointsSpan.Length; i++)
			{
				Point point = pointsSpan[i];
				if (extent.Contains(point) && devTin.Value.ContainsPoint(point, ref t))
				{
					outDataSpan[i] = polygonsLowestToHighest.Classifier;
				}
			}
		}
	}

	public int[] SamplePoints(Memory<Point> points)
	{
		int[] intValues = new int[points.Length];
		SamplePoints(points, intValues);
		return intValues;
	}

	private static ResampleArgs Sanitize(ResampleArgs args)
	{
		if (args.SampleParams.Mode == InterpolationMode.Nearest)
		{
			return args;
		}
		return new ResampleArgs(args.Raster);
	}

	public void Blame(Point pt, ProgressReporter reporter)
	{
		reporter.ReportMessage("ClassificationResampler: Querying " + pt.ToString());
		int ordinal = 0;
		foreach (PriorityPolygon polygonsHighestToLowest in PolygonsHighestToLowest)
		{
			ordinal++;
			if (polygonsHighestToLowest.DevTin.Value.ContainsPoint(pt))
			{
				int classifier = polygonsHighestToLowest.Classifier;
				reporter.ReportMessage("The #" + ordinal + " priority polygon contains this point. Class set to " + classifier + ", which maps to " + Classifier.GetName(classifier).InSingleQuotes());
				return;
			}
		}
		int samplePoint = _resampler.SamplePoint(pt);
		reporter.ReportMessage("No classification polygon contains this point. Raster value is " + samplePoint + ", which maps to " + Classifier.GetName(samplePoint).InSingleQuotes());
	}
}
