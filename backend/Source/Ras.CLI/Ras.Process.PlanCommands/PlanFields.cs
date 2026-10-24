using System;
using System.Collections.Generic;
using Ras.Engine;
using Ras.Layers;
using Utility.Filtering;

namespace Ras.Process.PlanCommands;

internal sealed class PlanFields : ISelectorFields
{
	private static readonly IReadOnlyList<PlanFieldDescriptor> Descriptors = new PlanFieldDescriptor[27]
	{
		new PlanFieldDescriptor("Name", typeof(string), (Plan p) => p.Name, null),
		new PlanFieldDescriptor("ShortID", typeof(string), (Plan p) => p.ShortID, (Plan p, object v) =>
		{
			p.ShortID = (string)v;
		}),
		new PlanFieldDescriptor("Description", typeof(string), (Plan p) => p.Description, (Plan p, object v) =>
		{
			p.Description = (string)v;
		}),
		new PlanFieldDescriptor("SolverType", typeof(SolverType), (Plan p) => p.SolverType, (Plan p, object v) =>
		{
			p.SolverType = (SolverType)v;
		}),
		new PlanFieldDescriptor("EquationSet", typeof(SolverControl.EquationSet), (Plan p) => p.EquationSet, (Plan p, object v) =>
		{
			p.EquationSet = (SolverControl.EquationSet)v;
		}),
		new PlanFieldDescriptor("TimeStepping", typeof(SolverControl.TimeStepping), (Plan p) => p.TimeStepping, (Plan p, object v) =>
		{
			p.TimeStepping = (SolverControl.TimeStepping)v;
		}),
		new PlanFieldDescriptor("ImplicitAdaptiveTimestepping", typeof(bool), (Plan p) => p.ImplicitAdaptiveTimestepping, (Plan p, object v) =>
		{
			p.ImplicitAdaptiveTimestepping = (bool)v;
		}),
		new PlanFieldDescriptor("UseGPUAdaptive", typeof(bool), (Plan p) => p.UseGPUAdaptive, (Plan p, object v) =>
		{
			p.UseGPUAdaptive = (bool)v;
		}),
		new PlanFieldDescriptor("UseFBF", typeof(bool), (Plan p) => p.UseFBF, (Plan p, object v) =>
		{
			p.UseFBF = (bool)v;
		}),
		new PlanFieldDescriptor("UseAdvection", typeof(bool), (Plan p) => p.UseAdvection, (Plan p, object v) =>
		{
			p.UseAdvection = (bool)v;
		}),
		new PlanFieldDescriptor("UseDiffusion", typeof(bool), (Plan p) => p.UseDiffusion, (Plan p, object v) =>
		{
			p.UseDiffusion = (bool)v;
		}),
		new PlanFieldDescriptor("SensitivityRun", typeof(bool), (Plan p) => p.SensitivityRun, (Plan p, object v) =>
		{
			p.SensitivityRun = (bool)v;
		}),
		new PlanFieldDescriptor("ExtraOutput", typeof(bool), (Plan p) => p.ExtraOutput, (Plan p, object v) =>
		{
			p.ExtraOutput = (bool)v;
		}),
		new PlanFieldDescriptor("CoreCount", typeof(int), (Plan p) => p.CoreCount, (Plan p, object v) =>
		{
			p.CoreCount = (int)v;
		}),
		new PlanFieldDescriptor("MaxCourant", typeof(double), (Plan p) => p.MaxCourant, (Plan p, object v) =>
		{
			p.MaxCourant = (double)v;
		}),
		new PlanFieldDescriptor("Theta", typeof(double), (Plan p) => p.Theta, (Plan p, object v) =>
		{
			p.Theta = (double)v;
		}),
		new PlanFieldDescriptor("TimestepSafetyFactor", typeof(double), (Plan p) => p.TimestepSafetyFactor, (Plan p, object v) =>
		{
			p.TimestepSafetyFactor = (double)v;
		}),
		new PlanFieldDescriptor("MaxInnerIterations", typeof(int), (Plan p) => p.MaxInnerIterations, (Plan p, object v) =>
		{
			p.MaxInnerIterations = (int)v;
		}),
		new PlanFieldDescriptor("MaxOuterIterations", typeof(int), (Plan p) => p.MaxOuterIterations, (Plan p, object v) =>
		{
			p.MaxOuterIterations = (int)v;
		}),
		new PlanFieldDescriptor("TimeWindowMode", typeof(PlanTimeWindowMode), (Plan p) => p.TimeWindowMode, (Plan p, object v) =>
		{
			p.TimeWindowMode = (PlanTimeWindowMode)v;
		}),
		new PlanFieldDescriptor("StartTime", typeof(DateTime), (Plan p) => p.InternalStartTime, (Plan p, object v) =>
		{
			p.InternalStartTime = (DateTime)v;
		}),
		new PlanFieldDescriptor("ComputeDurationSeconds", typeof(double), (Plan p) => p.InternalComputeDurationSeconds, (Plan p, object v) =>
		{
			p.InternalComputeDurationSeconds = (double)v;
		}),
		new PlanFieldDescriptor("ComputeTimestep", typeof(TimeSpan), (Plan p) => p.ComputeTimestep, (Plan p, object v) =>
		{
			p.ComputeTimestep = (TimeSpan)v;
		}),
		new PlanFieldDescriptor("BaseOutputInterval", typeof(TimeSpan), (Plan p) => p.BaseOutputInterval, (Plan p, object v) =>
		{
			p.BaseOutputInterval = (TimeSpan)v;
		}),
		new PlanFieldDescriptor("EffectiveStartTime", typeof(DateTime), (Plan p) => p.StartTime, null),
		new PlanFieldDescriptor("EffectiveEndTime", typeof(DateTime), (Plan p) => p.EndTime, null),
		new PlanFieldDescriptor("ExpectedProfileCount", typeof(int), (Plan p) => p.ExpectedProfileCount, null)
	};

