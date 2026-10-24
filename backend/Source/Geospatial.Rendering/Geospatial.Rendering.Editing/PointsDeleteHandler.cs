using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class PointsDeleteHandler<T, K> : IEditHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection, new() where K : GenericEditablePointCollection<T>
{
	private readonly K _editable;

	private Action _onAllFeaturesDeleted;

	public PointsDeleteHandler(K editable, Action onAllFeaturesDeleted = null)
	{
		_onAllFeaturesDeleted = onAllFeaturesDeleted;
		_editable = editable;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Delete;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		_editable.DeleteSelectedPoints();
		if (_onAllFeaturesDeleted != null && _editable.Count == 0)
		{
			_onAllFeaturesDeleted();
			return new HandlerResult
			{
				Handled = true,
				RequestParentReleaseCapture = true,
				RequestParentTrackCaptureStateChanged = true
			};
		}
		return new HandlerResult
		{
			Handled = true
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
