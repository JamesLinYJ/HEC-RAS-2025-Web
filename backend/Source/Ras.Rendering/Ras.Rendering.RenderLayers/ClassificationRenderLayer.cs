using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.Helpers;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Utility.DataStructures;
using Utility.Interfaces;

namespace Ras.Rendering.RenderLayers;

public class ClassificationRenderLayer : DiscreteRasterRenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection
{
	private ClassificationLayer _layer;

	private PolygonClassificationInput _polygonClassificationInput;

	public SelectedSet SelectedSet { get; } = new SelectedSet();

	public VectorSymbology VectorSymbology { get; set; }

	public int Count => _layer.Layers.Sum((IClassificationInput layer) =>
	{
		if (!(layer is PolygonClassificationInput { Count: var count }))
		{
			if (!(layer is ConstantClassificationInput) && !(layer is RasterClassificationInput))
			{
				throw new NotImplementedException();
			}
			return 0;
		}
		return count;
	});

	public ClassificationRenderLayer(ClassificationLayer layer)
		: base(layer)
	{
		_layer = layer;
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		hitTestedIdx = 0;
		if (!TryEnsurePolygonClassificationInput(out var polygonClassificationInput))
		{
			return default(CompactList<object>);
		}
		return PolygonRenderLayer.GetHitTestVectors(args, polygonClassificationInput, polygonClassificationInput.GetSpatialIndex(), out hitTestedIdx);
	}

	public IList<object> GetSelectedVectors()
	{
		if (!TryEnsurePolygonClassificationInput(out var polygonClassificationInput))
		{
			return null;
		}
		return PolygonRenderLayer.GetSelectedVectors(SelectedSet, polygonClassificationInput);
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return _layer;
	}

	private bool TryEnsurePolygonClassificationInput(out PolygonClassificationInput polygonClassificationInput)
	{
		if (_polygonClassificationInput == null)
		{
			foreach (IClassificationInput layer in _layer.Layers)
			{
				if (layer is PolygonClassificationInput polygonClassificationInput2)
				{
					_polygonClassificationInput = polygonClassificationInput2;
					polygonClassificationInput = polygonClassificationInput2;
					return true;
				}
			}
			polygonClassificationInput = null;
			return false;
		}
		polygonClassificationInput = _polygonClassificationInput;
		return true;
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		return SelectInternal(mode, selectExt, (int idx, PolygonClassificationInput pgonInput, Extent ext) => pgonInput.HitTest(idx, ext, 0.0));
	}

	public bool Select(Polyline lineIntersection, SelectMode mode)
	{
		return SelectInternal(mode, lineIntersection, (int idx, PolygonClassificationInput pgonInput, Polyline line) => pgonInput.HitTest(idx, line, 0.0));
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return false;
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		if (!TryEnsurePolygonClassificationInput(out var polygonClassificationInput))
		{
			return false;
		}
		(int, double) result;
		return VectorRenderLayer.SelectHelperPoint(polygonClassificationInput.TryHitTestByBestArea(selectArgs.QueryPoint, polygonClassificationInput.GetSpatialIndex(), 8.0 * selectArgs.Window.CellSize, out result) ? result.Item1.WrapInCompactList() : default(CompactList<int>), selectArgs, mode, SelectedSet);
	}

	private bool SelectInternal<T>(SelectMode mode, T payload, Func<int, PolygonClassificationInput, T, bool> hitTest) where T : IExtent
	{
		if (!TryEnsurePolygonClassificationInput(out var polygonClassificationInput))
		{
			return false;
		}
		return VectorRenderLayer.SelectHelper(payload.Extent, SelectedSet, Count, mode, polygonClassificationInput, payload, hitTest, (PolygonClassificationInput pgonInput) => pgonInput.GetSpatialIndex());
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _layer.VersionXY;
	}
}
