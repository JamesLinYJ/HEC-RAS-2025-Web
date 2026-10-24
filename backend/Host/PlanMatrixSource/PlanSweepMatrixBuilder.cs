// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ras.Layers;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public static class PlanSweepMatrixBuilder
{
	public const int MaxCombinations = 2048;

	public static bool TryBuild(Plan plan, IReadOnlyList<PlanSweepAxis> axes, PlanSweepOptions options, out List<PlanSweepCombination> combinations, out string error)
	{
		combinations = new List<PlanSweepCombination>();
		error = null;
		if (plan == null)
		{
			error = "No plan selected.";
			return false;
		}
		foreach (PlanSweepAxis axis in axes)
		{
			if (axis.Values.Count == 0)
			{
				error = "Axis \"" + axis.Property.DisplayName + "\" has no values selected.";
				return false;
			}
		}
		long count = 1L;
		foreach (PlanSweepAxis axis2 in axes)
		{
			count *= axis2.Values.Count;
			if (count > 2048)
			{
				error = $"That's more than {2048} combinations. Narrow the axes down.";
				return false;
			}
		}
		string planName = PlanSweepProperty.Sanitize(plan.Name);
		HashSet<string> usedFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> firstFileForRun = new Dictionary<string, string>(StringComparer.Ordinal);
		int[] indices = new int[axes.Count];
		for (int i = 0; i < count; i++)
		{
			combinations.Add(BuildOne(plan, planName, axes, indices, options, usedFilenames, firstFileForRun));
			Advance(axes, indices);
		}
		return true;
	}

	private static void Advance(IReadOnlyList<PlanSweepAxis> axes, int[] indices)
	{
		for (int countOffset = axes.Count - 1; countOffset >= 0; countOffset--)
		{
			indices[countOffset]++;
			if (indices[countOffset] < axes[countOffset].Values.Count)
			{
				break;
			}
			indices[countOffset] = 0;
		}
	}

	private static PlanSweepCombination BuildOne(Plan plan, string planName, IReadOnlyList<PlanSweepAxis> axes, int[] indices, PlanSweepOptions options, HashSet<string> usedFilenames, Dictionary<string, string> firstFileForRun)
	{
		Dictionary<string, object> ordinal = new Dictionary<string, object>(StringComparer.Ordinal);
		for (int i = 0; i < axes.Count; i++)
		{
			ordinal[axes[i].Property.PropertyName] = axes[i].Values[indices[i]];
		}
		PlanSettingsView settings = new PlanSettingsView(plan, ordinal);
		List<PlanSweepAssignment> assignments = new List<PlanSweepAssignment>(axes.Count);
		for (int j = 0; j < axes.Count; j++)
		{
			PlanSweepProperty property = axes[j].Property;
			bool isRelevant = true;
			string reason = null;
			if (options.CollapseIrrelevant && !axes[j].IsPinned)
			{
				isRelevant = PlanSweepRules.IsRelevant(property.PropertyName, settings, out reason);
			}
			assignments.Add(new PlanSweepAssignment
			{
				Property = property,
				Value = ordinal[property.PropertyName],
				IsRelevant = isRelevant,
				IrrelevantReason = reason
			});
		}
		string label = BuildLabel(assignments);
		string exclusionReason = FirstViolation(settings, options.IllegalRules);
		if (exclusionReason != null)
		{
			return new PlanSweepCombination
			{
				Assignments = assignments,
				Filename = string.Empty,
				Label = label,
				ExclusionReason = exclusionReason
			};
		}
		string key = BuildRunKey(assignments);
		if (firstFileForRun.TryGetValue(key, out var localValue))
		{
			return new PlanSweepCombination
			{
				Assignments = assignments,
				Filename = string.Empty,
				Label = label,
				ExclusionReason = "Same run as " + localValue + " (differs only in ignored values)",
				IsCollapsed = true
			};
		}
		string filename = (firstFileForRun[key] = BuildFilename(planName, assignments, usedFilenames));
		return new PlanSweepCombination
		{
			Assignments = assignments,
			Filename = filename,
			Label = label,
			ExclusionReason = string.Empty
		};
	}

	private static string FirstViolation(PlanSettingsView settings, IReadOnlyList<PlanCombinationRule> rules)
	{
		if (rules == null)
		{
			return null;
		}
		foreach (PlanCombinationRule rule in rules)
		{
			if (rule.IsViolatedBy(settings))
			{
				return rule.Reason;
			}
		}
		return null;
	}

	private static string BuildLabel(IReadOnlyList<PlanSweepAssignment> assignments)
	{
		if (assignments.Count == 0)
		{
			return "Plan defaults";
		}
		IEnumerable<string> values = assignments.Select((PlanSweepAssignment a) =>
		{
			string displayNameDescribeTerm = a.Property.DisplayName + " " + a.Property.Describe(a.Value);
			return (!a.IsRelevant) ? (displayNameDescribeTerm + " (ignored)") : displayNameDescribeTerm;
		});
		return string.Join(" · ", values);
	}

	private static string BuildRunKey(IReadOnlyList<PlanSweepAssignment> assignments)
	{
		IEnumerable<string> values = from a in assignments
			where a.IsRelevant
			select a.Property.PropertyName + "=" + a.Property.Token(a.Value);
		return string.Join("|", values);
	}

	private static string BuildFilename(string planName, IReadOnlyList<PlanSweepAssignment> assignments, HashSet<string> usedFilenames)
	{
		StringBuilder stringBuilder = new StringBuilder(planName);
		foreach (PlanSweepAssignment assignment in assignments)
		{
			if (assignment.IsRelevant)
			{
				stringBuilder.Append('_');
				stringBuilder.Append(PlanSweepProperty.Sanitize(assignment.Property.Token(assignment.Value)));
			}
		}
		string stringBuilder2 = stringBuilder.ToString();
		string filename = stringBuilder2 + ".r2r.h5";
		int ordinal = 2;
		while (!usedFilenames.Add(filename))
		{
			filename = stringBuilder2 + "-" + ordinal + ".r2r.h5";
			ordinal++;
		}
		return filename;
	}
}
