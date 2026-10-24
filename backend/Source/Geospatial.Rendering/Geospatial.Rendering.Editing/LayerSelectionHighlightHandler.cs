using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class LayerSelectionHighlightHandler : RenderLayerPreviewHandlerBase
{
	private const int SelectionZIndex = 6;

	private readonly Func<IBasicSelection> _getSelection;

	private readonly Func<long> _getSelectionVersion;

	private readonly VectorSymbology _symbology;

	private readonly VectorSymbology _polygonSymbology;

	private long _cachedVersion = long.MinValue;

	private IBasicSelection _lastSelection;

	public LayerSelectionHighlightHandler(Func<IBasicSelection> getSelection, Func<long> getSelectionVersion, VectorSymbology symbology, VectorSymbology polygonSymbology)
		: base("LayerSelectionHighlight")
	{
		_getSelection = getSelection ?? throw new ArgumentNullException("getSelection");
		_getSelectionVersion = getSelectionVersion ?? throw new ArgumentNullException("getSelectionVersion");
		_symbology = symbology;
		_polygonSymbology = polygonSymbology;
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		IVectorManager vectorManager = ctx.VectorManager;
		IBasicSelection basicSelection = _getSelection();
		if (basicSelection != _lastSelection)
		{
			_cachedVersion = long.MinValue;
			_lastSelection = basicSelection;
		}
		if (basicSelection == null)
		{
			return false;
		}
		GetVectorIds(ctx, out var ptsId, out var segsId, out var polylinesId, out var polygonsId, out var complexPolygonsId);
		OverlayChannels preview = ctx.Preview;
		_003C_003Ey__InlineArray5<VectorID> buffer = default;
		buffer[0] = ptsId;
		buffer[1] = segsId;
		buffer[2] = polylinesId;
		buffer[3] = polygonsId;
		buffer[4] = complexPolygonsId;
		bool areAllVectorIDsNull = preview.AreAllVectorIDsNull(buffer);
		long cachedVersion = _getSelectionVersion();
		if (!areAllVectorIDsNull && cachedVersion == _cachedVersion)
		{
			TouchAllVectors(ctx);
			return false;
		}
		_cachedVersion = cachedVersion;
		if (basicSelection is ISelection selection)
		{
			IReadOnlyList<Polyline> selectedPolylines = selection.GetSelectedPolylines();
			IReadOnlyList<Polygon> selectedPolygons = selection.GetSelectedPolygons();
			IReadOnlyList<ComplexPolygon> selectedComplexPolygons = selection.GetSelectedComplexPolygons();
			IReadOnlyList<Point> selectedPoints = selection.GetSelectedPoints();
			if (selectedPolylines == null || selectedPolylines.Count == 0)
			{
				vectorManager.TryClearVector(ref polylinesId);
			}
			else
			{
				vectorManager.AddOrUpdateVector(ref polylinesId, selectedPolylines, _symbology);
			}
			if (selectedPolygons == null || selectedPolygons.Count == 0)
			{
				vectorManager.TryClearVector(ref polygonsId);
			}
			else
			{
				vectorManager.AddOrUpdateVector(ref polygonsId, selectedPolygons, _symbology);
			}
			if (selectedComplexPolygons == null || selectedComplexPolygons.Count == 0)
			{
				vectorManager.TryClearVector(ref complexPolygonsId);
			}
			else
			{
				vectorManager.AddOrUpdateVector(ref complexPolygonsId, selectedComplexPolygons, _symbology);
			}
			if (selectedPoints == null || selectedPoints.Count == 0)
			{
				vectorManager.TryClearVector(ref ptsId);
			}
			else
			{
				vectorManager.AddOrUpdateVector(ref ptsId, selectedPoints, _symbology);
			}
		}
		else
		{
			IList<object> selectedVectors = basicSelection.GetSelectedVectors();
			if (selectedVectors == null || selectedVectors.Count == 0)
			{
				vectorManager.TryClearVector(ref ptsId);
				vectorManager.TryClearVector(ref segsId);
				vectorManager.TryClearVector(ref polylinesId);
				vectorManager.TryClearVector(ref polygonsId);
				vectorManager.TryClearVector(ref complexPolygonsId);
			}
			else
			{
				vectorManager.AddOrUpdateVectors(selectedVectors, _symbology, ref ptsId, ref segsId, ref polylinesId, ref polygonsId, ref complexPolygonsId);
			}
		}
		vectorManager.TrySetVectorZIndex(ptsId, 6);
		vectorManager.TrySetVectorZIndex(segsId, 6);
		vectorManager.TrySetVectorZIndex(polylinesId, 6);
		vectorManager.TrySetVectorZIndex(polygonsId, 6);
		vectorManager.TrySetVectorZIndex(complexPolygonsId, 6);
		vectorManager.TryUpdateSymbology(polygonsId, _polygonSymbology);
		vectorManager.TryUpdateSymbology(complexPolygonsId, _polygonSymbology);
		RegisterVectorIds(ctx, ptsId, segsId, polylinesId, polygonsId, complexPolygonsId);
		return false;
	}
}
