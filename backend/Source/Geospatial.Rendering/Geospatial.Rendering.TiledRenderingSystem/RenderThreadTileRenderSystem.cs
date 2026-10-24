using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class RenderThreadTileRenderSystem<TTask> : ITileRenderSystem<TTask> where TTask : IRenderTask
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RenderThreadTileRenderSystem");

	private List<Thread> _workerThreads;

	private bool _isRunning;

	private string _name;

	private WorkerContext<TTask> _context;

	private const int PollingFrequencySeconds = 60;

	private Thread _pollThread;

	private const bool _combineRenderThreadLogging = true;

	public ISynchronizationContext UIThreadSynchronizationContext { get; private init; }

	public Func<TTask, ValueTask> RenderTaskCompleted { get; private init; }

	public Func<TTask, bool> IsTaskStillValid { get; private init; }

	public IReadOnlyList<Thread> WorkerThreads => _workerThreads;

	public bool IsRunning => _isRunning;

	public string Name => _name;

	public RenderThreadTileRenderSystem(string name, ISynchronizationContext executeUIThreadOperation, Func<TTask, ValueTask> renderTaskCompleted, Func<TTask, bool> isTaskStillValid)
	{
		_name = name;
		_context = new WorkerContext<TTask>
		{
			SystemReference = new WeakReference<RenderThreadTileRenderSystem<TTask>>(this)
		};
		UIThreadSynchronizationContext = executeUIThreadOperation;
		RenderTaskCompleted = renderTaskCompleted;
		IsTaskStillValid = isTaskStillValid;
		_context.Reset.Set();
	}

	private int GetAppropriateRenderThreadCount()
	{
		return Environment.ProcessorCount;
	}

	public void InitializeRenderingSystem(int maxThreads = int.MaxValue)
	{
		int count = Math.Min(GetAppropriateRenderThreadCount(), maxThreads);
		_isRunning = true;
		_context.TaskAvailable = new SemaphoreSlim(0);
		_workerThreads = new List<Thread>(count);
		for (int i = 0; i < count; i++)
		{
			Thread thread = new Thread(WorkerLoop);
			thread.Name = $"{_name} Render Thread {i + 1}";
			thread.IsBackground = true;
			thread.Priority = ThreadPriority.Lowest;
			_context.Timers.Add(thread.ManagedThreadId, new Stopwatch());
			_workerThreads.Add(thread);
		}
		foreach (Thread workerThread in _workerThreads)
		{
			workerThread.Start(_context);
		}
		_logger.LogInformation("{name} render threads initialized with {Count} threads.", _name, count);
		InitializePollingThread();
	}

	private void InitializePollingThread()
	{
		_context.StartTime = DateTime.Now;
		_pollThread = new Thread(PollLoop);
		_pollThread.Name = _name + " Polling Thread";
		_pollThread.Priority = ThreadPriority.Lowest;
		_pollThread.IsBackground = true;
		_pollThread.Start(_context);
	}

	private static void WorkerLoop(object objCtx)
	{
		WorkerContext<TTask> workerContext = objCtx as WorkerContext<TTask>;
		Stopwatch stopwatch = workerContext.Timers[Thread.CurrentThread.ManagedThreadId];
		while (workerContext.IsRunning)
		{
			stopwatch.Stop();
			workerContext.Reset.WaitOne();
			workerContext.TaskAvailable.Wait();
			workerContext.Reset.WaitOne();
			stopwatch.Start();
			long renderThreadSystemVersion = workerContext._renderThreadSystemVersion;
			if (workerContext._renderThreadSystemVersion != renderThreadSystemVersion || !workerContext.TasksToRender.TryDequeue(out var result))
			{
				continue;
			}
			try
			{
				if (!workerContext.IsTaskValid(result))
				{
					result.RenderSuccess = false;
					workerContext.QueueCompletedTask(result);
					continue;
				}
				try
				{
					result.RenderSuccess = true;
					result.Render();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "{name} crash in {MethodName}", workerContext.Name, "WorkerLoop");
					result.RenderSuccess = false;
				}
				if (workerContext._renderThreadSystemVersion != renderThreadSystemVersion)
				{
					result.RenderSuccess = false;
				}
				workerContext.QueueCompletedTask(result);
			}
			finally
			{
				result = default;
			}
		}
	}

	private static void PollLoop(object objCtx)
	{
		WorkerContext<TTask> workerContext = objCtx as WorkerContext<TTask>;
		Dictionary<int, TimeSpan> timeSpanByInt = new Dictionary<int, TimeSpan>();
		foreach (KeyValuePair<int, Stopwatch> timer in workerContext.Timers)
		{
			timeSpanByInt.Add(timer.Key, TimeSpan.Zero);
		}
		TimeSpan timeSpan = TimeSpan.FromSeconds(60L);
		while (workerContext.IsRunning)
		{
			Thread.Sleep(timeSpan);
			TimeSpan timeSpan2 = DateTime.Now - workerContext.StartTime;
			TimeSpan zero = TimeSpan.Zero;
			TimeSpan zero2 = TimeSpan.Zero;
			foreach (KeyValuePair<int, Stopwatch> timer2 in workerContext.Timers)
			{
				int key = timer2.Key;
				TimeSpan elapsed = timer2.Value.Elapsed;
				zero += elapsed;
				TimeSpan timeSpan3 = timeSpanByInt[key];
				TimeSpan timeSpan4 = elapsed - timeSpan3;
				zero2 += timeSpan4;
				timeSpanByInt[key] = elapsed;
			}
			double scaledZeroOverTimeSpan2 = 100.0 * zero / timeSpan2;
			double scaledZero2OverTimeSpan = 100.0 * zero2 / timeSpan;
			scaledZeroOverTimeSpan2 /= (double)workerContext.WorkerThreads.Count;
			scaledZero2OverTimeSpan /= (double)workerContext.WorkerThreads.Count;
			if (scaledZero2OverTimeSpan > 0.0)
			{
				_logger.LogTrace("{name} threads {PercentBusyRecent}% busy in the last {RecentInterval} seconds. {PercentBusyTotal}% busy over the entire process.", workerContext.Name, scaledZero2OverTimeSpan, 60, scaledZeroOverTimeSpan2);
			}
		}
	}

	public async ValueTask RenderTasksAsync(IList<TTask> tasks, bool renderSerially = false)
	{
		Stopwatch sw = Stopwatch.StartNew();
		await TaskTileRenderSystem<TTask>.RenderTiles(tasks, renderSimultaneously: true, RenderTaskCompleted, renderSerially);
		sw.Stop();
		_logger.LogTrace("Full Screen Render: {TileCount} tiles in {TimeMs} ms.", tasks.Count, sw.Elapsed.TotalMilliseconds);
	}

	public void QueueRenderTasks(IList<TTask> tasks)
	{
		for (int i = 0; i < tasks.Count; i++)
		{
			_context.TasksToRender.Enqueue(tasks[i]);
			_context.TaskAvailable.Release();
		}
	}

	public void ShutdownRenderingSystem()
	{
		_isRunning = false;
		_context.TasksToRender.Clear();
		foreach (Thread workerThread in _workerThreads)
		{
			_ = workerThread;
			_context.TaskAvailable.Release();
		}
	}

	public void ForceCancelAllWork()
	{
		_context.Reset.Reset();
		Interlocked.Increment(ref _context._renderThreadSystemVersion);
		for (int i = 0; i < _context.TaskAvailable.CurrentCount; i++)
		{
			_context.TaskAvailable.Wait();
		}
		TTask result;
		while (_context.TasksToRender.TryDequeue(out result))
		{
			result.RenderSuccess = false;
			RenderTaskCompleted(result).GetAwaiter().GetResult();
		}
		TTask result2;
		while (_context.CompletedTasksPending.TryDequeue(out result2))
		{
			result2.RenderSuccess = false;
			RenderTaskCompleted(result2);
		}
		_context.Reset.Set();
	}
}
