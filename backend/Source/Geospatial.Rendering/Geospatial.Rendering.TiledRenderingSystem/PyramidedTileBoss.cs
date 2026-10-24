using System;
using System.Collections.Generic;
using Utility;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class PyramidedTileBoss : PyramidedTileEmployee
{
	private List<WeakReference<PyramidedTileManager>> _managers = new List<WeakReference<PyramidedTileManager>>();

	public static PyramidedTileBoss Instance { get; set; }

	public PyramidedTileBoss(ISynchronizationContext uiSyncContext)
		: base(uiSyncContext)
	{
	}

	public void Register(PyramidedTileManager manager)
	{
		_managers.Add(new WeakReference<PyramidedTileManager>(manager));
	}

	public void Unregister(PyramidedTileManager manager)
	{
		for (int i = 0; i < _managers.Count; i++)
		{
			if (_managers[i].TryGetTarget(out var target) && target.Equals(manager))
			{
				_managers.RemoveAt(i);
				break;
			}
		}
	}

	internal void NotifyRenderingComplete(PyramidedTileManager sender)
	{
		if (TryProcessQueuedUpdates())
		{
			QueueRenderTiles();
		}
	}

	public override bool IsAnythingRenderingRightNow()
	{
		foreach (WeakReference<PyramidedTileManager> manager in _managers)
		{
			if (manager.TryGetTarget(out var target) && target.IsAnythingRenderingRightNow())
			{
				return true;
			}
		}
		return false;
	}

	public override void QueueRenderTiles()
	{
		foreach (WeakReference<PyramidedTileManager> manager in _managers)
		{
			if (manager.TryGetTarget(out var target))
			{
				target.QueueRenderTiles();
			}
		}
	}
}
