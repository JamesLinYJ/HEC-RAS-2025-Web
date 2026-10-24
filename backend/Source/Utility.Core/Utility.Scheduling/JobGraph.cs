using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Utility.Scheduling;

public class JobGraph
{
	public IReadOnlyList<ScheduledJob> Jobs { get; }

	public JobGraph(IReadOnlyList<ScheduledJob> jobs)
	{
		Jobs = jobs ?? Array.Empty<ScheduledJob>();
	}

	public IReadOnlyList<IReadOnlyList<ScheduledJob>> ToStages()
	{
		Dictionary<ScheduledJob, int> depth = new Dictionary<ScheduledJob, int>();
		foreach (ScheduledJob job in Jobs)
		{
			Depth(job);
		}
		return ((IEnumerable<IGrouping<int, KeyValuePair<ScheduledJob, int>>>)(from kv in depth
			group kv by kv.Value into g
			orderby g.Key
			select g)).Select((Func<IGrouping<int, KeyValuePair<ScheduledJob, int>>, IReadOnlyList<ScheduledJob>>)((IGrouping<int, KeyValuePair<ScheduledJob, int>> g) => g.Select((KeyValuePair<ScheduledJob, int> kv) => kv.Key).ToList())).ToList();
		int Depth(ScheduledJob j)
		{
			if (depth.TryGetValue(j, out var localValue))
			{
				return localValue;
			}
			int depth2 = 0;
			foreach (ScheduledJob dependsOn in j.DependsOn)
			{
				depth2 = Math.Max(depth2, Depth(dependsOn) + 1);
			}
			depth[j] = depth2;
			return depth2;
		}
	}

	public string DescribeTree()
	{
		StringBuilder stringBuilder = new StringBuilder();
		IReadOnlyList<IReadOnlyList<ScheduledJob>> readOnlyList = ToStages();
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
			handler.AppendLiteral("Stage ");
			handler.AppendFormatted(i);
			handler.AppendLiteral(":");
			stringBuilder2.AppendLine(ref handler);
			foreach (ScheduledJob readOnlyList2 in readOnlyList[i])
			{
				stringBuilder.Append("  ").Append(readOnlyList2);
				if (readOnlyList2.DependsOn.Count > 0)
				{
					stringBuilder.Append("  <- ");
					stringBuilder.Append(string.Join(", ", readOnlyList2.DependsOn.Select((ScheduledJob d) => d.Kind + ":" + d.Identity)));
				}
				stringBuilder.AppendLine();
			}
		}
		return stringBuilder.ToString();
	}
}
