using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class EditablePolygon : GenericEditableLine<Polygon>
{
	public Polygon Polygon => Vector;

	public EditablePolygon(Polygon polygon, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation, SelectedSet selectionCache, OverlayChannels overlayChannels)
		: base(polygon, vectorManager, VectorType.Polygon, executeEditOperation, selectionCache, overlayChannels)
	{
	}

	public override void PreviewMoveSelectedPoints(HitTestArgs hitTest, Point interactionStartPt, bool removeUnusedPreviewVectors = true)
	{
		PreviewMoveSelectedPointsInternal(hitTest.QueryPoint, interactionStartPt, isOpenEnded: false, removeUnusedPreviewVectors);
	}

	public new void PreviewMovePoints(List<int> pointIndices, Vector offsetDelta, bool isOpenEnded = true, bool removeUnusedPreviewVectors = true)
	{
		base.PreviewMovePoints(pointIndices, offsetDelta, false, removeUnusedPreviewVectors);
	}

	public override void PreviewAppendPoint(Point point, bool removeUnusedPreviewVectors = true, bool isOpenEnded = true)
	{
		base.PreviewAppendPoint(point, removeUnusedPreviewVectors, isOpenEnded: false);
	}

	public override void PreviewInsertPoint(Point queryPt, int index, bool isOpenEnded = true)
	{
		base.PreviewInsertPoint(queryPt, index, isOpenEnded: false);
	}

	protected override void PreviewInsertPointInternal(Point queryPt, int segmentIndex, bool isOpenEnded = true)
	{
		base.PreviewInsertPointInternal(queryPt, segmentIndex, isOpenEnded: false);
	}

	public override bool TryAttachEndPoint(HitTestArgs hitTest, out int insertedIndex)
	{
		insertedIndex = -1;
		return false;
	}

	public override void AttachEndPoint(HitTestArgs hitTest)
	{
	}

	public override bool TryPreviewAttachEndPoint(HitTestArgs hitTest)
	{
		return false;
	}

	public override void PreviewAttachEndPoint(HitTestArgs hitTest)
	{
	}
}
