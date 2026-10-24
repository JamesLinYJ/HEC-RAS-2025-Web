using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class RBFSurrogateOptimizer : IGlobalOptimizer, IOptimizer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RBFSurrogateOptimizer");

	private readonly int _batchSize;

	private readonly int _rngSeed;

	private readonly RBFSurrogateOptions _options;

	private double[] _min;

	private double[] _max;

	private ParameterScale[] _scales;

	private double[] _warpedMin;

	private double[] _warpedMax;

	private int[] _freeDims;

	private double[][] _seedPoints;

	private double[] _seedObjectives;

	public string Name => "RBF surrogate (DYCORS)";

	public RBFSurrogateOptimizer(int batchSize, int rngSeed, RBFSurrogateOptions options = null)
	{
		if (batchSize < 1)
		{
			throw new ArgumentException("Batch size must be positive.", "batchSize");
		}
		_batchSize = batchSize;
		_rngSeed = rngSeed;
		_options = options ?? new RBFSurrogateOptions();
		_options.Validate();
	}

	public void Seed(double[][] physicalPoints, double[] objectives)
	{
		if (physicalPoints == null || objectives == null)
		{
			throw new ArgumentNullException((physicalPoints == null) ? "physicalPoints" : "objectives");
		}
		if (physicalPoints.Length != objectives.Length)
		{
			throw new ArgumentException("Points and objectives must have the same length.");
		}
		_seedPoints = new double[physicalPoints.Length][];
		for (int i = 0; i < physicalPoints.Length; i++)
		{
			if (physicalPoints[i] == null)
			{
				throw new ArgumentException($"Seed point {i} is null.", "physicalPoints");
			}
			_seedPoints[i] = (double[])physicalPoints[i].Clone();
		}
		_seedObjectives = (double[])objectives.Clone();
	}

	private void ResolveGeometry(IBoundsHandler bounds)
	{
		_min = bounds.PhysicalMin;
		_max = bounds.PhysicalMax;
		_scales = bounds.Scales;
		_warpedMin = new double[_min.Length];
		_warpedMax = new double[_min.Length];
		for (int i = 0; i < _min.Length; i++)
		{
			_warpedMin[i] = ParameterScaling.Warp(_min[i], _scales[i]);
			_warpedMax[i] = ParameterScaling.Warp(_max[i], _scales[i]);
		}
		List<int> intList = new List<int>();
		for (int j = 0; j < _min.Length; j++)
		{
			if (_max[j] > _min[j])
			{
				intList.Add(j);
			}
		}
		_freeDims = intList.ToArray();
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
		if (initialPhysical.Length != bounds.Dimension)
		{
			throw new ArgumentException($"Initial point length {initialPhysical.Length} does not match the bounds dimension {bounds.Dimension}.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OptimizerLoop loop = new OptimizerLoop(stopping);
		ResolveGeometry(bounds);
		Random rng = new Random(_rngSeed);
		int dFree = _freeDims.Length;
		int evals = 0;
		double[] fallbackPhysical = ClampToBounds(initialPhysical);
		if (dFree == 0)
		{
			return OptimizerLoop.BuildOutcome(fallbackPhysical, (await evaluateBatchAsync(new double[1][] { fallbackPhysical }))[0], 1, 0, StopReason.Exhausted);
		}
		List<double[]> designU = new List<double[]>();
		List<double> designF = new List<double>();
		if (_seedPoints != null)
		{
			for (int i = 0; i < _seedPoints.Length; i++)
			{
				if (_seedPoints[i].Length != _min.Length)
				{
					throw new ArgumentException($"Seed point {i} has length {_seedPoints[i].Length}; expected {_min.Length}.");
				}
				double[] toUnit = ToUnit(_seedPoints[i]);
				if (MinDistance(toUnit, designU) >= 1E-09)
				{
					designU.Add(toUnit);
					designF.Add(_seedObjectives[i]);
				}
			}
		}
		double[] initialU = ToUnit(fallbackPhysical);
		if (MinDistance(initialU, designU) >= _options.DuplicateTolerance)
		{
			double[] evaluateBatchAsyncArray = await evaluateBatchAsync(new double[1][] { ToPhysical(initialU) });
			evals++;
			designU.Add(initialU);
			designF.Add(evaluateBatchAsyncArray[0]);
		}
		int evals2 = evals;
		evals = evals2 + await TopUpDesignAsync(designU, designF, evaluateBatchAsync, loop.RemainingBudget(evals));
		double bestF = double.PositiveInfinity;
		double[] bestU = initialU;
		for (int j = 0; j < designF.Count; j++)
		{
			if (designF[j] < bestF)
			{
				bestF = designF[j];
				bestU = designU[j];
			}
		}
		loop.RecordIteration(bestF);
		double sigma = _options.InitialSigma;
		int failuresToShrink = Math.Max(5, dFree);
		int consecutiveSuccesses = 0;
		int consecutiveFailures = 0;
		int weightIndex = 0;
		int iteration = 0;
		StopReason reason = StopReason.Exhausted;
		while (!loop.ShouldStop(evals, pr, out reason))
		{
			RBFSurrogateModel model = FitSurrogate(designU, designF);
			double[][] pool = GenerateCandidates(designU, bestU, sigma, model != null, rng, evals, stopping.MaxEvaluations);
			int batch = Math.Min(_batchSize, loop.RemainingBudget(evals));
			double[][] picksU = SelectCandidates(pool, designU, model, batch, ref weightIndex);
			if (picksU.Length == 0)
			{
				reason = StopReason.Exhausted;
				break;
			}
			double[][] doubleValues = new double[picksU.Length][];
			for (int k = 0; k < picksU.Length; k++)
			{
				doubleValues[k] = ToPhysical(picksU[k]);
			}
			double[] evaluateBatchAsync2 = await evaluateBatchAsync(doubleValues);
			evals += picksU.Length;
			double positiveInfinity = double.PositiveInfinity;
			bool condition = false;
			for (int l = 0; l < picksU.Length; l++)
			{
				designU.Add(picksU[l]);
				designF.Add(evaluateBatchAsync2[l]);
				positiveInfinity = Math.Min(positiveInfinity, evaluateBatchAsync2[l]);
				if (evaluateBatchAsync2[l] < bestF)
				{
					bestF = evaluateBatchAsync2[l];
					bestU = picksU[l];
					condition = true;
				}
			}
			if (condition)
			{
				consecutiveFailures = 0;
				consecutiveSuccesses++;
				if (consecutiveSuccesses >= _options.SuccessesToExpand)
				{
					sigma = Math.Min(sigma * 2.0, _options.MaxSigma);
					consecutiveSuccesses = 0;
				}
			}
			else
			{
				consecutiveSuccesses = 0;
				consecutiveFailures++;
				if (consecutiveFailures >= failuresToShrink)
				{
					sigma *= 0.5;
					consecutiveFailures = 0;
				}
			}
			iteration++;
			loop.RecordIteration(bestF);
			if (stopping.StepTolerance.HasValue && sigma < stopping.StepTolerance.Value)
			{
				reason = StopReason.StepTolerance;
				break;
			}
			if (sigma < _options.MinSigma)
			{
				reason = StopReason.Exhausted;
				break;
			}
			if (loop.ObjectiveConverged(bestF, out reason))
			{
				break;
			}
			ReportIteration(pr, stopping, iteration, evals, sigma, positiveInfinity, bestF, condition, model != null);
		}
		_logger.LogDebug("RBF surrogate stopped: {Reason} after {Evals} evals, {Iters} iters, best {Best}.", reason, evals, iteration, bestF);
		return OptimizerLoop.BuildOutcome(double.IsFinite(bestF) ? ToPhysical(bestU) : fallbackPhysical, bestF, evals, iteration, reason);
	}

	private async Task<int> TopUpDesignAsync(List<double[]> designU, List<double> designF, Func<double[][], Task<double[]>> evaluateBatchAsync, int remainingBudget)
	{
		int freeDimLength = _freeDims.Length;
		if (designU.Count >= freeDimLength + 2)
		{
			return 0;
		}
		int freeDimLengthBatchSizeTerm = Math.Min(Math.Max(2 * (freeDimLength + 1), _batchSize) - designU.Count, remainingBudget);
		if (freeDimLengthBatchSizeTerm <= 0)
		{
			return 0;
		}
		double[][] sampleUnit = new LatinHypercubeSampler(_rngSeed).SampleUnit(freeDimLengthBatchSizeTerm, freeDimLength);
		List<double[]> freshU = new List<double[]>();
		double[][] sampleUnitArray = sampleUnit;
		foreach (double[] sampleUnit2 in sampleUnitArray)
		{
			if (MinDistance(sampleUnit2, designU) >= _options.DuplicateTolerance && MinDistance(sampleUnit2, freshU) >= _options.DuplicateTolerance)
			{
				freshU.Add(sampleUnit2);
			}
		}
		if (freshU.Count == 0)
		{
			return 0;
		}
		double[][] doubleValues = new double[freshU.Count][];
		for (int j = 0; j < freshU.Count; j++)
		{
			doubleValues[j] = ToPhysical(freshU[j]);
		}
		double[] evaluateBatchAsyncArray = await evaluateBatchAsync(doubleValues);
		for (int k = 0; k < freshU.Count; k++)
		{
			designU.Add(freshU[k]);
			designF.Add(evaluateBatchAsyncArray[k]);
		}
		return freshU.Count;
	}

	private RBFSurrogateModel FitSurrogate(List<double[]> designU, List<double> designF)
	{
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		int ordinal = 0;
		for (int i = 0; i < designF.Count; i++)
		{
			if (double.IsFinite(designF[i]))
			{
				ordinal++;
				positiveInfinity = Math.Min(positiveInfinity, designF[i]);
				negativeInfinity = Math.Max(negativeInfinity, designF[i]);
			}
		}
		if (ordinal == 0)
		{
			return null;
		}
		double negativeInfinityPositiveInfinityTerm = negativeInfinity + Math.Max(negativeInfinity - positiveInfinity, 1.0);
		double[] doubleValues = new double[designF.Count];
		for (int j = 0; j < designF.Count; j++)
		{
			doubleValues[j] = (double.IsFinite(designF[j]) ? designF[j] : negativeInfinityPositiveInfinityTerm);
		}
		if (_options.CapFitValuesAtMedian)
		{
			double[] doubleValuesArray = (double[])doubleValues.Clone();
			Array.Sort(doubleValuesArray);
			double computed = doubleValuesArray[doubleValuesArray.Length / 2];
			for (int k = 0; k < doubleValues.Length; k++)
			{
				doubleValues[k] = Math.Min(doubleValues[k], computed);
			}
		}
		if (!RBFSurrogateModel.TryFit(designU.ToArray(), doubleValues, _options.Ridge, out var model))
		{
			_logger.LogWarning("RBF fit unavailable ({Count} design points); falling back to distance-only scoring.", designF.Count);
			return null;
		}
		return model;
	}

	private double[][] GenerateCandidates(List<double[]> designU, double[] bestU, double sigma, bool haveModel, Random rng, int evals, int? maxEvaluations)
	{
		int freeDimLength = _freeDims.Length;
		int count = Math.Min(100 * freeDimLength, _options.MaxCandidates);
		int count2 = (haveModel ? ((int)((double)count * _options.UniformFraction)) : count);
		int count3 = count - count2;
		double scaledFreeDimLength = Math.Min(20.0 / (double)freeDimLength, 1.0);
		if (maxEvaluations.HasValue && maxEvaluations.Value > 1)
		{
			scaledFreeDimLength *= Math.Max(0.0, 1.0 - Math.Log((double)evals + 1.0) / Math.Log(maxEvaluations.Value));
		}
		List<double[]> doubleValuesList = new List<double[]>(count);
		for (int i = 0; i < count3; i++)
		{
			double[] doubleValues = (double[])bestU.Clone();
			bool condition = false;
			for (int j = 0; j < freeDimLength; j++)
			{
				if (rng.NextDouble() < scaledFreeDimLength)
				{
					doubleValues[j] = Reflect(doubleValues[j] + NextGaussian(rng) * sigma);
					condition = true;
				}
			}
			if (!condition)
			{
				int next = rng.Next(freeDimLength);
				doubleValues[next] = Reflect(doubleValues[next] + NextGaussian(rng) * sigma);
			}
			doubleValuesList.Add(doubleValues);
		}
		for (int k = 0; k < count2; k++)
		{
			double[] doubleValuesArray = new double[freeDimLength];
			for (int l = 0; l < freeDimLength; l++)
			{
				doubleValuesArray[l] = rng.NextDouble();
			}
			doubleValuesList.Add(doubleValuesArray);
		}
		List<double[]> doubleValuesListList = new List<double[]>(doubleValuesList.Count);
		foreach (double[] elements in doubleValuesList)
		{
			if (MinDistance(elements, designU) >= _options.DuplicateTolerance)
			{
				doubleValuesListList.Add(elements);
			}
		}
		if (doubleValuesListList.Count > 0)
		{
			return doubleValuesListList.ToArray();
		}
		double[][] doubleValues2 = new double[count][];
		for (int m = 0; m < count; m++)
		{
			doubleValues2[m] = new double[freeDimLength];
			for (int n = 0; n < freeDimLength; n++)
			{
				doubleValues2[m][n] = rng.NextDouble();
			}
		}
		return doubleValues2;
	}

	private double[][] SelectCandidates(double[][] pool, List<double[]> designU, RBFSurrogateModel model, int batch, ref int weightIndex)
	{
		double[] doubleValues = model?.PredictMany(pool);
		double[] doubleValuesArray = new double[pool.Length];
		for (int i = 0; i < pool.Length; i++)
		{
			doubleValuesArray[i] = MinDistance(pool[i], designU);
		}
		bool[] boolValues = new bool[pool.Length];
		List<double[]> doubleValuesList = new List<double[]>(batch);
		while (doubleValuesList.Count < batch)
		{
			double positiveInfinity = double.PositiveInfinity;
			double negativeInfinity = double.NegativeInfinity;
			double positiveInfinity2 = double.PositiveInfinity;
			double negativeInfinity2 = double.NegativeInfinity;
			for (int j = 0; j < pool.Length; j++)
			{
				if (!boolValues[j] && !(doubleValuesArray[j] < _options.DuplicateTolerance))
				{
					if (doubleValues != null)
					{
						positiveInfinity = Math.Min(positiveInfinity, doubleValues[j]);
						negativeInfinity = Math.Max(negativeInfinity, doubleValues[j]);
					}
					positiveInfinity2 = Math.Min(positiveInfinity2, doubleValuesArray[j]);
					negativeInfinity2 = Math.Max(negativeInfinity2, doubleValuesArray[j]);
				}
			}
			if (double.IsInfinity(positiveInfinity2))
			{
				break;
			}
			double weightCycle = ((doubleValues != null) ? _options.WeightCycle[weightIndex % _options.WeightCycle.Length] : 0.0);
			double negativeInfinityAbovePositiveInfinity = Math.Max(negativeInfinity - positiveInfinity, 1E-12);
			double negativeInfinity2AbovePositiveInfinity2 = Math.Max(negativeInfinity2 - positiveInfinity2, 1E-12);
			int ordinal = -1;
			double positiveInfinity3 = double.PositiveInfinity;
			for (int k = 0; k < pool.Length; k++)
			{
				if (!boolValues[k] && !(doubleValuesArray[k] < _options.DuplicateTolerance))
				{
					double positiveInfinityTerm = ((doubleValues != null) ? ((doubleValues[k] - positiveInfinity) / negativeInfinityAbovePositiveInfinity) : 0.0);
					double positiveInfinity2Term = 1.0 - (doubleValuesArray[k] - positiveInfinity2) / negativeInfinity2AbovePositiveInfinity2;
					double positiveInfinity32 = weightCycle * positiveInfinityTerm + (1.0 - weightCycle) * positiveInfinity2Term;
					if (positiveInfinity32 < positiveInfinity3)
					{
						positiveInfinity3 = positiveInfinity32;
						ordinal = k;
					}
				}
			}
			boolValues[ordinal] = true;
			doubleValuesList.Add(pool[ordinal]);
			weightIndex++;
			for (int l = 0; l < pool.Length; l++)
			{
				if (!boolValues[l])
				{
					doubleValuesArray[l] = Math.Min(doubleValuesArray[l], Distance(pool[l], pool[ordinal]));
				}
			}
		}
		return doubleValuesList.ToArray();
	}

	private void ReportIteration(ProgressReporter pr, StoppingCriteria stopping, int iteration, int evals, double sigma, double batchBest, double best, bool improved, bool haveModel)
	{
		if (stopping.MaxEvaluations.HasValue && stopping.MaxEvaluations.Value > 0)
		{
			pr.ReportProgressFraction(evals, stopping.MaxEvaluations.Value);
		}
		string textValue;
		if (improved)
		{
			textValue = "improved";
		}
		else
		{
			textValue = (haveModel ? "stalled" : "no-fit");
		}
		string msg = $"  RBF iter {iteration} [{textValue}]: sigma={sigma:F4} obj={batchBest:G6} best={best:G6} evals={evals}";
		pr.ReportMessage(msg, LogLevel.Information);
	}

	private double[] ClampToBounds(double[] physical)
	{
		double[] doubleValues = new double[_min.Length];
		for (int i = 0; i < _min.Length; i++)
		{
			doubleValues[i] = Math.Clamp(physical[i], _min[i], _max[i]);
		}
		return doubleValues;
	}

	private double[] ToUnit(double[] physical)
	{
		double[] doubleValues = new double[_freeDims.Length];
		for (int i = 0; i < _freeDims.Length; i++)
		{
			int freeDim = _freeDims[i];
			double warp = ParameterScaling.Warp(Math.Clamp(physical[freeDim], _min[freeDim], _max[freeDim]), _scales[freeDim]);
			doubleValues[i] = Math.Clamp((warp - _warpedMin[freeDim]) / (_warpedMax[freeDim] - _warpedMin[freeDim]), 0.0, 1.0);
		}
		return doubleValues;
	}

	private double[] ToPhysical(double[] unit)
	{
		double[] doubleValues = new double[_min.Length];
		for (int i = 0; i < _min.Length; i++)
		{
			doubleValues[i] = _min[i];
		}
		for (int j = 0; j < _freeDims.Length; j++)
		{
			int freeDim = _freeDims[j];
			doubleValues[freeDim] = ParameterScaling.Unwarp(_warpedMin[freeDim] + unit[j] * (_warpedMax[freeDim] - _warpedMin[freeDim]), _scales[freeDim]);
		}
		return doubleValues;
	}

	private static double Reflect(double v)
	{
		if (v < 0.0)
		{
			v = 0.0 - v;
		}
		if (v > 1.0)
		{
			v = 2.0 - v;
		}
		return Math.Clamp(v, 0.0, 1.0);
	}

	private static double NextGaussian(Random rng)
	{
		double d = 1.0 - rng.NextDouble();
		double nextDouble = rng.NextDouble();
		return Math.Sqrt(-2.0 * Math.Log(d)) * Math.Cos(Math.PI * 2.0 * nextDouble);
	}

	private static double MinDistance(double[] u, List<double[]> points)
	{
		double positiveInfinity = double.PositiveInfinity;
		for (int i = 0; i < points.Count; i++)
		{
			positiveInfinity = Math.Min(positiveInfinity, Distance(u, points[i]));
		}
		return positiveInfinity;
	}

	private static double Distance(double[] a, double[] b)
	{
		double computed = 0.0;
		for (int i = 0; i < a.Length; i++)
		{
			double computed2 = a[i] - b[i];
			computed += computed2 * computed2;
		}
		return Math.Sqrt(computed);
	}
}
