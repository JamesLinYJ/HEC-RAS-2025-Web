using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Utility.Scheduling;

public class JobScheduler
{
	private static readonly ILogger _logger = LogHelper.GetLogger("JobScheduler");

	public OperationResult Execute(JobGraph graph, JobSchedulerOptions options = null, ProgressReporter reporter = null)
	{
		if (graph == null)
		{
			throw new ArgumentNullException("graph");
		}
		if (options == null)
		{
			options = new JobSchedulerOptions();
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Dictionary<string, int> resourceBudgets = options.ResourceBudgets;
		Dictionary<string, int> inUse = new Dictionary<string, int>();
		foreach (string key in resourceBudgets.Keys)
		{
			inUse[key] = 0;
		}
		HashSet<ScheduledJob> jobs = new HashSet<ScheduledJob>(graph.Jobs);
		Dictionary<ScheduledJob, Task> inFlight = new Dictionary<ScheduledJob, Task>();
		object sync = new object();
		ManualResetEventSlim stepEvt = new ManualResetEventSlim(initialState: false);
		OperationResult operationResult = new OperationResult(success: true);
		bool anyFailure = false;
		Stopwatch stopwatch = Stopwatch.StartNew();
		reporter.ReportMessage($"Scheduling {graph.Jobs.Count} jobs. Budgets: " + ((resourceBudgets.Count == 0) ? "(none)" : string.Join(", ", resourceBudgets.Select((KeyValuePair<string, int> kv) => $"{kv.Key}={kv.Value}"))));
		while (jobs.Count > 0 || inFlight.Count > 0)
		{
			if (options.CancellationToken.IsCancellationRequested)
			{
				operationResult.AddFailure("Schedule cancelled.");
				break;
			}
			lock (sync)
			{
				foreach (ScheduledJob job2 in jobs.ToList())
				{
					if (job2.DependsOn.Any((ScheduledJob d) => d.Status == JobStatus.Failed || d.Status == JobStatus.Skipped))
					{
						job2.Status = JobStatus.Skipped;
						job2.Outcome = OperationResult.Fail("Skipped - upstream dependency failed.");
						jobs.Remove(job2);
						reporter.ReportMessage("Skipping " + job2.DisplayName + " (upstream failure).");
					}
				}
				foreach (ScheduledJob scheduledJob in (from j in jobs.Where(IsReady)
					orderby j.TotalCost descending
					select j).ToList())
				{
					if (!jobs.Contains(scheduledJob) || !CanFit(scheduledJob, resourceBudgets, inUse, inFlight.Count == 0, out var oversubMsg))
					{
						continue;
					}
					if (oversubMsg != null)
					{
						reporter.ReportMessage(oversubMsg);
					}
					jobs.Remove(scheduledJob);
					Reserve(scheduledJob, inUse);
					scheduledJob.Status = JobStatus.Running;
					ProgressReporter sub = reporter.SubTask(scheduledJob.DisplayName, 0f, 1f);
					ScheduledJob captured = scheduledJob;
					Stopwatch startedAt = Stopwatch.StartNew();
					Task inFlight2 = Task.Run(() =>
					{
						OperationResult operationResult2;
						try
						{
							operationResult2 = captured.Runner(sub) ?? OperationResult.Success();
						}
						catch (Exception ex)
						{
							operationResult2 = OperationResult.Fail(captured.DisplayName, ex);
							_logger.LogError(ex, "Job {Name} threw.", captured.DisplayName);
						}
						lock (sync)
						{
							captured.Elapsed = startedAt.Elapsed;
							captured.Outcome = operationResult2;
							captured.Status = ((operationResult2 != null && operationResult2.Result) ? JobStatus.Completed : JobStatus.Failed);
							Release(captured, inUse);
							inFlight.Remove(captured);
							if (captured.Status == JobStatus.Failed)
							{
								anyFailure = true;
							}
							stepEvt.Set();
						}
						reporter.ReportMessage($"{((captured.Status == JobStatus.Completed) ? "OK" : "FAIL")}: {captured.DisplayName} ({captured.Elapsed.TotalSeconds:F1}s)");
					});
					inFlight[scheduledJob] = inFlight2;
				}
			}
			if (inFlight.Count > 0)
			{
				stepEvt.Wait(TimeSpan.FromSeconds(1L));
				stepEvt.Reset();
			}
			else if (jobs.Count > 0)
			{
				foreach (ScheduledJob job3 in jobs)
				{
					job3.Status = JobStatus.Skipped;
				}
				jobs.Clear();
				operationResult.AddFailure("Scheduler stalled - remaining jobs marked skipped.");
				break;
			}
			if (anyFailure && !options.ContinueOnFailure)
			{
				jobs.Clear();
			}
		}
		Task.WaitAll(inFlight.Values.ToArray());
		stopwatch.Stop();
		reporter.ReportMessage($"Schedule complete in {stopwatch.Elapsed.TotalSeconds:F1}s.");
		foreach (ScheduledJob job in graph.Jobs)
		{
			if (job.Status == JobStatus.Failed && job.Outcome != null)
			{
				operationResult.AddFailure(job.Outcome);
			}
		}
		operationResult.Result = !graph.Jobs.Any((ScheduledJob j) => j.Status == JobStatus.Failed || j.Status == JobStatus.Skipped);
		return operationResult;
	}

	private static bool IsReady(ScheduledJob j)
	{
		foreach (ScheduledJob dependsOn in j.DependsOn)
		{
			if (dependsOn.Status != JobStatus.Completed)
			{
				return false;
			}
		}
		return true;
	}

	private static bool CanFit(ScheduledJob j, IReadOnlyDictionary<string, int> budgets, IReadOnlyDictionary<string, int> inUse, bool soloIfTooBig, out string oversubMsg)
	{
		oversubMsg = null;
		bool condition = false;
		bool condition2 = false;
		foreach (KeyValuePair<string, int> resourceCost in j.ResourceCost)
		{
			int tryGetValueBudgetsTerm = (budgets.TryGetValue(resourceCost.Key, out var localValue) ? localValue : 0);
			int tryGetValueInUseTerm = (inUse.TryGetValue(resourceCost.Key, out var localValue2) ? localValue2 : 0);
			if (resourceCost.Value > tryGetValueBudgetsTerm)
			{
				condition = true;
			}
			if (tryGetValueInUseTerm + resourceCost.Value > tryGetValueBudgetsTerm)
			{
				condition2 = true;
			}
		}
		if (!condition2)
		{
			return true;
		}
		if (soloIfTooBig & condition)
		{
			oversubMsg = "Note: " + j.DisplayName + " exceeds resource budget; running solo.";
			return true;
		}
		return false;
	}

	private static void Reserve(ScheduledJob j, Dictionary<string, int> inUse)
	{
		foreach (KeyValuePair<string, int> resourceCost in j.ResourceCost)
		{
			inUse.TryGetValue(resourceCost.Key, out var localValue);
			inUse[resourceCost.Key] = localValue + resourceCost.Value;
		}
	}

	private static void Release(ScheduledJob j, Dictionary<string, int> inUse)
	{
		foreach (KeyValuePair<string, int> resourceCost in j.ResourceCost)
		{
			inUse.TryGetValue(resourceCost.Key, out var localValue);
			inUse[resourceCost.Key] = localValue - resourceCost.Value;
		}
	}
}
