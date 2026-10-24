using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Rendering;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class PolylineRenderLayer : VectorRenderLayer, ISelection, IBasicSelection, ILineSourceLayer
{
	public static IPolylineRenderer Renderer = new CPUPolylineRenderer();

	private static readonly ILogger _logger = LogHelper.GetLogger("PolylineRenderLayer");

	private readonly IPolylineCollection _polylines;

	private Versioned<PointCollection> _allPointsCollection = new Versioned<PointCollection>();

	private Versioned<Point[]> _midpointsCache = new Versioned<Point[]>();

	private bool _showDirectionArrows;

	private bool _showStrokeVertices;

	private bool _showZValues;

	private string _labelIconResource;

	private Func<int, string> _getLabelName;

	public VectorSymbology VertexSymbology { get; set; } = VectorSymbology.DefaultSymbology();

	private PointCollection AllPointsCollection => _allPointsCollection.Get(_polylines.VersionXY, () => GetAllPoints(_polylines));

	private Point[] Midpoints => _midpointsCache.Get(_polylines.VersionXY, _polylines, (IPolylineCollection pls) => ComputeMidpoints(pls));

	public override Extent ValidExtent => _polylines.Extent;

	public override int Count => _polylines.Count;

	public IPolylineCollection Polylines => _polylines;

	[PlotOption("Show Direction Arrows")]
	public bool ShowDirectionArrows
	{
		get
		{
			return _showDirectionArrows;
		}
		set
		{
			if (_showDirectionArrows != value)
			{
				_showDirectionArrows = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Stroke Vertices")]
	public bool ShowStrokeVertices
	{
		get
		{
			return _showStrokeVertices;
		}
		set
		{
			if (_showStrokeVertices != value)
			{
				_showStrokeVertices = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Z Values")]
	public bool ShowZValues
	{
		get
		{
			return _showZValues;
		}
		set
		{
			if (_showZValues != value)
			{
				_showZValues = value;
				IncrementRenderState();
			}
		}
	}

	public bool LabelFeatures { get; set; }

	public bool TryGetLineSource(out ILineSegmentSource source)
	{
		source = _polylines;
		return true;
	}

	public PolylineRenderLayer()
	{
		_polylines = new PolylineCollection();
	}

	public PolylineRenderLayer(IPolylineCollection polylines)
	{
		_polylines = polylines;
	}

	public PolylineRenderLayer(IReadOnlyList<Polyline> polylines)
	{
		_polylines = new PolylineCollection(polylines);
	}

	public PolylineRenderLayer(IEnumerable<Polyline> polylines)
	{
		_polylines = new PolylineCollection(polylines);
	}

	public PolylineRenderLayer(IEnumerable<Segment> segments)
		: this(segments.Select((Segment seg) => new Polyline(new Point[2] { seg.A, seg.B })))
	{
	}

	public override ISpatialVersion GetSpatialVersionContext()
	{
		return _polylines;
	}

	public IReadOnlyList<Polyline> GetSelectedPolylines()
	{
		if (SelectedSet.Count == 0)
		{
			return null;
		}
		if (SelectedSet.Count == 1)
		{
			int elementsSelectedSetTerm = SelectedSet.Elements.First();
			if (!IsSelectableIndex_LogFailure(elementsSelectedSetTerm))
			{
				return null;
			}
			return new CompactList<Polyline>(Polylines[elementsSelectedSetTerm]);
		}
		List<Polyline> polylines = new List<Polyline>(SelectedSet.Count);
		foreach (int selectedSet in SelectedSet)
		{
			if (IsSelectableIndex_LogFailure(selectedSet))
			{
				polylines.Add(Polylines[selectedSet]);
			}
		}
		return polylines;
	}

	private bool IsSelectableIndex(int idx)
	{
		if (idx >= 0)
		{
			return idx < Polylines.Count;
		}
		return false;
	}

	private bool IsSelectableIndex_LogFailure(int idx)
	{
		bool isSelectableIndex = IsSelectableIndex(idx);
		if (!isSelectableIndex)
		{
			_logger.LogWarning("Invalid selected index: {Index}", idx);
		}
		return isSelectableIndex;
	}

	public IReadOnlyList<Polygon> GetSelectedPolygons()
	{
		return null;
	}

	public override IList<object> GetSelectedVectors()
	{
		if (SelectedSet.Count == 0)
		{
			return null;
		}
		List<object> objectList = new List<object>(SelectedSet.Count);
		foreach (int selectedSet in SelectedSet)
		{
			if (IsSelectableIndex_LogFailure(selectedSet))
			{
				objectList.Add(Polylines[selectedSet]);
			}
		}
		return objectList;
	}

	public override IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		hitTestedIdx = HitTest(args, HitTestSortOrder.Distance).FirstOrDefault(-1);
		if (hitTestedIdx == -1)
		{
			return default(CompactList<object>);
		}
		return new CompactList<object>(Polylines[hitTestedIdx]);
	}

	private Point GetRepresentativePoint(int fid)
	{
		return Midpoints[fid];
	}

	public void SetLabels(Func<int, string> labelName, string iconResource)
	{
		_getLabelName = labelName;
		_labelIconResource = iconResource;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		try
		{
			_polylines.Lock.AcquireReaderLock(10);
			using (_polylines.FreezeSafely())
			{
				try
				{
					if (VectorSymbology.PenWidth <= 0.0)
					{
						VectorSymbology.PenWidth = 1.0;
					}
					Extent validExtent = ValidExtent;
					if (validExtent.IsNull())
					{
						return false;
					}
					if (!validExtent.Overlaps(request.RasterQuery.Extent))
					{
						return false;
					}
					SpatialIndex<FeatureSegmentSubset> segmentSpatialIndex = ((ISpatiallyIndexedSegmentCollection)_polylines).GetSegmentSpatialIndex();
					if (segmentSpatialIndex == null)
					{
						return false;
					}
					RasterDefinition raster = request.RasterQuery.ExtendRaster((int)Math.Ceiling(VectorSymbology.PenWidth));
					List<FeatureSegmentSubset> elements = segmentSpatialIndex.GetElements(raster, dedupe: false);
					elements.Dedupe();
					if (elements.Count == 0)
					{
						return false;
					}
					_ = elements.Count;
					RenderLabels(request, elements);
					RenderFIDLabels(request, elements);
					RenderZLabels(request, elements);
					if (ShowDirectionArrows)
					{
						RepeatedSymbolRenderer.RenderArrowsAlongLine(request, _polylines, (int idx, IPolylineCollection mdl) => mdl[idx], elements, VectorSymbology, 64.0, 8.0);
					}
					bool render = Renderer.Render<Polyline, IPolylineCollection>(request, _polylines, (int idx, IPolylineCollection lines) => lines[idx], elements, VectorSymbology);
					if (ShowStrokeVertices)
					{
						render |= PointRenderLayer.PaintUniformSymbology(AllPointsCollection, VertexSymbology, request);
					}
					return render;
				}
				finally
				{
					_polylines.Lock.ReleaseReaderLock();
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Possibly failed to acquire read lock.");
			return false;
		}
	}

	private void RenderFIDLabels(PlotRequest request, IEnumerable<FeatureSegmentSubset> candidates)
	{
		if (!LabelFIDs)
		{
			return;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (FeatureSegmentSubset candidate in candidates)
		{
			int fid = candidate.Fid;
			if (!intSet.Add(fid))
			{
				continue;
			}
			Point representativePoint = GetRepresentativePoint(fid);
			if (request.RasterQuery.Contains(representativePoint))
			{
				request.AddLabel(new LazyLabelDefinition<int>((int ordinal) => ordinal.ToString(), fid, representativePoint));
			}
		}
	}

	private void RenderZLabels(PlotRequest request, IEnumerable<FeatureSegmentSubset> candidates)
	{
		if (!ShowZValues)
		{
			return;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (FeatureSegmentSubset candidate in candidates)
		{
			int fid = candidate.Fid;
			if (!intSet.Add(fid))
			{
				continue;
			}
			Polyline polyline = _polylines[fid];
			if (polyline == null || !polyline.HasZ)
			{
				continue;
			}
			IList<double> z = polyline.Z;
			int count = Math.Min(polyline.PointCount, z.Count);
			for (int i = 0; i < count; i++)
			{
				double computed = z[i];
				if (!double.IsNaN(computed))
				{
					Point pt = polyline.Points[i];
					if (request.RasterQuery.Contains(pt))
					{
						request.AddLabel(new LazyLabelDefinition<double>(FormatVertexLabel, computed, pt));
					}
				}
			}
		}
	}

	public static string FormatVertexLabel(double doubleValue)
	{
		return doubleValue.ToString("0.##");
	}

	private void RenderLabels(PlotRequest request, IEnumerable<FeatureSegmentSubset> candidates)
	{
		if (!LabelFeatures)
		{
			return;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (FeatureSegmentSubset candidate in candidates)
		{
			int fid = candidate.Fid;
			if (!intSet.Contains(fid))
			{
				Point representativePoint = GetRepresentativePoint(fid);
				if (request.RasterQuery.Contains(representativePoint))
				{
					request.AddLabel(new LazyLabelDefinition<int>(_getLabelName, fid, representativePoint));
					intSet.Add(fid);
				}
			}
		}
	}

	public override bool Highlight(int idx, PlotRequest request)
	{
		return Paint(_polylines[idx], VectorSymbology.GetHighlightSymbology(), request);
	}

	public static bool Paint(Polyline pline, VectorSymbology symb, PlotRequest request)
	{
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(request.RasterQuery);
		double penWidthSymbTerm = symb.PenWidth * request.RasterQuery.CellSize / 2.0;
		RasterDefinition rasterQuery = request.RasterQuery;
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(rasterQuery.CellCount, out clrBuf))
		{
			bool anyPaint = false;
			int clr = symb.PenColor.ToArgb();
			for (int i = 0; i < pline.SegmentCount; i++)
			{
				Segment segment = pline.Segments[i];
				if (rasterQuery.Extent.Overlaps(segment.GetExtent()))
				{
					Segment segment2 = segment.Offset(penWidthSymbTerm);
					Segment segment3 = segment.Offset(0.0 - penWidthSymbTerm);
					Polygon polygon = new Polygon(new Point[4] { segment2.A, segment2.B, segment3.B, segment3.A });
					polygonRasterizer.Rasterize(polygon, (int px) =>
					{
						anyPaint = true;
						clrBuf.Span[px] = clr;
					});
				}
			}
			if (anyPaint)
			{
				request.Splat(clrBuf);
			}
			return anyPaint;
		}
	}

	public override bool HitTest(int candidateIdx, Point pt, double hitDistance)
	{
		return _polylines[candidateIdx].DistanceToPoint(pt) < hitDistance;
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		return HitTestExtent(_polylines, ext, candidateIdx);
	}

	public static bool HitTestExtent(IReadOnlyList<Polyline> lines, Extent ext, int candidateIdx)
	{
		return Polygon.FromExtent(ext).ContainsOrIntersects(lines[candidateIdx]);
	}

	public static PointCollection GetAllPoints(IReadOnlyList<Polyline> polylines)
	{
		int polylineCountSum = 0;
		foreach (Polyline polyline in polylines)
		{
			polylineCountSum += polyline.Count;
		}
		PointCollection pointCollection = new PointCollection(polylineCountSum);
		foreach (Polyline polyline2 in polylines)
		{
			pointCollection.AddRange(polyline2.Points);
		}
		return pointCollection;
	}

	private static Point[] ComputeMidpoints(IReadOnlyList<Polyline> polylines)
	{
		Point[] points = new Point[polylines.Count];
		for (int i = 0; i < polylines.Count; i++)
		{
			points[i] = polylines[i].MidPointOnPolyline().Point;
		}
		return points;
	}

	public override bool HitTest(int candidateIdx, Polyline ext)
	{
		return ext.Intersects(_polylines[candidateIdx]);
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		Polyline polyline = _polylines[candidateIdx];
		if (hitMode == MarqueeHitMode.Contained)
		{
			return lasso.Contains(polyline);
		}
		if (!lasso.ContainsAny(polyline.Points))
		{
			return lasso.Intersects(polyline);
		}
		return true;
	}

	public override double DistanceTo(int idx, Point pt)
	{
		return _polylines[idx].DistanceToPoint(pt);
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _polylines.VersionXY + VertexSymbology.VersionXY;
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return _polylines.GetSpatialIndex();
	}
}
