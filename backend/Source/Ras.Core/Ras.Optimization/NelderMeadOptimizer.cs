using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class NelderMeadOptimizer : IOptimizer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("NelderMeadOptimizer");

	private readonly double _initialStepFraction;

	private readonly double _reflection;

	private readonly double _expansion;

	private readonly double _contraction;

	private readonly double _shrink;

	public string Name => "Nelder-Mead";

	public NelderMeadOptimizer(double initialStepFraction = 0.25, double reflection = 1.0, double expansion = 2.0, double contraction = 0.5, double shrink = 0.5)
	{
		if (initialStepFraction <= 0.0)
		{
			throw new ArgumentException("Initial step fraction must be positive.", "initialStepFraction");
		}
		if (reflection <= 0.0)
		{
			throw new ArgumentException("Reflection coefficient must be positive.", "reflection");
		}
		if (expansion <= 1.0)
		{
			throw new ArgumentException("Expansion coefficient must be greater than 1.", "expansion");
		}
		if (contraction <= 0.0 || contraction >= 1.0)
		{
			throw new ArgumentException("Contraction coefficient must be in (0,1).", "contraction");
		}
		if (shrink <= 0.0 || shrink >= 1.0)
		{
			throw new ArgumentException("Shrink coefficient must be in (0,1).", "shrink");
		}
		_initialStepFraction = initialStepFraction;
		_reflection = reflection;
		_expansion = expansion;
		_contraction = contraction;
		_shrink = shrink;
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
		double[] canonical = state.Canonical(bounds.ToSearch(initialPhysical));
		int vertexCount = dim + 1;
		double[][] simplex = new double[vertexCount][];
		simplex[0] = canonical;
		for (int i = 0; i < dim; i++)
		{
			double[] doubleValues = (double[])canonical.Clone();
			doubleValues[i] += scale[i] * _initialStepFraction;
			simplex[i + 1] = state.Canonical(doubleValues);
		}
		double[] objectives = await state.EvaluateBatchAsync(simplex);
		SortSimplex(simplex, objectives);
		loop.RecordIteration(objectives[0]);
		int iteration = 0;
		StopReason reason = StopReason.Exhausted;
		bool condition = false;
		for (int j = 0; j < dim; j++)
		{
			condition |= scale[j] * _initialStepFraction > 0.0;
		}
		if (!condition)
		{
			return OptimizerLoop.BuildOutcome(bounds.ToPhysical(simplex[0]), objectives[0], state.Evaluations, 0, StopReason.Exhausted);
		}
		while (!loop.ShouldStop(state.Evaluations, pr, out reason))
		{
			int worst = vertexCount - 1;
			int secondWorst = vertexCount - 2;
			double bestObjective = objectives[0];
			double[] centroid = Centroid(simplex, worst);
			double[] reflected = state.Canonical(Combine(centroid, simplex[worst], _reflection));
			double reflectedObjective = await state.EvaluateAsync(reflected);
			string move;
			if (reflectedObjective < bestObjective)
			{
				if (loop.ShouldStop(state.Evaluations, pr, out reason))
				{
					simplex[worst] = reflected;
					objectives[worst] = reflectedObjective;
					SortSimplex(simplex, objectives);
					break;
				}
				double[] expanded = state.Canonical(Combine(centroid, simplex[worst], _reflection * _expansion));
				double objectives2 = await state.EvaluateAsync(expanded);
				if (objectives2 < reflectedObjective)
				{
					simplex[worst] = expanded;
					objectives[worst] = objectives2;
					move = "expand";
				}
				else
				{
					simplex[worst] = reflected;
					objectives[worst] = reflectedObjective;
					move = "reflect";
				}
			}
			else if (reflectedObjective < objectives[secondWorst])
			{
				simplex[worst] = reflected;
				objectives[worst] = reflectedObjective;
				move = "reflect";
			}
			else
			{
				if (loop.ShouldStop(state.Evaluations, pr, out reason))
				{
					break;
				}
				bool outside = reflectedObjective < objectives[worst];
				double coefficient = (outside ? (_reflection * _contraction) : (0.0 - _contraction));
				double[] expanded = state.Canonical(Combine(centroid, simplex[worst], coefficient));
				double objectives3 = await state.EvaluateAsync(expanded);
				double reflectedObjective2 = (outside ? reflectedObjective : objectives[worst]);
				if (objectives3 < reflectedObjective2)
				{
					simplex[worst] = expanded;
					objectives[worst] = objectives3;
					move = "contract";
				}
				else
				{
					if (loop.ShouldStop(state.Evaluations, pr, out reason))
					{
						break;
					}
					await ShrinkAsync(state, simplex, objectives);
					move = "shrink";
				}
			}
			SortSimplex(simplex, objectives);
			iteration++;
			loop.RecordIteration(objectives[0]);
			double relativeSimplexSize = RelativeSimplexSize(simplex, scale);
			ReportIteration(pr, stopping, iteration, state.Evaluations, relativeSimplexSize, objectives, move, bounds, simplex);
			if (stopping.StepTolerance.HasValue && relativeSimplexSize < stopping.StepTolerance.Value)
			{
				reason = StopReason.StepTolerance;
				break;
			}
			if (loop.ObjectiveConverged(objectives[0], out reason))
			{
				break;
			}
		}
		_logger.LogDebug("Nelder-Mead stopped: {Reason} after {Evals} evals, {Iters} iters, best {Best}.", reason, state.Evaluations, iteration, objectives[0]);
		return OptimizerLoop.BuildOutcome(bounds.ToPhysical(simplex[0]), objectives[0], state.Evaluations, iteration, reason);
	}

	private static double[] Centroid(double[][] simplex, int excluded)
	{
		int simplexLength = simplex[0].Length;
		double[] doubleValues = new double[simplexLength];
		for (int i = 0; i < simplex.Length; i++)
		{
			if (i != excluded)
			{
				for (int j = 0; j < simplexLength; j++)
				{
					doubleValues[j] += simplex[i][j];
				}
			}
		}
		int lengthOffset = simplex.Length - 1;
		for (int k = 0; k < simplexLength; k++)
		{
			doubleValues[k] /= lengthOffset;
		}
		return doubleValues;
	}

	private static double[] Combine(double[] centroid, double[] point, double coefficient)
	{
		double[] doubleValues = new double[centroid.Length];
		for (int i = 0; i < centroid.Length; i++)
		{
			doubleValues[i] = centroid[i] + coefficient * (centroid[i] - point[i]);
		}
		return doubleValues;
	}

	private async Task ShrinkAsync(SearchSpaceEvaluator state, double[][] simplex, double[] objectives)
	{
		double[] simplexArray = simplex[0];
		int simplexLength = simplexArray.Length;
		double[][] shrunk = new double[simplex.Length - 1][];
		for (int i = 1; i < simplex.Length; i++)
		{
			double[] doubleValues = new double[simplexLength];
			for (int j = 0; j < simplexLength; j++)
			{
				doubleValues[j] = simplexArray[j] + _shrink * (simplex[i][j] - simplexArray[j]);
			}
			shrunk[i - 1] = state.Canonical(doubleValues);
		}
		double[] batchAsync = await state.EvaluateBatchAsync(shrunk);
		for (int k = 1; k < simplex.Length; k++)
		{
			simplex[k] = shrunk[k - 1];
			objectives[k] = batchAsync[k - 1];
		}
	}

	private static void SortSimplex(double[][] simplex, double[] objectives)
	{
		int[] intValues = new int[objectives.Length];
		for (int i = 0; i < intValues.Length; i++)
		{
			intValues[i] = i;
		}
		Array.Sort(intValues, (int a, int b) => objectives[a].CompareTo(objectives[b]));
		double[][] simplexArray = new double[simplex.Length][];
		double[] objectivesArray = new double[objectives.Length];
		for (int intervalIndex = 0; intervalIndex < intValues.Length; intervalIndex++)
		{
			simplexArray[intervalIndex] = simplex[intValues[intervalIndex]];
			objectivesArray[intervalIndex] = objectives[intValues[intervalIndex]];
		}
		Array.Copy(simplexArray, simplex, simplex.Length);
		Array.Copy(objectivesArray, objectives, objectives.Length);
	}

	private static double RelativeSimplexSize(double[][] simplex, double[] scale)
	{
		int simplexLength = simplex[0].Length;
		double simplex3Simplex2Term = 0.0;
		for (int i = 0; i < simplexLength; i++)
		{
			if (!(scale[i] <= 0.0))
			{
				double simplex2 = simplex[0][i];
				double simplex3 = simplex[0][i];
				for (int j = 1; j < simplex.Length; j++)
				{
					simplex2 = Math.Min(simplex2, simplex[j][i]);
					simplex3 = Math.Max(simplex3, simplex[j][i]);
				}
				simplex3Simplex2Term = Math.Max(simplex3Simplex2Term, (simplex3 - simplex2) / scale[i]);
			}
		}
		return simplex3Simplex2Term;
	}

	private static void ReportIteration(ProgressReporter pr, StoppingCriteria stopping, int iteration, int evaluations, double relSize, double[] objectives, string move, IBoundsHandler bounds, double[][] simplex)
	{
		if (stopping.MaxEvaluations.HasValue && stopping.MaxEvaluations.Value > 0)
		{
			pr.ReportProgressFraction(evaluations, stopping.MaxEvaluations.Value);
		}
		string vector = OptimizerLoop.FormatVector(bounds.ToPhysical(simplex[0]));
		string msg = $"  NM iter {iteration} [{move}]: size={relSize:F4} obj={objectives[0]:G6} worst={objectives[^1]:G6} evals={evaluations} params=[{vector}]";
		pr.ReportMessage(msg, LogLevel.Information);
	}
}
