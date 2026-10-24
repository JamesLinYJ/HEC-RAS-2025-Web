using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Rendering.TiledRenderingSystem;

public abstract class PyramidedTileEmployee
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("PyramidedTileEmployee");

	private ISynchronizationContext _mapSyncContextCrossThread;

	private ISynchronizationContext _mapSyncContext;

	private readonly int _ownerThreadId;

	private readonly List<Action> _updateQueue = new List<Action>();

	private bool _updateQueueReEntrencyCheck;

	private int _updateQueueOwnerThreadId;

	public ISynchronizationContext MapSyncContextCrossThread
	{
		get
		{
			if (_mapSyncContextCrossThread == null)
			{
				_mapSyncContextCrossThread = new Utility.SynchronizationContext(QueueUpdate_CrossThread);
			}
			return _mapSyncContextCrossThread;
		}
	}

	public ISynchronizationContext MapSyncContext
	{
		get
		{
			if (_mapSyncContext == null)
			{
				_mapSyncContext = new Utility.SynchronizationContext(QueueUpdate);
			}
			return _mapSyncContext;
		}
	}

	public ISynchronizationContext UIThreadSynchronizationContext { get; set; }

	public PyramidedTileEmployee(ISynchronizationContext uiThreadSyncContext)
	{
		_ownerThreadId = Thread.CurrentThread.ManagedThreadId;
		UIThreadSynchronizationContext = uiThreadSyncContext;
	}

	public abstract bool IsAnythingRenderingRightNow();

	protected void AssertOwnerThread()
	{
		if (_ownerThreadId != Thread.CurrentThread.ManagedThreadId)
		{
			throw new Exception("This action must be invoked from the main/owner thread.");
		}
	}

	public abstract void QueueRenderTiles();

	public void QueueUpdate(Action updateValue)
	{
		AssertOwnerThread();
		if (_updateQueueReEntrencyCheck)
		{
			throw new Exception("Recursive QueueRenderUpdate calls.");
		}
		_updateQueue.Add(updateValue);
		TryProcessQueuedUpdates();
		if (!IsAnythingRenderingRightNow())
		{
			QueueRenderTiles();
		}
	}

	public void QueueUpdate_CrossThread(Action updateValue)
	{
		UIThreadSynchronizationContext?.Sync(() =>
		{
			QueueUpdate(updateValue);
		});
	}

	protected bool TryProcessQueuedUpdates()
	{
		if (IsAnythingRenderingRightNow())
		{
			return false;
		}
		if (_updateQueue.Count == 0)
		{
			return false;
		}
		if (_updateQueueReEntrencyCheck)
		{
			throw new Exception($"Render updates should not be re-entrant. Owner thread {_ownerThreadId}, thread that entered the update loop {_updateQueueOwnerThreadId}, current thread {Thread.CurrentThread.ManagedThreadId}, {_updateQueue.Count} queued updates.");
		}
		_updateQueueReEntrencyCheck = true;
		_updateQueueOwnerThreadId = Thread.CurrentThread.ManagedThreadId;
		try
		{
			foreach (Action updateQueue in _updateQueue)
			{
				try
				{
					updateQueue?.Invoke();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Error invoking action in TryProcessQueuedUpdates");
				}
			}
			_updateQueue.Clear();
		}
		finally
		{
			_updateQueueReEntrencyCheck = false;
		}
		return true;
	}
}
