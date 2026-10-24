using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.ContextActions;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class MultiTinRenderLayer : RenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection, IRasterRenderLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("MultiTinRenderLayer");

	private readonly List<Tin> _tins;

	private SpatialIndex<Tuple<int, int>> _pointSpatialIndex;

	private SpatialIndex<Tuple<int, int>> _triSpatialIndex;

	private SpatialIndex<Tuple<int, int>> _segSpatialIndex;

	private Extent _validExt;

	private bool _renderEdges = true;

	private bool _renderPoints = true;

	private bool _renderDeletedTris = true;

	private bool _labelTris;

	private bool _labelEdges;

	private bool _labelPoints;

	private readonly List<Func<int, BarycentricWeights, IContextBase>> _triangleHitTestActions = new List<Func<int, BarycentricWeights, IContextBase>>();

	private readonly object _tsiLock = new object();

	private readonly object _psiLock = new object();

	private readonly object _ssiLock = new object();

	public override Extent ValidExtent => _validExt;

	public VectorSymbology VectorSymbology { get; set; } = VectorSymbology.DefaultSymbology();

	public RasterSymbology RasterSymbology { get; set; } = RasterSymbology.DefaultRasterSymbology();

	public TinSurfaceFillMode SurfaceFillMode { get; set; }

	[PlotOption("PaintZ", "Paint Z Surface")]
	public bool PaintZ
	{
		get
		{
			return SurfaceFillMode == TinSurfaceFillMode.RenderZ;
		}
		set
		{
			if (value)
			{
				SurfaceFillMode = TinSurfaceFillMode.RenderZ;
			}
			else
			{
				SurfaceFillMode = TinSurfaceFillMode.None;
			}
			IncrementRenderState();
		}
	}

	[PlotOption("PaintM", "Paint M Surface")]
	public bool PaintM
	{
		get
		{
			return SurfaceFillMode == TinSurfaceFillMode.RenderM;
		}
		set
		{
			if (value)
			{
				SurfaceFillMode = TinSurfaceFillMode.RenderM;
			}
			else
			{
				SurfaceFillMode = TinSurfaceFillMode.None;
			}
			IncrementRenderState();
		}
	}

	public Func<int, float, float, float> CustomTinRenderFunction { get; set; }

	[PlotOption("RenderEdges", "Render TIN Edges")]
	public bool RenderEdges
	{
		get
		{
			return _renderEdges;
		}
		set
		{
			if (_renderEdges != value)
			{
				_renderEdges = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("RenderPoints", "Render Points")]
	public bool RenderPoints
	{
		get
		{
			return _renderPoints;
		}
		set
		{
			if (_renderPoints != value)
			{
				_renderPoints = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("RenderDeletedTris", "Render Deleted Triangles")]
	public bool RenderDeletedTris
	{
		get
		{
			return _renderDeletedTris;
		}
		set
		{
			if (_renderDeletedTris != value)
			{
				_renderDeletedTris = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("LabelTriangles", "Label Triangles")]
	public bool LabelTris
	{
		get
		{
			return _labelTris;
		}
		set
		{
			if (_labelTris != value)
			{
				_labelTris = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("LabelEdges", "Label Edges")]
	public bool LabelEdges
	{
		get
		{
			return _labelEdges;
		}
		set
		{
			if (_labelEdges != value)
			{
				_labelEdges = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("LabelPoints", "Label Points")]
	public bool LabelPoints
	{
		get
		{
			return _labelPoints;
		}
		set
		{
			if (_labelPoints != value)
			{
				_labelPoints = value;
				IncrementRenderState();
			}
		}
	}

	public int Count => _tins.Count();

	public SelectedSet SelectedSet { get; } = new SelectedSet();

	public static RasterSymbology InterpolationSurfaceZSymbology()
	{
		Color[] colors = new Color[2]
		{
			Color.White,
			Color.Black
		};
		double[] values = new double[2] { 0.0, 1.0 };
		return new RasterSymbology(new InterpolatedSurfaceFill(colors, values));
	}

	public static RasterSymbology InterpolationSurfaceMSymbology()
	{
		Color[] colors = new Color[3]
		{
			Color.Blue,
			Color.White,
			Color.Red
		};
		double[] values = new double[3] { -2.0, 0.0, 2.0 };
		return new RasterSymbology(new InterpolatedSurfaceFill(colors, values));
	}

	public MultiTinRenderLayer(IList<Tin> tins)
	{
		_tins = tins.ToList();
		_validExt = Extent.Union(tins.Select((Tin t) => t.Extent));
		VectorSymbology = VectorSymbology.DefaultSymbology();
	}

	public void AddTin(Tin tin)
	{
		_tins.Add(tin);
		_validExt = Extent.Union(_tins.Select((Tin t) => t.Extent));
		IncrementRenderState();
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return null;
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		bool condition = false;
		if (SurfaceFillMode != TinSurfaceFillMode.None)
		{
			condition |= RenderSurface(plot);
		}
		if (RenderEdges)
		{
			condition |= RenderSegments(plot);
		}
		if (RenderPoints)
		{
			condition |= RenderPointSymbols(plot);
		}
		ProcessLabels(plot);
		return condition;
	}

	private bool RenderSegments(PlotRequest plot)
	{
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		List<Tuple<int, int>> elements = EnsureGetSegmentSpatialIndex().GetElements(plot.RasterQuery);
		if (elements.Count == 0)
		{
			return false;
		}
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out clrBuf))
		{
			int clr = VectorSymbology.PenColor.ToArgb();
			bool anyPaint = false;
			if (VectorSymbology.PenWidth <= 1.0)
			{
				foreach (Tuple<int, int> element in elements)
				{
					int elementTerm = element.Item1;
					int elementTerm2 = element.Item2;
					Tin tin = _tins[elementTerm];
					Segment segment = GetSegment(tin, elementTerm2);
					if (!RenderDeletedTris)
					{
						var (localValue, localValue2) = GetSegmentTriangles(tin, elementTerm2);
						if ((localValue == -1 || tin.TriangleDefinition[localValue].IsDeleted) && (localValue2 == -1 || tin.TriangleDefinition[localValue2].IsDeleted))
						{
							continue;
						}
					}
					segment.Rasterize(plot.RasterQuery, (int idx) =>
					{
						anyPaint = true;
						clrBuf.Span[idx] = clr;
					});
				}
			}
			else
			{
				PolygonRasterizer polygonRasterizer = new PolygonRasterizer(plot.RasterQuery);
				double penWidthVectorSymbologyTerm = VectorSymbology.PenWidth * plot.RasterQuery.CellSize / 2.0;
				foreach (Tuple<int, int> element2 in elements)
				{
					int element2Term = element2.Item1;
					int element2Term2 = element2.Item2;
					Segment segment2 = GetSegment(_tins[element2Term], element2Term2);
					Segment segment3 = segment2.Offset(penWidthVectorSymbologyTerm);
					Segment segment4 = segment2.Offset(0.0 - penWidthVectorSymbologyTerm);
					Polygon polygon = new Polygon(new Geospatial.Vectors.Point[4] { segment3.A, segment3.B, segment4.B, segment4.A });
					polygonRasterizer.Rasterize(polygon, (int idx) =>
					{
						anyPaint = true;
						clrBuf.Span[idx] = clr;
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

	private bool RenderPointSymbols(PlotRequest plot)
	{
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		int pointSizeVectorSymbologyTerm = (int)Math.Round((float)VectorSymbology.PointSize / 2f, MidpointRounding.AwayFromZero);
		double delta = (double)pointSizeVectorSymbologyTerm * plot.RasterQuery.CellSize;
		Extent extent = plot.RasterQuery.Extent.Extend(delta);
		List<Tuple<int, int>> elements = EnsureGetPointSpatialIndex().GetElements(extent, dedupe: false);
		if (elements.Count == 0)
		{
			return false;
		}
		int cellCount = plot.RasterQuery.CellCount;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(cellCount);
		Memory<int> bufARGB = memoryOwner.Memory.Slice(0, cellCount);
		Span<int> bufARGBSpan = bufARGB.Span;
		int toArgb = VectorSymbology.FillColor.ToArgb();
		bool condition = false;
		int rows = plot.RasterQuery.Rows;
		int cols = plot.RasterQuery.Cols;
		for (int i = 0; i < elements.Count; i++)
		{
			Tuple<int, int> tuple = elements[i];
			int tupleTerm = tuple.Item1;
			int tupleTerm2 = tuple.Item2;
			Geospatial.Vectors.Point pt = _tins[tupleTerm].Points[tupleTerm2];
			if (!extent.Contains(pt))
			{
				continue;
			}
			Pixel pixel = plot.RasterQuery.CellIndex2D(pt);
			for (int j = pixel.Y - pointSizeVectorSymbologyTerm; j <= pixel.Y + pointSizeVectorSymbologyTerm; j++)
			{
				if (j < 0)
				{
					continue;
				}
				if (j >= rows)
				{
					break;
				}
				for (int k = pixel.X - pointSizeVectorSymbologyTerm; k <= pixel.X + pointSizeVectorSymbologyTerm; k++)
				{
					if (k >= 0)
					{
						if (k >= cols)
						{
							break;
						}
						bufARGBSpan[plot.RasterQuery.CellIndex(j, k)] = toArgb;
						condition = true;
					}
				}
			}
		}
		if (condition)
		{
			plot.Splat(bufARGB);
		}
		return condition;
	}

	private bool RenderSurface(PlotRequest plot)
	{
		if (SurfaceFillMode == TinSurfaceFillMode.None)
		{
			return false;
		}
		if (SurfaceFillMode == TinSurfaceFillMode.Constant)
		{
			return RenderSurface_Constant(plot);
		}
		IMemoryOwner<float> data = SharedMemoryPool<float>.Rent(plot.RasterQuery.CellCount, -9999f, out var rasterData);
		bool anyPaint = false;
		for (int tinIdx = 0; tinIdx < _tins.Count; tinIdx++)
		{
			Tin tin = _tins[tinIdx];
			if (!tin.Extent.Overlaps(plot.RasterQuery.Extent))
			{
				continue;
			}
			tin.Rasterize(plot.RasterQuery, (int px, int tIdx, BarycentricWeights bw) =>
			{
				if (RenderDeletedTris || !tin.TriangleDefinition[tIdx].IsDeleted)
				{
					Span<float> rasterDataSpan = rasterData.Span;
					TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
					float rasterDataSpan2;
					if (SurfaceFillMode == TinSurfaceFillMode.RenderM)
					{
						rasterDataSpan2 = (float)bw.Interpolate(tin.M[triangleDefinition.p0], tin.M[triangleDefinition.p1], tin.M[triangleDefinition.p2]);
					}
					else if (SurfaceFillMode == TinSurfaceFillMode.RenderZ)
					{
						rasterDataSpan2 = (float)bw.Interpolate(tin.Z[triangleDefinition.p0], tin.Z[triangleDefinition.p1], tin.Z[triangleDefinition.p2]);
					}
					else
					{
						float interpolate = (float)bw.Interpolate(tin.Z[triangleDefinition.p0], tin.Z[triangleDefinition.p1], tin.Z[triangleDefinition.p2]);
						float interpolate2 = (float)bw.Interpolate(tin.M[triangleDefinition.p0], tin.M[triangleDefinition.p1], tin.M[triangleDefinition.p2]);
						rasterDataSpan2 = CustomTinRenderFunction(tinIdx, interpolate, interpolate2);
					}
					rasterDataSpan[px] = rasterDataSpan2;
					anyPaint = true;
				}
			});
		}
		plot.SetRasterData_TransferOwnership(this, data);
		if (!anyPaint)
		{
			return false;
		}
		RasterRenderHelper.Render(rasterData, plot, RasterSymbology);
		return true;
	}

	private bool RenderSurface_Constant(PlotRequest plot)
	{
		throw new NotImplementedException();
	}

	private void ProcessLabels(PlotRequest plot)
	{
		if (LabelTris)
		{
			for (int i = 0; i < _tins.Count; i++)
			{
				TinRenderLayer.LabelTinTriangles(_tins[i], plot);
			}
		}
		if (LabelPoints)
		{
			_logger.LogOnce(LogLevel.Warning, "LabelPoints not implemented in MultiTinRenderLayer.");
		}
		if (LabelEdges)
		{
			_logger.LogOnce(LogLevel.Warning, "LabelEdges not implemented in MultiTinRenderLayer.");
		}
	}

	public void AddTriangleHitTestAction(Func<int, BarycentricWeights, IContextBase> onHit)
	{
		_triangleHitTestActions.Add(onHit);
		if (_triangleHitTestActions.Count == 1)
		{
			AddMapHitTest((HitTestArgs args) =>
			{
				EnsureGetTriangleSpatialIndex().GetElements(args.QueryPoint);
				return (IContextBase)null;
			});
		}
	}

	public void AddPointHitTestAction(Func<int, IContextBase> onHit)
	{
		AddMapHitTest((HitTestArgs args) =>
		{
			throw new NotImplementedException();
		});
	}

	public SpatialIndex<Tuple<int, int>> EnsureGetTriangleSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<Tuple<int, int>>>.AllocateOnce(ref _triSpatialIndex, _tsiLock, this, (MultiTinRenderLayer t) => t.ComputeTriangleSpatialIndex());
	}

	private SpatialIndex<Tuple<int, int>> ComputeTriangleSpatialIndex()
	{
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(ValidExtent);
		for (int i = 0; i < _tins.Count; i++)
		{
			Tin tin = _tins[i];
			int triangleCount = tin.TriangleCount;
			for (int j = 0; j < triangleCount; j++)
			{
				spatialIndex.Add(tin.TriangleExtent(j), new Tuple<int, int>(i, j));
			}
		}
		return spatialIndex;
	}

	public SpatialIndex<Tuple<int, int>> EnsureGetPointSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<Tuple<int, int>>>.AllocateOnce(ref _pointSpatialIndex, _psiLock, this, (MultiTinRenderLayer t) => t.ComputePointSpatialIndex());
	}

	private SpatialIndex<Tuple<int, int>> ComputePointSpatialIndex()
	{
		Extent validExtent = ValidExtent;
		if (!validExtent.IsFiniteAndValid())
		{
			return SpatialIndex<Tuple<int, int>>.DefaultSpatialIndex();
		}
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(validExtent);
		for (int i = 0; i < _tins.Count; i++)
		{
			Tin tin = _tins[i];
			int pointCount = tin.PointCount;
			for (int j = 0; j < pointCount; j++)
			{
				spatialIndex.Add(tin.Points[j], new Tuple<int, int>(i, j));
			}
		}
		return spatialIndex;
	}

	public SpatialIndex<Tuple<int, int>> EnsureGetSegmentSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<Tuple<int, int>>>.AllocateOnce(ref _segSpatialIndex, _ssiLock, this, (MultiTinRenderLayer t) => t.ComputeSegmentSpatialIndex());
	}

	private SpatialIndex<Tuple<int, int>> ComputeSegmentSpatialIndex()
	{
		Extent validExtent = ValidExtent;
		if (!validExtent.IsFiniteAndValid())
		{
			return SpatialIndex<Tuple<int, int>>.DefaultSpatialIndex();
		}
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(validExtent);
		for (int i = 0; i < _tins.Count; i++)
		{
			Tin tin = _tins[i];
			int triangleCount = tin.TriangleCount;
			for (int j = 0; j < triangleCount; j++)
			{
				TriangleDefinition triangleDefinition = tin.TriangleDefinition[j];
				for (int k = 0; k < 3; k++)
				{
					int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(k);
					if (j < adjacentTriangleIdx || adjacentTriangleIdx == -1)
					{
						int convertToSegIndex = ConvertToSegIndex(j, k);
						Segment seg = tin.Edge(j, k);
						spatialIndex.Add(seg, new Tuple<int, int>(i, convertToSegIndex));
					}
				}
			}
		}
		return spatialIndex;
	}

	private static Segment GetSegment(Tin t, int segIdx)
	{
		ConvertFromSegIndex(segIdx, out var triIdx, out var edgeIdx);
		return t.Edge(triIdx, edgeIdx);
	}

	private static (int tA, int tB) GetSegmentTriangles(Tin t, int segIdx)
	{
		ConvertFromSegIndex(segIdx, out var triIdx, out var edgeIdx);
		int adjacentTriangleIdx = t.TriangleDefinition[triIdx].AdjacentTriangleIdx(edgeIdx);
		return (tA: triIdx, tB: adjacentTriangleIdx);
	}

	public static void ConvertFromSegIndex(int segIdx, out int triIdx, out int edgeIdx)
	{
		triIdx = segIdx / 3;
		edgeIdx = segIdx % 3;
	}

	private static int ConvertToSegIndex(int triIdx, int edgeIdx)
	{
		return triIdx * 3 + edgeIdx;
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(Polyline intersectionLine, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool RenderSelected(PlotRequest plot)
	{
		ThrowHitTestErr();
		return false;
	}

	private static void ThrowHitTestErr()
	{
		_logger.LogOnce(LogLevel.Error, "HitTest not implemented on MultiTinRenderLayer");
	}

	public IList<object> GetSelectedVectors()
	{
		return null;
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx)
	{
		_hitTestedIdx = -1;
		return null;
	}
}
