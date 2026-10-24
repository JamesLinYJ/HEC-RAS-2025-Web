using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility;

public static class Parallel
{
	public const int DefaultLoopSize = 4000;

	private static readonly ILogger _logger = LogHelper.GetLogger("Parallel");

	public static void SmartFor(int count, Action<int, int> StartToEndSubLoop, int loopSize = 4000)
	{
		int countLoopSizeTerm = (int)Math.Ceiling((double)count / (double)loopSize);
		int max = count - 1;
		if (countLoopSizeTerm == 1)
		{
			StartToEndSubLoop(0, count - 1);
			return;
		}
		System.Threading.Tasks.Parallel.For(0, countLoopSizeTerm, (int n) =>
		{
			int loopSizeTerm = n * loopSize;
			int maxTerm = (n + 1) * loopSize - 1;
			if (maxTerm > max)
			{
				maxTerm = max;
			}
			StartToEndSubLoop(loopSizeTerm, maxTerm);
		});
	}

	public static void SmartFor(int count, Action<int, int> StartToEndSubLoop, ParallelOptions parallelOptions, int loopSize = 4000)
	{
		if (count == 0)
		{
			return;
		}
		int countLoopSizeTerm = (int)Math.Ceiling((double)count / (double)loopSize);
		int max = count - 1;
		if (countLoopSizeTerm == 1 || parallelOptions == null || parallelOptions.MaxDegreeOfParallelism == 1)
		{
			StartToEndSubLoop(0, count - 1);
			return;
		}
		System.Threading.Tasks.Parallel.For(0, countLoopSizeTerm, parallelOptions, (int n) =>
		{
			int loopSizeTerm = n * loopSize;
			int maxTerm = (n + 1) * loopSize - 1;
			if (maxTerm > max)
			{
				maxTerm = max;
			}
			StartToEndSubLoop(loopSizeTerm, maxTerm);
		});
	}

	public static void SmartForEach(List<int> items, SpanAction<int, int> SpanSubLoop, ParallelOptions parallelOptions, int loopSize = 4000)
	{
		int count = items.Count;
		int countLoopSizeTerm = (int)Math.Ceiling((double)count / (double)loopSize);
		if (countLoopSizeTerm == 1 || parallelOptions == null)
		{
			Span<int> intSpan = CollectionsMarshal.AsSpan(items);
			SpanSubLoop(intSpan, 0);
			return;
		}
		System.Threading.Tasks.Parallel.For(0, countLoopSizeTerm, parallelOptions, (int n) =>
		{
			Span<int> intSpan2 = CollectionsMarshal.AsSpan(items);
			int loopSizeTerm = n * loopSize;
			int countTerm = (n + 1) * loopSize;
			if (countTerm > count)
			{
				countTerm = count;
			}
			SpanSubLoop(intSpan2.Slice(loopSizeTerm, countTerm - loopSizeTerm), 0);
		});
	}

	public static async Task LimitAsyncLoadBalanced(IList<Task> tasks, int parallelLimitCount = 8)
	{
		if (parallelLimitCount < 1)
		{
			throw new ArgumentException("Parallel limit set too low.");
		}
		int ctTotal = 0;
		Task[] tasksArray = new Task[parallelLimitCount];
		for (int i = 0; i < parallelLimitCount; i++)
		{
			tasksArray[i] = Task.Run(() =>
			{
				while (true)
				{
					int incrementInterlockedTerm = Interlocked.Increment(ref ctTotal) - 1;
					if (incrementInterlockedTerm >= tasks.Count)
					{
						break;
					}
					tasks[incrementInterlockedTerm].RunSynchronously();
				}
			});
		}
		await Task.WhenAll(tasksArray);
	}

	public static async Task LimitAsyncWorse(Func<IEnumerable<Task>> NextTask, int parallelLimitCount = 8)
	{
		if (parallelLimitCount < 1)
		{
			throw new ArgumentException("Parallel limit set too low.");
		}
		int ordinal = 0;
		Task[] tasks = new Task[parallelLimitCount];
		Array.Fill(tasks, Task.CompletedTask);
		List<Task> tasksList = new List<Task>();
		foreach (Task t in NextTask())
		{
			if (t == null)
			{
				continue;
			}
			int parallelLimitCountTerm = ordinal % parallelLimitCount;
			Task scheduled = RunAfter(tasks[parallelLimitCountTerm], t);
			tasks[parallelLimitCountTerm] = scheduled;
			tasksList.Add(scheduled);
			ordinal++;
		}
		await Task.WhenAll(tasksList);

		static async Task RunAfter(Task previous, Task task)
		{
			// A failed predecessor must not prevent later work from running.
			// Every scheduled task is retained above, so WhenAll observes all
			// failures, including failures to start a task.
			await Task.WhenAny(previous).ConfigureAwait(false);
			task.Start();
			await task.ConfigureAwait(false);
		}
	}

	public static ParallelOptions GetReasonableParallelOptions()
	{
		return new ParallelOptions
		{
			MaxDegreeOfParallelism = 32
		};
	}

	public static ParallelOptions FORCE_SERIAL_OPTIONS()
	{
		return new ParallelOptions
		{
			MaxDegreeOfParallelism = 1
		};
	}
}
