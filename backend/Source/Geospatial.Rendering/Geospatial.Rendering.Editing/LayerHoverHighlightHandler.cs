using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class LayerHoverHighlightHandler : RenderLayerPreviewHandlerBase
{
	private const int HoverHighlightZIndex = 500;

	private readonly Func<IVectorRenderLayer> _getActiveLayer;

	private readonly VectorSymbology _symbology;

	private int _previewFeatureVectorIndex = -1;

	private IVectorRenderLayer _lastLayer;

	private readonly List<Polyline> _arrowsCache = new List<Polyline>();

	private readonly List<FeatureSegmentSubset> _arrowsSubsetCache = new List<FeatureSegmentSubset>();

	public LayerHoverHighlightHandler(Func<IVectorRenderLayer> getActiveLayer, VectorSymbology symbology)
		: base("LayerHoverHighlight")
	{
		_getActiveLayer = getActiveLayer ?? throw new ArgumentNullException("getActiveLayer");
		_symbology = symbology;
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		IVectorManager vectorManager = ctx.VectorManager;
		IVectorRenderLayer vectorRenderLayer = _getActiveLayer();
		if (vectorRenderLayer != _lastLayer)
		{
			Reset(ctx);
			_lastLayer = vectorRenderLayer;
		}
		if (hit == null || hit.QueryPoint.IsNull() || vectorRenderLayer == null)
		{
			TouchAllVectors(ctx);
			Reset(ctx);
			return false;
		}
		IList<object> hitTestVectors = vectorRenderLayer.GetHitTestVectors(hit, out var _hitTestedIdx);
		if (hitTestVectors == null || hitTestVectors.Count == 0)
		{
			TouchAllVectors(ctx);
			Reset(ctx);
			return false;
		}
		if (_previewFeatureVectorIndex == _hitTestedIdx)
		{
			TouchAllVectors(ctx);
			return false;
		}
		GetVectorIds(ctx, out var ptsId, out var segsId, out var polylinesId, out var polygonsId, out var complexPolygonsId);
		vectorManager.AddOrUpdateVectors(hitTestVectors, _symbology, ref ptsId, ref segsId, ref polylinesId, ref polygonsId, ref complexPolygonsId);
		_arrowsCache.Clear();
		_arrowsSubsetCache.Clear();
		foreach (object hitTestVector in hitTestVectors)
		{
			if (hitTestVector is Polyline polyline)
			{
				_arrowsCache.Add(polyline);
				_arrowsSubsetCache.Add(new FeatureSegmentSubset(0, polyline.SegmentCount - 1 + ((polyline is Polygon) ? (-1) : 0))
				{
					Fid = _arrowsCache.Count - 1
				});
			}
		}
		if (_arrowsCache.Count > 0)
		{
			List<Segment> chevronsAlongLine = RepeatedSymbolRenderer.GetChevronsAlongLine(hit.Window, _arrowsCache, (int fid, List<Polyline> arrows) => arrows[fid], _arrowsSubsetCache);
			vectorManager.AddOrUpdateVector(ref segsId, chevronsAlongLine, _symbology);
		}
		vectorManager.SetVectorZIndex(ptsId, 500);
		vectorManager.SetVectorZIndex(segsId, 500);
		vectorManager.SetVectorZIndex(polylinesId, 500);
		vectorManager.SetVectorZIndex(polygonsId, 500);
		vectorManager.SetVectorZIndex(complexPolygonsId, 500);
		RegisterVectorIds(ctx, ptsId, segsId, polylinesId, polygonsId, complexPolygonsId);
		_previewFeatureVectorIndex = _hitTestedIdx;
		return false;
	}

	private void Reset(EditContext ctx)
	{
		IVectorManager vectorManager = ctx.VectorManager;
		if (_previewFeatureVectorIndex != -1)
		{
			GetVectorIds(ctx, out var ptsId, out var segsId, out var polylinesId, out var polygonsId, out var complexPolygonsId);
			_previewFeatureVectorIndex = -1;
			vectorManager.TryClearVector(ref ptsId);
			vectorManager.TryClearVector(ref segsId);
			vectorManager.TryClearVector(ref polylinesId);
			vectorManager.TryClearVector(ref polygonsId);
			vectorManager.TryClearVector(ref complexPolygonsId);
			RegisterVectorIds(ctx, ptsId, segsId, polylinesId, polygonsId, complexPolygonsId);
		}
	}
}
