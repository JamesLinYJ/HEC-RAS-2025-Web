using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Utility.Progress;

namespace Ras.Optimization;

internal sealed class OptimizerLoop
{
	private readonly StoppingCriteria _stopping;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly Queue<double> _recentBest = new Queue<double>();

	public OptimizerLoop(StoppingCriteria stopping)
	{
		if (stopping == null || !stopping.HasAny)
		{
			throw new ArgumentException("At least one stopping criterion must be enabled.", "stopping");
		}
		stopping.Validate();
		_stopping = stopping;
	}

	public bool ShouldStop(int evaluations, ProgressReporter pr, out StopReason reason)
	{
		if (pr.CancelRequested)
		{
			reason = StopReason.Cancelled;
			return true;
		}
		if (_stopping.MaxEvaluations.HasValue && evaluations >= _stopping.MaxEvaluations.Value)
		{
			reason = StopReason.MaxEvaluations;
			return true;
		}
		if (_stopping.MaxWallClock.HasValue && _clock.Elapsed >= _stopping.MaxWallClock.Value)
		{
			reason = StopReason.MaxWallClock;
			return true;
		}
		reason = StopReason.None;
		return false;
	}

	public void RecordIteration(double currentBest)
	{
		_recentBest.Enqueue(currentBest);
		while (_recentBest.Count > _stopping.ObjectiveWindow + 1)
		{
			_recentBest.Dequeue();
		}
	}

	public bool ObjectiveConverged(double currentBest, out StopReason reason)
	{
		reason = StopReason.None;
		if (!_stopping.ObjectiveTolerance.HasValue || _recentBest.Count <= _stopping.ObjectiveWindow)
		{
			return false;
		}
		if (_recentBest.Peek() - currentBest < _stopping.ObjectiveTolerance.Value)
		{
			reason = StopReason.ObjectiveTolerance;
			return true;
		}
		return false;
	}

	public int RemainingBudget(int evaluations)
	{
		if (!_stopping.MaxEvaluations.HasValue)
		{
			return int.MaxValue;
		}
		return Math.Max(0, _stopping.MaxEvaluations.Value - evaluations);
	}

	public static string FormatVector(double[] v)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < v.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append(v[i].ToString("G5", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	public static OptimizationOutcome BuildOutcome(double[] bestPhysical, double bestObjective, int evaluations, int iterations, StopReason reason)
	{
		return new OptimizationOutcome
		{
			BestPhysical = bestPhysical,
			BestObjective = bestObjective,
			Evaluations = evaluations,
			Iterations = iterations,
			StopReason = reason
		};
	}
}
