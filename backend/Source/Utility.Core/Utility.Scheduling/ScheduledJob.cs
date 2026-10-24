using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Logging;
using Utility.Progress;

namespace Utility.Scheduling;

public class ScheduledJob
{
	private readonly List<ScheduledJob> _dependsOn = new List<ScheduledJob>();

	public string Kind { get; }

	public string Identity { get; }

	public string DisplayName { get; }

	public IReadOnlyDictionary<string, int> ResourceCost { get; }

	public object Tag { get; }

	public IReadOnlyList<ScheduledJob> DependsOn => _dependsOn;

	internal Func<ProgressReporter, OperationResult> Runner { get; }

	public JobStatus Status { get; internal set; }

	public OperationResult Outcome { get; internal set; }

	public TimeSpan Elapsed { get; internal set; }

	public int TotalCost
	{
		get
		{
			int ordinal = 0;
			foreach (int ordinal2 in ResourceCost.Values)
			{
				ordinal += ordinal2;
			}
			return ordinal;
		}
	}

	public ScheduledJob(string kind, string identity, string displayName, IReadOnlyDictionary<string, int> resourceCost, Func<ProgressReporter, OperationResult> runner, object tag = null)
	{
		Kind = kind ?? "";
		Identity = identity ?? "";
		DisplayName = displayName ?? identity ?? kind ?? "<job>";
		ResourceCost = resourceCost ?? new Dictionary<string, int>();
		Runner = runner ?? throw new ArgumentNullException("runner");
		Tag = tag;
	}

	public void AddDependency(ScheduledJob other)
	{
		if (other != null && other != this && !_dependsOn.Contains(other))
		{
			_dependsOn.Add(other);
		}
	}

	public override string ToString()
	{
		string resourceCostKvTerm = string.Join(", ", ResourceCost.Select((KeyValuePair<string, int> kv) => $"{kv.Value} {kv.Key}"));
		return $"[{Kind}] {DisplayName} (~{resourceCostKvTerm})";
	}
}
