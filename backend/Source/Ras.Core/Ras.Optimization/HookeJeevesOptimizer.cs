using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class HookeJeevesOptimizer : IOptimizer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("HookeJeevesOptimizer");

	private readonly double _initialStepFraction;

	private readonly double _stepReduction;

	public string Name => "Hooke-Jeeves";

	public HookeJeevesOptimizer(double initialStepFraction = 0.25, double stepReduction = 0.5)
	{
		if (initialStepFraction <= 0.0)
		{
			throw new ArgumentException("Initial step fraction must be positive.", "initialStepFraction");
		}
		if (stepReduction <= 0.0 || stepReduction >= 1.0)
		{
			throw new ArgumentException("Step reduction must be in (0,1).", "stepReduction");
		}
		_initialStepFraction = initialStepFraction;
		_stepReduction = stepReduction;
	}

	public async Task<OptimizationOutcome> MinimizeAsync(IBoundsHandler bounds, double[] initialPhysical, Func<double[][], Task<double[]>> evaluateBatchAsync, StoppingCriteria stopping, ProgressReporter pr = null)
	{
		if (bounds == null)
		{
			throw new ArgumentNullException("bounds");
		}
		if (initialPhysical == null)
		{
			throw new ArgumentNullException("initialPhysical");
		}
		if (evaluateBatchAsync == null)
		{
			throw new ArgumentNullException("evaluateBatchAsync");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OptimizerLoop loop = new OptimizerLoop(stopping);
		SearchSpaceEvaluator state = new SearchSpaceEvaluator(bounds, evaluateBatchAsync);
		int dim = bounds.Dimension;
		double[] scale = bounds.SearchScale();
		double[] step = new double[dim];
		for (int i = 0; i < dim; i++)
		{
			step[i] = scale[i] * _initialStepFraction;
		}
		double[] basePoint = state.Canonical(bounds.ToSearch(initialPhysical));
		double baseObjective = await state.EvaluateAsync(basePoint);
		double[] bestPoint = (double[])basePoint.Clone();
		double bestObjective = baseObjective;
		loop.RecordIteration(bestObjective);
		int iteration = 0;
		StopReason reason = StopReason.Exhausted;
		bool condition = false;
		for (int j = 0; j < dim; j++)
		{
			condition |= step[j] > 0.0;
		}
		if (!condition)
		{
			return OptimizerLoop.BuildOutcome(bounds.ToPhysical(bestPoint), bestObjective, state.Evaluations, 0, StopReason.Exhausted);
		}
		while (!loop.ShouldStop(state.Evaluations, pr, out reason))
		{
			(double[], double) tuple = await ExploreAsync(state, basePoint, baseObjective, step, loop, pr);
			double[] basePointArray = tuple.Item1;
			double baseObjective2 = tuple.Item2;
			bool improved = baseObjective2 < baseObjective;
			if (improved)
			{
				StopReason reason2;
				while (baseObjective2 < baseObjective && !loop.ShouldStop(state.Evaluations, pr, out reason2))
				{
					double[] b = Subtract(basePointArray, basePoint);
					basePoint = basePointArray;
					baseObjective = baseObjective2;
					double[] patternPoint = state.Canonical(Add(basePoint, b));
					(basePointArray, baseObjective2) = await ExploreAsync(state, patternPoint, await state.EvaluateAsync(patternPoint), step, loop, pr);
				}
			}
			else
			{
				for (int k = 0; k < dim; k++)
				{
					step[k] *= _stepReduction;
				}
			}
			if (baseObjective < bestObjective)
			{
				bestObjective = baseObjective;
				bestPoint = (double[])basePoint.Clone();
			}
			iteration++;
			loop.RecordIteration(baseObjective);
			ReportIteration(pr, stopping, iteration, state.Evaluations, step, scale, basePoint, baseObjective, bestObjective, improved, bounds);
			if (CheckStepTolerance(step, scale, stopping, out reason) || loop.ObjectiveConverged(baseObjective, out reason))
			{
				break;
			}
		}
		_logger.LogDebug("Hooke-Jeeves stopped: {Reason} after {Evals} evals, {Iters} iters, best {Best}.", reason, state.Evaluations, iteration, bestObjective);
		return OptimizerLoop.BuildOutcome(bounds.ToPhysical(bestPoint), bestObjective, state.Evaluations, iteration, reason);
	}

	private static async Task<(double[] point, double objective)> ExploreAsync(SearchSpaceEvaluator state, double[] start, double startObjective, double[] step, OptimizerLoop loop, ProgressReporter pr)
	{
		double[] current = (double[])start.Clone();
		double currentObjective = startObjective;
		for (int i = 0; i < current.Length; i++)
		{
			if (!(step[i] <= 0.0))
			{
				if (loop.ShouldStop(state.Evaluations, pr, out var _))
				{
					break;
				}
				double[] plus = (double[])current.Clone();
				plus[i] += step[i];
				plus = state.Canonical(plus);
				double[] minus = (double[])current.Clone();
				minus[i] -= step[i];
				minus = state.Canonical(minus);
				double[] batchAsync = await state.EvaluateBatchAsync(new double[2][] { plus, minus });
				double batchAsync2 = batchAsync[0];
				double batchAsync3 = batchAsync[1];
				if (batchAsync2 <= batchAsync3 && batchAsync2 < currentObjective)
				{
					current = plus;
					currentObjective = batchAsync2;
				}
				else if (batchAsync3 < currentObjective)
				{
					current = minus;
					currentObjective = batchAsync3;
				}
			}
		}
		return (point: current, objective: currentObjective);
	}

	private static bool CheckStepTolerance(double[] step, double[] scale, StoppingCriteria stopping, out StopReason reason)
	{
		reason = StopReason.None;
		if (!stopping.StepTolerance.HasValue)
		{
			return false;
		}
		double stepScaleTerm = 0.0;
		for (int i = 0; i < step.Length; i++)
		{
			if (scale[i] > 0.0)
			{
				stepScaleTerm = Math.Max(stepScaleTerm, step[i] / scale[i]);
			}
		}
		if (stepScaleTerm < stopping.StepTolerance.Value)
		{
			reason = StopReason.StepTolerance;
			return true;
		}
		return false;
	}

	private static void ReportIteration(ProgressReporter pr, StoppingCriteria stopping, int iteration, int evaluations, double[] step, double[] scale, double[] basePoint, double baseObjective, double bestObjective, bool improved, IBoundsHandler bounds)
	{
		if (stopping.MaxEvaluations.HasValue && stopping.MaxEvaluations.Value > 0)
		{
			pr.ReportProgressFraction(evaluations, stopping.MaxEvaluations.Value);
		}
		double stepScaleTerm = 0.0;
		for (int i = 0; i < step.Length; i++)
		{
			if (scale[i] > 0.0)
			{
				stepScaleTerm = Math.Max(stepScaleTerm, step[i] / scale[i]);
			}
		}
		string improvedTerm = (improved ? "improved" : "contracted");
		string vector = OptimizerLoop.FormatVector(bounds.ToPhysical(basePoint));
		string msg = $"  HJ iter {iteration} [{improvedTerm}]: step={stepScaleTerm:F4} obj={baseObjective:G6} best={bestObjective:G6} evals={evaluations} params=[{vector}]";
		pr.ReportMessage(msg, LogLevel.Information);
	}

	private static double[] Subtract(double[] a, double[] b)
	{
		double[] doubleValues = new double[a.Length];
		for (int i = 0; i < a.Length; i++)
		{
			doubleValues[i] = a[i] - b[i];
		}
		return doubleValues;
	}

	private static double[] Add(double[] a, double[] b)
	{
		double[] doubleValues = new double[a.Length];
		for (int i = 0; i < a.Length; i++)
		{
			doubleValues[i] = a[i] + b[i];
		}
		return doubleValues;
	}
}
