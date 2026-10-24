using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.LightweightPlotting;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class HydraulicCalibrator
{
	private static readonly ILogger _logger = LogHelper.GetLogger("HydraulicCalibrator");

	private readonly IOptimizer _optimizer;

	public HydraulicCalibrator(IOptimizer optimizer = null)
	{
		_optimizer = optimizer;
	}

	public async Task<CalibrationResult> CalibrateAsync(ICalibrationModel model, IReadOnlyList<ICalibrationParameter> parameters, IReadOnlyList<Gauge> gauges, ISimulatedSeriesExtractor extractor, CalibrationControl control, ProgressReporter pr = null)
	{
		if (model == null)
		{
			throw new ArgumentNullException("model");
		}
		if (control == null)
		{
			throw new ArgumentNullException("control");
		}
		control.Validate();
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		ParameterSpace space = new ParameterSpace(parameters);
		ObjectiveFunction objective = new ObjectiveFunction(gauges, extractor);
		InProcessRunExecutor executor = new InProcessRunExecutor(space, model, objective, control);
		return await SearchAsync(space, executor, control, pr);
	}

	public async Task<CalibrationResult> SearchAsync(ParameterSpace space, ICalibrationRunExecutor executor, CalibrationControl control, ProgressReporter pr = null)
	{
		if (space == null)
		{
			throw new ArgumentNullException("space");
		}
		if (executor == null)
		{
			throw new ArgumentNullException("executor");
		}
		if (control == null)
		{
			throw new ArgumentNullException("control");
		}
		control.Validate();
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		CalibrationRunner runner = new CalibrationRunner(executor, control.MaxConcurrentRuns)
		{
			Progress = pr
		};
		List<RunResult> priorRuns;
		bool warmStart;
		using (SetUpJournal(space, control, runner, pr, out priorRuns, out warmStart))
		{
			IBoundsHandler boundsHandler2;
			if (control.BoundsMode != BoundsMode.Clamp)
			{
				IBoundsHandler boundsHandler = new TransformBoundsHandler(space.Min, space.Max, space.Scales);
				boundsHandler2 = boundsHandler;
			}
			else
			{
				IBoundsHandler boundsHandler = new ClampBoundsHandler(space.Min, space.Max, space.Scales);
				boundsHandler2 = boundsHandler;
			}
			IBoundsHandler bounds = boundsHandler2;
			IOptimizer optimizer = _optimizer;
			if (optimizer == null)
			{
				optimizer = control.Method switch
				{
					OptimizationMethod.NelderMead => (IOptimizer)new NelderMeadOptimizer(control.InitialStepFraction), 
					OptimizationMethod.RBFSurrogate => new RBFSurrogateOptimizer(control.MaxConcurrentRuns, control.RngSeed), 
					_ => new HookeJeevesOptimizer(control.InitialStepFraction), 
				};
			}
			IOptimizer optimizer2 = optimizer;
			Func<double[][], Task<double[]>> evaluateBatch = runner.EvaluateBatchAsync;
			pr.ReportMessage($"LHS: drawing {control.LhsSampleCount} samples across {space.Dimension} parameters.", LogLevel.Information);
			LatinHypercubeSampler latinHypercubeSampler = new LatinHypercubeSampler(control.RngSeed);
			double[][] samples = latinHypercubeSampler.Sample(control.LhsSampleCount, space.Min, space.Max, space.Scales);
			double[] sampleObjectives = await evaluateBatch(samples);
			int bestLhs = 0;
			for (int i = 1; i < sampleObjectives.Length; i++)
			{
				if (sampleObjectives[i] < sampleObjectives[bestLhs])
				{
					bestLhs = i;
				}
			}
			pr.ReportMessage($"LHS complete: best objective {sampleObjectives[bestLhs]:G6} at params=[{FormatNamedParams(space, samples[bestLhs])}].", LogLevel.Information);
			OptimizationOutcome best = null;
			if (optimizer2 is IGlobalOptimizer globalOptimizer)
			{
				if (warmStart)
				{
					double[][] physicalPoints = samples.Concat(priorRuns.Select((RunResult r) => r.Physical)).ToArray();
					double[] objectives = sampleObjectives.Concat(priorRuns.Select((RunResult r) => r.Objective)).ToArray();
					globalOptimizer.Seed(physicalPoints, objectives);
				}
				else
				{
					globalOptimizer.Seed(samples, sampleObjectives);
				}
				pr.ReportMessage($"Global search ({optimizer2.Name}) seeded with {samples.Length} LHS evaluations.", LogLevel.Information);
				ProgressReporter pr2 = pr.SubTask("Optimize (" + optimizer2.Name + ")", 0f, 1f);
				best = await optimizer2.MinimizeAsync(bounds, space.Initial, evaluateBatch, control.Stopping, pr2);
				pr.ReportMessage($"Global search done: objective {best.BestObjective:G6} ({best.StopReason}).", LogLevel.Information);
				_logger.LogInformation("Global search finished: objective {Objective} ({Reason}).", best.BestObjective, best.StopReason);
			}
			else
			{
				int[] intValues = (from num2 in Enumerable.Range(0, samples.Length)
					orderby sampleObjectives[num2]
					select num2).ToArray();
				List<double[]> starts = new List<double[]> { space.Initial };
				for (int intervalIndex = 0; intervalIndex < control.LhsStartCount && intervalIndex < intValues.Length; intervalIndex++)
				{
					starts.Add(samples[intValues[intervalIndex]]);
				}
				if (warmStart && TryGetBestPrior(priorRuns, out var bestPrior))
				{
					starts.Insert(0, bestPrior);
				}
				for (int s = 0; s < starts.Count; s++)
				{
					if (pr.CancelRequested)
					{
						break;
					}
					pr.ReportMessage($"Start {s + 1}/{starts.Count}: optimizing from params=[{FormatNamedParams(space, starts[s])}].", LogLevel.Information);
					ProgressReporter pr3 = pr.SubTask($"Optimize (start {s + 1}/{starts.Count})", (float)s / (float)starts.Count, 1f / (float)starts.Count);
					OptimizationOutcome optimizationOutcome = await optimizer2.MinimizeAsync(bounds, starts[s], evaluateBatch, control.Stopping, pr3);
					if (best == null || optimizationOutcome.BestObjective < best.BestObjective)
					{
						best = optimizationOutcome;
					}
					pr.ReportMessage($"Start {s + 1}/{starts.Count} done: objective {optimizationOutcome.BestObjective:G6} ({optimizationOutcome.StopReason}); best-so-far {best.BestObjective:G6}.", LogLevel.Information);
					_logger.LogInformation("Start {Index} finished: objective {Objective} ({Reason}).", s + 1, optimizationOutcome.BestObjective, optimizationOutcome.StopReason);
				}
			}
			if (best == null)
			{
				best = OptimizerLoop.BuildOutcome(samples[bestLhs], sampleObjectives[bestLhs], 0, 0, StopReason.Cancelled);
			}
			pr.ReportMessage($"Persisting optimum (objective {best.BestObjective:G6}).", LogLevel.Information);
			RunResult runResult = await runner.PersistOptimumAsync(best.BestPhysical);
			IReadOnlyList<RunResult> history = runner.History;
			double[] convergenceTrace = BuildConvergenceTrace(history);
			string convergenceChartPath = TryWriteConvergenceChart(convergenceTrace, control.OutputDirectory);
			pr.ReportMessage($"Calibration complete: best objective {best.BestObjective:G6} over {history.Count} runs ({best.StopReason}); params=[{FormatNamedParams(space, best.BestPhysical)}].", LogLevel.Information);
			if (convergenceChartPath != null)
			{
				pr.ReportMessage("Convergence chart: " + convergenceChartPath, LogLevel.Information);
			}
			return new CalibrationResult
			{
				BestPhysical = best.BestPhysical,
				BestObjective = best.BestObjective,
				Parameters = space.Parameters,
				OptimumPerGauge = runResult.PerGauge,
				OptimumRun = runResult,
				History = history,
				ConvergenceTrace = convergenceTrace,
				StopReason = best.StopReason,
				TotalRuns = history.Count,
				ConvergenceChartPath = convergenceChartPath
			};
		}
	}

	private static CalibrationJournal SetUpJournal(ParameterSpace space, CalibrationControl control, CalibrationRunner runner, ProgressReporter pr, out List<RunResult> priorRuns, out bool warmStart)
	{
		priorRuns = null;
		warmStart = false;
		if (string.IsNullOrEmpty(control.OutputDirectory))
		{
			return null;
		}
		string path = Path.Combine(control.OutputDirectory, "calibration.journal.jsonl");
		JournalFingerprint journalFingerprint = JournalFingerprint.Create(space, control);
		if (control.Resume)
		{
			if (CalibrationJournal.TryLoad(path, out var fingerprint, out priorRuns))
			{
				if (!journalFingerprint.ParameterSpaceMatches(fingerprint))
				{
					throw new InvalidOperationException("Cannot resume: the journal's parameter space (names/bounds/scales) differs from the current configuration, so its vectors are incomparable. Restore the original parameters or start fresh without Resume.");
				}
				warmStart = !journalFingerprint.ReplayConfigMatches(fingerprint);
				runner.Preload(priorRuns);
				pr.ReportMessage(warmStart ? $"Resume (warm-start): configuration changed; {priorRuns.Count} prior evaluations seed the search." : $"Resume (replay): {priorRuns.Count} prior evaluations preloaded; the search continues at the frontier.", LogLevel.Information);
				_logger.LogInformation("Resuming from {Count} journaled runs (warm-start: {WarmStart}).", priorRuns.Count, warmStart);
			}
			else
			{
				pr.ReportMessage("Resume requested but no usable journal was found; starting fresh.", LogLevel.Warning);
			}
		}
		try
		{
			Directory.CreateDirectory(control.OutputDirectory);
			return runner.Journal = CalibrationJournal.CreateNew(path, journalFingerprint, priorRuns);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to create the calibration journal; this run will not be resumable.");
			pr.ReportMessage("Journal creation failed; this run will not be resumable.", LogLevel.Warning);
			return null;
		}
	}

	private static bool TryGetBestPrior(IReadOnlyList<RunResult> priorRuns, out double[] bestPrior)
	{
		bestPrior = null;
		double positiveInfinity = double.PositiveInfinity;
		foreach (RunResult priorRun in priorRuns)
		{
			if (priorRun.Succeeded && priorRun.Objective < positiveInfinity)
			{
				positiveInfinity = priorRun.Objective;
				bestPrior = priorRun.Physical;
			}
		}
		return bestPrior != null;
	}

	private static string FormatNamedParams(ParameterSpace space, double[] physical)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < physical.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append(space.Parameters[i].Name).Append('=').Append(physical[i].ToString("G5", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	private static double[] BuildConvergenceTrace(IReadOnlyList<RunResult> history)
	{
		RunResult[] orderBy = history.OrderBy((RunResult r) => r.RunId).ToArray();
		double[] convergenceTrace = new double[orderBy.Length];
		double positiveInfinity = double.PositiveInfinity;
		for (int intervalIndex = 0; intervalIndex < orderBy.Length; intervalIndex++)
		{
			double objective = (orderBy[intervalIndex].Succeeded ? orderBy[intervalIndex].Objective : double.PositiveInfinity);
			if (objective < positiveInfinity)
			{
				positiveInfinity = objective;
			}
			convergenceTrace[intervalIndex] = positiveInfinity;
		}
		return convergenceTrace;
	}

	private static string TryWriteConvergenceChart(double[] trace, string outputDirectory)
	{
		if (string.IsNullOrEmpty(outputDirectory) || trace.Length == 0)
		{
			return null;
		}
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		for (int i = 0; i < trace.Length; i++)
		{
			if (double.IsFinite(trace[i]))
			{
				doubleList.Add(i + 1);
				doubleListList.Add(trace[i]);
			}
		}
		if (doubleListList.Count == 0)
		{
			return null;
		}
		try
		{
			Directory.CreateDirectory(outputDirectory);
			string path = Path.Combine(outputDirectory, "convergence.svg");
			double computed = doubleListList.Min();
			double computed2 = doubleListList.Max();
			if (computed2 <= computed)
			{
				computed2 = computed + 1.0;
			}
			SvgFigure svgFigure = new SvgFigure(900, 500);
			svgFigure.SetLimits(doubleList[0], (doubleList[doubleList.Count - 1] <= doubleList[0]) ? (doubleList[0] + 1.0) : doubleList[doubleList.Count - 1], computed, computed2);
			svgFigure.PlotLine(doubleList.ToArray(), doubleListList.ToArray());
			svgFigure.DrawAxes("Run", "Best objective", "", "Calibration convergence");
			svgFigure.Save(path);
			return path;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to write convergence chart.");
			return null;
		}
	}
}
