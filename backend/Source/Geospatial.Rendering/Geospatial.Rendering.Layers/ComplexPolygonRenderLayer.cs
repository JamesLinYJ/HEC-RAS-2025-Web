using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class ComplexPolygonRenderLayer : VectorRenderLayer, ISelection, IBasicSelection
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ComplexPolygonRenderLayer");

	private readonly IReadOnlyComplexPolygonCollection _polygons;

	private bool _renderFill = true;

	private bool _renderPerimeter = true;

	private bool _useExperimentalTinRenderer = true;

	private readonly object _spIdxLock = new object();

	public IReadOnlyComplexPolygonCollection Polygons => _polygons;

	public override Extent ValidExtent => _polygons.Extent;

	public override int Count => _polygons.Count;

	[PlotOption("RenderFill", "Fill Complex Polygon")]
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

	[PlotOption("UseTinRenderer", "Use Experimental TIN Renderer")]
	public bool UseExperimentalTinRenderer
	{
		get
		{
			return _useExperimentalTinRenderer;
		}
		set
		{
			if (_useExperimentalTinRenderer != value)
			{
				_useExperimentalTinRenderer = value;
				IncrementRenderState();
			}
		}
	}

	public ComplexPolygonRenderLayer(IReadOnlyComplexPolygonCollection polygons)
	{
		_polygons = polygons;
	}

	public ComplexPolygonRenderLayer(IEnumerable<ComplexPolygon> polygons)
		: this(new ComplexPolygonCollection(polygons))
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
		return null;
	}

	public IReadOnlyList<ComplexPolygon> GetSelectedComplexPolygons()
	{
		if (SelectedSet.Count == 0)
		{
			return null;
		}
		List<ComplexPolygon> complexPolygons = new List<ComplexPolygon>();
		complexPolygons.AddRange(SelectedSet.Elements.Select((int idx) => _polygons[idx]));
		return new _003C_003Ez__ReadOnlyList<ComplexPolygon>(complexPolygons);
	}

	public override IList<object> GetSelectedVectors()
	{
		return GetSelectedVectors(SelectedSet, Polygons);
	}

	public static List<object> GetSelectedVectors(SelectedSet selectedSet, IReadOnlyComplexPolygonCollection polygons)
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

	public static IList<object> GetHitTestVectors(HitTestArgs args, IReadOnlyComplexPolygonCollection pgons, SpatialIndex<int> spIdx, out int hitTestedIdx)
	{
		hitTestedIdx = -1;
		List<object> hitTestVectors = new List<object>();
		List<int> elements = spIdx.GetElements(args.QueryPoint);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			ComplexPolygon complexPolygon = pgons[element];
			if (complexPolygon.ContainsPoint_Slow(args.QueryPoint))
			{
				hitTestedIdx = element;
				hitTestVectors.Add(complexPolygon);
			}
		}
		return hitTestVectors;
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

	private bool RenderInternalFill(PlotRequest plot)
	{
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out clrBuf))
		{
			int toArgb = VectorSymbology.FillColor.ToArgb();
			PolygonRasterizer polygonRasterizer = new PolygonRasterizer(plot.RasterQuery);
			Memory<int> scanlineColor;
			using (SharedMemoryPool<int>.RentNoFill(plot.RasterQuery.Cols, out scanlineColor))
			{
				scanlineColor.Span.Fill(toArgb);
				bool anyPaint = false;
				List<int> elements = EnsureGetSpatialIndex().GetElements(plot.RasterQuery.Extent);
				for (int countOffset = elements.Count - 1; countOffset >= 0; countOffset--)
				{
					int index = elements[countOffset];
					ComplexPolygon complexPolygon = _polygons[index];
					if (complexPolygon != null && plot.RasterQuery.Extent.Overlaps(complexPolygon.Extent))
					{
						polygonRasterizer.Rasterize(complexPolygon, (int row, int startC, int stopC) =>
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
				return anyPaint;
			}
		}
	}

	public override bool Highlight(int idx, PlotRequest request)
	{
		return Highlight(_polygons[idx], VectorSymbology.GetHighlightSymbology(), request);
	}

	public static bool Highlight(ComplexPolygon pgon, VectorSymbology highlightSymb, PlotRequest request)
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

	private bool RenderInternalPerimeter(PlotRequest request)
	{
		SpatialIndex<FeatureSegmentSubset> segmentSpatialIndex = _polygons.GetSegmentSpatialIndex();
		if (segmentSpatialIndex == null)
		{
			return false;
		}
		List<FeatureSegmentSubset> elements = segmentSpatialIndex.GetElements(request.RasterQuery);
		elements.Dedupe();
		if (elements.Count == 0)
		{
			return false;
		}
		return PolylineRenderLayer.Renderer.Render<Polygon, List<Polygon>>(request, _polygons.FlattenedRingsList, (int idx, List<Polygon> polygons) => polygons[idx], elements, VectorSymbology);
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return _polygons.GetSpatialIndex();
	}

	public override bool HitTest(int candidateIdx, Point pt, double hitDistance)
	{
		return false;
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		return false;
	}

	public override bool HitTest(int candidateIdx, Polyline line)
	{
		return false;
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		return false;
	}

	public override bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		CompactList<int> compactList = default;
		List<int> elements = EnsureGetSpatialIndex().GetElements(selectArgs.QueryPoint);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (_polygons[element].ContainsPoint_Slow(selectArgs.QueryPoint))
			{
				compactList.Add(element);
			}
		}
		return VectorRenderLayer.SelectHelperPoint(compactList, selectArgs, mode, SelectedSet);
	}

	public override bool Select(Extent selectExt, SelectMode mode)
	{
		return VectorRenderLayer.SelectHelper(selectExt, SelectedSet, Polygons.Count, mode, this, selectExt, (int fid, ComplexPolygonRenderLayer model, Extent ext) => model.Polygons[fid].Extent.Overlaps(in ext), (ComplexPolygonRenderLayer k) => k.EnsureGetSpatialIndex());
	}

	public override double DistanceTo(int idx, Point pt)
	{
		return -1.0;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _polygons.VersionXY;
	}
}
