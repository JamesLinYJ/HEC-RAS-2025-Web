// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;
using System.Collections.Generic;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanSettingsView
{
	private readonly Plan _plan;

	private readonly IReadOnlyDictionary<string, object> _overrides;

	public bool IsExplicit => Get<SolverControl.TimeStepping>("TimeStepping") == SolverControl.TimeStepping.Explicit;

	public bool IsImplicit
	{
		get
		{
			if (Get<SolverControl.TimeStepping>("TimeStepping") != SolverControl.TimeStepping.Implicit)
			{
				return Get<SolverControl.EquationSet>("EquationSet") == SolverControl.EquationSet.DWE;
			}
			return true;
		}
	}

	public bool IsShallowWater => Get<SolverControl.EquationSet>("EquationSet") == SolverControl.EquationSet.SWE;

	public bool IsGpu => Get<SolverType>("SolverType") == SolverType.GPU;

	public PlanSettingsView(Plan plan, IReadOnlyDictionary<string, object> overrides)
	{
		_plan = plan;
		_overrides = overrides;
	}

	public object Get(string propertyName)
	{
		if (_overrides.TryGetValue(propertyName, out var localValue))
		{
			return localValue;
		}
		return (PlanSweepProperty.Find(propertyName) ?? throw new ArgumentException("Unknown plan property " + propertyName)).GetValue(_plan);
	}

	public T Get<T>(string propertyName)
	{
		return (T)Get(propertyName);
	}
}