	private static readonly Dictionary<string, PlanFieldDescriptor> ByNormalized = BuildLookup();

	private readonly Plan _plan;

	public IReadOnlyList<string> FieldNames
	{
		get
		{
			List<string> stringList = new List<string>(Descriptors.Count);
			foreach (PlanFieldDescriptor descriptor in Descriptors)
			{
				stringList.Add(descriptor.Name);
			}
			return stringList;
		}
	}

	public PlanFields(Plan plan)
	{
		_plan = plan;
	}

	private static Dictionary<string, PlanFieldDescriptor> BuildLookup()
	{
		Dictionary<string, PlanFieldDescriptor> ordinal = new Dictionary<string, PlanFieldDescriptor>(StringComparer.Ordinal);
		foreach (PlanFieldDescriptor descriptor in Descriptors)
		{
			ordinal[FilterNames.Normalize(descriptor.Name)] = descriptor;
		}
		return ordinal;
	}

	public bool TryGetField(string normalizedName, out Type type, out bool settable)
	{
		if (ByNormalized.TryGetValue(normalizedName, out var localValue))
		{
			type = localValue.Type;
			settable = localValue.Settable;
			return true;
		}
		type = null;
		settable = false;
		return false;
	}

	public bool TryGetValue(string normalizedName, out object localValue2)
	{
		localValue2 = null;
		if (_plan == null)
		{
			return false;
		}
		if (!ByNormalized.TryGetValue(normalizedName, out var localValue))
		{
			return false;
		}
		localValue2 = localValue.Getter(_plan);
		return true;
	}

	public bool TrySetValue(string normalizedName, object localValue2, out string error)
	{
		error = null;
		if (_plan == null)
		{
			error = "no plan loaded.";
			return false;
		}
		if (!ByNormalized.TryGetValue(normalizedName, out var localValue))
		{
			error = "Unknown field '" + normalizedName + "'.";
			return false;
		}
		if (!localValue.Settable)
		{
			error = "'" + localValue.Name + "' is read-only.";
			return false;
		}
		localValue.Setter(_plan, localValue2);
		return true;
	}
}
