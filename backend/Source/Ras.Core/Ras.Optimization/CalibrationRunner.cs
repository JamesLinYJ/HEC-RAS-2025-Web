using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class CalibrationRunner
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CalibrationRunner");

	internal const string CancelledReason = "Cancelled";

	private const int CacheKeyPrecision = 8;

	private readonly ICalibrationRunExecutor _executor;

	private readonly SemaphoreSlim _gate;

	private readonly object _lock = new object();

	private readonly Dictionary<string, Task<RunResult>> _inFlight = new Dictionary<string, Task<RunResult>>();

	private readonly Dictionary<string, RunResult> _cache = new Dictionary<string, RunResult>();

	private readonly List<RunResult> _history = new List<RunResult>();

	private int _runCounter = -1;

	public ProgressReporter Progress { get; set; } = ProgressReporter.None();

	public CalibrationJournal Journal { get; set; }

	public IReadOnlyList<RunResult> History
	{
		get
		{
			lock (_lock)
			{
				return _history.ToList();
			}
		}
	}

	public CalibrationRunner(ICalibrationRunExecutor executor, int maxConcurrentRuns)
	{
		_executor = executor ?? throw new ArgumentNullException("executor");
		if (maxConcurrentRuns < 1)
		{
			throw new ArgumentException("maxConcurrentRuns must be at least 1.", "maxConcurrentRuns");
		}
		_gate = new SemaphoreSlim(maxConcurrentRuns);
	}

	public void Preload(IReadOnlyList<RunResult> priorRuns)
	{
		if (priorRuns == null)
		{
			throw new ArgumentNullException("priorRuns");
		}
		lock (_lock)
		{
			foreach (RunResult priorRun in priorRuns)
			{
				_cache[Key(priorRun.Physical)] = priorRun;
				_history.Add(priorRun);
				if (priorRun.RunId > _runCounter)
				{
					_runCounter = priorRun.RunId;
				}
			}
		}
	}

	public async Task<double[]> EvaluateBatchAsync(double[][] vectors)
	{
		if (vectors == null)
		{
			throw new ArgumentNullException("vectors");
		}
		string[] strings = vectors.Select(Key).ToArray();
		Task<RunResult>[] tasks = new Task<RunResult>[vectors.Length];
		lock (_lock)
		{
			for (int i = 0; i < vectors.Length; i++)
			{
				if (_cache.TryGetValue(strings[i], out var localValue))
				{
					tasks[i] = Task.FromResult(localValue);
					continue;
				}
				if (_inFlight.TryGetValue(strings[i], out var tasks2))
				{
					tasks[i] = tasks2;
					continue;
				}
				string key = strings[i];
				double[] copy = (double[])vectors[i].Clone();
				Task<RunResult> task = Task.Run(() => RunOneAsync(key, copy));
				_inFlight[key] = task;
				tasks[i] = task;
			}
		}
		await Task.WhenAll(tasks);
		double[] batchAsync = new double[vectors.Length];
		for (int intervalIndex = 0; intervalIndex < vectors.Length; intervalIndex++)
		{
			batchAsync[intervalIndex] = tasks[intervalIndex].Result.Objective;
		}
		return batchAsync;
	}

	public async Task<RunResult> PersistOptimumAsync(double[] physical)
	{
		if (physical == null)
		{
			throw new ArgumentNullException("physical");
		}
		double[] copy = (double[])physical.Clone();
		int runId = Interlocked.Increment(ref _runCounter);
		RunResult runResult;
		try
		{
			runResult = (await _executor.EvaluateAsync(runId, copy, forcePersist: true, Progress)) ?? Failure(runId, copy, "Executor returned null.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Optimum run {RunId} threw; recording as failed.", runId);
			runResult = Failure(runId, copy, ex.GetType().Name + ": " + ex.Message);
		}
		int count;
		lock (_lock)
		{
			_history.Add(runResult);
			count = _history.Count;
		}
		Journal?.Append(runResult);
		ReportRun(runResult, count);
		return runResult;
	}

	private async Task<RunResult> RunOneAsync(string key, double[] vector)
	{
		int runId = Interlocked.Increment(ref _runCounter);
		RunResult runResult;
		if (Progress.CancelRequested)
		{
			runResult = Failure(runId, vector, "Cancelled");
		}
		else
		{
			await _gate.WaitAsync();
			try
			{
				if (Progress.CancelRequested)
				{
					runResult = Failure(runId, vector, "Cancelled");
				}
				else
				{
					try
					{
						runResult = (await _executor.EvaluateAsync(runId, vector, forcePersist: false, Progress)) ?? Failure(runId, vector, "Executor returned null.");
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Run {RunId} threw; recording as failed.", runId);
						runResult = Failure(runId, vector, ex.GetType().Name + ": " + ex.Message);
					}
				}
			}
			finally
			{
				_gate.Release();
			}
		}
		int count;
		lock (_lock)
		{
			_cache[key] = runResult;
			_inFlight.Remove(key);
			_history.Add(runResult);
			count = _history.Count;
		}
		Journal?.Append(runResult);
		ReportRun(runResult, count);
		return runResult;
	}

	private static RunResult Failure(int runId, double[] physical, string reason)
	{
		return new RunResult
		{
			RunId = runId,
			Physical = physical,
			Objective = double.PositiveInfinity,
			Succeeded = false,
			FailureReason = reason
		};
	}

	private void ReportRun(RunResult result, int totalCount)
	{
		string succeededFailureReasonTerm = (result.Succeeded ? "ok" : ("FAILED: " + result.FailureReason));
		Progress.ReportMessage($"Run {result.RunId} ({totalCount} total): objective {result.Objective:G6} [{succeededFailureReasonTerm}] params=[{OptimizerLoop.FormatVector(result.Physical)}]", LogLevel.Information);
	}

	private static string Key(double[] vector)
	{
		string[] strings = new string[vector.Length];
		for (int i = 0; i < vector.Length; i++)
		{
			strings[i] = Math.Round(vector[i], 8).ToString("R", CultureInfo.InvariantCulture);
		}
		return string.Join('|', strings);
	}
}
