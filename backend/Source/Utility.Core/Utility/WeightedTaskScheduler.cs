using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Utility;

public class WeightedTaskScheduler : TaskScheduler
{
	[ThreadStatic]
	private static bool _currentThreadIsProcessingItems;

	private readonly LinkedList<Task> _tasks = new LinkedList<Task>();

	private readonly int _maxDegreeOfParallelism;

	private readonly int _maxWeight;

	private int _delegatesQueuedOrRunning;

	private ManualResetEvent _wakeUpEngine = new ManualResetEvent(initialState: false);

	private Dictionary<Task, WaitHandle> _waitHandle2 = new Dictionary<Task, WaitHandle>();

	private ManualResetEvent _taskCompleted = new ManualResetEvent(initialState: false);

	public sealed override int MaximumConcurrencyLevel => _maxDegreeOfParallelism;

	public WeightedTaskScheduler(int maxWeight)
	{
		if (maxWeight < 1)
		{
			throw new ArgumentOutOfRangeException("maxWeight");
		}
		_maxDegreeOfParallelism = int.MaxValue;
		_maxWeight = maxWeight;
		throw new NotImplementedException();
	}

	protected sealed override void QueueTask(Task task)
	{
		lock (_tasks)
		{
			if (task is WeightedTask weightedTask)
			{
				_ = weightedTask.Weight;
			}
			_tasks.AddLast(task);
			if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism)
			{
				_delegatesQueuedOrRunning++;
				NotifyThreadPoolOfPendingWork();
			}
		}
	}

	private void AllocateThread(Task t, int weight)
	{
		ManualResetEvent taskCompleted = new ManualResetEvent(initialState: false);
		ThreadPool.UnsafeQueueUserWorkItem((object? _) =>
		{
			_currentThreadIsProcessingItems = true;
			try
			{
				Task tasksTerm;
				lock (_tasks)
				{
					if (_tasks.Count == 0)
					{
						_delegatesQueuedOrRunning--;
						return;
					}
					tasksTerm = _tasks.First.Value;
					_tasks.RemoveFirst();
				}
				TryExecuteTask(tasksTerm);
			}
			finally
			{
				_currentThreadIsProcessingItems = false;
			}
			taskCompleted.Set();
		}, null);
	}

	private void OnTaskCompleted()
	{
	}

	private void NotifyThreadPoolOfPendingWork()
	{
		ThreadPool.UnsafeQueueUserWorkItem((object? _) =>
		{
			_currentThreadIsProcessingItems = true;
			try
			{
				while (true)
				{
					Task tasksTerm;
					lock (_tasks)
					{
						if (_tasks.Count == 0)
						{
							_delegatesQueuedOrRunning--;
							break;
						}
						tasksTerm = _tasks.First.Value;
						_tasks.RemoveFirst();
					}
					TryExecuteTask(tasksTerm);
				}
			}
			finally
			{
				_currentThreadIsProcessingItems = false;
			}
		}, null);
	}

	protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
	{
		if (!_currentThreadIsProcessingItems)
		{
			return false;
		}
		if (taskWasPreviouslyQueued)
		{
			if (TryDequeue(task))
			{
				return TryExecuteTask(task);
			}
			return false;
		}
		return TryExecuteTask(task);
	}

	protected sealed override bool TryDequeue(Task task)
	{
		lock (_tasks)
		{
			return _tasks.Remove(task);
		}
	}

	protected sealed override IEnumerable<Task> GetScheduledTasks()
	{
		bool lockTaken = false;
		try
		{
			Monitor.TryEnter(_tasks, ref lockTaken);
			if (lockTaken)
			{
				return _tasks;
			}
			throw new NotSupportedException();
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(_tasks);
			}
		}
	}
}
