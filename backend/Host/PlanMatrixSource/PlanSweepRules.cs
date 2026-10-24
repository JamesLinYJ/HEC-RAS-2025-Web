// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;
using System.Collections.Generic;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public static class PlanSweepRules
{
	public static IReadOnlyList<PlanCombinationRule> Illegal { get; } = new List<PlanCombinationRule>
	{
		new PlanCombinationRule("GPU + Implicit", "The GPU processor only supports explicit time-stepping.", (PlanSettingsView s) => s.IsGpu && s.Get<SolverControl.TimeStepping>("TimeStepping") == SolverControl.TimeStepping.Implicit),
		new PlanCombinationRule("GPU + DWE", "The GPU processor does not support the Diffusion Wave Equation (DWE).", (PlanSettingsView s) => s.IsGpu && s.Get<SolverControl.EquationSet>("EquationSet") == SolverControl.EquationSet.DWE),
		new PlanCombinationRule("DWE + Explicit", "The Diffusion Wave Equation (DWE) only supports implicit time-stepping.", (PlanSettingsView s) => s.Get<SolverControl.EquationSet>("EquationSet") == SolverControl.EquationSet.DWE && s.Get<SolverControl.TimeStepping>("TimeStepping") == SolverControl.TimeStepping.Explicit),
		new PlanCombinationRule("Non-positive timestep", "The compute timestep must be greater than zero.", (PlanSettingsView s) => s.Get<TimeSpan>("ComputeTimestep") <= TimeSpan.Zero),
		new PlanCombinationRule("Output interval mismatch", "The output interval must be a whole multiple of the compute timestep.", (PlanSettingsView s) => !IsWholeMultiple(s.Get<TimeSpan>("BaseOutputInterval"), s.Get<TimeSpan>("ComputeTimestep")))
	};

	public static IReadOnlyList<PlanRelevanceRule> Relevance { get; } = BuildRelevanceRules();

	public static bool IsRelevant(string propertyName, PlanSettingsView settings, out string reason)
	{
		reason = null;
		foreach (PlanRelevanceRule relevance in Relevance)
		{
			if (!(relevance.PropertyName != propertyName) && !relevance.IsRelevant(settings))
			{
				reason = relevance.Reason;
				return false;
			}
		}
		return true;
	}

	private static bool IsWholeMultiple(TimeSpan interval, TimeSpan step)
	{
		if (step <= TimeSpan.Zero)
		{
			return false;
		}
		return interval.Ticks % step.Ticks == 0;
	}

	private static List<PlanRelevanceRule> BuildRelevanceRules()
	{
		List<PlanRelevanceRule> rules = new List<PlanRelevanceRule>();
		ExplicitOnly("LtsLevels");
		ExplicitOnly("TimestepSafetyFactor");
		ExplicitOnly("UseFBF");
		ExplicitOnly("UseMinAdvectiveTimeStep");
		ExplicitOnly("MinAdvectiveTimeStep");
		ExplicitOnly("MaxPercentWetAdvTimeStep");
		rules.Add(new PlanRelevanceRule("MinAdvectiveTimeStep", "the minimum advective timestep is switched off", (PlanSettingsView s) => s.Get<bool>("UseMinAdvectiveTimeStep")));
		rules.Add(new PlanRelevanceRule("MaxPercentWetAdvTimeStep", "the minimum advective timestep is switched off", (PlanSettingsView s) => s.Get<bool>("UseMinAdvectiveTimeStep")));
		ImplicitOnly("ImplicitAdaptiveTimestepping");
		ImplicitOnly("ImplicitFactor");
		ImplicitOnly("MaxInnerIterations");
		ImplicitOnly("VolumeTolerance");
		ImplicitOnly("VolumeMaxTolerance");
		ImplicitOnly("VolumeRMSTolerance");
		ImplicitOnly("MaxOuterIterations");
		ImplicitOnly("UsePartialGrid");
		ImplicitOnly("MaxCourant");
		ImplicitOnly("ActiveThresholdPartGrid");
		ImplicitOnly("BufferCountPartGrid");
		ImplicitOnly("ElevationThresholdPartGrid");
		ImplicitOnly("WaterSurfaceTolerance");
		ImplicitOnly("WaterSurfaceMaxTolerance");
		ImplicitOnly("WaterSurfaceRMSTolerance");
		rules.Add(new PlanRelevanceRule("MaxCourant", "adaptive implicit time-stepping is switched off", (PlanSettingsView s) => s.Get<bool>("ImplicitAdaptiveTimestepping")));
		string[] lookupTable = new string[3] { "ActiveThresholdPartGrid", "BufferCountPartGrid", "ElevationThresholdPartGrid" };
		foreach (string propertyName in lookupTable)
		{
			rules.Add(new PlanRelevanceRule(propertyName, "the partial grid is switched off", (PlanSettingsView s) => s.Get<bool>("UsePartialGrid")));
		}
		lookupTable = new string[3] { "WaterSurfaceTolerance", "WaterSurfaceMaxTolerance", "WaterSurfaceRMSTolerance" };
		foreach (string propertyName2 in lookupTable)
		{
			rules.Add(new PlanRelevanceRule(propertyName2, "the water surface loop is disabled (no outer iterations)", (PlanSettingsView s) => s.Get<int>("MaxOuterIterations") >= 1));
		}
		ShallowWaterOnly("UseAdvection");
		ShallowWaterOnly("AdvectionMethod");
		ShallowWaterOnly("AdvectionImplicitFactor");
		ShallowWaterOnly("UseDiffusion");
		ShallowWaterOnly("TurbulenceModel");
		ShallowWaterOnly("BaseEddyViscosity");
		ShallowWaterOnly("LongMixCoeff");
		ShallowWaterOnly("TransMixCoeff");
		ShallowWaterOnly("HorizMixCoeff");
		ShallowWaterOnly("Latitude");
		rules.Add(new PlanRelevanceRule("AdvectionMethod", "advection is switched off", (PlanSettingsView s) => s.Get<bool>("UseAdvection")));
		rules.Add(new PlanRelevanceRule("AdvectionImplicitFactor", "advection is switched off", (PlanSettingsView s) => s.Get<bool>("UseAdvection")));
		lookupTable = new string[5] { "TurbulenceModel", "BaseEddyViscosity", "LongMixCoeff", "TransMixCoeff", "HorizMixCoeff" };
		foreach (string propertyName3 in lookupTable)
		{
			rules.Add(new PlanRelevanceRule(propertyName3, "turbulence is switched off", (PlanSettingsView s) => s.Get<bool>("UseDiffusion")));
		}
		lookupTable = new string[3] { "LongMixCoeff", "TransMixCoeff", "HorizMixCoeff" };
		foreach (string propertyName4 in lookupTable)
		{
			rules.Add(new PlanRelevanceRule(propertyName4, "the mixing coefficients belong to the Smagorinsky closure", (PlanSettingsView s) => s.Get<TurbulenceModel>("TurbulenceModel") == TurbulenceModel.Smagorinsky));
		}
		rules.Add(new PlanRelevanceRule("CoreCount", "the GPU solver does not use CPU cores", (PlanSettingsView s) => !s.IsGpu));
		rules.Add(new PlanRelevanceRule("ParallelMethod", "the GPU solver does not use CPU threads", (PlanSettingsView s) => !s.IsGpu));
		rules.Add(new PlanRelevanceRule("UseGPUAdaptive", "GPU solver only", (PlanSettingsView s) => s.IsGpu));
		rules.Add(new PlanRelevanceRule("InternalStartTime", "the time window is inherited from the boundary conditions", (PlanSettingsView s) => s.Get<PlanTimeWindowMode>("TimeWindowMode") == PlanTimeWindowMode.Plan));
		rules.Add(new PlanRelevanceRule("InternalComputeDurationSeconds", "the time window is inherited from the boundary conditions", (PlanSettingsView s) => s.Get<PlanTimeWindowMode>("TimeWindowMode") == PlanTimeWindowMode.Plan));
		return rules;
		void ExplicitOnly(string property)
		{
			rules.Add(new PlanRelevanceRule(property, "explicit solver only", (PlanSettingsView s) => s.IsExplicit));
		}
		void ImplicitOnly(string property)
		{
			rules.Add(new PlanRelevanceRule(property, "implicit solver only", (PlanSettingsView s) => s.IsImplicit));
		}
		void ShallowWaterOnly(string property)
		{
			rules.Add(new PlanRelevanceRule(property, "shallow water equations only", (PlanSettingsView s) => s.IsShallowWater));
		}
	}
}
