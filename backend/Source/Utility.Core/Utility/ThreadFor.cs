using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility;

public class ThreadFor : IDisposable
{
	public class Worker
	{
		private readonly int rank;

		public Thread thread;

		private readonly ThreadFor parent;

		public Worker(int rank, ThreadFor parent)
		{
			this.rank = rank;
			this.parent = parent;
			if (rank > 0)
			{
				thread = new Thread(WorkLoop);
				thread.Start();
			}
		}

		public void SliceWork(int count, out int start, out int stop)
		{
			start = (int)((float)(count * rank) / (float)parent.numThreads);
			stop = (int)((float)(count * (rank + 1)) / (float)parent.numThreads) - 1;
		}

		public void WorkLoop()
		{
			try
			{
				while (!parent.canExit)
				{
					parent.barrier.SignalAndWait();
					SliceWork(parent.count, out var start, out var stop);
					parent.operation(start, stop);
					parent.barrier.SignalAndWait();
				}
			}
			catch (Exception ex)
			{
				_logger.LogTrace("Exception in ThreadFor: {ex}", ex);
			}
		}
	}

	public readonly int numThreads;

	public readonly Barrier barrier;

	public readonly Worker[] workers;

	public static ThreadFor DefaultSingleThread = new ThreadFor(1, useSmartFor: true);

	private Action<int, int> operation;

	private int count;

	private bool active;

	private bool canExit;

	private static readonly ILogger _logger = LogHelper.GetLogger("ThreadFor");

	public readonly bool useParallelFor = true;

	public readonly ParallelOptions parallelOptions;

	public ThreadFor(int numThreads, bool useSmartFor = false)
	{
		// Browser WebAssembly has no ProcessThreadCollection and cannot set
		// operating-system processor affinity. Preserve dedicated workers on
		// native platforms; in the browser use the existing bounded managed
		// parallel-for path so the solver keeps its serial correctness semantics.
		useParallelFor = useSmartFor || OperatingSystem.IsBrowser();
		if (useParallelFor)
		{
			parallelOptions = new ParallelOptions();
			parallelOptions.MaxDegreeOfParallelism = numThreads;
			return;
		}
		this.numThreads = numThreads;
		barrier = new Barrier(numThreads);
		workers = new Worker[numThreads];
		active = false;
		ProcessThreadCollection threads = Process.GetCurrentProcess().Threads;
		List<int> intList = new List<int>();
		foreach (ProcessThread thread in threads)
		{
			intList.Add(thread.Id);
		}
		for (int i = 0; i < numThreads; i++)
		{
			workers[i] = new Worker(i, this);
		}
		ProcessThreadCollection threads2 = Process.GetCurrentProcess().Threads;
		if (numThreads <= 1)
		{
			return;
		}
		int x = 0;
		Run(numThreads, (int start, int stop) =>
		{
			x++;
		});
		int idealProcessor = 0;
		foreach (ProcessThread threads22 in threads2)
		{
			if (threads22.ThreadState == System.Diagnostics.ThreadState.Wait && intList.Contains(threads22.Id))
			{
				continue;
			}
			threads22.IdealProcessor = idealProcessor;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				try
				{
					threads22.ProcessorAffinity = 1 << idealProcessor;
				}
				catch (InvalidOperationException exception)
				{
					_logger.LogWarning(exception, "Error setting affinity in ThreadFor");
				}
			}
			idealProcessor += 2;
			if (idealProcessor >= Environment.ProcessorCount)
			{
				idealProcessor = ((idealProcessor % 2 == 0) ? 1 : 0);
			}
		}
	}

	public void Dispose()
	{
		if (useParallelFor)
		{
			return;
		}
		operation = (int start, int stop) =>
		{
		};
		if (numThreads > 1)
		{
			barrier.SignalAndWait();
			canExit = true;
			barrier.SignalAndWait();
			for (int unity = 1; unity < numThreads; unity++)
			{
				workers[unity].thread.Join();
				workers[unity].thread = null;
			}
		}
	}

	public void Run(int count, Action<int, int> action, int chunkSize = 128)
	{
		if (useParallelFor)
		{
			Parallel.SmartFor(count, action, parallelOptions, chunkSize);
			return;
		}
		if (active)
		{
			throw new Exception("Attempt to reenter ThreadFor Run");
		}
		active = true;
		operation = action;
		this.count = count;
		barrier.SignalAndWait();
		workers[0].SliceWork(count, out var start, out var stop);
		operation(start, stop);
		barrier.SignalAndWait();
		active = false;
	}

	public static bool InterlockedInplaceMin(ref int location, int newValue)
	{
		int location2;
		do
		{
			location2 = location;
			if (location2 <= newValue)
			{
				return false;
			}
		}
		while (Interlocked.CompareExchange(ref location, newValue, location2) != location2);
		return true;
	}
}
