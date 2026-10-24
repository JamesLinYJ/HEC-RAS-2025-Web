using System;
using System.Collections.Generic;
using Geospatial.DataStructures;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.ContextActions;
using Utility.Interfaces;
using Utility.Logging;

namespace Geospatial.Rendering.Layers;

public class TinRenderLayer : RenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection
{
	public enum TinElementType
	{
		Point,
		Triangle,
		Edge
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("TinRenderLayer");

	private readonly Tin _tin;

	private PolylineRenderLayer _segmentRenderer;

	private PointRenderLayer _pointRenderer;

	public static int HitTestPixelCount = MeshRenderLayer.HitTestPixelCount;

	private VectorSymbology _vectorSymbology;

	private bool _renderEdges = true;

	private bool _renderPoints = true;

	private bool _labelTris;

	private bool _labelEdges;

	private bool _labelPoints;

	private readonly SelectedSet _selectedSet = new SelectedSet();

	private List<Func<int, BarycentricWeights, IContextBase>> _triangleHitTestActions = new List<Func<int, BarycentricWeights, IContextBase>>();

	public Tin Tin => _tin;

	public override Extent ValidExtent => _tin.Extent;

	public int Count => 1;

	public VectorSymbology VectorSymbology
	{
		get
		{
			return _vectorSymbology;
		}
		set
		{
			_vectorSymbology = value;
			_segmentRenderer.VectorSymbology = value;
			_pointRenderer.VectorSymbology = value;
		}
	}

	public bool RenderFill { get; set; }

	[PlotOption("RenderEdges", "Render Edges ??")]
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

	[PlotOption("RenderPoints", "Render Points ??")]
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
				_segmentRenderer.LabelFeatures = value;
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

	public SelectedSet SelectedSet => _selectedSet;

	public TinRenderLayer(Tin tin)
	{
		_tin = tin;
		_segmentRenderer = new PolylineRenderLayer(_tin.GetAllSegments());
		_pointRenderer = new VirtualPointRenderLayer(_tin);
		VectorSymbology = VectorSymbology.DefaultSymbology();
		_segmentRenderer.SetLabels((int e) =>
		{
			int ordinal = e / 3;
			int at = e % 3;
			int adjacentTriangleIdx = _tin.TriangleDefinition[ordinal].AdjacentTriangleIdx(at);
			if (adjacentTriangleIdx == -1)
			{
				return "T[" + ordinal + "]-E[" + at + "]";
			}
			if (ordinal < adjacentTriangleIdx)
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[adjacentTriangleIdx];
				return "T[" + ordinal + "]-E[" + at + "], T[" + adjacentTriangleIdx + "]-E[" + triangleDefinition.AdjacentTriangleByGlobalIdx(ordinal) + "]";
			}
			return "";
		}, "");
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return null;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (!ValidExtent.Overlaps(request.RasterQuery.Extent))
		{
			return false;
		}
		bool condition = false;
		_ = RenderFill;
		if (RenderEdges)
		{
			condition |= _segmentRenderer.Render(request);
			if (condition)
			{
				_ = LabelEdges;
			}
		}
		if (RenderPoints)
		{
			condition |= _pointRenderer.Render(request);
		}
		ProcessLabels(request);
		return condition;
	}

