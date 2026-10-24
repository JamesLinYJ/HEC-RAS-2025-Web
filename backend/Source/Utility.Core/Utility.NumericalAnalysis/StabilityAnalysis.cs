using System;
using System.Collections.Generic;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public static class StabilityAnalysis<T> where T : INumber<T>
{
	public static StabilityResult<T> EvaluateSeries(IReadOnlyList<(double Time, T Value)> pts, in StabilityConfig<T> cfg)
	{
		int count = pts.Count;
		if (count == 0)
		{
			return StabilityResult<T>.Unknown(double.NaN, T.Zero);
		}
		var (time, localValue) = pts[count - 1];
		if (count < cfg.MinSamples || count < 3)
		{
			return StabilityResult<T>.Unknown(time, localValue);
		}
		T scale = ((cfg.Scale > T.Zero) ? cfg.Scale : Span(pts));
		T deadband = cfg.Deadband(scale);
		int trailingSecondDifferenceReversals = TrailingSecondDifferenceReversals(pts, deadband);
		int trailingSecondDifferenceReversalsTerm = ((trailingSecondDifferenceReversals >= cfg.OscErrorSteps) ? 3 : ((trailingSecondDifferenceReversals < cfg.OscWarnSteps) ? 1 : 2));
		T ptsCountTerm = Abs(localValue - pts[count - 2].Value);
		T magWarnTol = cfg.MagWarnTol(scale);
		T magErrorTol = cfg.MagErrorTol(scale);
		StabilityState stabilityState = ((ptsCountTerm >= magErrorTol) ? StabilityState.Error : ((!(ptsCountTerm >= magWarnTol)) ? StabilityState.Stable : StabilityState.Warning));
		double trailingGrowthRatio = TrailingGrowthRatio(pts, cfg.GrowthFactor, deadband, out var growingCycles);
		StabilityState stabilityState2 = ((growingCycles >= cfg.GrowthCycles) ? StabilityState.Error : ((!(trailingGrowthRatio >= cfg.GrowthFactor)) ? StabilityState.Stable : StabilityState.Warning));
		StabilityState state = Max(Max((StabilityState)trailingSecondDifferenceReversalsTerm, stabilityState), stabilityState2);
		InstabilityKind instabilityKind = InstabilityKind.None;
		if (trailingSecondDifferenceReversalsTerm >= 2)
		{
			instabilityKind |= InstabilityKind.Oscillation;
		}
		if (stabilityState >= StabilityState.Warning)
		{
			instabilityKind |= InstabilityKind.Magnitude;
		}
		if (stabilityState2 >= StabilityState.Warning)
		{
			instabilityKind |= InstabilityKind.Divergence;
		}
		return new StabilityResult<T>(state, instabilityKind, time, localValue, trailingSecondDifferenceReversals, ptsCountTerm, trailingGrowthRatio);
	}

	public static int TrailingSecondDifferenceReversals(IReadOnlyList<(double Time, T Value)> pts, T deadband)
	{
		int count = pts.Count;
		int signOfSignOf2Term = 0;
		int signOf = 0;
		for (int i = 2; i < count; i++)
		{
			int signOf2 = SignOf(pts[i].Value - (T.One + T.One) * pts[i - 1].Value + pts[i - 2].Value, deadband);
			if (signOf2 == 0)
			{
				signOfSignOf2Term = 0;
				signOf = 0;
			}
			else
			{
				signOfSignOf2Term = ((signOf != 0 && signOf2 == -signOf) ? (signOfSignOf2Term + 1) : 0);
				signOf = signOf2;
			}
		}
		return signOfSignOf2Term;
	}

	public static double TrailingGrowthRatio(IReadOnlyList<(double Time, T Value)> pts, double growthFactor, T deadband, out int growingCycles)
	{
		growingCycles = 0;
		List<double> halfCycleSwings = HalfCycleSwings(pts, deadband);
		int count = halfCycleSwings.Count;
		if (count < 2)
		{
			return double.NaN;
		}
		double result = ((halfCycleSwings[count - 2] > 0.0) ? (halfCycleSwings[count - 1] / halfCycleSwings[count - 2]) : double.NaN);
		int countOffset = count - 1;
		while (countOffset >= 1 && halfCycleSwings[countOffset - 1] > 0.0 && halfCycleSwings[countOffset] / halfCycleSwings[countOffset - 1] >= growthFactor)
		{
			growingCycles++;
			countOffset--;
		}
		return result;
	}

	public static List<double> HalfCycleSwings(IReadOnlyList<(double Time, T Value)> pts, T deadband)
	{
		List<double> doubleList = new List<double>();
		int count = pts.Count;
		if (count < 3)
		{
			return doubleList;
		}
		double deadbandTerm = D(deadband);
		int index = 0;
		int signOf = 0;
		for (int i = 1; i < count; i++)
		{
			int signOf2 = SignOf(pts[i].Value - pts[i - 1].Value, deadband);
			if (signOf2 == 0)
			{
				continue;
			}
			if (signOf != 0 && signOf2 != signOf)
			{
				double ptsIndexTerm = Math.Abs(D(pts[i - 1].Value) - D(pts[index].Value));
				if (ptsIndexTerm >= deadbandTerm)
				{
					doubleList.Add(ptsIndexTerm);
				}
				index = i - 1;
			}
			signOf = signOf2;
		}
		double ptsCountTerm = Math.Abs(D(pts[count - 1].Value) - D(pts[index].Value));
		if (ptsCountTerm >= deadbandTerm)
		{
			doubleList.Add(ptsCountTerm);
		}
		return doubleList;
	}

	private static T Span(IReadOnlyList<(double Time, T Value)> pts)
	{
		bool condition = false;
		T zero = T.Zero;
		T zero2 = T.Zero;
		for (int i = 0; i < pts.Count; i++)
		{
			T ptsTerm = pts[i].Value;
			if (!T.IsFinite(ptsTerm))
			{
				continue;
			}
			if (!condition)
			{
				zero = ptsTerm;
				zero2 = ptsTerm;
				condition = true;
				continue;
			}
			if (ptsTerm < zero)
			{
				zero = ptsTerm;
			}
			if (ptsTerm > zero2)
			{
				zero2 = ptsTerm;
			}
		}
		if (!condition)
		{
			return T.Zero;
		}
		return zero2 - zero;
	}

	internal static int SignOf(T x)
	{
		if (!T.IsFinite(x))
		{
			return 0;
		}
		if (x > T.Zero)
		{
			return 1;
		}
		if (x < T.Zero)
		{
			return -1;
		}
		return 0;
	}

	internal static int SignOf(T x, T deadband)
	{
		if (!T.IsFinite(x) || T.Abs(x) < deadband)
		{
			return 0;
		}
		if (!(x > T.Zero))
		{
			return -1;
		}
		return 1;
	}

	internal static T Abs(T x)
	{
		return T.Abs(x);
	}

	internal static double D(T x)
	{
		return double.CreateChecked(x);
	}

	internal static StabilityState Max(StabilityState a, StabilityState b)
	{
		if (a < b)
		{
			return b;
		}
		return a;
	}
}
