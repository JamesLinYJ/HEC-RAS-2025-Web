using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class EditablePointCollection : GenericEditablePointCollection<IPointCollection>
{
	public override bool IsValid()
	{
		return true;
	}

	public EditablePointCollection(IPointCollection vector, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation, SelectedSet selectionCache, OverlayChannels overlayChannels)
		: base(vector, vectorManager, VectorType.Point, executeEditOperation, selectionCache, overlayChannels)
	{
	}

	public void PreviewMoveSelectedPoints(HitTestArgs hitTest, Point interactionStartPt)
	{
		PreviewMoveSelectedPoints(hitTest.QueryPoint, interactionStartPt);
	}

	public void PreviewMoveSelectedPoints(Point hitPoint, Point interactionStartPt)
	{
		PreviewMovePoints(hitPoint - interactionStartPt);
	}

	private void PreviewMovePoints(Vector offset)
	{
		if (SelectedPoints.Count != 0)
		{
			_overlay.TryGetVector(_previewPointChannel, out var id);
			List<Point> points = (from index in SelectedPoints.Elements
				where index >= 0 && index < Vector.Count
				select ((IReadOnlyList<Point>)Vector)[index].Translate(offset)).ToList();
			VectorManager.AddOrUpdateVector(ref id, points, DefaultEditSymbology.SelectedPointSymbology);
			VectorManager.TrySetVectorZIndex(id, 10);
			_overlay.RegisterVector(_previewPointChannel, id);
		}
	}
}
