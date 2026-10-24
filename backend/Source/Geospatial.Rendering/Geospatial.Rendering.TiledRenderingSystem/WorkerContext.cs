using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Logging;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class WorkerContext<TTask> where TTask : IRenderTask
{
	public WeakReference<RenderThreadTileRenderSystem<TTask>> SystemReference;

	public readonly Dictionary<int, Stopwatch> Timers = new Dictionary<int, Stopwatch>();

	public readonly ConcurrentQueue<TTask> TasksToRender = new ConcurrentQueue<TTask>();

	public SemaphoreSlim TaskAvailable;

	public ManualResetEvent Reset = new ManualResetEvent(initialState: true);

	public DateTime StartTime;

	public long _renderThreadSystemVersion = 1L;

	private static readonly ILogger _logger = LogHelper.GetLogger("WorkerContext");

	public readonly ConcurrentQueue<TTask> CompletedTasksPending = new ConcurrentQueue<TTask>();

	private int _completionDrainScheduled;

	private Action _drainCompletedTasks;

	public bool IsRunning
	{
		get
		{
			if (SystemReference.TryGetTarget(out var target))
			{
				return target.IsRunning;
			}
			return false;
		}
	}

	public string Name
	{
		get
		{
			if (!SystemReference.TryGetTarget(out var target))
			{
				return "Collected Reference";
			}
			return target.Name;
		}
	}

	public IReadOnlyList<Thread> WorkerThreads
	{
		get
		{
			if (!SystemReference.TryGetTarget(out var target))
			{
				return default(CompactList<Thread>);
			}
			return target.WorkerThreads;
		}
	}

	public bool IsTaskValid(TTask task)
	{
		if (!SystemReference.TryGetTarget(out var target))
		{
			return false;
		}
		return target.IsTaskStillValid(task);
	}

	public void SyncToUIThread(Action act)
	{
		if (SystemReference.TryGetTarget(out var target))
		{
			target.UIThreadSynchronizationContext.Sync(act);
		}
	}

	public ValueTask OnRenderTaskCompleted(TTask task)
	{
		if (!SystemReference.TryGetTarget(out var target))
		{
			return ValueTask.CompletedTask;
		}
		return target.RenderTaskCompleted(task);
	}

	public void QueueCompletedTask(TTask task)
	{
		CompletedTasksPending.Enqueue(task);
		if (Interlocked.Exchange(ref _completionDrainScheduled, 1) == 0)
		{
			if (_drainCompletedTasks == null)
			{
				_drainCompletedTasks = DrainCompletedTasks;
			}
			SyncToUIThread(_drainCompletedTasks);
		}
	}

	private void DrainCompletedTasks()
	{
		Interlocked.Exchange(ref _completionDrainScheduled, 0);
		TTask result;
		while (CompletedTasksPending.TryDequeue(out result))
		{
			try
			{
				OnRenderTaskCompleted(result);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to complete render task, this is likely a fatal exception to the application and will not go well.");
			}
		}
	}
}
