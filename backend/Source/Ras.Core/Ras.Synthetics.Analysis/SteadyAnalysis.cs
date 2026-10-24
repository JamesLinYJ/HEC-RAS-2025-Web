using System;
using System.Collections.Generic;
using Ras.Engine;

namespace Ras.Synthetics.Analysis;

public static class SteadyAnalysis
{
	public static SteadyResult EvaluateSeries(IReadOnlyList<(double Time, double Value)> series, SteadyConfig cfg)
	{
		List<(double, double)> cleanAndSort = CleanAndSort(series);
		int count = cleanAndSort.Count;
		if (count == 0)
		{
			return SteadyResult.None();
		}
		double scale = ((cfg.Scale > 0.0) ? cfg.Scale : Span(cleanAndSort));
		double bandTol = cfg.BandTol(scale);
		double driftTol = cfg.DriftTol(scale);
		double window = cfg.Window;
		double cleanAndSortTerm = cleanAndSort[0].Item1;
		double naN = double.NaN;
		bool condition = false;
		double finalBand = double.NaN;
		double finalDrift = double.NaN;
		double naN2 = double.NaN;
		int i = 0;
		for (int j = 0; j < count; j++)
		{
			double cleanAndSortTerm2;
			for (cleanAndSortTerm2 = cleanAndSort[j].Item1; i < j && cleanAndSort[i].Item1 < cleanAndSortTerm2 - window; i++)
			{
			}
			bool window2 = EvaluateWindow(cleanAndSort, i, j, cleanAndSortTerm2, window, bandTol, driftTol, cfg.MinSamplesPerWindow, out var band, out var drift, out var mean);
			bool windowTerm = cleanAndSortTerm <= cleanAndSortTerm2 - window;
			if (j == count - 1)
			{
				finalBand = band;
				finalDrift = drift;
				naN2 = mean;
			}
			if (!windowTerm)
			{
				continue;
			}
			condition = true;
			if (window2)
			{
				if (double.IsNaN(naN))
				{
					naN = cleanAndSortTerm2 - window;
				}
			}
			else
			{
				naN = double.NaN;
			}
		}
		bool notIsNaN = !double.IsNaN(naN);
		return new SteadyResult(notIsNaN ? SteadyState.True : (condition ? SteadyState.False : SteadyState.Unknown), steadyValue: notIsNaN ? naN2 : double.NaN, reachedSteady: notIsNaN, onsetTime: notIsNaN ? naN : double.NaN, finalBand: finalBand, finalDrift: finalDrift);
	}

	public static bool TryFindSteadyOnset(IReadOnlyList<(double Time, double Value)> series, SteadyConfig cfg, out double onsetTime)
	{
		SteadyResult steadyResult = EvaluateSeries(series, cfg);
		onsetTime = steadyResult.OnsetTime;
		return steadyResult.ReachedSteady;
	}

	public static CellSteadyResult EvaluateCell(IReadOnlyList<(double Time, double Value)> depth, IReadOnlyList<(double Time, double Value)> velocity, SteadyConfig depthCfg, SteadyConfig velCfg)
	{
		SteadyResult depth2 = EvaluateSeries(depth, depthCfg);
		SteadyResult velocity2 = EvaluateSeries(velocity, velCfg);
		if (TailMax(depth, depthCfg.Window) < depthCfg.DryDepth)
		{
			return new CellSteadyResult(SteadyState.Dry, double.NaN, depth2, velocity2);
		}
		if (depth2.ReachedSteady && velocity2.ReachedSteady)
		{
			double onsetTime = Math.Max(depth2.OnsetTime, velocity2.OnsetTime);
			return new CellSteadyResult(SteadyState.True, onsetTime, depth2, velocity2);
		}
		return new CellSteadyResult(SteadyState.False, double.NaN, depth2, velocity2);
	}

