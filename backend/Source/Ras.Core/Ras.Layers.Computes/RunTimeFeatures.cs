using System;
using Ras.Engine;
using Utility.Matrices;

namespace Ras.Layers.Computes;

public class RunTimeFeatures
{
	public SolverControl.EquationSet EquationSet;

	public SolverControl.TimeStepping TimeStepping;

	public SolverControl.StepType StepType;

	public SolverKind MatrixSolver;

	public bool AdvectionEnabled;

	public bool TurbulenceEnabled;

	public bool VariableDt;

	public int CellCount;

	public int Cores;

	public double DtSeconds;

	public double SimDurationSeconds;

	public double OutputIntervalSeconds;

	public static RunTimeFeatures FromEngine(SolverControl control, int cellCount, double simDurationSeconds, double outputIntervalSeconds = 0.0)
	{
		return new RunTimeFeatures
		{
			EquationSet = control.equationSet,
			TimeStepping = control.timeStepping,
			StepType = control.stepType,
			MatrixSolver = control.MatrixSolverPrimary,
			AdvectionEnabled = (control.equationSet == SolverControl.EquationSet.SWE && control.advectionMethod != SolverControl.AdvectionMethod.None),
			TurbulenceEnabled = control.Turbulence.IsActive,
			VariableDt = control.variableDt,
			CellCount = cellCount,
			Cores = Math.Max(1, control.numCores),
			DtSeconds = ComputeDriver.MicrosToSeconds(control.dtMicros),
			SimDurationSeconds = simDurationSeconds,
			OutputIntervalSeconds = outputIntervalSeconds
		};
	}

	public static bool TryFromPlan(Plan plan, int cellCount, out RunTimeFeatures features)
	{
		features = null;
		if (plan == null || cellCount <= 0)
		{
			return false;
		}
		if (plan.SolverType == SolverType.GPU)
		{
			return false;
		}
		double computeBaseTimestepSeconds = plan.ComputeBaseTimestepSeconds;
		double computeDurationSeconds = plan.ComputeDurationSeconds;
		if (computeBaseTimestepSeconds <= 0.0 || computeDurationSeconds <= 0.0)
		{
			return false;
		}
		SolverControl.EquationSet equationSet = plan.EquationSet;
		SolverControl.TimeStepping timeStepping = ((equationSet == SolverControl.EquationSet.DWE) ? SolverControl.TimeStepping.Implicit : plan.TimeStepping);
		bool timeSteppingSolverControlTerm = timeStepping == SolverControl.TimeStepping.Implicit;
		SolverControl.StepType stepType = ((!timeSteppingSolverControlTerm) ? ((plan.LtsLevels == SolverControl.LtsLevelOptions.GlobalStepping) ? (plan.UseFBF ? SolverControl.StepType.FWDBACKFWD : SolverControl.StepType.FWDBACK) : (plan.UseFBF ? SolverControl.StepType.FWDBACKFWD_LTS : SolverControl.StepType.FWDBACK_LTS)) : SolverControl.StepType.FWDBACK);
		bool turbulenceEnabled = plan.UseDiffusion && (plan.BaseEddyViscosity != 0.0 || plan.LongMixCoeff != 0.0 || plan.TransMixCoeff != 0.0 || plan.HorizMixCoeff != 0.0);
		features = new RunTimeFeatures
		{
			EquationSet = equationSet,
			TimeStepping = timeStepping,
			StepType = stepType,
			MatrixSolver = SolverKind.Pardiso,
			AdvectionEnabled = (equationSet == SolverControl.EquationSet.SWE && plan.UseAdvection),
			TurbulenceEnabled = turbulenceEnabled,
			VariableDt = (!timeSteppingSolverControlTerm || plan.ImplicitAdaptiveTimestepping),
			CellCount = cellCount,
			Cores = Math.Max(1, (plan.CoreCount > 0) ? plan.CoreCount : Plan.DefaultCoreCount),
			DtSeconds = computeBaseTimestepSeconds,
			SimDurationSeconds = computeDurationSeconds,
			OutputIntervalSeconds = plan.BaseOutputInterval.TotalSeconds
		};
		return true;
	}
}