	private void ProcessLabels(PlotRequest plot)
	{
		if (LabelTris)
		{
			LabelTinTriangles(_tin, plot);
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

	public static void LabelTinTriangles(Tin tin, PlotRequest plot)
	{
		if (!tin.Extent.Overlaps(plot.RasterQuery.Extent))
		{
			return;
		}
		List<int> elements = tin.TriangleSpatialIndex.GetElements(plot.RasterQuery);
		if (elements == null || elements.Count <= 0)
		{
			return;
		}
		foreach (int element in elements)
		{
			Point pt = tin.Triangles[element].Centroid();
			if (plot.RasterQuery.Contains(pt))
			{
				plot.AddLabel(new LazyLabelDefinition<int>((int idx) => "T[" + idx + "]", element, pt));
			}
		}
	}

	public void AddTriangleHitTestAction(Func<int, BarycentricWeights, IContextBase> onHit)
	{
		_triangleHitTestActions.Add(onHit);
		if (_triangleHitTestActions.Count != 1)
		{
			return;
		}
		AddMapHitTest((HitTestArgs args) =>
		{
			foreach (int element in _tin.TriangleSpatialIndex.GetElements(args.QueryPoint))
			{
				if (_tin.Triangles[element].Contains(args.QueryPoint, out var weights))
				{
					ContextCollection contextCollection = new ContextCollection("", enabled: true, flatten: true);
					foreach (Func<int, BarycentricWeights, IContextBase> triangleHitTestAction in _triangleHitTestActions)
					{
						IContextBase contextBase = triangleHitTestAction(element, weights);
						if (contextBase != null)
						{
							contextCollection.ContextElements.Add(contextBase);
						}
					}
					if (contextCollection.ContextElements.Count == 0)
					{
						return (IContextBase)null;
					}
					return contextCollection;
				}
			}
			return (IContextBase)null;
		});
	}

	public void AddPointHitTestAction(Func<int, IContextBase> onHit)
	{
		AddMapHitTest((HitTestArgs args) =>
		{
			int nearestPoint = SpatialIndex.NearestPoint(_tin.PointSpatialIndex, _tin, args.QueryPoint, args.Window.CellSize * 8.0, out var _);
			return (nearestPoint == -1) ? null : onHit(nearestPoint);
		});
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(Polyline lineIntersection, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		ThrowHitTestErr();
		return false;
	}

	private static void ThrowHitTestErr()
	{
		_logger.LogOnce(LogLevel.Error, "HitTest not implemented on TinRenderLayer");
	}

	public bool RenderSelected(PlotRequest plot)
	{
		if (SelectedSet.IsEmpty)
		{
			return false;
		}
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		_ = RenderFill;
		return false;
	}

	public IList<object> GetSelectedVectors()
	{
		return null;
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx)
	{
		if (!HitTest(args, out var _, out var triIdx, out var tdef, out var element, out var globalElementIdx, out var localElementIdx))
		{
			_hitTestedIdx = -1;
			return null;
		}
		if (tdef.IsDeleted)
		{
			_hitTestedIdx = -1;
			return null;
		}
		List<object> objectList = new List<object>();
		_hitTestedIdx = ((int)element << 30) | (triIdx << 2);
		switch (element)
		{
		case TinElementType.Triangle:
		{
			Polygon asPolygon = _tin.Triangles[triIdx].AsPolygon();
			objectList.Add(asPolygon);
			break;
		}
		case TinElementType.Edge:
			_hitTestedIdx |= localElementIdx;
			objectList.Add(_tin.Edge(triIdx, localElementIdx));
			break;
		case TinElementType.Point:
			_hitTestedIdx |= localElementIdx;
			objectList.Add(_tin.Points[globalElementIdx]);
			break;
		}
		return objectList;
	}

	public bool HitTest(HitTestArgs args, out int tinIdx, out int triIdx, out TriangleDefinition tdef, out TinElementType element, out int globalElementIdx, out int localElementIdx)
	{
		Extent extent = _tin.Extent;
		Extent extent2 = args.HitBox(HitTestPixelCount);
		double hitTestPixelCountTimesCellSize = (double)HitTestPixelCount * args.Window.CellSize;
		if (!extent.MBROverlaps(extent2))
		{
			tdef = TriangleDefinition.NullTriangleDef();
			element = TinElementType.Point;
			tinIdx = -1;
			triIdx = -1;
			globalElementIdx = -1;
			localElementIdx = -1;
			return false;
		}
		triIdx = _tin.FindTriangleContainingPoint(args.QueryPoint);
		if (triIdx < 0)
		{
			tdef = TriangleDefinition.NullTriangleDef();
			element = TinElementType.Point;
			tinIdx = -1;
			globalElementIdx = -1;
			localElementIdx = -1;
			return false;
		}
		tinIdx = 0;
		tdef = _tin.TriangleDefinition[triIdx];
		for (int i = 0; i <= 2; i++)
		{
			int globalElementIdx2 = tdef.Point(i);
			Point other = _tin.Points[globalElementIdx2];
			if (args.QueryPoint.DistanceTo(other) < hitTestPixelCountTimesCellSize)
			{
				element = TinElementType.Point;
				globalElementIdx = globalElementIdx2;
				localElementIdx = i;
				return true;
			}
		}
		for (int j = 0; j <= 2; j++)
		{
			tdef.EdgePoints(j, out var ptA, out var ptB);
			if (new Segment(_tin.Points[ptA], _tin.Points[ptB]).DistanceToPoint(args.QueryPoint) < hitTestPixelCountTimesCellSize)
			{
				element = TinElementType.Edge;
				globalElementIdx = -1;
				localElementIdx = j;
				return true;
			}
		}
		element = TinElementType.Triangle;
		globalElementIdx = triIdx;
		localElementIdx = -1;
		return true;
	}
}
