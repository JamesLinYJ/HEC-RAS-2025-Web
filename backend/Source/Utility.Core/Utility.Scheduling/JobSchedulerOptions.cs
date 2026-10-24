using System;
using System.Collections.Generic;
using System.Threading;

namespace Utility.Scheduling;

public class JobSchedulerOptions
{
	public Dictionary<string, int> ResourceBudgets { get; } = new Dictionary<string, int>();

	public bool ContinueOnFailure { get; set; } = true;

	public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

	public static JobSchedulerOptions ForCpu(int cores)
	{
		JobSchedulerOptions jobSchedulerOptions = new JobSchedulerOptions();
		jobSchedulerOptions.ResourceBudgets["cpu"] = Math.Max(1, cores);
		return jobSchedulerOptions;
	}
}
