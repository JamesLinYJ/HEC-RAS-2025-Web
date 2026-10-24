using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class PolygonRenderLayer : VectorRenderLayer, ISelection, IBasicSelection, ILineSourceLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("PolygonRenderLayer");

	private static readonly Pool<PolygonRasterizer> _rasterizerPool = new Pool<PolygonRasterizer>(() => PolygonRasterizer.MakeUninitializedPolygonRasterizer());

	private readonly IReadOnlyPolygonCollection _polygons;

	private Versioned<PointCollection> _allPointsCollection = new Versioned<PointCollection>();

	private Versioned<Geospatial.Vectors.Point[]> _labelPointsCache = new Versioned<Geospatial.Vectors.Point[]>();

	private bool _renderFill = true;

	private bool _renderPerimeter = true;

	private bool _showStrokeVertices;

	private readonly object _spIdxLock = new object();

	private PointCollection AllPointsCollection => _allPointsCollection.Get(_polygons.VersionXY, () => PolylineRenderLayer.GetAllPoints(_polygons));

	private Geospatial.Vectors.Point[] LabelPoints => _labelPointsCache.Get(_polygons.VersionXY, _polygons, (IReadOnlyPolygonCollection pgons) => ComputeLabelPoints(pgons));

	public VectorSymbology VertexSymbology { get; set; } = VectorSymbology.DefaultSymbology();

	public IReadOnlyPolygonCollection Polygons => _polygons;

	public override Extent ValidExtent => _polygons.Extent;

	public override int Count => _polygons.Count;

	[PlotOption("RenderFill", "Fill Polygon")]
	public bool RenderFill
	{
		get
		{
			return _renderFill;
		}
		set
		{
			if (_renderFill != value)
			{
				_renderFill = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("RenderPerimeter", "Render Perimeter")]
	public bool RenderPerimeter
	{
		get
		{
			return _renderPerimeter;
		}
		set
		{
			if (_renderPerimeter != value)
			{
				_renderPerimeter = value;
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

	public bool TryGetLineSource(out ILineSegmentSource source)
	{
		source = _polygons;
		return true;
	}

	public PolygonRenderLayer(IReadOnlyPolygonCollection polygons)
	{
		_polygons = polygons;
	}

	public PolygonRenderLayer(IEnumerable<Polygon> polygons)
		: this(new PolygonCollection(polygons))
	{
	}

	public override ISpatialVersion GetSpatialVersionContext()
	{
		return _polygons;
	}

	public IReadOnlyList<Polyline> GetSelectedPolylines()
	{
		return null;
	}

	public IReadOnlyList<Polygon> GetSelectedPolygons()
	{
		if (Polygons.Count == 0)
		{
			return null;
		}
		if (SelectedSet.Count == 0)
		{
			return null;
		}
		if (SelectedSet.Count == 1)
		{
			return new CompactList<Polygon>(Polygons[SelectedSet.Elements.First()]);
		}
		List<Polygon> polygons = new List<Polygon>(SelectedSet.Count);
		foreach (int selectedSet in SelectedSet)
		{
			polygons.Add(Polygons[selectedSet]);
		}
		return polygons;
	}

	public override IList<object> GetSelectedVectors()
	{
		return GetSelectedVectors(SelectedSet, Polygons);
	}

	public static List<object> GetSelectedVectors(SelectedSet selectedSet, IReadOnlyPolygonCollection polygons)
	{
		if (selectedSet.Count == 0)
		{
			return null;
		}
		return new List<object>(selectedSet.Elements.Select((int idx) => polygons[idx]));
	}

	public override IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		return GetHitTestVectors(args, Polygons, EnsureGetSpatialIndex(), out hitTestedIdx);
	}

	public static IList<object> GetHitTestVectors(HitTestArgs args, IReadOnlyPolygonCollection pgons, SpatialIndex<int> spIdx, out int hitTestedIdx)
	{
		if (pgons.TryHitTestByBestArea(args.QueryPoint, spIdx, 8.0 * args.Window.CellSize, out var result))
		{
			(hitTestedIdx, _) = result;
			return new CompactList<object>(pgons[result.Item1]);
		}
		hitTestedIdx = -1;
		return default(CompactList<object>);
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		try
		{
			_polygons.Lock.AcquireReaderLock(1);
			try
			{
				if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
				{
					return false;
				}
				bool condition = false;
				if (RenderFill)
				{
					condition |= RenderInternalFill(plot);
				}
				if (RenderPerimeter)
				{
					condition |= RenderInternalPerimeter(plot);
				}
				if (ShowStrokeVertices)
				{
					RasterDefinition maximalPointRenderExtent = PointRenderLayer.GetMaximalPointRenderExtent(plot.RasterQuery, VertexSymbology);
					List<int> elements = AllPointsCollection.PointSpatialIndex.GetElements(maximalPointRenderExtent);
					condition |= PointRenderLayer.PaintPoints(elements, (int idx) => AllPointsCollection[idx], VertexSymbology, plot);
				}
				if (LabelFIDs)
				{
					RenderFIDLabels(plot);
				}
				return condition;
			}
			finally
			{
				_polygons.Lock.ReleaseReaderLock();
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Possibly failed to acquire read lock.");
			return false;
		}
	}

	private void RenderFIDLabels(PlotRequest plot)
	{
		Geospatial.Vectors.Point[] labelPoints = LabelPoints;
		for (int i = 0; i < labelPoints.Length; i++)
		{
			Geospatial.Vectors.Point pt = labelPoints[i];
			if (plot.RasterQuery.Contains(pt))
			{
				plot.AddLabel(new LazyLabelDefinition<int>((int fid) => fid.ToString(), i, pt));
			}
		}
	}

	private static Geospatial.Vectors.Point[] ComputeLabelPoints(IReadOnlyPolygonCollection pgons)
	{
		Geospatial.Vectors.Point[] points = new Geospatial.Vectors.Point[pgons.Count];
		for (int i = 0; i < pgons.Count; i++)
		{
			points[i] = pgons[i].BestLabelPoint();
		}
		return points;
	}

	private bool RenderInternalFill(PlotRequest plot)
	{
		if (VectorSymbology is IConditionalVectorSymbology { HasAnyOverrides: not false })
		{
			if (VectorSymbology is ConditionalVectorSymbology<ConditionalVectorSymbology.Unit> symbology)
			{
				return RenderConditionalFill_Internal(plot, symbology, ConditionalVectorSymbology.Unit.Value);
			}
			throw new InvalidOperationException("Conditional symbology model type mismatch (expected ConditionalVectorSymbology.Unit)");
		}
		return RenderUniformFill_Internal(plot);
	}

	private bool RenderUniformFill_Internal(PlotRequest plot)
	{
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out clrBuf))
		{
			int toArgb = VectorSymbology.FillColor.ToArgb();
			PoolHandle<PolygonRasterizer> poolHandle = _rasterizerPool.Acquire();
			PolygonRasterizer data = poolHandle.Data;
			data.Reset(plot.RasterQuery);
			Memory<int> scanlineColor;
			using (SharedMemoryPool<int>.RentNoFill(plot.RasterQuery.Cols, out scanlineColor))
			{
				scanlineColor.Span.Fill(toArgb);
				bool anyPaint = false;
				for (int countOffset = _polygons.Count - 1; countOffset >= 0; countOffset--)
				{
					Polygon polygon = _polygons[countOffset];
					if (plot.RasterQuery.Extent.Overlaps(polygon.Extent))
					{
						data.Rasterize(polygon, (int row, int startC, int stopC) =>
						{
							anyPaint = true;
							int start = row * plot.RasterQuery.Cols + startC;
							int length = stopC - startC + 1;
							Memory<int> slice = scanlineColor.Slice(0, length);
							Memory<int> destination = clrBuf.Slice(start, length);
							slice.CopyTo(destination);
						});
					}
				}
				if (anyPaint)
				{
					plot.Splat(clrBuf);
				}
				_rasterizerPool.Return(poolHandle);
				return anyPaint;
			}
		}
	}

	private bool RenderConditionalFill_Internal<T>(PlotRequest plot, ConditionalVectorSymbology<T> symbology, T model)
	{
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out clrBuf))
		{
			PoolHandle<PolygonRasterizer> poolHandle = _rasterizerPool.Acquire();
			PolygonRasterizer data = poolHandle.Data;
			data.Reset(plot.RasterQuery);
			Memory<int> scanlineColor;
			using (SharedMemoryPool<int>.RentNoFill(plot.RasterQuery.Cols, out scanlineColor))
			{
				bool anyPaint = false;
				Color? color = null;
				for (int countOffset = _polygons.Count - 1; countOffset >= 0; countOffset--)
				{
					Polygon polygon = _polygons[countOffset];
					if (plot.RasterQuery.Extent.Overlaps(polygon.Extent))
					{
						Color fillColorDynamic = symbology.GetFillColorDynamic(model, countOffset);
						if (fillColorDynamic.A != 0)
						{
							if (color?.ToARGB() != fillColorDynamic.ToARGB())
							{
								scanlineColor.Span.Fill(fillColorDynamic.ToArgb());
								color = fillColorDynamic;
							}
							data.Rasterize(polygon, (int row, int startC, int stopC) =>
							{
								anyPaint = true;
								int start = row * plot.RasterQuery.Cols + startC;
								int length = stopC - startC + 1;
								Memory<int> slice = scanlineColor.Slice(0, length);
								Memory<int> destination = clrBuf.Slice(start, length);
								slice.CopyTo(destination);
							});
						}
					}
				}
				if (anyPaint)
				{
					plot.Splat(clrBuf);
				}
				_rasterizerPool.Return(poolHandle);
				return anyPaint;
			}
		}
	}

	public override bool Highlight(int idx, PlotRequest request)
	{
		return Highlight(_polygons[idx], VectorSymbology.GetHighlightSymbology(), request);
	}

	public static bool Highlight(Polygon pgon, VectorSymbology highlightSymb, PlotRequest request)
	{
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(request.RasterQuery);
		double penWidthHighlightSymbTerm = highlightSymb.PenWidth * request.RasterQuery.CellSize / 2.0;
		RasterDefinition rasterQuery = request.RasterQuery;
		bool anyPaint = false;
		int cellCount = request.RasterQuery.CellCount;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(cellCount);
		Memory<int> clrBuf = memoryOwner.Memory.Slice(0, cellCount);
		int clr = highlightSymb.PenColor.ToArgb();
		int fillClr = highlightSymb.FillColor.ToArgb();
		polygonRasterizer.Rasterize(pgon, (int px) =>
		{
			anyPaint = true;
			clrBuf.Span[px] = fillClr;
		});
		for (int intervalIndex = 0; intervalIndex < pgon.SegmentCount; intervalIndex++)
		{
			Segment segment = pgon.Segments[intervalIndex];
			if (rasterQuery.Extent.Overlaps(segment.GetExtent()))
			{
				Segment segment2 = segment.Offset(penWidthHighlightSymbTerm);
				Segment segment3 = segment.Offset(0.0 - penWidthHighlightSymbTerm);
				Polygon polygon = new Polygon(new Geospatial.Vectors.Point[4] { segment2.A, segment2.B, segment3.B, segment3.A });
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

	private bool RenderInternalPerimeter(PlotRequest request)
	{
		SpatialIndex<FeatureSegmentSubset> segmentSpatialIndex = ((ISpatiallyIndexedSegmentCollection)_polygons).GetSegmentSpatialIndex();
		if (segmentSpatialIndex == null)
		{
			return false;
		}
		RasterDefinition raster = request.RasterQuery.ExtendRaster((int)Math.Ceiling(VectorSymbology.PenWidth));
		List<FeatureSegmentSubset> elements = segmentSpatialIndex.GetElements(raster);
		elements.Dedupe();
		if (elements.Count == 0)
		{
			return false;
		}
		return PolylineRenderLayer.Renderer.Render<Polygon, IReadOnlyPolygonCollection>(request, _polygons, (int idx, IReadOnlyPolygonCollection polygons) => polygons[idx], elements, VectorSymbology);
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return _polygons.GetSpatialIndex();
	}

	public override bool HitTest(int candidateIdx, Geospatial.Vectors.Point pt, double hitDistance)
	{
		return _polygons.HitTest(candidateIdx, pt, hitDistance);
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		return _polygons.HitTest(candidateIdx, ext, 0.0);
	}

	public override bool HitTest(int candidateIdx, Polyline line)
	{
		return _polygons.HitTest(candidateIdx, line, 0.0);
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		Polygon pgon = _polygons[candidateIdx];
		if (hitMode == MarqueeHitMode.Contained)
		{
			return lasso.Contains(pgon);
		}
		return lasso.AreaIntersects(pgon);
	}

	public override bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		Polygons.TryHitTestByBestArea(selectArgs.QueryPoint, Polygons.GetSpatialIndex(), 0.0, out var result);
		return VectorRenderLayer.SelectHelperPoint(result.Item1.WrapInCompactList(), selectArgs, mode, SelectedSet);
	}

	public override double DistanceTo(int idx, Geospatial.Vectors.Point pt)
	{
		if (!_polygons[idx].Contains(pt))
		{
			return _polygons[idx].DistanceToPoint(pt);
		}
		return 0.0;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _polygons.VersionXY + VertexSymbology.VersionXY;
	}
}
