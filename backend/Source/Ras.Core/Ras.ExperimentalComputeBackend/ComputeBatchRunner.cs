using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Utility;
using Utility.Progress;

namespace Ras.ExperimentalComputeBackend;

public static class ComputeBatchRunner
{
	public static async Task<IReadOnlyList<ComputeOutcome>> RunAsync(IComputeBackend backend, IReadOnlyList<ComputeRequest> requests, ProgressReporter parent, ISynchronizationContext uiSync = null, int maxConcurrencyOverride = 0, CancellationToken ct = default(CancellationToken))
	{
		if (backend == null)
		{
			throw new ArgumentNullException("backend");
		}
		if (requests == null || requests.Count == 0)
		{
			return Array.Empty<ComputeOutcome>();
		}
		int maxConcurrencyOverride2 = ((maxConcurrencyOverride > 0) ? maxConcurrencyOverride : backend.Info.MaxConcurrency);
		if (maxConcurrencyOverride2 <= 0)
		{
			maxConcurrencyOverride2 = requests.Count;
		}
		maxConcurrencyOverride2 = Math.Min(maxConcurrencyOverride2, requests.Count);
		float fraction = 1f / (float)requests.Count;
		ComputeOutcome[] outcomes = new ComputeOutcome[requests.Count];
		SemaphoreSlim gate = new SemaphoreSlim(maxConcurrencyOverride2);
		try
		{
			Task[] tasks = new Task[requests.Count];
			for (int i = 0; i < requests.Count; i++)
			{
				tasks[i] = RunOneAsync(i);
			}
			await Task.WhenAll(tasks);
			return outcomes;
		}
		finally
		{
			if (gate != null)
			{
				((IDisposable)gate).Dispose();
			}
		}
		async Task RunOneAsync(int index)
		{
			await gate.WaitAsync(ct);
			try
			{
				ProgressReporter reporter = ((parent == null) ? ProgressReporter.None() : parent.SubTask(requests[index].DisplayName, (float)index * fraction, fraction));
				ComputeContext context = new ComputeContext
				{
					Reporter = reporter,
					UiSync = uiSync
				};
				ComputeOutcome[] outcomesArray = outcomes;
				outcomesArray[index] = await backend.ComputeAsync(requests[index], context, ct);
			}
			finally
			{
				gate.Release();
			}
		}
	}
}
