using System;
using System.Linq;

namespace Ras.Optimization;

public sealed class JournalFingerprint
{
	public int Schema { get; init; } = 1;

	public string[] ParameterNames { get; init; }

	public double[] Min { get; init; }

	public double[] Max { get; init; }

	public ParameterScale[] Scales { get; init; }

	public double[] Initial { get; init; }

	public int RngSeed { get; init; }

	public OptimizationMethod Method { get; init; }

	public BoundsMode BoundsMode { get; init; }

	public int LhsSampleCount { get; init; }

	public int LhsStartCount { get; init; }

	public double InitialStepFraction { get; init; }

	public double? StepTolerance { get; init; }

	public int? MaxEvaluations { get; init; }

	public double? ObjectiveTolerance { get; init; }

	public int ObjectiveWindow { get; init; }

	public double? MaxWallClockSeconds { get; init; }

	public static JournalFingerprint Create(ParameterSpace space, CalibrationControl control)
	{
		if (space == null)
		{
			throw new ArgumentNullException("space");
		}
		if (control == null)
		{
			throw new ArgumentNullException("control");
		}
		return new JournalFingerprint
		{
			ParameterNames = space.Parameters.Select((ICalibrationParameter p) => p.Name).ToArray(),
			Min = (double[])space.Min.Clone(),
			Max = (double[])space.Max.Clone(),
			Scales = (ParameterScale[])space.Scales.Clone(),
			Initial = (double[])space.Initial.Clone(),
			RngSeed = control.RngSeed,
			Method = control.Method,
			BoundsMode = control.BoundsMode,
			LhsSampleCount = control.LhsSampleCount,
			LhsStartCount = control.LhsStartCount,
			InitialStepFraction = control.InitialStepFraction,
			StepTolerance = control.Stopping.StepTolerance,
			MaxEvaluations = control.Stopping.MaxEvaluations,
			ObjectiveTolerance = control.Stopping.ObjectiveTolerance,
			ObjectiveWindow = control.Stopping.ObjectiveWindow,
			MaxWallClockSeconds = control.Stopping.MaxWallClock?.TotalSeconds
		};
	}

	public bool ParameterSpaceMatches(JournalFingerprint other)
	{
		if (other == null)
		{
			return false;
		}
		if (SequenceEquals(ParameterNames, other.ParameterNames) && SequenceEquals(Min, other.Min) && SequenceEquals(Max, other.Max))
		{
			return SequenceEquals(Scales, other.Scales);
		}
		return false;
	}

	public bool ReplayConfigMatches(JournalFingerprint other)
	{
		if (other == null)
		{
			return false;
		}
		if (SequenceEquals(Initial, other.Initial) && RngSeed == other.RngSeed && Method == other.Method && BoundsMode == other.BoundsMode && LhsSampleCount == other.LhsSampleCount && LhsStartCount == other.LhsStartCount)
		{
			return InitialStepFraction == other.InitialStepFraction;
		}
		return false;
	}

	private static bool SequenceEquals<T>(T[] a, T[] b)
	{
		if (a == null || b == null)
		{
			return a == b;
		}
		return a.SequenceEqual(b, null);
	}
}