	public static SteadyMonitor Monitor(Solver solver, Func<MeshTimestep, double> extract, SteadyConfig cfg)
	{
		SteadyMonitor monitor = new SteadyMonitor(cfg);
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			monitor.Add(mt.Time, extract(mt));
		});
		return monitor;
	}

	public static SteadyMonitor Monitor(ComputeDriver driver, Func<MeshTimestep, double> extract, SteadyConfig cfg)
	{
		SteadyMonitor monitor = new SteadyMonitor(cfg);
		driver.TimestepDone = (Action<MeshTimestep>)Delegate.Combine(driver.TimestepDone, (Action<MeshTimestep>)((MeshTimestep mt) =>
		{
			monitor.Add(mt.Time, extract(mt));
		}));
		return monitor;
	}

	public static CellSteadyMonitor MonitorCell(Solver solver, int cellIdx, SteadyConfig depthCfg, SteadyConfig velCfg)
	{
		double minElev = solver.PropertyTables.CellMinElevation(cellIdx);
		CellSteadyMonitor monitor = new CellSteadyMonitor(depthCfg, velCfg);
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			double depth = Math.Max(0.0, mt.WaterState.CellElevation[cellIdx] - minElev);
			double length = mt.WaterState.CellVelocity[cellIdx].Length;
			monitor.Add(mt.Time, depth, length);
		});
		return monitor;
	}

	private static bool EvaluateWindow(IReadOnlyList<(double Time, double Value)> pts, int lo, int hi, double tEnd, double window, double bandTol, double driftTol, int minSamples, out double band, out double drift, out double mean)
	{
		double tEndWindowTerm = tEnd - window / 2.0;
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double computed = 0.0;
		double computed2 = 0.0;
		double computed3 = 0.0;
		int ordinal = 0;
		int ordinal2 = 0;
		int ordinal3 = 0;
		for (int i = lo; i <= hi; i++)
		{
			double ptsTerm = pts[i].Value;
			if (ptsTerm < positiveInfinity)
			{
				positiveInfinity = ptsTerm;
			}
			if (ptsTerm > negativeInfinity)
			{
				negativeInfinity = ptsTerm;
			}
			computed += ptsTerm;
			ordinal++;
			if (pts[i].Time <= tEndWindowTerm)
			{
				computed2 += ptsTerm;
				ordinal2++;
			}
			else
			{
				computed3 += ptsTerm;
				ordinal3++;
			}
		}
		if (ordinal == 0)
		{
			band = double.NaN;
			drift = double.NaN;
			mean = double.NaN;
			return false;
		}
		band = negativeInfinity - positiveInfinity;
		mean = computed / (double)ordinal;
		bool condition = ordinal2 > 0 && ordinal3 > 0;
		drift = (condition ? Math.Abs(computed3 / (double)ordinal3 - computed2 / (double)ordinal2) : double.NaN);
		if (ordinal < minSamples)
		{
			return false;
		}
		if (!condition)
		{
			return false;
		}
		if (band < bandTol)
		{
			return drift < driftTol;
		}
		return false;
	}

	private static List<(double Time, double Value)> CleanAndSort(IReadOnlyList<(double Time, double Value)> series)
	{
		List<(double, double)> valueTuples = new List<(double, double)>(series.Count);
		foreach (var sery in series)
		{
			if (double.IsFinite(sery.Time) && double.IsFinite(sery.Value))
			{
				valueTuples.Add(sery);
			}
		}
		valueTuples.Sort(((double Time, double Value) a, (double Time, double Value) b) => a.Time.CompareTo(b.Time));
		return valueTuples;
	}

	private static double Span(List<(double Time, double Value)> pts)
	{
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		foreach (var pt in pts)
		{
			if (pt.Value < positiveInfinity)
			{
				positiveInfinity = pt.Value;
			}
			if (pt.Value > negativeInfinity)
			{
				negativeInfinity = pt.Value;
			}
		}
		return negativeInfinity - positiveInfinity;
	}

	private static double TailMax(IReadOnlyList<(double Time, double Value)> series, double window)
	{
		if (series.Count == 0)
		{
			return double.NegativeInfinity;
		}
		double negativeInfinity = double.NegativeInfinity;
		foreach (var sery in series)
		{
			if (double.IsFinite(sery.Time) && sery.Time > negativeInfinity)
			{
				(negativeInfinity, _) = sery;
			}
		}
		double negativeInfinity2 = double.NegativeInfinity;
		foreach (var sery2 in series)
		{
			if (double.IsFinite(sery2.Time) && double.IsFinite(sery2.Value) && !(negativeInfinity - sery2.Time > window) && sery2.Value > negativeInfinity2)
			{
				negativeInfinity2 = sery2.Value;
			}
		}
		return negativeInfinity2;
	}
}
