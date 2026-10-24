using System;
using System.Threading.Tasks;

namespace Ras.Optimization;

internal sealed class SearchSpaceEvaluator
{
	private readonly IBoundsHandler _bounds;

	private readonly Func<double[][], Task<double[]>> _evaluate;

	public int Evaluations { get; private set; }

	public SearchSpaceEvaluator(IBoundsHandler bounds, Func<double[][], Task<double[]>> evaluate)
	{
		_bounds = bounds;
		_evaluate = evaluate;
	}

	public double[] Canonical(double[] searchPoint)
	{
		return _bounds.ToSearch(_bounds.ToPhysical(searchPoint));
	}

	public async Task<double> EvaluateAsync(double[] searchPoint)
	{
		return (await EvaluateBatchAsync(new double[1][] { searchPoint }))[0];
	}

	public async Task<double[]> EvaluateBatchAsync(double[][] searchPoints)
	{
		double[][] doubleValues = new double[searchPoints.Length][];
		for (int i = 0; i < searchPoints.Length; i++)
		{
			doubleValues[i] = _bounds.ToPhysical(searchPoints[i]);
		}
		double[] evaluate = await _evaluate(doubleValues);
		if (evaluate == null || evaluate.Length != searchPoints.Length)
		{
			throw new InvalidOperationException("Objective evaluation returned the wrong number of values.");
		}
		Evaluations += searchPoints.Length;
		return evaluate;
	}
}
