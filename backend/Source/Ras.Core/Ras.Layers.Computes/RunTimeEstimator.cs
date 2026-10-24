using System;
using Ras.Engine;

namespace Ras.Layers.Computes;

public static class RunTimeEstimator
{
	public const string FamilyExplicitSWE = "SWE-Explicit";

	public const string FamilyImplicitSWE = "SWE-Implicit";

	public const string FamilyDWE = "DWE";

	private const double FallbackWideningFactor = 2.0;

	public static string MakeFamily(RunTimeFeatures features)
	{
		if (features.EquationSet == SolverControl.EquationSet.DWE)
		{
			return "DWE";
		}
		if (features.TimeStepping != SolverControl.TimeStepping.Implicit)
		{
			return "SWE-Explicit";
		}
		return "SWE-Implicit";
	}

	public static string MakeSchemeKey(RunTimeFeatures features)
	{
		if (features.EquationSet != SolverControl.EquationSet.DWE)
		{
			if (features.TimeStepping != SolverControl.TimeStepping.Implicit)
			{
				return $"SWE-Exp-{features.StepType}";
			}
			return $"SWE-Imp/{features.MatrixSolver}";
		}
		return $"DWE/{features.MatrixSolver}";
	}

	public static bool TryEstimate(RunTimeFeatures features, RunTimeModelCoefficients coefficients, out RunTimeEstimate estimate)
	{
		estimate = default;
		if (features == null || coefficients == null)
		{
			return false;
		}
		if (features.CellCount <= 0 || features.DtSeconds <= 0.0 || features.SimDurationSeconds <= 0.0)
		{
			return false;
		}
		string key = MakeFamily(features);
		if (!TryResolveStepCostC(features, coefficients, out var c, out var usedFallback))
		{
			return false;
		}
		if (!coefficients.AlphaByFamily.TryGetValue(key, out var localValue))
		{
			return false;
		}
		double cellCount = features.CellCount;
		int cores = Math.Max(1, features.Cores);
		double cellCountBetaTerm = c * Math.Pow(cellCount, localValue) * Math.Pow(cores, coefficients.Beta);
		if (features.AdvectionEnabled)
		{
			cellCountBetaTerm *= coefficients.AdvectionMultiplier;
		}
		if (features.TurbulenceEnabled)
		{
			cellCountBetaTerm *= coefficients.TurbulenceMultiplier;
		}
		long predictedStepCount = (long)Math.Ceiling(features.SimDurationSeconds / features.DtSeconds);
		double computed = 0.0;
		if (coefficients.InitBaseSeconds.TryGetValue(key, out var localValue2))
		{
			computed += localValue2;
		}
		if (coefficients.InitPerCellSeconds.TryGetValue(key, out var localValue3))
		{
			computed += localValue3 * cellCount;
		}
		double simDurationSecondsFeaturesTerm = 0.0;
		if (features.OutputIntervalSeconds > 0.0 && coefficients.OutputPerCellSeconds > 0.0)
		{
			simDurationSecondsFeaturesTerm = (Math.Ceiling(features.SimDurationSeconds / features.OutputIntervalSeconds) + 1.0) * coefficients.OutputPerCellSeconds * cellCount;
		}
		double predictedStepCountTerm = (double)predictedStepCount * cellCountBetaTerm;
		double totalSeconds = coefficients.MachineFactor * (computed + predictedStepCountTerm + simDurationSecondsFeaturesTerm);
		if (!double.IsFinite(totalSeconds) || totalSeconds <= 0.0)
		{
			return false;
		}
		double computed2 = 1.5;
		if (coefficients.LogErrorFactorByFamily.TryGetValue(key, out var localValue4) && localValue4 > 1.0)
		{
			computed2 = localValue4;
		}
		if (usedFallback)
		{
			computed2 *= 2.0;
		}
		double computed3 = computed2;
		if (features.VariableDt)
		{
			computed3 *= Math.Max(1.0, coefficients.AdaptiveDtHighFactor);
		}
		estimate = new RunTimeEstimate
		{
			TotalSeconds = totalSeconds,
			ComputationalSpeed = features.SimDurationSeconds / totalSeconds,
			LowSeconds = totalSeconds / computed2,
			HighSeconds = totalSeconds * computed3,
			InitSeconds = coefficients.MachineFactor * computed,
			StepSeconds = coefficients.MachineFactor * predictedStepCountTerm,
			OutputSeconds = coefficients.MachineFactor * simDurationSecondsFeaturesTerm,
			PredictedStepCount = predictedStepCount,
			UsedFallbackCoefficients = usedFallback
		};
		return true;
	}

	public static bool TryComputeMachineFactor(double measuredTotalSeconds, RunTimeEstimate predicted, out double machineFactor)
	{
		machineFactor = 1.0;
		if (measuredTotalSeconds <= 0.0 || !double.IsFinite(measuredTotalSeconds))
		{
			return false;
		}
		if (predicted.TotalSeconds <= 0.0 || !double.IsFinite(predicted.TotalSeconds))
		{
			return false;
		}
		machineFactor = measuredTotalSeconds / predicted.TotalSeconds;
		return true;
	}

	private static bool TryResolveStepCostC(RunTimeFeatures features, RunTimeModelCoefficients coefficients, out double c, out bool usedFallback)
	{
		c = 0.0;
		usedFallback = false;
		if (coefficients.StepCostC == null || coefficients.StepCostC.Count == 0)
		{
			return false;
		}
		string schemeKey = MakeSchemeKey(features);
		if (coefficients.StepCostC.TryGetValue(schemeKey, out c))
		{
			return true;
		}
		usedFallback = true;
		if (schemeKey.EndsWith("_LTS", StringComparison.Ordinal))
		{
			string key = schemeKey.Substring(0, schemeKey.Length - 4);
			if (coefficients.StepCostC.TryGetValue(key, out c))
			{
				return true;
			}
		}
		int indexOf = schemeKey.IndexOf('/');
		if (indexOf >= 0)
		{
			string key2 = schemeKey.Substring(0, indexOf) + "/Pardiso";
			if (coefficients.StepCostC.TryGetValue(key2, out c))
			{
				return true;
			}
		}
		string family = MakeFamily(features);
		string textValue;
		if (family == "DWE")
		{
			textValue = "DWE/Pardiso";
		}
		else
		{
			textValue = ((!(family == "SWE-Implicit")) ? $"SWE-Exp-{SolverControl.StepType.FWDBACKFWD}" : "SWE-Imp/Pardiso");
		}
		string key3 = textValue;
		return coefficients.StepCostC.TryGetValue(key3, out c);
	}
}
